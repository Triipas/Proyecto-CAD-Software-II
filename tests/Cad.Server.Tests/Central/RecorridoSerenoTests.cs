using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// Recorrido por calles del sereno hasta su caso en el mapa de la central: la misma red de calles que viaja en la app (ciudad de
// Arequipa) y la misma búsqueda. Coordenadas ficticias.
[Collection(ColeccionServidor.Nombre)]
public class RecorridoSerenoTests(FabricaCad fabrica)
{
    private const string Rutas = "/panel/central?handler=Rutas";

    private async Task<(Guid Id, HttpClient App)> SerenoEnAsync(double lat, double lon)
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

    private static async Task<JsonElement> RecorridoAsync(HttpClient operador, Guid serenoId)
    {
        using var json = JsonDocument.Parse(await operador.GetStringAsync(Rutas));
        return json.RootElement.EnumerateArray().Single(r => r.GetProperty("sereno").GetGuid() == serenoId).Clone();
    }

    [Fact]
    public async Task SerenoEnCamino_SuRecorridoVaPorLasCallesDesdeSuPosicionHastaElCaso()
    {
        var (serenoId, _) = await SerenoEnAsync(-16.4024, -71.5369); // a ~400 m del caso en línea recta
        var casoId = await SosAsync();
        var operador = await OperadorAsync();
        await AsignarAsync(operador, casoId, serenoId);

        var recorrido = await RecorridoAsync(operador, serenoId);

        Assert.Equal(casoId, recorrido.GetProperty("caso").GetGuid());
        var puntos = recorrido.GetProperty("puntos").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
        Assert.True(puntos.Count > 2); // no es la línea recta
        Assert.Equal((-16.4024, -71.5369), puntos[0]);
        Assert.Equal((-16.3988, -71.5369), puntos[^1]);
        Assert.InRange(recorrido.GetProperty("metros").GetDouble(), 400, 1500);
    }

    [Fact]
    public async Task FueraDeLaZonaConCalles_NoHayRecorrido_YElMapaQuedaConLaLineaRecta()
    {
        var (serenoId, _) = await SerenoEnAsync(-12.0464, -77.0428); // en Lima, a ~760 km de la zona
        var casoId = await SosAsync();
        var operador = await OperadorAsync();
        await AsignarAsync(operador, casoId, serenoId);

        var recorrido = await RecorridoAsync(operador, serenoId);

        Assert.Equal(JsonValueKind.Null, recorrido.GetProperty("puntos").ValueKind);
        Assert.Equal(JsonValueKind.Null, recorrido.GetProperty("metros").ValueKind);
    }

    [Fact]
    public async Task SerenoSinCaso_NoTieneRecorrido()
    {
        var (serenoId, _) = await SerenoEnAsync(-16.4024, -71.5369);

        var json = await (await OperadorAsync()).GetStringAsync(Rutas);

        Assert.DoesNotContain(serenoId.ToString(), json);
    }

    [Theory]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Ciudadano)]
    public async Task LaAppDeSerenoOCiudadano_NoConsultaLosRecorridos(string rol)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.GetAsync(Rutas);

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
