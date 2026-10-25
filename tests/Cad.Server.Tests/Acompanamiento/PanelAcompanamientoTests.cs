using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Acompanamiento;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Panel.Pages.Acompanamientos;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Acompanamiento;

// Panel de acompañamientos (24/10/2026): la tarjeta entera ubica la sesión, la lista y el detalle se actualizan al llegar
// algo del ciudadano (aviso «AcompanamientoActualizado», sin coordenadas) y las posiciones del detalle van por páginas.
// Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PanelAcompanamientoTests(FabricaCad fabrica)
{
    private async Task<(Guid SesionId, HttpClient App)> SesionAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sesion = (await (await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        return (sesion.Id, app);
    }

    private static async Task PosicionAsync(HttpClient app, Guid sesionId) =>
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsJsonAsync($"/api/acompanamiento/{sesionId}/posiciones",
            new PosicionAcompanamientoEnviada(-16.3988, -71.5369, 8, DateTimeOffset.UtcNow))).StatusCode);

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static int Filas(string html) => Regex.Matches(html, "<tr data-posicion ").Count;

    [Fact]
    public async Task LaCentralRecibeUnAviso_AlIniciar_AlLlegarCadaPosicion_YAlFinalizar()
    {
        await using var operador = await fabrica.OperadorEnCentralAsync();
        var (sesionId, app) = await SesionAsync();
        Assert.True(await CentralDePrueba.EsperarAsync(() => operador.AvisosAcompanamientos.Contains(sesionId)), "aviso del inicio");

        var antes = operador.AvisosAcompanamientos.Count;
        await PosicionAsync(app, sesionId);
        Assert.True(await CentralDePrueba.EsperarAsync(() => operador.AvisosAcompanamientos.Count > antes), "aviso de la posición");

        antes = operador.AvisosAcompanamientos.Count;
        await app.PostAsync($"/api/acompanamiento/{sesionId}/finalizacion", null);
        Assert.True(await CentralDePrueba.EsperarAsync(() => operador.AvisosAcompanamientos.Count > antes), "aviso del cierre");
        Assert.All(operador.AvisosAcompanamientos, id => Assert.Equal(sesionId, id));
    }

    [Fact]
    public async Task LasPosicionesDelDetalle_VanPorPaginas_YElMapaLasTieneTodas()
    {
        var (sesionId, app) = await SesionAsync();
        var total = DetalleModel.PosicionesPorPagina + 5;
        for (var i = 0; i < total; i++)
            await PosicionAsync(app, sesionId);
        var operador = await OperadorAsync();
        var ruta = $"/panel/acompanamientos/{sesionId}";

        var primera = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(ruta));
        Assert.Equal(DetalleModel.PosicionesPorPagina, Filas(primera));
        Assert.Contains($"Posiciones 1–{DetalleModel.PosicionesPorPagina} de {total}", primera);
        Assert.Contains("data-pagina=\"2\"", primera);
        Assert.Contains("· actual", primera); // la más reciente, en la primera página
        Assert.Equal(total, Regex.Match(primera, "data-puntos=\"([^\"]*)\"").Groups[1].Value.Split(';').Length);

        var segunda = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"{ruta}?pagina=2"));
        Assert.Equal(5, Filas(segunda));
        Assert.Contains("← Más recientes", segunda);
        Assert.DoesNotContain("· actual", segunda);
        Assert.Equal(5, Filas(System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"{ruta}?pagina=99")))); // fuera de rango: la última
    }

    [Fact]
    public async Task LaTarjeta_EsElBotonQueUbicaLaSesion_YElDetalleMarcaLasPartesVivas()
    {
        var (conPosicion, app) = await SesionAsync();
        await PosicionAsync(app, conPosicion);
        var (sinPosicion, _) = await SesionAsync();
        var operador = await OperadorAsync();

        var lista = await operador.GetStringAsync("/panel/acompanamientos?handler=Lista");
        string Tarjeta(Guid id) => Regex.Match(lista, $"<li class=\"acompanamiento[^\"]*\" data-acompanamiento=\"{id}\".*?</li>", RegexOptions.Singleline).Value;
        Assert.Contains("data-elegir-acompanamiento", Tarjeta(conPosicion));
        Assert.DoesNotContain("data-elegir-acompanamiento", Tarjeta(sinPosicion)); // sin posición no hay nada que ubicar
        Assert.DoesNotContain("data-ver-en-mapa", lista);

        var detalle = await operador.GetStringAsync($"/panel/acompanamientos/{conPosicion}");
        foreach (var parte in new[] { "cabecera", "posiciones", "bienestar", "datos", "avisos" })
            Assert.Contains($"data-vivo=\"{parte}\"", detalle);
        Assert.Contains("data-activa=\"true\"", detalle);
        Assert.Contains("signalr.min.js", detalle);
    }
}
