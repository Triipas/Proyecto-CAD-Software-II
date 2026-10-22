using Cad.Calles;

namespace Cad.Movil.Servicios;

// Ruta del sereno hasta el caso, calculada en la propia app y sin internet (22/10/2026): la red de calles de la zona viaja dentro
// de la app (Resources/Raw/mapa/red-viaria.bin, preparada con herramientas/red-viaria desde OpenStreetMap) y se busca el
// camino más corto con A* (Cad.Calles).

public abstract record ResultadoRuta
{
    public sealed record Encontrada(IReadOnlyList<PuntoRuta> Puntos, double Metros) : ResultadoRuta;
    // Motivo legible: fuera de la zona con calles cargadas, sin camino…
    public sealed record NoDisponible(string Motivo) : ResultadoRuta;
}

public interface IServicioRutas
{
    Task<ResultadoRuta> CalcularAsync(PuntoRuta origen, PuntoRuta destino, CancellationToken ct);
}

public sealed class RutasNoDisponibles : IServicioRutas
{
    public Task<ResultadoRuta> CalcularAsync(PuntoRuta origen, PuntoRuta destino, CancellationToken ct) =>
        Task.FromResult<ResultadoRuta>(new ResultadoRuta.NoDisponible("El cálculo de rutas no está disponible."));
}

// Las calles son las que puede recorrer un vehículo del serenazgo (moto o camioneta), respetando los sentidos de circulación;
// si así no hay camino (red recortada en el borde de la zona), se repite sin sentidos.
public sealed class RutasLocales(Func<Task<Stream>> abrirRed) : IServicioRutas
{
    // Un punto a más de esta distancia de la calle más cercana está fuera de la zona con calles cargadas.
    public const double DistanciaMaximaACalleMetros = 300;
    public const string MensajeFueraDeZona = "El caso está fuera de la zona con calles cargadas en la app.";

    private Task<(GrafoViario ConSentidos, GrafoViario SinSentidos)>? grafos;

    public async Task<ResultadoRuta> CalcularAsync(PuntoRuta origen, PuntoRuta destino, CancellationToken ct)
    {
        (GrafoViario ConSentidos, GrafoViario SinSentidos) red;
        try
        {
            red = await (grafos ??= CargarAsync()).WaitAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            grafos = null; // se reintenta la próxima vez
            return new ResultadoRuta.NoDisponible("No se pudo cargar la red de calles de la app.");
        }
        return await Task.Run(() => Calcular(red.ConSentidos, red.SinSentidos, origen, destino), ct); // A* fuera del hilo de la interfaz
    }

    // La red se lee una sola vez, en segundo plano, la primera vez que hace falta.
    private Task<(GrafoViario, GrafoViario)> CargarAsync() => Task.Run(async () =>
    {
        await using var flujo = await abrirRed();
        using var copia = new MemoryStream();
        await flujo.CopyToAsync(copia); // el archivo de la app no siempre admite búsqueda
        copia.Position = 0;
        var red = RedViaria.Leer(copia);
        return (red.Grafo(respetarSentido: true), red.Grafo(respetarSentido: false));
    });

    public static ResultadoRuta Calcular(GrafoViario conSentidos, GrafoViario sinSentidos, PuntoRuta origen, PuntoRuta destino)
    {
        if (conSentidos.MasCercano(origen) is not { } a || conSentidos.MasCercano(destino) is not { } b
            || a.Metros > DistanciaMaximaACalleMetros || b.Metros > DistanciaMaximaACalleMetros)
            return new ResultadoRuta.NoDisponible(MensajeFueraDeZona);

        foreach (var grafo in new[] { conSentidos, sinSentidos })
        {
            if (grafo.CaminoMasCorto(a.Nodo, b.Nodo) is not { } camino)
                continue;
            // La ruta empieza en el sereno y termina en el punto del caso, no solo en las esquinas más cercanas.
            var puntos = new List<PuntoRuta>(camino.Puntos.Count + 2) { origen };
            puntos.AddRange(camino.Puntos);
            puntos.Add(destino);
            return new ResultadoRuta.Encontrada(puntos, camino.Metros + a.Metros + b.Metros);
        }
        return new ResultadoRuta.NoDisponible("No se encontró un camino por las calles de la zona.");
    }
}
