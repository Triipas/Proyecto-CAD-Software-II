using Cad.Calles;
using Cad.Server.Modulos.Consultas;

namespace Cad.Server.Panel.Pages;

// Bloque de asignación del detalle de un SOS o de un reporte (_AsignarCaso, 29/10/2026): los serenos disponibles ordenados
// por cercanía al caso y, si hay ubicación de ambos lados, el más cercano como sugerencia.
public sealed record SerenoParaAsignar(Guid Id, string Nombre, string Detalle, double? Metros)
{
    public string Distancia => Metros is not { } m ? "" : m < 1000 ? $"{Math.Round(m):0} m" : $"{m / 1000:0.0} km".Replace('.', ',');
}

public sealed record VistaAsignacion(Guid IdSolicitud, string Lugar, IReadOnlyList<SerenoParaAsignar> Disponibles, string? Volver = null)
{
    // «el lugar del hecho» / «el SOS»: de qué punto se mide la cercanía.
    public SerenoParaAsignar? Sugerido => Disponibles.FirstOrDefault(s => s.Metros is not null);
    public IReadOnlyList<SerenoParaAsignar> Otros => Sugerido is { } sugerido ? Disponibles.Where(s => s.Id != sugerido.Id).ToList() : Disponibles;

    public static VistaAsignacion Crear(Guid idSolicitud, string lugar, double? latitud, double? longitud,
        IEnumerable<SerenoMonitoreo> disponibles, TimeZoneInfo zona, string? volver = null)
    {
        var lista = disponibles
            .Select(s => new SerenoParaAsignar(s.Id, s.Nombre,
                s.UltimaPosicionEn is { } h ? $"última ubicación a las {TimeZoneInfo.ConvertTime(h, zona):HH:mm:ss}" : "ubicación no disponible",
                latitud is { } la && longitud is { } lo && s.Latitud is { } sla && s.Longitud is { } slo ? Geo.DistanciaMetros(la, lo, sla, slo) : null))
            .OrderBy(s => s.Metros ?? double.MaxValue).ThenBy(s => s.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new VistaAsignacion(idSolicitud, lugar, lista, volver);
    }
}
