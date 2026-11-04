using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// HU47 (S6-T16; plan de asistencia con IA, 7.4 y 7.6) — la admisión automática en «Activa». Pasado el retraso, cada reporte
// elegible que sigue «Recibido» se admite con los mismos casos de uso que un operador, con el usuario del sistema como responsable:
// iniciar la revisión (si un operador ya la tomó, IniciarAsync rechaza y el sistema se aparta, CA04), corregir la categoría si está
// permitido y admitir con el motivo «Admitido automáticamente por el sistema: …». Si el modo se pausó durante el retraso, no se admite
// nada (CA01). Después vigila el freno: con suficientes admisiones automáticas en 24 h y demasiadas descartadas luego por los
// operadores, el modo pasa solo a «Sombra» y queda registrado (CA08).
public sealed class AdmitirAutomaticamente(CadDbContext db, RevisarReporte revisar, DecidirReporte decidir, CambiarAjusteIa cambiar,
    TimeProvider reloj, ILogger<AdmitirAutomaticamente> registro)
{
    public async Task<int> ProcesarAsync(CancellationToken ct)
    {
        var ajuste = await db.AjustesIa.AsNoTracking().SingleOrDefaultAsync(a => a.Id == AjusteIa.IdUnico, ct);
        if (ajuste?.Modo != ModoAdmision.Activa)
            return 0;

        var ahora = reloj.GetUtcNow();
        var pendientes = await (from e in db.EvaluacionesAdmision.AsNoTracking()
                                join caso in db.Casos.AsNoTracking() on e.CasoId equals caso.Id
                                where e.Modo == ModoAdmision.Activa && e.Elegible && e.AdmitirDesde <= ahora && caso.Estado == EstadoCaso.Recibido
                                orderby e.AdmitirDesde
                                select e)
            .Take(20)
            .ToListAsync(ct);

        var admitidos = 0;
        foreach (var evaluacion in pendientes)
        {
            if (await AdmitirAsync(evaluacion, ajuste, ct))
                admitidos++;
        }
        // En cada vuelta, no solo tras admitir: los descartes de la central llegan después.
        await FrenarSiCorrespondeAsync(ct);
        return admitidos;
    }

    private async Task<bool> AdmitirAsync(EvaluacionAdmision evaluacion, AjusteIa ajuste, CancellationToken ct)
    {
        var casoId = evaluacion.CasoId;
        // Un operador que ya inició la revisión gana siempre: el sistema se aparta sin error (la respuesta trae su nombre).
        if (await revisar.IniciarAsync(casoId, UsuarioSistema.Id, ct) is not (ResultadoRevision.Registrado or ResultadoRevision.YaRegistrado))
        {
            registro.LogInformation("Reporte {CasoId}: un operador tomó la revisión durante el retraso; no se admite solo.", casoId);
            return false;
        }

        var categoria = await db.Casos.AsNoTracking().Where(c => c.Id == casoId).Select(c => c.CategoriaId).SingleAsync(ct);
        if (categoria != evaluacion.CategoriaId && ajuste.PermitirCorregirCategoria)
            await revisar.CorregirCategoriaAsync(casoId, evaluacion.CategoriaId, UsuarioSistema.Id, ct);

        var resultado = await decidir.EjecutarAsync(casoId, new DatosDecision(TipoDecisionReporte.Admision, evaluacion.Motivo,
            Prioridad: evaluacion.Prioridad ?? PrioridadesCaso.Media, Heridos: PersonasHeridas.NoSeSabe), UsuarioSistema.Id, ct);
        if (resultado is ResultadoDecision.Registrado or ResultadoDecision.YaRegistrado)
        {
            registro.LogInformation("Reporte {CasoId}: admitido automáticamente.", casoId);
            return true;
        }
        // Raro (otro operador decidió entre la revisión y la decisión): queda como esté y lo resuelve la central.
        registro.LogWarning("Reporte {CasoId}: la admisión automática no se pudo registrar ({Resultado}).", casoId, resultado.GetType().Name);
        return false;
    }

    // 7.6: en las últimas 24 h, al menos FrenoMinimoAdmisiones admisiones automáticas y más del FrenoPorcentajeDescartes % de ellas
    // descartadas después por la central.
    public async Task<bool> FrenarSiCorrespondeAsync(CancellationToken ct)
    {
        var ajuste = await db.AjustesIa.AsNoTracking().SingleOrDefaultAsync(a => a.Id == AjusteIa.IdUnico, ct);
        if (ajuste?.Modo != ModoAdmision.Activa)
            return false;
        var desde = reloj.GetUtcNow().AddHours(-24);
        var estados = await (from d in db.DecisionesReporte.AsNoTracking()
                             join caso in db.Casos.AsNoTracking() on d.CasoId equals caso.Id
                             where d.OperadorId == UsuarioSistema.Id && d.Tipo == TipoDecisionReporte.Admision && d.DecididaEn >= desde
                             select caso.Estado)
            .ToListAsync(ct);
        var descartados = estados.Count(e => e == EstadoCaso.Descartada);
        if (estados.Count < ajuste.FrenoMinimoAdmisiones || descartados * 100 <= ajuste.FrenoPorcentajeDescartes * estados.Count)
            return false;

        var porcentaje = (int)Math.Round(100.0 * descartados / estados.Count);
        await cambiar.FrenarAsync($"Freno automático: de {estados.Count} admisiones automáticas en 24 h, los operadores descartaron {descartados} " +
            $"({porcentaje} %, más del {ajuste.FrenoPorcentajeDescartes} %).", ct);
        registro.LogWarning("Admisión automática: freno automático, pasa a Sombra ({Descartados} de {Admisiones} descartadas).", descartados, estados.Count);
        return true;
    }
}

// Revisa cada pocos segundos si hay admisiones vencidas. Lee de la base, así que sobrevive a un reinicio: lo programado se admite al
// volver si sigue «Recibido» y el modo sigue «Activa».
public sealed class ServicioAdmisionAutomatica(IServiceScopeFactory alcances, Microsoft.Extensions.Options.IOptions<OpcionesIa> opciones,
    TimeProvider reloj, ILogger<ServicioAdmisionAutomatica> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, opciones.Value.IntervaloAdmisionSegundos));
        while (!detener.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalo, reloj, detener);
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<AdmitirAutomaticamente>().ProcesarAsync(detener);
            }
            catch (OperationCanceledException) when (detener.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                registro.LogError("La admisión automática falló en esta vuelta ({Error}); se reintenta en la siguiente.", ex.GetType().Name);
            }
        }
    }
}
