using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoAsignacion
{
    public sealed record Asignado(Asignacion Asignacion) : ResultadoAsignacion;
    // La misma solicitud reenviada: se devuelve la asignación ya registrada (CA08).
    public sealed record YaAsignado(Asignacion Asignacion) : ResultadoAsignacion;
    // Rechazos sin modificar datos ni dejar registros parciales (CA02, CA03, CA04, CA05, CA08).
    public sealed record Rechazado(string Motivo) : ResultadoAsignacion;
    public sealed record CasoNoEncontrado : ResultadoAsignacion;
    public sealed record Invalido(string Motivo) : ResultadoAsignacion;
}

// HU20 — Asignar un caso pendiente a un sereno disponible (S2-T05, S2-T06). Unificada (A+B) por el PO el
// 13/10/2026: vale para cualquier caso «Pendiente», sin distinguir el origen; CA07 (reporte admitido) se
// verifica cuando existan reportes (HU15, Sprint 3). La autorización (solo operador, CA09) la aplica la página.
public sealed class AsignarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador,
    Notificaciones.ColaAvisosDespacho avisosDespacho, ILogger<AsignarCaso> registro)
{
    public const string MotivoNoPendiente = "El caso ya no está pendiente: no se puede asignar.";
    public const string MotivoCancelado = "El ciudadano canceló este SOS: no se puede asignar.";
    public const string MotivoSerenoNoDisponible = "El sereno no está disponible (no está operativo o ya tiene una asignación vigente).";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se asignaba. Revisa su estado e inténtalo de nuevo.";

    private static readonly Guid RolSereno = Roles.Todos.Single(r => r.Nombre == Roles.Sereno).Id;

    // El token xmin cambia con cualquier modificación del caso (también la clasificación de HU13). Ante un
    // conflicto se vuelve a evaluar con los datos nuevos: solo se rechaza si el caso ya no está pendiente.
    private const int Intentos = 3;

    public async Task<ResultadoAsignacion> EjecutarAsync(Guid casoId, Guid serenoId, Guid operadorId, Guid idSolicitud, CancellationToken ct)
    {
        if (idSolicitud == Guid.Empty)
            return new ResultadoAsignacion.Invalido("Falta el identificador de la solicitud. Recarga la página e inténtalo de nuevo.");
        if (serenoId == Guid.Empty)
            return new ResultadoAsignacion.Invalido("Elige un sereno disponible.");

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await IntentarAsync(casoId, serenoId, operadorId, idSolicitud, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return await RechazoTrasCarreraAsync(operadorId, idSolicitud, MotivoCambioConcurrente, ct);
            }
        }
    }

    private async Task<ResultadoAsignacion> IntentarAsync(Guid casoId, Guid serenoId, Guid operadorId, Guid idSolicitud, CancellationToken ct)
    {
        // Primero la idempotencia: si esta solicitud ya se registró, el caso ya no estará pendiente.
        if (await BuscarAsync(operadorId, idSolicitud, ct) is { } existente)
            return new ResultadoAsignacion.YaAsignado(existente);

        var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
        if (caso is null)
            return new ResultadoAsignacion.CasoNoEncontrado();
        // Antes de rechazar se vuelve a comprobar la solicitud: la primera petición de un reenvío pudo confirmar
        // justo después de la comprobación inicial (CA08).
        if (!caso.PuedeAsignarse)
            return await RechazoTrasCarreraAsync(operadorId, idSolicitud,
                caso.Estado == EstadoCaso.Cancelada ? MotivoCancelado : MotivoNoPendiente, ct); // CA02; HU09: cancelado por su titular

        // CA03 — regla de disponibilidad del MVP (HU19-CA02): sereno activo, operativo y sin asignación vigente.
        var disponible = await db.Users.AnyAsync(u => u.Id == serenoId && u.Activo
                && db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == RolSereno), ct)
            && await db.EstadosSereno.AnyAsync(e => e.SerenoId == serenoId && e.Operativo, ct)
            && !await db.Asignaciones.AnyAsync(a => a.SerenoId == serenoId && a.Vigente, ct);
        if (!disponible)
            return await RechazoTrasCarreraAsync(operadorId, idSolicitud, MotivoSerenoNoDisponible, ct);

        var ahora = reloj.GetUtcNow();
        caso.Asignar();
        var asignacion = Asignacion.Registrar(caso, serenoId, operadorId, idSolicitud, ahora);
        db.Asignaciones.Add(asignacion);
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoAsignado, operadorId, VisibilidadEvento.Interna, ahora));

        // Caso, asignación y evento en una sola transacción. Las carreras las resuelve la base de datos:
        // el token xmin del caso (CA04; el reintento lo vuelve a leer ya asignado) y los índices únicos
        // parciales de asignaciones vigentes (CA04, CA05).
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
            var motivo = await db.Asignaciones.AnyAsync(a => a.CasoId == casoId && a.Vigente, ct) ? MotivoNoPendiente : MotivoSerenoNoDisponible;
            return await RechazoTrasCarreraAsync(operadorId, idSolicitud, motivo, ct);
        }

        registro.LogInformation("Caso {CasoId} asignado al sereno {SerenoId}.", casoId, serenoId);
        await AvisarAsync(casoId, serenoId);
        return new ResultadoAsignacion.Asignado(asignacion);
    }

    // Si la carrera fue contra el reenvío de esta misma solicitud, se devuelve la asignación ya registrada.
    private async Task<ResultadoAsignacion> RechazoTrasCarreraAsync(Guid operadorId, Guid idSolicitud, string motivo, CancellationToken ct) =>
        await BuscarAsync(operadorId, idSolicitud, ct) is { } existente
            ? new ResultadoAsignacion.YaAsignado(existente)
            : new ResultadoAsignacion.Rechazado(motivo);

    private Task<Asignacion?> BuscarAsync(Guid operadorId, Guid idSolicitud, CancellationToken ct) =>
        db.Asignaciones.AsNoTracking().SingleOrDefaultAsync(a => a.OperadorId == operadorId && a.IdSolicitud == idSolicitud, ct);

    // Avisos después del COMMIT: la central retira el caso de «Pendientes» y el sereno deja de estar disponible.
    // HU21-CA01: el aviso al sereno asignado (tiempo real y push, ADR-07) se envía en segundo plano.
    private async Task AvisarAsync(Guid casoId, Guid serenoId)
    {
        avisosDespacho.Encolar(serenoId);
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            await notificador.SerenoActualizadoAsync(serenoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central de la asignación del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
