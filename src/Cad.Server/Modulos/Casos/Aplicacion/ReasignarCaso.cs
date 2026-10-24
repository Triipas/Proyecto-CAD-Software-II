using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoReasignacion
{
    public sealed record Reasignado(Asignacion Asignacion) : ResultadoReasignacion;
    // La misma solicitud reenviada: la reasignación ya estaba registrada.
    public sealed record YaReasignado(Asignacion Asignacion) : ResultadoReasignacion;
    public sealed record Rechazado(string Motivo) : ResultadoReasignacion;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoReasignacion;
    public sealed record NoEncontrado : ResultadoReasignacion;
}

// Reasignar un caso a otro sereno desde la central (decisión del 24/10/2026): solo «Asignada» o «En camino» (en atención,
// no), a un sereno disponible y con un motivo obligatorio. En una transacción: la asignación anterior se cancela, el caso
// vuelve a «Asignada» con la nueva y se guardan la reasignación y un evento. El sereno anterior deja de ver el despacho; el
// nuevo lo recibe como cualquier asignación.
public sealed class ReasignarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador,
    Notificaciones.ColaAvisosDespacho avisosDespacho, ILogger<ReasignarCaso> registro)
{
    public const string MotivoNoReasignable = "Solo se reasigna un caso asignado o en camino: con el sereno ya en atención, no.";
    public const string MotivoMismoSereno = "El caso ya está asignado a ese sereno.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se reasignaba. Revisa su estado e inténtalo de nuevo.";

    private static readonly Guid RolSereno = Roles.Todos.Single(r => r.Nombre == Roles.Sereno).Id;

    public async Task<ResultadoReasignacion> EjecutarAsync(Guid casoId, Guid serenoNuevoId, string? motivo, Guid operadorId, Guid idSolicitud,
        CancellationToken ct)
    {
        if (await db.Asignaciones.AsNoTracking().SingleOrDefaultAsync(a => a.OperadorId == operadorId && a.IdSolicitud == idSolicitud, ct) is { } previa)
            return new ResultadoReasignacion.YaReasignado(previa);

        var errores = new Dictionary<string, string[]>();
        if (idSolicitud == Guid.Empty)
            errores["idSolicitud"] = ["Falta el identificador de la solicitud. Recarga la página e inténtalo de nuevo."];
        if (serenoNuevoId == Guid.Empty)
            errores["serenoId"] = ["Elige el sereno que atenderá el caso."];
        if (string.IsNullOrWhiteSpace(motivo))
            errores["motivo"] = ["Indica por qué se reasigna el caso."];
        else if (motivo.Trim().Length > Reasignacion.MaximoMotivo)
            errores["motivo"] = [$"El motivo admite hasta {Reasignacion.MaximoMotivo} caracteres."];
        if (errores.Count > 0)
            return new ResultadoReasignacion.Invalido(errores);

        var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
        if (caso is null)
            return new ResultadoReasignacion.NoEncontrado();
        if (!caso.PuedeReasignarse)
            return new ResultadoReasignacion.Rechazado(MotivoNoReasignable);
        var anterior = await db.Asignaciones.SingleOrDefaultAsync(a => a.CasoId == casoId && a.Vigente, ct);
        if (anterior is null)
            return new ResultadoReasignacion.Rechazado(MotivoNoReasignable);
        if (anterior.SerenoId == serenoNuevoId)
            return new ResultadoReasignacion.Rechazado(MotivoMismoSereno);

        // La misma regla de disponibilidad que una asignación (HU19-CA02): activo, operativo y sin asignación vigente.
        var disponible = await db.Users.AnyAsync(u => u.Id == serenoNuevoId && u.Activo
                && db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == RolSereno), ct)
            && await db.EstadosSereno.AnyAsync(e => e.SerenoId == serenoNuevoId && e.Operativo, ct)
            && !await db.Asignaciones.AnyAsync(a => a.SerenoId == serenoNuevoId && a.Vigente, ct);
        if (!disponible)
            return new ResultadoReasignacion.Rechazado(AsignarCaso.MotivoSerenoNoDisponible);

        var ahora = reloj.GetUtcNow();
        Asignacion nueva;
        // Dos pasos en una transacción: primero deja de estar vigente la anterior (una sola vigente por caso) y después la nueva.
        await using (var transaccion = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                anterior.Cancelar(ahora);
                caso.Reasignar();
                // De «Asignada» a «Asignada» el estado no cambia y EF no actualizaría el caso: se fuerza la actualización para
                // que compruebe su versión (xmin). Así una reasignación y un avance del sereno anterior no confirman a la vez.
                db.Entry(caso).Property(c => c.Estado).IsModified = true;
                await db.SaveChangesAsync(ct);
                nueva = Asignacion.Registrar(caso, serenoNuevoId, operadorId, idSolicitud, ahora);
                db.Asignaciones.Add(nueva);
                db.Reasignaciones.Add(Reasignacion.Registrar(caso, anterior.SerenoId, serenoNuevoId, operadorId, motivo!, ahora));
                db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoReasignado, operadorId, VisibilidadEvento.Interna, ahora));
                await db.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException u && u.EsViolacionDeUnicidad())
            {
                // Otro cambio confirmó antes (el sereno avanzó, el nuevo sereno recibió otra asignación…): no queda nada parcial.
                db.ChangeTracker.Clear();
                return new ResultadoReasignacion.Rechazado(MotivoCambioConcurrente);
            }
        }

        registro.LogInformation("Caso {CasoId} reasignado del sereno {Anterior} al sereno {Nuevo}.", casoId, anterior.SerenoId, serenoNuevoId);
        await AvisarAsync(casoId, anterior.SerenoId, serenoNuevoId);
        return new ResultadoReasignacion.Reasignado(nueva);
    }

    // Después del COMMIT: la central se actualiza, el sereno anterior deja de ver el despacho y el nuevo lo recibe (ADR-07).
    private async Task AvisarAsync(Guid casoId, Guid serenoAnterior, Guid serenoNuevo)
    {
        avisosDespacho.Encolar(serenoNuevo);
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            await notificador.SerenoActualizadoAsync(serenoAnterior, CancellationToken.None);
            await notificador.SerenoActualizadoAsync(serenoNuevo, CancellationToken.None);
            await notificador.DespachoActualizadoAsync(serenoAnterior, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar de la reasignación del caso {CasoId}; se verá al resincronizar.", casoId);
        }
    }
}
