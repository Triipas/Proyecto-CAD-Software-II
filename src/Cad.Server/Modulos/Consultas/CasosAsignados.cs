using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// Cola de trabajo de la central (29/10/2026): los casos que ya tienen sereno (asignados, en camino o en atención) siguen a
// la vista junto a los pendientes, para seguirlos y ver el recorrido de su sereno al elegirlos.
public sealed record CasoAsignado(Guid Id, OrigenCaso Origen, EstadoCaso Estado, string Ciudadano, Guid SerenoId, string Sereno,
    DateTimeOffset AsignadaEn, double? Latitud, double? Longitud)
{
    public bool EsReporte => Origen == OrigenCaso.Reporte;
    public string TextoOrigen => Origen switch { OrigenCaso.Sos => "SOS", OrigenCaso.Reporte => "Reporte", _ => "Caso urgente" };
    public string TextoEstado => EstadosCaso.Texto(Origen, Estado);
    public string Detalle => EsReporte ? $"/panel/reportes/{Id}" : $"/panel/central/sos/{Id}";
    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;
}

public sealed class ConsultarCasosAsignados(CadDbContext db)
{
    public static readonly EstadoCaso[] Estados = [EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion];

    // Los más recientemente asignados primero, igual que la cola de pendientes.
    public async Task<IReadOnlyList<CasoAsignado>> EjecutarAsync(CancellationToken ct)
    {
        var sos = await (from caso in db.Casos.AsNoTracking()
                         join asignacion in db.Asignaciones.AsNoTracking() on caso.Id equals asignacion.CasoId
                         join sereno in db.Users.AsNoTracking() on asignacion.SerenoId equals sereno.Id
                         join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                         where caso.Origen != OrigenCaso.Reporte && asignacion.Vigente && Estados.Contains(caso.Estado)
                         select new CasoAsignado(caso.Id, caso.Origen, caso.Estado, ciudadano.Nombres + " " + ciudadano.Apellidos,
                             sereno.Id, sereno.Nombres + " " + sereno.Apellidos, asignacion.AsignadaEn, caso.Latitud, caso.Longitud))
                        .ToListAsync(ct);
        var reportes = await (from caso in db.Casos.AsNoTracking()
                              join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                              join asignacion in db.Asignaciones.AsNoTracking() on caso.Id equals asignacion.CasoId
                              join sereno in db.Users.AsNoTracking() on asignacion.SerenoId equals sereno.Id
                              from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                              where caso.Origen == OrigenCaso.Reporte && asignacion.Vigente && Estados.Contains(caso.Estado)
                              select new CasoAsignado(caso.Id, caso.Origen, caso.Estado,
                                  ciudadano == null ? (detalle.ContactoNombre == null ? AutorReporte.DeLaCentral : detalle.ContactoNombre + AutorReporte.SufijoContacto)
                                      : ciudadano.Nombres + " " + ciudadano.Apellidos,
                                  sereno.Id, sereno.Nombres + " " + sereno.Apellidos, asignacion.AsignadaEn, detalle.LatitudHecho, detalle.LongitudHecho))
                             .ToListAsync(ct);
        return [.. sos.Concat(reportes).OrderByDescending(c => c.AsignadaEn)];
    }
}
