using Cad.Calles;

namespace Cad.Server.Modulos.Consultas;

// Recorrido por calles de cada sereno hasta su caso, para el mapa de la central (antes, una línea recta). Es la misma red de
// calles que viaja en la app (red-viaria.bin) y la misma búsqueda (Cad.Calles), así que la central ve la ruta que sigue el
// sereno. Sin ruta (fuera de la zona con calles cargadas), Puntos es nulo y el mapa vuelve a la línea recta.
public sealed record RutaEnCamino(Guid SerenoId, Guid CasoId, IReadOnlyList<PuntoRuta>? Puntos, double? Metros);

// La red se lee una sola vez, la primera vez que hace falta, y se comparte entre peticiones (solo lectura).
public sealed class RedCalles(ILogger<RedCalles> registro)
{
    public static readonly string Ruta = Path.Combine(AppContext.BaseDirectory, "Calles", "red-viaria.bin");

    private readonly Lazy<(GrafoViario ConSentidos, GrafoViario SinSentidos)?> grafos = new(() =>
    {
        try
        {
            using var flujo = File.OpenRead(Ruta);
            var red = RedViaria.Leer(flujo);
            return (red.Grafo(respetarSentido: true), red.Grafo(respetarSentido: false));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            registro.LogWarning(ex, "No se pudo cargar la red de calles; el mapa de la central mostrará líneas rectas.");
            return null;
        }
    });

    public ResultadoRuta Calcular(PuntoRuta origen, PuntoRuta destino) => grafos.Value is { } red
        ? BuscadorRutas.Calcular(red.ConSentidos, red.SinSentidos, origen, destino)
        : new ResultadoRuta.NoDisponible("La red de calles no está disponible.");
}

public sealed class ConsultarRutasEnCamino(ConsultarSerenos serenos, ConsultarCasosEnMapa casosEnMapa, RedCalles red)
{
    public async Task<IReadOnlyList<RutaEnCamino>> EjecutarAsync(CancellationToken ct)
    {
        var casos = (await casosEnMapa.EjecutarAsync(ct)).ToDictionary(c => c.Id);
        var rutas = new List<RutaEnCamino>();
        foreach (var sereno in await serenos.EjecutarAsync(ct))
        {
            if (sereno is not { CasoAsignadoId: { } casoId, Latitud: { } lat, Longitud: { } lon } || !casos.TryGetValue(casoId, out var caso))
                continue;
            rutas.Add(red.Calcular(new PuntoRuta(lat, lon), new PuntoRuta(caso.Latitud, caso.Longitud)) is ResultadoRuta.Encontrada e
                ? new RutaEnCamino(sereno.Id, casoId, e.Puntos, e.Metros)
                : new RutaEnCamino(sereno.Id, casoId, null, null));
        }
        return rutas;
    }
}
