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
        // El servidor de pruebas es compartido: la central también recibe los avisos de otras pruebas; solo cuentan los de esta sesión.
        int Avisos() => operador.AvisosAcompanamientos.Count(id => id == sesionId);
        Assert.True(await CentralDePrueba.EsperarAsync(() => Avisos() > 0), "aviso del inicio");

        var antes = Avisos();
        await PosicionAsync(app, sesionId);
        Assert.True(await CentralDePrueba.EsperarAsync(() => Avisos() > antes), "aviso de la posición");

        antes = Avisos();
        await app.PostAsync($"/api/acompanamiento/{sesionId}/finalizacion", null);
        Assert.True(await CentralDePrueba.EsperarAsync(() => Avisos() > antes), "aviso del cierre");
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

    // Rediseño (25/10/2026): cualquier sesión se puede seguir, también sin posición (se le puede pedir confirmación de bienestar).
    [Fact]
    public async Task LaTarjeta_EsElBotonQueSigueLaSesion_YElDetalleMarcaLasPartesVivas()
    {
        var (conPosicion, app) = await SesionAsync();
        await PosicionAsync(app, conPosicion);
        var (sinPosicion, _) = await SesionAsync();
        var operador = await OperadorAsync();

        var lista = await operador.GetStringAsync("/panel/acompanamientos?handler=Lista");
        string Tarjeta(Guid id) => Regex.Match(lista, $"<li class=\"vigilado[^\"]*\" data-acompanamiento=\"{id}\".*?</li>", RegexOptions.Singleline).Value;
        Assert.Contains("data-elegir-acompanamiento", Tarjeta(conPosicion));
        Assert.Contains("data-elegir-acompanamiento", Tarjeta(sinPosicion));
        // Sin JavaScript, el nombre sigue llevando al seguimiento: es un enlace a ?sesion=….
        Assert.Contains($"href=\"/panel/acompanamientos?sesion={conPosicion}\"", Tarjeta(conPosicion));
        Assert.Contains("data-sin-posicion", Tarjeta(sinPosicion));
        Assert.DoesNotContain("data-ver-en-mapa", lista);

        var detalle = await operador.GetStringAsync($"/panel/acompanamientos/{conPosicion}");
        foreach (var parte in new[] { "cabecera", "posiciones", "bienestar", "datos", "avisos" })
            Assert.Contains($"data-vivo=\"{parte}\"", detalle);
        Assert.Contains("data-activa=\"true\"", detalle);
        Assert.Contains("signalr.min.js", detalle);
    }

    // Modo «sesión»: ?sesion=… abre la vigilancia siguiendo a una persona (también sin JavaScript), y la ficha se pide sola.
    [Fact]
    public async Task ElModoSesion_MuestraSoloSuFicha_ConSuRecorridoYLaSiguienteAccion()
    {
        var (sesionId, app) = await SesionAsync();
        await PosicionAsync(app, sesionId);
        await PosicionAsync(app, sesionId);
        var operador = await OperadorAsync();

        var pagina = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/acompanamientos?sesion={sesionId}"));
        Assert.Contains("data-modo=\"sesion\"", pagina);
        Assert.Contains($"data-seguimiento=\"{sesionId}\"", pagina);
        Assert.Equal(2, Regex.Match(pagina, @"data-seguimiento=""[^""]*"" data-activa=""true""\s+data-puntos=""([^""]*)""").Groups[1].Value.Split(';').Length);
        Assert.Contains("Pedir confirmación de bienestar", pagina);
        Assert.Contains("name=\"Volver\" value=\"lista\"", pagina);

        var ficha = await operador.GetAsync($"/panel/acompanamientos?handler=Sesion&id={sesionId}");
        Assert.Equal(HttpStatusCode.OK, ficha.StatusCode);
        Assert.Contains($"data-seguimiento=\"{sesionId}\"", await ficha.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await operador.GetAsync($"/panel/acompanamientos?handler=Sesion&id={Guid.NewGuid()}")).StatusCode);
        Assert.Contains("data-modo=\"todas\"", await operador.GetStringAsync($"/panel/acompanamientos?sesion={Guid.NewGuid()}"));
    }

    // Modo «detalle» (26/10/2026): el detalle es la misma vigilancia sobre el mismo mapa; desde el seguimiento se abre sin
    // recargar (la página trae solo su ficha), y la ficha lleva el recorrido que dibuja el mapa.
    [Fact]
    public async Task ElDetalle_EsLaVigilanciaEnModoDetalle_ConSuRecorridoYLaVueltaAlSeguimiento()
    {
        var (sesionId, app) = await SesionAsync();
        await PosicionAsync(app, sesionId);
        await PosicionAsync(app, sesionId);
        var operador = await OperadorAsync();

        var seguimiento = await operador.GetStringAsync($"/panel/acompanamientos?handler=Sesion&id={sesionId}");
        Assert.Contains($"href=\"/panel/acompanamientos/{sesionId}\" data-ver-detalle", seguimiento);

        var detalle = await operador.GetStringAsync($"/panel/acompanamientos/{sesionId}");
        Assert.Contains("data-vigilancia data-modo=\"detalle\"", detalle);
        Assert.Contains($"data-region-detalle data-sesion=\"{sesionId}\" data-activa=\"true\"", detalle);
        Assert.Contains("data-region-acompanamientos", detalle); // la lista se pide al volver a «todas»
        Assert.Equal(2, Regex.Match(detalle, @"data-recorrido\s+data-puntos=""([^""]*)""").Groups[1].Value.Split(';').Length);
        Assert.Contains($"href=\"/panel/acompanamientos?sesion={sesionId}\" data-volver-seguimiento", detalle);
        Assert.Contains("acompanamientos.js", detalle);
        Assert.DoesNotContain("recorrido.js", detalle);
    }

    // Pedir la confirmación desde el seguimiento vuelve al seguimiento de la misma persona.
    [Fact]
    public async Task PedirConfirmacionDesdeElSeguimiento_VuelveAlSeguimiento()
    {
        var (sesionId, app) = await SesionAsync();
        await PosicionAsync(app, sesionId);
        var operador = await OperadorAsync();
        var ruta = $"/panel/acompanamientos/{sesionId}";

        var respuesta = await operador.PostAsync($"{ruta}?handler=SolicitarConfirmacion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["IdSolicitudConfirmacion"] = Guid.NewGuid().ToString(), ["Volver"] = "lista",
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal($"/panel/acompanamientos?sesion={sesionId}", respuesta.Headers.Location?.OriginalString);
        var seguimiento = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(respuesta.Headers.Location!.OriginalString));
        Assert.Contains("Esperando su respuesta", seguimiento);
        Assert.Contains("Confirmación solicitada", seguimiento);
    }
}
