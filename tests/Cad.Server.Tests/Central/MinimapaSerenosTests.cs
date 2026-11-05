using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// Minimapa del detalle de un SOS con los serenos (05/11/2026): los operativos con ubicación y su distancia al caso, y para el
// que tiene asignado el caso, su recorrido por calles (la misma red que la central). Coordenadas ficticias de Arequipa.
[Collection(ColeccionServidor.Nombre)]
public partial class MinimapaSerenosTests(FabricaCad fabrica)
{
    private async Task<Guid> SerenoEnAsync(double lat, double lon) => (await SerenoConAppEnAsync(lat, lon)).Id;

    private async Task<(Guid Id, HttpClient App)> SerenoConAppEnAsync(double lat, double lon)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsJsonAsync("/api/sereno/posiciones", new PosicionSerenoEnviada(lat, lon, 7, DateTimeOffset.UtcNow))).StatusCode);
        return (cuenta.Id, app);
    }

    private async Task<Guid> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            new UbicacionSos(-16.3988, -71.5369, 10, ahora.AddSeconds(1))));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task AsignarAsync(HttpClient operador, Guid casoId, Guid serenoId)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        var respuesta = await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(),
            ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }

    // La fila del sereno en la lista del minimapa, o null.
    private static string? FilaDe(string html, Guid serenoId) =>
        Regex.Match(html, $"<li data-sereno=\"{serenoId}\"[^>]*>").Value is { Length: > 0 } fila ? fila : null;

    [Fact]
    public async Task SinAsignar_ElMinimapaListaAlSerenoDisponibleConSuDistancia_YSinRecorrido()
    {
        var serenoId = await SerenoEnAsync(-16.4024, -71.5369); // a ~400 m del caso
        var casoId = await SosAsync();

        var html = await (await OperadorAsync()).GetStringAsync($"/panel/central/sos/{casoId}");

        var fila = FilaDe(html, serenoId);
        Assert.NotNull(fila);
        Assert.Contains("data-disponible=\"true\"", fila);
        Assert.Contains("data-asignado=\"false\"", fila);
        Assert.Matches(new Regex("data-distancia=\"(39\\d|40\\d) m\""), fila);
        Assert.Matches(new Regex("<ul class=\"visualmente-oculto\" data-serenos-minimapa data-ruta=\"\""), html);
    }

    [Fact]
    public async Task ConSerenoAsignado_ElMinimapaLlevaSuRecorridoPorLasCalles()
    {
        var serenoId = await SerenoEnAsync(-16.4024, -71.5369);
        var casoId = await SosAsync();
        var operador = await OperadorAsync();
        await AsignarAsync(operador, casoId, serenoId);

        var html = await operador.GetStringAsync($"/panel/central/sos/{casoId}");

        var fila = FilaDe(html, serenoId);
        Assert.NotNull(fila);
        Assert.Contains("data-asignado=\"true\"", fila);
        var ruta = WebUtility.HtmlDecode(Regex.Match(html, "data-serenos-minimapa data-ruta=\"([^\"]+)\"").Groups[1].Value);
        var puntos = JsonSerializer.Deserialize<double[][]>(ruta)!;
        Assert.True(puntos.Length > 2); // no es la línea recta
        Assert.Equal([-16.4024, -71.5369], puntos[0]);
        Assert.Equal([-16.3988, -71.5369], puntos[^1]);
    }

    [Fact]
    public async Task SerenoNoOperativo_NoApareceEnElMinimapa()
    {
        var (serenoId, app) = await SerenoConAppEnAsync(-16.4024, -71.5369);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(false, true))).StatusCode);
        var casoId = await SosAsync();

        var html = await (await OperadorAsync()).GetStringAsync($"/panel/central/sos/{casoId}");

        Assert.Null(FilaDe(html, serenoId));
    }
}
