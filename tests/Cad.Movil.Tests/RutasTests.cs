using System.Text.Json;
using Cad.Movil.Servicios;
using Cad.Calles;

namespace Cad.Movil.Tests;

// Ruta del sereno calculada en la app y sin internet: red de calles compacta (Cad.Calles) y A*. Calles ficticias, salvo la última
// prueba, que usa la red real que viaja en la app.
public class RutasTests
{
    // Cuadrícula de 3 × 3 esquinas separadas ~111 m (0,001°). Nodos: 1 2 3 (fila norte) / 4 5 6 / 7 8 9 (fila sur).
    private static readonly Dictionary<long, (double Lat, double Lon)> Esquinas = new()
    {
        [1] = (-12.000, -77.002), [2] = (-12.000, -77.001), [3] = (-12.000, -77.000),
        [4] = (-12.001, -77.002), [5] = (-12.001, -77.001), [6] = (-12.001, -77.000),
        [7] = (-12.002, -77.002), [8] = (-12.002, -77.001), [9] = (-12.002, -77.000),
    };

    // Red como la devolvería Overpass (vías con etiquetas y sus nodos), convertida al formato de la app.
    private static RedViaria Red(params (long[] Nodos, string? Oneway)[] vias)
    {
        var elementos = new List<object>();
        foreach (var (id, (lat, lon)) in Esquinas)
            elementos.Add(new { type = "node", id, lat, lon });
        var n = 100;
        foreach (var (nodos, oneway) in vias)
            elementos.Add(new
            {
                type = "way", id = n++, nodes = nodos,
                tags = oneway is null ? new Dictionary<string, string> { ["highway"] = "residential" }
                    : new Dictionary<string, string> { ["highway"] = "residential", ["oneway"] = oneway },
            });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { elements = elementos }));
        return RedViaria.DesdeOverpass(json);
    }

    private static PuntoRuta P(long esquina) => new(Esquinas[esquina].Lat, Esquinas[esquina].Lon);

    private static ResultadoRuta Calcular(RedViaria red, PuntoRuta origen, PuntoRuta destino) =>
        BuscadorRutas.Calcular(red.Grafo(true), red.Grafo(false), origen, destino);

    [Fact]
    public void AEstrella_EncuentraElCaminoMasCorto()
    {
        // El borde de la cuadrícula y la calle central 2-5-8. De 1 a 9 hay varios caminos; el más corto mide 4 tramos.
        var red = Red(([1, 2, 3], null), ([3, 6, 9], null), ([1, 4, 7], null), ([7, 8, 9], null), ([2, 5, 8], null));

        var ruta = Assert.IsType<ResultadoRuta.Encontrada>(Calcular(red, P(1), P(9)));

        Assert.InRange(ruta.Metros, 4 * 105, 4 * 115); // 4 tramos de ~109-111 m
        Assert.Equal(P(1), ruta.Puntos[0]);
        Assert.Equal(P(9), ruta.Puntos[^1]);
    }

    [Fact]
    public void AEstrella_RespetaLosSentidosDeCirculacion()
    {
        // La calle norte 1→2→3 es de un solo sentido. De 3 a 1 hay que dar la vuelta por el sur.
        var red = Red(([1, 2, 3], "yes"), ([3, 6, 9], null), ([1, 4, 7], null), ([7, 8, 9], null));

        var ida = Assert.IsType<ResultadoRuta.Encontrada>(Calcular(red, P(1), P(3)));
        var vuelta = Assert.IsType<ResultadoRuta.Encontrada>(Calcular(red, P(3), P(1)));

        Assert.True(vuelta.Metros > ida.Metros * 2.5); // 6 tramos frente a 2
    }

    [Fact]
    public void Oneway_MenosUno_EsEnSentidoContrario()
    {
        var grafo = Red(([1, 2], "-1")).Grafo();

        Assert.Null(grafo.CaminoMasCorto(0, 1));
        Assert.NotNull(grafo.CaminoMasCorto(1, 0));
    }

    [Fact]
    public void LaRuta_EmpiezaEnElSereno_TerminaEnElCaso_YSumaLosTramosHastaLaCalle()
    {
        var red = Red(([1, 2, 3], null));
        var sereno = new PuntoRuta(-12.0001, -77.0021); // a ~15 m de la esquina 1
        var caso = new PuntoRuta(-11.9999, -76.9999); // a ~15 m de la esquina 3

        var ruta = Assert.IsType<ResultadoRuta.Encontrada>(Calcular(red, sereno, caso));

        Assert.Equal(sereno, ruta.Puntos[0]);
        Assert.Equal(caso, ruta.Puntos[^1]);
        Assert.InRange(ruta.Metros, 2 * 109 + 20, 2 * 109 + 40);
    }

    [Fact]
    public void SiLosSentidosLoImpiden_SeRepiteSinSentidos()
    {
        // Única calle, de un solo sentido 3→2→1: de 1 a 3 no hay camino respetándolo (red recortada en el borde).
        var red = Red(([3, 2, 1], "yes"));

        Assert.IsType<ResultadoRuta.Encontrada>(Calcular(red, P(1), P(3)));
    }

    [Fact]
    public void FueraDeLaZona_LoDice()
    {
        var red = Red(([1, 2, 3], null));

        var resultado = Calcular(red, P(1), new PuntoRuta(-12.05, -77.0)); // a ~5 km de cualquier calle cargada

        Assert.Equal(BuscadorRutas.MensajeFueraDeZona, Assert.IsType<ResultadoRuta.NoDisponible>(resultado).Motivo);
    }

    [Fact]
    public void ElFormatoBinario_ConservaLaRed()
    {
        var red = Red(([1, 2, 3], "yes"), ([3, 6, 9], null));
        using var flujo = new MemoryStream();

        red.Escribir(flujo);
        flujo.Position = 0;
        var leida = RedViaria.Leer(flujo);

        Assert.Equal(red.Nodos, leida.Nodos);
        Assert.Equal(red.Vias.Select(v => (v.Sentido, string.Join(",", v.Nodos))), leida.Vias.Select(v => (v.Sentido, string.Join(",", v.Nodos))));
        Assert.Throws<InvalidDataException>(() => RedViaria.Leer(new MemoryStream("no es una red"u8.ToArray())));
    }

    [Fact]
    public async Task ConLaRedRealDeLaApp_SeEncuentraLaRutaEnArequipa()
    {
        // La red que viaja en la app (ciudad de Arequipa). De ~400 m al sur de la Plaza de Armas a la plaza.
        var archivo = BuscarEnRepositorio(Path.Combine("src", "Cad.Movil", "Resources", "Raw", "mapa", "red-viaria.bin"));
        var rutas = new RutasLocales(() => Task.FromResult<Stream>(File.OpenRead(archivo)));

        var resultado = await rutas.CalcularAsync(new PuntoRuta(-16.4024, -71.5369), new PuntoRuta(-16.3988, -71.5369), CancellationToken.None);

        var ruta = Assert.IsType<ResultadoRuta.Encontrada>(resultado);
        Assert.InRange(ruta.Metros, 400, 1500); // ~400 m en línea recta; por calles, algo más
        Assert.True(ruta.Puntos.Count > 2);
    }

    private static string BuscarEnRepositorio(string relativa)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relativa)))
                return Path.Combine(dir.FullName, relativa);
        throw new FileNotFoundException(relativa);
    }
}
