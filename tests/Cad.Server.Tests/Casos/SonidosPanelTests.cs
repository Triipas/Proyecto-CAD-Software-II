using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Panel;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Casos;

// Sonidos del panel (24/10/2026): tras asignar, la página siguiente lleva una sola vez el sonido a reproducir. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class SonidosPanelTests(FabricaCad fabrica)
{
    [Fact]
    public async Task TrasAsignar_LaPaginaSiguienteLlevaElSonido_SoloUnaVez()
    {
        var operadorCuenta = await fabrica.CrearAsync(Roles.Operador);
        var operador = fabrica.CrearNavegador();
        await operador.IngresarAlPanelAsync(operadorCuenta.Correo, operadorCuenta.Clave);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var ruta = $"/panel/central/sos/{sos!.Id}";
        Assert.DoesNotContain($"data-sonido=\"{SonidoPanel.CasoAsignado}\"", await operador.GetStringAsync(ruta));

        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);

        Assert.Contains($"data-sonido=\"{SonidoPanel.CasoAsignado}\"", await operador.GetStringAsync(ruta));
        Assert.DoesNotContain($"data-sonido=\"{SonidoPanel.CasoAsignado}\"", await operador.GetStringAsync(ruta)); // al recargar no vuelve a sonar
    }

    [Theory]
    [InlineData("/sonidos/sos-nuevo.wav")]
    [InlineData("/sonidos/reporte-admitido.wav")]
    [InlineData("/sonidos/caso-asignado.wav")]
    [InlineData("/sonidos/aviso-acompanamiento.wav")]
    [InlineData("/sonidos/error.wav")]
    [InlineData("/js/sonidos.js")]
    public async Task LosArchivosDeSonido_SeSirven(string ruta)
    {
        var respuesta = await fabrica.CreateClient().GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
