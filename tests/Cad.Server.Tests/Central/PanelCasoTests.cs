using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// Panel del caso (05/11/2026): el detalle del reporte o del SOS se muestra dentro de la central, en un marco, sin la barra del
// panel y con panel-caso.js. Lo activa la navegación dentro de un marco («Sec-Fetch-Dest: iframe») o «?vista=panel»; fuera
// de un marco la página es la de siempre.
[Collection(ColeccionServidor.Nombre)]
public class PanelCasoTests(FabricaCad fabrica)
{
    private async Task<Guid> ReporteAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id,
            "Robo de una bicicleta frente al mercado (ficticio)", DateTimeOffset.UtcNow.AddHours(-1),
            new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0631, -77.0365, null, "Frente al mercado")));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<string> EnMarcoAsync(HttpClient navegador, string ruta)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Add("Sec-Fetch-Dest", "iframe");
        var respuesta = await navegador.SendAsync(peticion);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await respuesta.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task ElReporteDentroDeUnMarco_SeMuestraEnModoPanel_SinLaBarraYConSusPestanas()
    {
        var id = await ReporteAsync();
        var operador = await OperadorAsync();

        var html = await EnMarcoAsync(operador, $"/panel/reportes/{id}");

        Assert.Contains("data-en-panel=\"true\"", html);
        Assert.DoesNotContain("<header class=\"barra\"", html);
        Assert.Contains("/js/panel-caso.js", html);
        Assert.Contains("/css/panel-caso.css", html);
        Assert.Contains("Iniciar la revisión", html); // los mismos formularios que la página completa
    }

    [Fact]
    public async Task ConVistaPanel_TambienSeMuestraEnModoPanel_YSinMarcoEsLaPaginaDeSiempre()
    {
        var id = await ReporteAsync();
        var operador = await OperadorAsync();

        var panel = await operador.GetStringAsync($"/panel/reportes/{id}?vista=panel");
        var completa = await operador.GetStringAsync($"/panel/reportes/{id}");

        Assert.Contains("data-en-panel=\"true\"", panel);
        Assert.Contains("<header class=\"barra\"", completa);
        Assert.DoesNotContain("panel-caso.js", completa);
        Assert.Contains("data-en-panel=\"false\"", completa);
    }

    [Fact]
    public async Task UnaPaginaQueNoLoPermite_NoSeMuestraEnModoPanelAunqueEsteEnUnMarco()
    {
        var operador = await OperadorAsync();

        var html = await EnMarcoAsync(operador, "/panel/casos");

        Assert.Contains("<header class=\"barra\"", html);
        Assert.Contains("data-en-panel=\"false\"", html);
    }

    [Fact]
    public async Task LaCentral_TraeElPanelDelCaso_YLasAccionesDeLaBandejaLoAbren()
    {
        var id = await ReporteAsync();
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync("/panel/central");

        Assert.Contains("data-panel-caso hidden", html);
        Assert.Contains("name=\"panel-caso\"", html);
        var formulario = System.Text.RegularExpressions.Regex.Match(html, $"<form[^>]*/panel/reportes/{id}\\?handler=IniciarRevision[^>]*>").Value;
        Assert.Contains("target=\"panel-caso\"", formulario);
        Assert.Contains("data-abrir-panel", formulario);
    }
}
