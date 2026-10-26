using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// Llamar desde el paso de contacto del SOS (26/10/2026): el teléfono del ciudadano y su contacto de emergencia como enlaces tel:
// junto al formulario del intento. La llamada no pasa por el sistema. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class LlamarCiudadanoTests(FabricaCad fabrica)
{
    private const string Pin = "482915";

    private static string NumeroNuevo() => "9" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();

    private async Task<(string Celular, HttpClient Cliente)> CiudadanoAsync()
    {
        var anonimo = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var codigo = await (await anonimo.PostAsJsonAsync("/api/auth/celular/codigo", new SolicitudCodigoCelular(celular)))
            .Content.ReadFromJsonAsync<RespuestaCodigoCelular>();
        var verificacion = await (await anonimo.PostAsJsonAsync("/api/auth/celular/verificar",
            new SolicitudVerificacionCelular(celular, codigo!.CodigoDePrueba!))).Content.ReadFromJsonAsync<RespuestaVerificacionCelular>();
        var sesion = await (await anonimo.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion!.TokenVerificacion, "Rosa", "Ficticia Quispe", Pin, true)))
            .Content.ReadFromJsonAsync<RespuestaIngresoCelular>();
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion!.Token);
        return (celular, cliente);
    }

    private async Task<string> DetalleDelSosAsync(HttpClient ciudadano)
    {
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        return await navegador.GetStringAsync($"/panel/central/sos/{sos!.Id}");
    }

    [Fact]
    public async Task ElPasoDeContacto_OfreceLlamarAlCiudadano_AntesDelFormularioDelIntento()
    {
        var (celular, cliente) = await CiudadanoAsync();

        var html = System.Net.WebUtility.HtmlDecode(await DetalleDelSosAsync(cliente));

        var boton = html.IndexOf("data-llamar=\"ciudadano\"", StringComparison.Ordinal);
        Assert.True(boton >= 0, "No está el botón para llamar al ciudadano.");
        Assert.Contains($"href=\"tel:{celular}\" data-llamar=\"ciudadano\" data-nombre=\"Rosa\"", html);
        Assert.Contains($"Llamar a Rosa <span class=\"num\">{celular[..3]} {celular[3..6]} {celular[6..]}</span>", html);
        Assert.True(boton < html.IndexOf("Resultado del intento", StringComparison.Ordinal), "El botón debe ir antes del resultado del intento.");
        Assert.Contains("data-llamada-estado hidden", html);
        Assert.DoesNotContain("data-llamar=\"contacto\"", html); // sin contacto de emergencia registrado
    }

    [Fact]
    public async Task ConContactoDeEmergencia_TambienSePuedeLlamarlo()
    {
        var (_, cliente) = await CiudadanoAsync();
        await cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", new SolicitudPerfilCiudadano("Rosa", "Ficticia Quispe", null, null,
            null, null, "Marta Ficticia", "Madre", "911222333"));

        var html = System.Net.WebUtility.HtmlDecode(await DetalleDelSosAsync(cliente));

        Assert.Contains("href=\"tel:911222333\" data-llamar=\"contacto\" data-nombre=\"Marta Ficticia\"", html);
        Assert.Contains("Contacto de emergencia: Marta Ficticia (Madre)", html);
    }
}
