using Cad.Contratos.Casos;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU31 — la bandeja del ciudadano (S3-T14, S3-T15). Solo las notificaciones del ciudadano que consulta (CA04); se
// generan al guardar el cambio de estado público (CadDbContext) y persisten hasta que las consulte (CA02).
public sealed class BandejaCiudadano(CadDbContext db, TimeProvider reloj)
{
    // Límite técnico de la respuesta: las más recientes. El total sin leer se cuenta aparte.
    public const int Maximo = 100;

    public async Task<BandejaNotificaciones> ConsultarAsync(Guid ciudadanoId, CancellationToken ct)
    {
        var propias = db.NotificacionesCiudadano.AsNoTracking().Where(n => n.CiudadanoId == ciudadanoId);
        var noLeidas = await propias.CountAsync(n => n.LeidaEn == null, ct);
        var lista = await propias
            .OrderByDescending(n => n.CreadaEn).ThenByDescending(n => n.Id)
            .Take(Maximo)
            .Select(n => new NotificacionCiudadanoVista(n.Id, n.CasoId, n.Texto, n.CreadaEn, n.LeidaEn != null))
            .ToListAsync(ct);
        return new BandejaNotificaciones(noLeidas, lista);
    }

    // false si no existe o no es del ciudadano (CA04): no se distingue.
    public async Task<bool> MarcarLeidaAsync(Guid ciudadanoId, Guid notificacionId, CancellationToken ct)
    {
        var notificacion = await db.NotificacionesCiudadano.SingleOrDefaultAsync(n => n.Id == notificacionId && n.CiudadanoId == ciudadanoId, ct);
        if (notificacion is null)
            return false;
        notificacion.MarcarLeida(reloj.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
