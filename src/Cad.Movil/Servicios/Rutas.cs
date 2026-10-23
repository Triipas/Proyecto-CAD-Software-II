using Cad.Calles;

namespace Cad.Movil.Servicios;

// Ruta del sereno hasta el caso, calculada en la propia app y sin internet (22/10/2026): la red de calles de la zona viaja dentro
// de la app (Resources/Raw/mapa/red-viaria.bin, preparada con herramientas/red-viaria desde OpenStreetMap) y se busca el
// camino más corto con A* (Cad.Calles).

public interface IServicioRutas
{
    Task<ResultadoRuta> CalcularAsync(PuntoRuta origen, PuntoRuta destino, CancellationToken ct);
}

public sealed class RutasNoDisponibles : IServicioRutas
{
    public Task<ResultadoRuta> CalcularAsync(PuntoRuta origen, PuntoRuta destino, CancellationToken ct) =>
        Task.FromResult<ResultadoRuta>(new ResultadoRuta.NoDisponible("El cálculo de rutas no está disponible."));
}

// La búsqueda en sí (A* por las calles del serenazgo) está en Cad.Calles: la central dibuja la misma ruta.
public sealed class RutasLocales(Func<Task<Stream>> abrirRed) : IServicioRutas
{
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
        return await Task.Run(() => BuscadorRutas.Calcular(red.ConSentidos, red.SinSentidos, origen, destino), ct); // A* fuera del hilo de la interfaz
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
}
