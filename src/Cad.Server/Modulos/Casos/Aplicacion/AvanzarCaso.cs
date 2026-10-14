using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public enum Avance
{
    // HU22: el sereno asignado confirma que se dirige al caso. Solo el sereno (CA04).
    ConfirmarDesplazamiento,
    // HU23: inicio de la atención. El sereno asignado o un operador (CA01, CA02).
    IniciarAtencion,
}

public enum ActorAvance { Sereno, Operador }

public abstract record ResultadoAvance
{
    public sealed record Registrado(EstadoCaso Estado) : ResultadoAvance;
    // La misma solicitud reenviada por el mismo responsable: no se duplica nada (HU22-CA05, HU23-CA06).
    public sealed record YaRegistrado(EstadoCaso Estado) : ResultadoAvance;
    public sealed record Rechazado(string Motivo) : ResultadoAvance;
    // El caso no existe o, para un sereno, no está asignado a él (HU22-CA03, HU23-CA05): no se distingue.
    public sealed record NoEncontrado : ResultadoAvance;
}

// HU22 y HU23 (S2-T10, S2-T11). El responsable, la fecha y la hora quedan en el historial del caso (evento de
// solo inserción). Un solo cambio aunque lleguen solicitudes a la vez: token xmin del caso y nueva evaluación.
public sealed class AvanzarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<AvanzarCaso> registro)
{
    public const string MotivoNoAsignada = "El caso no está «Asignada»: no se puede confirmar el desplazamiento.";
    public const string MotivoNoEnCamino = "El caso no está «En camino»: no se puede registrar el inicio de atención.";
    public const string MotivoSoloSereno = "Solo el sereno asignado puede confirmar el desplazamiento.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se registraba. Revisa su estado e inténtalo de nuevo.";

    private const int Intentos = 3;

    public static string MotivoYaRegistrado(Avance avance) => avance == Avance.ConfirmarDesplazamiento
        ? "El desplazamiento ya fue confirmado."
        : "El inicio de atención ya fue registrado por otro responsable.";

    public async Task<ResultadoAvance> EjecutarAsync(Guid casoId, Avance avance, Guid actorId, ActorAvance actor, CancellationToken ct)
    {
        // HU22-CA04: RF13 define la confirmación solo para el sereno.
        if (avance == Avance.ConfirmarDesplazamiento && actor != ActorAvance.Sereno)
            return new ResultadoAvance.Rechazado(MotivoSoloSereno);

        var tipoEvento = avance == Avance.ConfirmarDesplazamiento ? TipoEventoCaso.DesplazamientoConfirmado : TipoEventoCaso.AtencionIniciada;

        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
            var asignacion = await db.Asignaciones.AsNoTracking().SingleOrDefaultAsync(a => a.CasoId == casoId && a.Vigente, ct);
            if (caso is null || (actor == ActorAvance.Sereno && asignacion?.SerenoId != actorId))
                return new ResultadoAvance.NoEncontrado();

            // Primero lo ya registrado: un reenvío del mismo responsable no es un error.
            var previo = await db.EventosCaso.AsNoTracking()
                .Where(e => e.CasoId == casoId && e.Tipo == tipoEvento)
                .Select(e => (Guid?)e.ActorId)
                .FirstOrDefaultAsync(ct);
            if (previo is { } responsable)
                return responsable == actorId
                    ? new ResultadoAvance.YaRegistrado(caso.Estado)
                    : new ResultadoAvance.Rechazado(MotivoYaRegistrado(avance));

            var origen = avance == Avance.ConfirmarDesplazamiento ? EstadoCaso.Asignada : EstadoCaso.EnCamino;
            if (caso.Estado != origen)
                return new ResultadoAvance.Rechazado(avance == Avance.ConfirmarDesplazamiento ? MotivoNoAsignada : MotivoNoEnCamino);

            var ahora = reloj.GetUtcNow();
            if (avance == Avance.ConfirmarDesplazamiento)
                caso.ConfirmarDesplazamiento();
            else
                caso.IniciarAtencion();
            db.EventosCaso.Add(EventoCaso.Registrar(caso, tipoEvento, actorId, VisibilidadEvento.Interna, ahora));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoAvance.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Caso {CasoId}: {Avance} registrado.", casoId, avance);
            await AvisarAsync(casoId, asignacion?.SerenoId, actor);
            return new ResultadoAvance.Registrado(caso.Estado);
        }
    }

    // Después del COMMIT: la central refleja el estado (HU23-CA03) y, si lo registró un operador, la app del sereno
    // vuelve a consultar su despacho.
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
            registro.LogError(ex, "No se pudo avisar del cambio de estado del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
