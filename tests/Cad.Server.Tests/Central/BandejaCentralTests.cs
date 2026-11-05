using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Central;

// Bandeja de la central (05/11/2026): los reportes por revisar están en la central, entre los SOS y los reportes por asignar.
// «Revisar» inicia la revisión desde ahí (HU14: quien la inicia queda como responsable) y el reporte pasa a «Decidir».
[Collection(ColeccionServidor.Nombre)]
public partial class BandejaCentralTests(FabricaCad fabrica)
{
    private const string Central = "/panel/central";
    private const string PorRevisar = "/panel/central?handler=PorRevisar";

    private async Task<Guid> ReporteAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id,
            "Robo de una bicicleta frente al mercado (ficticio)", DateTimeOffset.UtcNow.AddHours(-1),
            new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0631, -77.0365, null, "Frente al mercado")));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    // La tarjeta del reporte en la lista de «Por revisar», o una cadena vacía.
    private static string Tarjeta(string html, Guid id) =>
        Regex.Match(html, $"<li[^>]*data-revisar=\"{id}\"[\\s\\S]*?</li>\\s*(?=<li|</ol>)").Value;

    [Fact]
    public async Task UnReporteRecibido_ApareceEnPorRevisarDeLaCentral_ConRevisarYSinMarcadorPropio()
    {
        var id = await ReporteAsync();
        var (operador, _) = await OperadorAsync();

        var pagina = await operador.GetStringAsync(Central);

        Assert.Contains("Por revisar · reportes", pagina);
        var tarjeta = Tarjeta(pagina, id);
        Assert.NotEmpty(tarjeta);
        Assert.Contains("data-estado=\"Recibido\"", tarjeta);
        Assert.Contains("Punto en el mapa", tarjeta); // cómo se indicó el lugar
        Assert.Contains($"action=\"/panel/reportes/{id}?handler=IniciarRevision\"", tarjeta);
        Assert.Contains("name=\"__RequestVerificationToken\"", tarjeta);
        Assert.Contains(">Revisar<", tarjeta);
        Assert.Contains("data-seleccionar-revisar", tarjeta); // la tarjeta entera lo elige, como las demás de la bandeja
        Assert.DoesNotContain("data-sos=", tarjeta); // no es un caso por asignar: no se dibuja dos veces en el mapa
    }

    [Fact]
    public async Task Revisar_DesdeLaBandeja_IniciaLaRevision_YElReportePasaADecidir()
    {
        var id = await ReporteAsync();
        var (operador, cuenta) = await OperadorAsync();
        var tarjeta = Tarjeta(await operador.GetStringAsync(Central), id);
        var token = Regex.Match(tarjeta, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var respuesta = await operador.PostAsync($"/panel/reportes/{id}?handler=IniciarRevision",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal($"/panel/reportes/{id}", respuesta.Headers.Location!.ToString());
        var caso = await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal(EstadoCaso.EnRevision, caso.Estado);
        var despues = Tarjeta(await operador.GetStringAsync(PorRevisar), id);
        Assert.Contains("data-estado=\"En revisión\"", despues);
        Assert.Contains(">Decidir<", despues);
        Assert.DoesNotContain("handler=IniciarRevision", despues);
        Assert.NotEqual(Guid.Empty, cuenta.Id);
    }

    [Fact]
    public async Task LaCabecera_DiceCuantosSerenosEstanActivosDeLosRegistrados_YAbreSuPanel()
    {
        var (operador, _) = await OperadorAsync();

        var pagina = await operador.GetStringAsync(Central);

        Assert.Matches(new Regex("data-abrir-serenos>\\s*<b data-valor>\\d+</b> <span data-etiqueta>de \\d+ serenos? activos?</span>"), pagina);
        Assert.Contains("id=\"panel-serenos\" aria-labelledby=\"titulo-serenos\" data-panel hidden", pagina);
        Assert.DoesNotContain("data-filtro-cola", pagina);
    }

    // La cifra cuenta todos los serenos registrados, también los de cuenta desactivada (no salen en el monitoreo).
    private static (int Activos, int Registrados) CifraSerenos(string pagina)
    {
        var m = Regex.Match(pagina, @"data-abrir-serenos>\s*<b data-valor>(\d+)</b> <span data-etiqueta>de (\d+) serenos? activos?</span>");
        Assert.True(m.Success, "No está la cifra de serenos en la cabecera.");
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    [Fact]
    public async Task UnSerenoConLaCuentaDesactivada_CuentaComoRegistrado_PeroNoComoActivo()
    {
        var (operador, _) = await OperadorAsync();
        var antes = CifraSerenos(await operador.GetStringAsync(Central));

        await fabrica.CrearAsync(Roles.Sereno, activa: false);
        var despues = CifraSerenos(await operador.GetStringAsync(Central));

        Assert.Equal(antes.Registrados + 1, despues.Registrados);
        Assert.Equal(antes.Activos, despues.Activos);
        Assert.Contains($"data-registrados=\"{despues.Registrados}\"", await operador.GetStringAsync("/panel/central?handler=Serenos"));
    }
}
