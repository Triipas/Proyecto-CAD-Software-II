using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU25 — un caso en curso con ubicación válida, para el mapa operativo. La ubicación de un SOS es la del dispositivo (HU08); la
// de un reporte, el punto del lugar del hecho (HU10). Una referencia textual no genera marcador (DP10) y un caso sin
// ubicación no tiene coordenadas inventadas (CA04): sigue en el listado de «Casos» (HU26).
public sealed record CasoEnMapa(Guid Id, OrigenCaso Origen, EstadoCaso Estado, double Latitud, double Longitud, string Ciudadano)
{
    public string TextoOrigen => Origen == OrigenCaso.Sos ? "SOS" : "Reporte";
    public string TextoEstado => EstadosCaso.Texto(Estado);
    public string Detalle => Origen == OrigenCaso.Sos ? $"/panel/central/sos/{Id}" : $"/panel/reportes/{Id}";
}

public sealed class ConsultarCasosEnMapa(CadDbContext db)
{
    // Interpretación de HU25 (Sprint 4): el mapa operativo muestra los casos en curso. Los pendientes de asignación ya están en
    // las listas de la central (SOS pendientes y reportes admitidos); aquí van los demás estados no finales. Los cerrados,
    // derivados y descartados se consultan en «Casos» (HU26) y no ocupan el mapa (HU17-CA04 excluye además los descartados).
    public static readonly EstadoCaso[] EstadosEnCurso =
        [EstadoCaso.Recibido, EstadoCaso.EnRevision, EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion];

    public async Task<IReadOnlyList<CasoEnMapa>> EjecutarAsync(CancellationToken ct)
    {
        var sos = await (from caso in db.Casos.AsNoTracking()
                         join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                         where caso.Origen == OrigenCaso.Sos && EstadosEnCurso.Contains(caso.Estado)
                             && caso.Latitud != null && caso.Longitud != null
                         select new CasoEnMapa(caso.Id, caso.Origen, caso.Estado, caso.Latitud!.Value, caso.Longitud!.Value,
                             ciudadano.Nombres + " " + ciudadano.Apellidos))
                        .ToListAsync(ct);
        var reportes = await (from caso in db.Casos.AsNoTracking()
                              join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                              from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                              where caso.Origen == OrigenCaso.Reporte && EstadosEnCurso.Contains(caso.Estado)
                                  && detalle.LatitudHecho != null && detalle.LongitudHecho != null
                              select new CasoEnMapa(caso.Id, caso.Origen, caso.Estado, detalle.LatitudHecho!.Value, detalle.LongitudHecho!.Value,
                                  ciudadano == null ? (detalle.ContactoNombre == null ? AutorReporte.DeLaCentral : detalle.ContactoNombre + AutorReporte.SufijoContacto) : ciudadano.Nombres + " " + ciudadano.Apellidos))
                             .ToListAsync(ct);
        return [.. sos, .. reportes];
    }
}
