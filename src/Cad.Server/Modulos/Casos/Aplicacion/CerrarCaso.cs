using Cad.Contratos.Casos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public sealed record DatosCierre(Guid? CategoriaId, string? ResultadoAtencion, string? ResultadoPublico, string? NotasInternas);

public abstract record ResultadoCierre
{
    public sealed record Cerrado(CierreCaso Cierre) : ResultadoCierre;
    // La misma solicitud reenviada por el mismo responsable: se devuelve el cierre ya registrado (CA08).
    public sealed record YaCerrado(CierreCaso Cierre) : ResultadoCierre;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCierre;
    public sealed record Rechazado(string Motivo) : ResultadoCierre;
    // El caso no existe o, para un sereno, no le fue asignado (CA06): no se distingue.
    public sealed record NoEncontrado : ResultadoCierre;
}

// HU24 — Cerrar un caso con su tipificación y resultado (S2-T12, S2-T13). En una sola transacción: el caso pasa a
// «Cerrada», la asignación deja de estar vigente (el sereno se libera, CA07), se guarda el cierre y un evento.
public sealed class CerrarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<CerrarCaso> registro)
{
    public const string MotivoNoEnAtencion = "El caso no está «En atención»: no se puede cerrar.";
    public const string MotivoYaCerrado = "El caso ya fue cerrado por otro responsable.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se cerraba. Revisa su estado e inténtalo de nuevo.";

    private const int Intentos = 3;

    public async Task<ResultadoCierre> EjecutarAsync(Guid casoId, DatosCierre datos, Guid actorId, ActorAvance actor, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
            // La asignación más reciente del caso: tras el cierre ya no está vigente, y el reenvío del sereno que cerró
            // debe reconocerse igualmente.
            var asignacion = await db.Asignaciones.Where(a => a.CasoId == casoId).OrderByDescending(a => a.AsignadaEn).FirstOrDefaultAsync(ct);
            if (caso is null || (actor == ActorAvance.Sereno && asignacion?.SerenoId != actorId))
                return new ResultadoCierre.NoEncontrado();

            // Primero el cierre ya registrado (CA08).
            var previo = await db.CierresCaso.AsNoTracking().SingleOrDefaultAsync(c => c.CasoId == casoId, ct);
            if (previo is not null)
                return previo.ResponsableId == actorId ? new ResultadoCierre.YaCerrado(previo) : new ResultadoCierre.Rechazado(MotivoYaCerrado);

            if (caso.Estado != EstadoCaso.EnAtencion)
                return new ResultadoCierre.Rechazado(MotivoNoEnAtencion); // CA03

            var errores = await ValidarAsync(datos, ct);
            if (errores.Count > 0)
                return new ResultadoCierre.Invalido(errores); // CA02: el caso sigue «En atención»

            var ahora = reloj.GetUtcNow();
            caso.Cerrar();
            if (asignacion is { Vigente: true })
                asignacion.Finalizar(ahora);
            var cierre = CierreCaso.Registrar(caso, datos.CategoriaId!.Value, datos.ResultadoAtencion!, datos.ResultadoPublico!.Trim(),
                string.IsNullOrWhiteSpace(datos.NotasInternas) ? null : datos.NotasInternas.Trim(), actorId, ahora);
            db.CierresCaso.Add(cierre);
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoCerrado, actorId, VisibilidadEvento.Interna, ahora));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException u && u.EsViolacionDeUnicidad()))
            {
                // Otro cierre (o cambio) confirmó antes: se vuelve a evaluar con los datos nuevos.
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoCierre.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Caso {CasoId} cerrado.", casoId);
            await AvisarAsync(casoId, asignacion?.SerenoId, actor);
            return new ResultadoCierre.Cerrado(cierre);
        }
    }

    private async Task<Dictionary<string, string[]>> ValidarAsync(DatosCierre datos, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        // DP07: solo categorías del catálogo.
        if (datos.CategoriaId is not { } categoria || !await db.CategoriasIncidencia.AnyAsync(c => c.Id == categoria, ct))
            errores["categoriaId"] = ["Elige la tipificación final del catálogo."];
        // 27/10/2026: cómo terminó la atención, del catálogo (base de la tasa de efectividad).
        if (!Catalogo.Contiene(ResultadosAtencion.Todos, datos.ResultadoAtencion))
            errores["resultadoAtencion"] = ["Elige cómo terminó la atención."];
        if (string.IsNullOrWhiteSpace(datos.ResultadoPublico))
            errores["resultadoPublico"] = ["Describe el resultado de la atención."];
        else if (datos.ResultadoPublico.Trim().Length > CierreCaso.MaximoResultado)
            errores["resultadoPublico"] = [$"El resultado no puede superar {CierreCaso.MaximoResultado} caracteres."];
        if (datos.NotasInternas?.Trim().Length > CierreCaso.MaximoNotas)
            errores["notasInternas"] = [$"Las notas internas no pueden superar {CierreCaso.MaximoNotas} caracteres."];
        return errores;
    }

    // Después del COMMIT: la central refleja el cierre y la nueva disponibilidad del sereno (CA07); si cerró un
    // operador, la app del sereno vuelve a consultar su despacho.
    private async Task AvisarAsync(Guid casoId, Guid? serenoId, ActorAvance actor)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            if (serenoId is { } sereno)
            {
                await notificador.SerenoActualizadoAsync(sereno, CancellationToken.None);
                if (actor == ActorAvance.Operador)
                    await notificador.DespachoActualizadoAsync(sereno, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar del cierre del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
