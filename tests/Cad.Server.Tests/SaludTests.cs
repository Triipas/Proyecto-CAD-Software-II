using System.Net;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests;

// S1-T01 (TEC01): el servidor arranca y responde al sondeo de salud.
[Collection(ColeccionServidor.Nombre)]
public class SaludTests(FabricaCad fabrica)
{
    [Fact]
    public async Task Salud_ServidorEnEjecucion_Responde200Healthy()
    {
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RutaRaiz_SinSesion_RedirigeAlIngresoPorDenegacionPorDefecto()
    {
        // HU03: con la política por defecto, una ruta no declarada exige sesión; no se responde 404
        // para no revelar qué rutas existen.
        var respuesta = await fabrica.CrearNavegador().GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.StartsWith("https://localhost/panel/ingresar", respuesta.Headers.Location?.ToString());
    }
}
