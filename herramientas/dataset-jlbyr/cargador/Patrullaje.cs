using System.Text.Json;

namespace Cad.CargadorDataset;

// Lo que el dataset no guarda del sereno y la analítica necesita (README del dataset, «Para cuando se cargue»): cuándo estuvo
// operativo y dónde estaba. Cada turno trabajado lo pone operativo al empezar y fuera de servicio al terminar; durante el turno
// envía una posición cada MinutosEntrePosiciones. Patrulla su sector por las vías del distrito y, mientras atiende un caso, va
// hacia el punto del caso y se queda ahí hasta cerrarlo.
public sealed class Patrullaje
{
    public const int MinutosEntrePosiciones = 5;
    private const double MetrosPorPaso = 350;
    private static readonly TimeSpan Lima = TimeSpan.FromHours(-5);
    private static readonly Dictionary<string, (int Inicio, int Fin)> Turnos = new()
    {
        ["Mañana"] = (6, 14), ["Tarde"] = (14, 22), ["Noche"] = (22, 30),
    };

    private readonly Dictionary<int, List<(double Lat, double Lon)>> puntosPorSector;
    private readonly Random azar = new(20261029);

    public Patrullaje(Dataset datos, string carpetaDataset)
    {
        // El mismo centro y los mismos cuadrantes que usa generar.py para asignar el sector de cada caso.
        using var limite = JsonDocument.Parse(File.ReadAllText(Path.Combine(carpetaDataset, "fuentes/limite-jlbyr.geojson")));
        var anillo = limite.RootElement.GetProperty("coordinates")[0].EnumerateArray()
            .Select(p => (Lat: p[1].GetDouble(), Lon: p[0].GetDouble())).ToList();
        var centro = (Lat: anillo.Average(p => p.Lat), Lon: anillo.Average(p => p.Lon));
        puntosPorSector = datos.Vias.SelectMany(v => v.Puntos)
            .Select(p => (Lat: p[0], Lon: p[1]))
            .Where(p => Dentro(anillo, p.Lat, p.Lon))
            .GroupBy(p => Sector(centro, p.Lat, p.Lon))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public static (DateTimeOffset Inicio, DateTimeOffset Fin) Horario(TurnoDataset turno)
    {
        var (inicio, fin) = Turnos[turno.Turno];
        var dia = new DateTimeOffset(turno.Dia.ToDateTime(TimeOnly.MinValue), Lima);
        return (dia.AddHours(inicio), dia.AddHours(fin));
    }

    // Posiciones de un sereno en sus turnos (horas ya desplazadas), hasta el fin del periodo. Los tramos son sus casos: desde que
    // se le asigna hasta que llega (va hacia el punto) y desde que llega hasta que termina (se queda en el punto).
    public IEnumerable<(DateTimeOffset En, double Lat, double Lon, double Precision)> Posiciones(
        IEnumerable<(DateTimeOffset Inicio, DateTimeOffset Fin, int Sector)> turnos, IReadOnlyList<Tramo> tramos, DateTimeOffset fin)
    {
        foreach (var turno in turnos.OrderBy(t => t.Inicio))
        {
            if (turno.Inicio >= fin)
                yield break;
            var puntos = puntosPorSector[turno.Sector];
            var actual = puntos[azar.Next(puntos.Count)];
            var destino = puntos[azar.Next(puntos.Count)];
            (double Lat, double Lon)? salida = null;
            for (var t = turno.Inicio.AddSeconds(azar.Next(5, 60)); t < turno.Fin && t <= fin;
                 t = t.AddMinutes(MinutosEntrePosiciones).AddSeconds(azar.Next(-20, 21)))
            {
                var tramo = tramos.FirstOrDefault(x => x.Desde <= t && t < x.Hasta);
                if (tramo is { Lat: { } lat, Lon: { } lon })
                {
                    salida ??= actual;
                    var avance = tramo.Llegada is { } llegada && t < llegada
                        ? (t - tramo.Desde) / (llegada - tramo.Desde)
                        : 1;
                    actual = (salida.Value.Lat + (lat - salida.Value.Lat) * avance, salida.Value.Lon + (lon - salida.Value.Lon) * avance);
                    yield return (t, actual.Lat + Ruido(8), actual.Lon + Ruido(8), azar.Next(5, 19));
                    continue;
                }
                salida = null;
                actual = Avanzar(actual, destino, MetrosPorPaso * (0.6 + azar.NextDouble() * 0.8));
                if (Metros(actual, destino) < 30)
                    destino = puntos[azar.Next(puntos.Count)];
                yield return (t, actual.Lat + Ruido(6), actual.Lon + Ruido(6), azar.Next(4, 21));
            }
        }
    }

    private double Ruido(double metros) => (azar.NextDouble() - 0.5) * 2 * metros / 111_320;

    private static (double Lat, double Lon) Avanzar((double Lat, double Lon) desde, (double Lat, double Lon) hacia, double metros)
    {
        var distancia = Metros(desde, hacia);
        if (distancia <= metros)
            return hacia;
        var f = metros / distancia;
        return (desde.Lat + (hacia.Lat - desde.Lat) * f, desde.Lon + (hacia.Lon - desde.Lon) * f);
    }

    public static double Metros((double Lat, double Lon) a, (double Lat, double Lon) b)
    {
        var dLat = (a.Lat - b.Lat) * 111_320;
        var dLon = (a.Lon - b.Lon) * 111_320 * Math.Cos(a.Lat * Math.PI / 180);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    // Un punto a cierta distancia y rumbo de otro (aproximación plana, suficiente para unos cientos de metros).
    public static (double Lat, double Lon) Desplazar((double Lat, double Lon) punto, double metros, double rumboRadianes) =>
        (punto.Lat + metros * Math.Cos(rumboRadianes) / 111_320,
         punto.Lon + metros * Math.Sin(rumboRadianes) / (111_320 * Math.Cos(punto.Lat * Math.PI / 180)));

    private static int Sector((double Lat, double Lon) centro, double lat, double lon)
    {
        var angulo = Math.Atan2(lat - centro.Lat, lon - centro.Lon) * 180 / Math.PI;
        return angulo is >= 45 and < 135 ? 1 : angulo is >= -45 and < 45 ? 2 : angulo is >= -135 and < -45 ? 3 : 4;
    }

    private static bool Dentro(List<(double Lat, double Lon)> anillo, double lat, double lon)
    {
        var dentro = false;
        for (int i = 0, j = anillo.Count - 1; i < anillo.Count; j = i++)
        {
            var (a, b) = (anillo[i], anillo[j]);
            if ((a.Lat > lat) != (b.Lat > lat) && lon < (b.Lon - a.Lon) * (lat - a.Lat) / (b.Lat - a.Lat) + a.Lon)
                dentro = !dentro;
        }
        return dentro;
    }
}

// Un caso del sereno: desde que se le asigna hasta que lo cierra (o se le quita). Sin punto, sigue patrullando.
public sealed record Tramo(DateTimeOffset Desde, DateTimeOffset? Llegada, DateTimeOffset Hasta, double? Lat, double? Lon);
