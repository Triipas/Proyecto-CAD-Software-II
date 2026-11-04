using System.Diagnostics;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// HU44 y HU45 (S6-T11; plan de asistencia con IA, 5.6) — analiza un reporte recién registrado: arma la entrada anonimizada con los
// candidatos a duplicado y decisiones parecidas ya tomadas (S6-T09), llama al asistente con tiempo máximo, valida la respuesta, la
// guarda y avisa a la central. Con la admisión automática en «Sombra» o «Activa», además evalúa si se admitiría sola (S6-T16) y lo
// guarda en la misma transacción. Es idempotente (una sugerencia por reporte y versión del prompt). Si algo falla no se guarda nada
// y el operador trabaja como hoy (CA03).
// Nunca registra la descripción, la justificación ni la respuesta: solo identificadores, latencia, tokens y el resultado (RNF07).
public sealed class AnalizarReporte(CadDbContext db, IAsistenteReportes asistente, BuscarDuplicados duplicados, IRecuperadorEjemplos ejemplos,
    EvaluarAdmision evaluar, IOptions<OpcionesIa> opciones, TimeProvider reloj, INotificador notificador, ILogger<AnalizarReporte> registro)
{
    public async Task EjecutarAsync(Guid casoId, CancellationToken ct)
    {
        if (!asistente.Disponible)
            return;
        var config = opciones.Value;
        if (await db.SugerenciasReporte.AsNoTracking().AnyAsync(s => s.CasoId == casoId && s.VersionPrompt == config.VersionPrompt, ct))
            return;

        // Solo mientras la central todavía no decidió: después la sugerencia ya no sirve a nadie.
        var reporte = await (from caso in db.Casos.AsNoTracking()
                             join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                             where caso.Id == casoId && caso.Origen == OrigenCaso.Reporte
                                   && (caso.Estado == EstadoCaso.Recibido || caso.Estado == EstadoCaso.EnRevision)
                             select new { caso.RegistradoEn, caso.CiudadanoId, caso.Estado, detalle })
            .SingleOrDefaultAsync(ct);
        if (reporte is null)
            return;

        var d = reporte.detalle;
        var catalogo = await db.CategoriasIncidencia.AsNoTracking().OrderBy(c => c.Orden)
            .Select(c => new CategoriaParaAnalisis(c.Id, c.Nombre, c.Ayuda)).ToListAsync(ct);
        var candidatos = (await duplicados.EjecutarAsync(casoId, ct))
            .Select(c => new CandidatoDuplicado(c.CasoId, Anonimizador.Limpiar(c.Descripcion), c.Categoria, c.Metros, c.Minutos))
            .ToList();
        var parecidos = await ejemplos.RecuperarAsync(casoId, d.Descripcion, config.Ejemplos, ct);
        var entrada = new EntradaAnalisis(casoId, Anonimizador.Limpiar(d.Descripcion), d.CategoriaOriginalId, d.FuenteUbicacion,
            d.LatitudHecho is not null, d.ReferenciaHecho is { } referencia ? Anonimizador.Limpiar(referencia) : null,
            d.OcurridoAproximadamenteEn, reporte.RegistradoEn, catalogo, parecidos, candidatos);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.TiempoMaximoSegundos)));
        var cronometro = Stopwatch.StartNew();
        ResultadoAsistente? resultado;
        try
        {
            resultado = await asistente.AnalizarAsync(entrada, limite.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registro.LogWarning("Reporte {CasoId}: el asistente no respondió ({Error}, {LatenciaMs} ms); queda sin sugerencia.",
                casoId, ex.GetType().Name, cronometro.ElapsedMilliseconds);
            return;
        }
        var latencia = (int)cronometro.ElapsedMilliseconds;
        if (resultado is null)
        {
            registro.LogInformation("Reporte {CasoId}: sin sugerencia del asistente ({LatenciaMs} ms).", casoId, latencia);
            return;
        }
        if (ValidarAnalisis.Aplicar(resultado.Analisis, entrada) is not { } valido)
        {
            registro.LogWarning("Reporte {CasoId}: la respuesta del asistente {Modelo} no pasó la validación; queda sin sugerencia.", casoId, resultado.Modelo);
            return;
        }

        var ahora = reloj.GetUtcNow();
        var sugerencia = SugerenciaReporte.Registrar(casoId, config.VersionPrompt, config.VersionCriterios, resultado with { Analisis = valido },
            latencia, ahora);
        db.SugerenciasReporte.Add(sugerencia);
        var admision = await evaluar.EjecutarAsync(
            new ReporteParaAdmision(casoId, reporte.CiudadanoId is not null && d.RegistradoPorOperadorId is null, reporte.Estado == EstadoCaso.Recibido,
                d.CategoriaOriginalId, d.LatitudHecho is not null && d.LongitudHecho is not null, d.Descripcion.Length),
            valido, entrada, TimeSpan.FromSeconds(Math.Max(1, config.TiempoMaximoVerificadorSegundos)), ahora, ct);
        if (admision is not null && !await db.EvaluacionesAdmision.AnyAsync(e => e.CasoId == casoId, ct))
            db.EvaluacionesAdmision.Add(admision);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            return; // otro análisis del mismo reporte ganó la carrera
        }

        registro.LogInformation(
            "Reporte {CasoId}: sugerencia {Decision} con confianza {Confianza} ({Modelo}{Escalada}; {LatenciaMs} ms; {TokensEntrada}+{TokensSalida} tokens; {Ejemplos} ejemplos).",
            casoId, sugerencia.Decision, sugerencia.Confianza, sugerencia.Modelo, sugerencia.Escalada ? ", escalada" : "", latencia,
            sugerencia.TokensEntrada, sugerencia.TokensSalida, parecidos.Count);
        if (admision is not null)
            registro.LogInformation("Reporte {CasoId}: admisión automática ({Modo}) {Resultado}.", casoId, admision.Modo,
                admision.Elegible ? "elegible" : $"no elegible: {admision.Condicion}");
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None); // CA01: la tarjeta aparece sin recargar a mano
        }
        catch (Exception ex)
        {
            registro.LogWarning("No se pudo avisar a la central de la sugerencia del reporte {CasoId} ({Error}).", casoId, ex.GetType().Name);
        }
    }
}
