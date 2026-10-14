using System.Net;
using System.Text.RegularExpressions;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests;

// TEC01 (S1-T01, S1-T02, S1-T08) y RNF09: el servidor configurado como en Render (detrás de un proxy que
// termina el TLS). Lo que solo puede comprobarse en el entorno publicado está en docs/despliegue.md.
[Collection(ColeccionServidor.Nombre)]
public class DespliegueTests(FabricaCad fabrica)
{
    private WebApplicationFactory<Program> ComoEnRender() => fabrica.WithWebHostBuilder(b =>
    {
        b.UseSetting("Despliegue:DetrasDeProxy", "true");
        b.UseSetting("https_port", "443"); // ASPNETCORE_HTTPS_PORT en render.yaml
    });

    private static HttpClient Cliente(WebApplicationFactory<Program> servidor, string baseUrl) =>
        servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(baseUrl), AllowAutoRedirect = false });

    [Theory]
    [InlineData("/panel/ingresar")]
    [InlineData("/api/auth/token")]
    public async Task RNF09_SolicitudPorHttpSinCifrar_SeRedirigeAHttps(string ruta)
    {
        await using var servidor = ComoEnRender();

        var respuesta = await Cliente(servidor, "http://cad-validacion.test").GetAsync(ruta);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, respuesta.StatusCode);
        Assert.StartsWith("https://cad-validacion.test", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RNF09_TrasElProxyConTls_SeAtiendeYLasCookiesDelPanelSonSecure()
    {
        // El tramo proxy → servidor es HTTP con X-Forwarded-Proto. Las cookies se gestionan a mano: un gestor
        // de cookies no reenvía una cookie Secure por HTTP, y el navegador real sí las envía porque está en HTTPS.
        await using var servidor = ComoEnRender();
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://cad-validacion.test"), AllowAutoRedirect = false, HandleCookies = false,
        });
        navegador.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var pagina = await navegador.GetAsync("/panel/ingresar");
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        var antifalsificacion = Assert.Single(pagina.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Antiforgery."));
        Assert.Contains("secure", antifalsificacion, StringComparison.OrdinalIgnoreCase);

        var token = Regex.Match(await pagina.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        using var formulario = new HttpRequestMessage(HttpMethod.Post, "/panel/ingresar")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Formulario.Correo"] = fabrica.AdminCorreo,
                ["Formulario.Contrasena"] = fabrica.AdminClave,
                ["__RequestVerificationToken"] = token,
            }),
        };
        formulario.Headers.Add("Cookie", antifalsificacion.Split(';')[0]);
        var ingreso = await navegador.SendAsync(formulario);
        var cookie = Assert.Single(ingreso.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cad.panel="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RNF09_HttpsEnUnDominioReal_EnviaHsts()
    {
        await using var servidor = ComoEnRender();

        var respuesta = await Cliente(servidor, "https://cad-validacion.test").GetAsync("/panel/ingresar");

        Assert.True(respuesta.Headers.Contains("Strict-Transport-Security"), "Falta la cabecera HSTS.");
    }

    [Theory]
    [InlineData("/salud")]
    [InlineData("/salud/lista")]
    public async Task SondeosDeSalud_FuncionanPorHttpInterno_SinRedireccion(string ruta)
    {
        // Render comprueba la salud por HTTP interno, sin X-Forwarded-Proto.
        await using var servidor = ComoEnRender();

        var respuesta = await Cliente(servidor, "http://cad-validacion.test").GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Healthy", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SinProxyDeclarado_LasCabecerasReenviadasNoSeAceptan()
    {
        // Fuera de Render, un cliente no puede hacerse pasar por HTTPS con X-Forwarded-Proto:
        // el panel en local sigue sin marcar la cookie como Secure al ir por http.
        var navegador = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        navegador.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var respuesta = await navegador.GetAsync("/salud");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(respuesta.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task ClavesDeProteccionDeDatos_SeGuardanEnPostgreSqlParaSobrevivirALosDespliegues()
    {
        await fabrica.CrearNavegador().IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);

        var claves = await fabrica.ConBaseDatosAsync(db => db.DataProtectionKeys.CountAsync());

        Assert.True(claves > 0, "No hay claves de Data Protection en la base de datos.");
    }
}
