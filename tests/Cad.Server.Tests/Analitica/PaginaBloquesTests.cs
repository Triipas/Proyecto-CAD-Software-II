using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// «Comparación por bloques» (10/11/2026): los casos van al bloque de su zona, una zona con pocos casos no se muestra sola, lo que
// queda fuera de los bloques no entra y la evolución usa los meses completos. Fechas propias de 2005. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public partial class PaginaBloquesTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.RoboOHurto.Id;
    private const double LatK16 = -16.4275, LonK16 = -71.5375; // zona K16: bloque Avelino Cáceres
    private const double LatO20 = -16.4475, LonO20 = -71.5175; // zona O20: bloque Simón Bolívar

    private static DateTimeOffset Local(int mes, int dia, int hora) => new DateTimeOffset(2005, mes, dia, hora, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
    private static Caso Sos(Guid ciudadano, DateTimeOffset en, double lat, double lon) =>
        Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(lat, lon, 5, en, en), en);

    private async Task<(HttpClient Navegador, Guid Operador)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta.Id);
    }

    [GeneratedRegex("<script type=\"application/json\" data-datos-bloques>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex Datos();

    [Fact]
    public async Task CasosEnLasZonas_VanASuBloque_ConLaZonaChicaReunidaYLoDeFueraAparte()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var (navegador, operador) = await OperadorAsync();
        var k16 = Enumerable.Range(0, 4).Select(i => Sos(ciudadano, Local(3, 2 + i, 10), LatK16, LonK16)).ToList();
        foreach (var caso in k16)
            caso.Clasificar(Robo, PrioridadesCaso.Media, PersonasHeridas.No, operador, caso.RegistradoEn);
        var o20 = Sos(ciudadano, Local(3, 10, 10), LatO20, LonO20); // sin clasificar: cuenta en el total, no en un eje
        var fuera = Sos(ciudadano, Local(3, 11, 10), -12.0525, -77.0425);
        await fabrica.ConBaseDatosAsync(async db => { db.Casos.AddRange([.. k16, o20, fuera]); return await db.SaveChangesAsync(); });

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/bloques?periodo=rango&desde=2005-03-01&hasta=2005-03-31"));

        Assert.Contains("data-respuesta=\"bloques\"", html);
        Assert.Contains("El bloque Avelino Cáceres reúne <em>4 casos de robo o hurto</em> en el periodo del 1/03 al 31/03.", html);
        using var datos = JsonDocument.Parse(Datos().Match(html).Groups[1].Value);
        var raiz = datos.RootElement;
        Assert.Equal(1, raiz.GetProperty("fuera").GetInt32());
        var bloques = raiz.GetProperty("bloques").EnumerateArray().ToDictionary(b => b.GetProperty("clave").GetString()!);
        var avelino = bloques["avelino"];
        Assert.Equal(4, avelino.GetProperty("casos")[0].GetInt32()); // eje «robo»
        Assert.Equal(4, avelino.GetProperty("casos")[12].GetInt32()); // total
        Assert.Equal("K16", Assert.Single(avelino.GetProperty("zonas").EnumerateArray()).GetProperty("nombre").GetString());
        // Marzo terminó: es el último de los 12 meses de la evolución.
        Assert.Equal(4, avelino.GetProperty("meses")[11][12].GetInt32());
        Assert.Equal(3, raiz.GetProperty("meses")[11].GetProperty("mes").GetInt32());
        var simon = bloques["simon"];
        Assert.Equal(1, simon.GetProperty("casos")[12].GetInt32());
        Assert.Equal(0, simon.GetProperty("casos").EnumerateArray().Take(12).Sum(x => x.GetInt32()));
        Assert.Empty(simon.GetProperty("zonas").EnumerateArray()); // un caso: la zona no va sola
        Assert.Equal(1, simon.GetProperty("zonasReunidas").GetInt32());
        Assert.Contains("Fuera de las zonas de los bloques quedaron 1 caso del periodo", html);
    }

    [Fact]
    public async Task SinCasosEnLosBloques_LoDiceSinInventarCeros()
    {
        var (navegador, _) = await OperadorAsync();

        var html = await navegador.GetStringAsync("/panel/analitica/bloques?periodo=rango&desde=2005-07-01&hasta=2005-07-31");

        Assert.Contains("data-estado=\"vacio\"", html);
        Assert.DoesNotContain("data-respuesta=", html);
    }

    [Fact]
    public async Task VaPrimeroEnLaColumna_AbreConDoceMeses_YNoLlevaEsePeriodoALasDemas()
    {
        var (navegador, _) = await OperadorAsync();

        var html = await navegador.GetStringAsync("/panel/analitica/bloques");

        Assert.Equal(10, html.Split("class=\"pregunta\"").Length - 1);
        Assert.True(html.IndexOf("data-pregunta=\"bloques\"", StringComparison.Ordinal) < html.IndexOf("data-pregunta=\"ahora\"", StringComparison.Ordinal));
        Assert.Contains("aria-current=\"true\">12 meses</a>", html);
        Assert.Contains("href=\"/panel/analitica/tiempo\"", html); // «Tiempos de respuesta» abre con sus 7 días
        Assert.DoesNotContain("tiempo?periodo=365", html);
    }
}
