using System.Globalization;
using System.Text.Json;
using Cad.Calles;
using Cad.Server.Modulos.Consultas;

namespace Cad.Server.Panel.Pages;

// Serenos en el minimapa del detalle de un SOS o de un reporte (_SerenosMinimapa, 05/11/2026): los operativos con ubicación y
// el que tiene asignado el caso, con su distancia en línea recta al caso y, para el asignado, su recorrido por calles (el mismo
// que ve la central). mapa-hecho.js los dibuja y traza la línea al sereno elegido para asignar.
public sealed record SerenoEnMinimapa(Guid Id, string Nombre, bool Disponible, bool Asignado, double Latitud, double Longitud,
    string Distancia, string Hora);

public sealed record VistaMinimapa(IReadOnlyList<SerenoEnMinimapa> Serenos, string? Ruta)
{
    public static VistaMinimapa Crear(Guid casoId, double? latitud, double? longitud, IReadOnlyList<SerenoMonitoreo> serenos,
        ConsultarRutasEnCamino rutas, TimeZoneInfo zona)
    {
        if (latitud is not { } la || longitud is not { } lo)
            return new VistaMinimapa([], null);
        var lista = serenos
            .Where(s => s.Latitud is not null && s.Longitud is not null && (s.Operativo || s.CasoAsignadoId == casoId))
            .Select(s => new SerenoEnMinimapa(s.Id, s.Nombre, s.Disponible, s.CasoAsignadoId == casoId, s.Latitud!.Value, s.Longitud!.Value,
                SerenoParaAsignar.Texto(Geo.DistanciaMetros(la, lo, s.Latitud.Value, s.Longitud.Value)),
                s.UltimaPosicionEn is { } h ? TimeZoneInfo.ConvertTime(h, zona).ToString("HH:mm:ss", CultureInfo.InvariantCulture) : ""))
            .ToList();
        // El recorrido del asignado, como [[lat, lon], …]; sin ruta por calles, el minimapa une ambos puntos con una línea punteada.
        var ruta = lista.Any(s => s.Asignado) && rutas.DelCaso(casoId, la, lo, serenos) is { Puntos: { } puntos }
            ? JsonSerializer.Serialize(puntos.Select(p => new[] { Math.Round(p.Latitud, 6), Math.Round(p.Longitud, 6) }))
            : null;
        return new VistaMinimapa(lista, ruta);
    }
}
