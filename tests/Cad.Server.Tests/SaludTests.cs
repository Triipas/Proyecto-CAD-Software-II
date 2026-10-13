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
    public async Task RutaRaiz_SinEndpointDefinido_Responde404()
    {
        var cliente = fabrica.CreateClient();

        var respuesta = await cliente.GetAsync("/");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
