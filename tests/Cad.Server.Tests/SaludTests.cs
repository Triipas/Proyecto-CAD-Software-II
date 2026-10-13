using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cad.Server.Tests;

// S1-T01 (TEC01): el servidor arranca y responde al sondeo de salud.
public class SaludTests(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
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
