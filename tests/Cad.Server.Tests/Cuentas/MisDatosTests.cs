using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc;

namespace Cad.Server.Tests.Cuentas;

// ADR-08, fase D — «Mis datos» del ciudadano (con las reglas del registro único), cambio de PIN y ficha del ciudadano en el panel.
// Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class MisDatosTests(FabricaCad fabrica)
{
    private const string Pin = "482915";

    private sealed record Ciudadano(string Celular, HttpClient Cliente, string Token);

    private static string NumeroNuevo() => "9" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
    private static string DniNuevo() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    private async Task<Ciudadano> CrearCiudadanoAsync()
    {
        var anonimo = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var codigo = await (await anonimo.PostAsJsonAsync("/api/auth/celular/codigo", new SolicitudCodigoCelular(celular)))
            .Content.ReadFromJsonAsync<RespuestaCodigoCelular>();
        var verificacion = await (await anonimo.PostAsJsonAsync("/api/auth/celular/verificar",
            new SolicitudVerificacionCelular(celular, codigo!.CodigoDePrueba!))).Content.ReadFromJsonAsync<RespuestaVerificacionCelular>();
        var sesion = await (await anonimo.PostAsJsonAsync("/api/auth/celular/registro",
            RegistroDePrueba.Solicitud(verificacion!.TokenVerificacion, "Rosa", "Ficticia Quispe", Pin, true)))
            .Content.ReadFromJsonAsync<RespuestaIngresoCelular>();
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion!.Token);
        return new Ciudadano(celular, cliente, sesion.Token);
    }

    private static SolicitudPerfilCiudadano Completo(string dni) => new("Rosa", "Ficticia Quispe", DatosCiudadano.Dni, dni,
        new DateOnly(1990, 5, 17), "Jr. Ficticio 123, Cercado de Lima", "Marta Ficticia", "Madre", "911222333");

    [Fact]
    public async Task Perfil_RecienRegistrado_TieneTodosLosDatosDelFormulario()
    {
        var c = await CrearCiudadanoAsync();

        var perfil = await c.Cliente.GetFromJsonAsync<PerfilCiudadanoVista>("/api/cuentas/yo/perfil");

        Assert.Equal(("Rosa", "Ficticia Quispe", c.Celular, true), (perfil!.Nombres, perfil.Apellidos, perfil.Celular, perfil.CelularVerificado));
        Assert.NotNull(perfil.NumeroDocumento);
        Assert.Equal("911222333", perfil.ContactoTelefono);
        Assert.True(perfil.DatosCompletos);
        Assert.True(perfil.TienePin);
        Assert.NotNull(perfil.TerminosAceptadosEn);
    }

    [Fact]
    public async Task Perfil_CuentaAnteriorAlRegistroUnico_NoTieneLosDatosCompletos()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var cliente = await fabrica.CrearClienteAppAsync(cuenta);

        var perfil = await cliente.GetFromJsonAsync<PerfilCiudadanoVista>("/api/cuentas/yo/perfil");

        Assert.False(perfil!.DatosCompletos);
    }

    [Fact]
    public async Task Perfil_ConTodosLosDatos_SeGuardaYSeVuelveALeer()
    {
        var c = await CrearCiudadanoAsync();
        var dni = DniNuevo();

        var respuesta = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(dni) with { ContactoTelefono = "911 222 333" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var perfil = await c.Cliente.GetFromJsonAsync<PerfilCiudadanoVista>("/api/cuentas/yo/perfil");
        Assert.Equal((DatosCiudadano.Dni, dni, new DateOnly(1990, 5, 17)), (perfil!.TipoDocumento, perfil.NumeroDocumento, perfil.FechaNacimiento));
        Assert.Equal(("Marta Ficticia", "Madre", "911222333"), (perfil.ContactoNombre, perfil.ContactoParentesco, perfil.ContactoTelefono));
        Assert.Equal("Jr. Ficticio 123, Cercado de Lima", perfil.Direccion);
        Assert.True(perfil.DatosCompletos);
    }

    [Fact]
    public async Task Perfil_DatosObligatoriosVacios_NoSeBorran()
    {
        var c = await CrearCiudadanoAsync();
        await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(DniNuevo()));

        var respuesta = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil",
            new SolicitudPerfilCiudadano("Rosa", "Ficticia", "", " ", null, "", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var perfil = await c.Cliente.GetFromJsonAsync<PerfilCiudadanoVista>("/api/cuentas/yo/perfil");
        Assert.True(perfil!.DatosCompletos);
    }

    [Fact]
    public async Task Perfil_DireccionVacia_SeBorra()
    {
        var c = await CrearCiudadanoAsync();
        await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(DniNuevo()));

        var respuesta = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(DniNuevo()) with { Direccion = " " });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Null((await respuesta.Content.ReadFromJsonAsync<PerfilCiudadanoVista>())!.Direccion);
    }

    [Theory]
    [InlineData("DNI", "1234567", "numeroDocumento")]
    [InlineData("DNI", "12345678A", "numeroDocumento")]
    [InlineData("CE", "12345", "numeroDocumento")]
    [InlineData("Licencia", "12345678", "tipoDocumento")]
    [InlineData(null, "12345678", "tipoDocumento")]
    [InlineData("DNI", null, "numeroDocumento")]
    public async Task Perfil_DocumentoInvalidoOIncompleto_SeRechaza(string? tipo, string? numero, string campo)
    {
        var c = await CrearCiudadanoAsync();

        var respuesta = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil",
            Completo(DniNuevo()) with { TipoDocumento = tipo, NumeroDocumento = numero });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(campo, (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task Perfil_ContactoIncompletoOConElPropioNumero_SeRechaza()
    {
        var c = await CrearCiudadanoAsync();

        var incompleto = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(DniNuevo()) with { ContactoParentesco = null });
        var propio = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(DniNuevo()) with { ContactoTelefono = c.Celular });

        Assert.Contains("contactoParentesco", (await incompleto.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        Assert.Equal(["El contacto de emergencia debe tener un número distinto del tuyo."],
            (await propio.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["contactoTelefono"]);
    }

    [Fact]
    public async Task Perfil_FechaDeNacimientoFutura_SeRechaza()
    {
        var c = await CrearCiudadanoAsync();

        var respuesta = await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil",
            Completo(DniNuevo()) with { FechaNacimiento = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)) });

        Assert.Contains("fechaNacimiento", (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
    }

    [Fact]
    public async Task Perfil_DocumentoDeOtraCuenta_SeRechazaEnSuCampo()
    {
        var dni = DniNuevo();
        var primera = await CrearCiudadanoAsync();
        Assert.Equal(HttpStatusCode.OK, (await primera.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(dni))).StatusCode);
        var segunda = await CrearCiudadanoAsync();

        var respuesta = await segunda.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(dni) with { ContactoTelefono = "922333444" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(["Ese documento ya está registrado en otra cuenta."],
            (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["numeroDocumento"]);
    }

    [Fact]
    public async Task CambiarPin_ConElActualCorrecto_EmiteSesionNuevaYElPinNuevoSirve()
    {
        var c = await CrearCiudadanoAsync();

        var respuesta = await c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin(Pin, "730591"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sesion = await respuesta.Content.ReadFromJsonAsync<RespuestaIngresoCelular>();
        // El token anterior deja de valer (sello nuevo); el nuevo sí.
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Cliente.GetAsync("/api/cuentas/yo")).StatusCode);
        var nuevo = fabrica.CreateClient();
        nuevo.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion!.Token);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.GetAsync("/api/cuentas/yo")).StatusCode);
    }

    [Fact]
    public async Task CambiarPin_ConElActualIncorrecto_DescuentaIntentosSinCerrarLaSesion()
    {
        var c = await CrearCiudadanoAsync();

        var respuesta = await c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin("000999", "730591"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("PIN actual incorrecto. Te quedan 4 intentos.", (await respuesta.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(HttpStatusCode.OK, (await c.Cliente.GetAsync("/api/cuentas/yo")).StatusCode);
    }

    [Fact]
    public async Task CambiarPin_ActualIncorrectoSimultaneo_NoSuperaLosCincoIntentos()
    {
        var c = await CrearCiudadanoAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin("000999", "730591"))));

        Assert.InRange(respuestas.Count(r => r.StatusCode == HttpStatusCode.BadRequest), 0, 4);
        Assert.All(respuestas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Locked }));
        Assert.Equal(HttpStatusCode.Locked,
            (await c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin(Pin, "730591"))).StatusCode);
    }

    [Fact]
    public async Task CambiarPin_NuevoObvioOIgualAlActual_SeRechaza()
    {
        var c = await CrearCiudadanoAsync();

        var obvio = await c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin(Pin, "123456"));
        var igual = await c.Cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin(Pin, Pin));

        Assert.Equal(HttpStatusCode.BadRequest, obvio.StatusCode);
        Assert.Equal(["El PIN nuevo debe ser distinto del actual."],
            (await igual.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["pinNuevo"]);
    }

    [Fact]
    public async Task CambiarPin_CuentaAntiguaConContrasena_NoTienePin()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var cliente = await fabrica.CrearClienteAppAsync(cuenta);

        var perfil = await cliente.GetFromJsonAsync<PerfilCiudadanoVista>("/api/cuentas/yo/perfil");
        var respuesta = await cliente.PostAsJsonAsync("/api/cuentas/yo/pin", new SolicitudCambioPin("482915", "730591"));

        Assert.False(perfil!.TienePin);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    [Fact]
    public async Task Panel_DetalleDelSos_MuestraLaFichaDelCiudadanoConSusDatosOpcionales()
    {
        var c = await CrearCiudadanoAsync();
        var dni = DniNuevo();
        await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(dni));
        var sos = await (await c.Cliente.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);

        var html = await navegador.GetStringAsync($"/panel/central/sos/{sos!.Id}");

        Assert.Contains("data-ficha-ciudadano", html);
        Assert.Contains($"DNI {dni}", html);
        Assert.Contains("· verificado", html);
        Assert.Contains("Marta Ficticia (Madre)", html);
        Assert.Contains("href=\"tel:911222333\"", html);
        Assert.Contains("Jr. Ficticio 123, Cercado de Lima", html);
        Assert.Contains(" años", html);
    }

    [Fact]
    public async Task Panel_CuentaAnteriorSinDatos_LosIndicaComoNoRegistrados()
    {
        var cliente = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await cliente.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);

        var html = await navegador.GetStringAsync($"/panel/central/sos/{sos!.Id}");

        // 05/11/2026: los que faltan se nombran juntos, en una línea.
        Assert.Contains("Sin registrar: contacto de emergencia, documento, edad y dirección.", html);
        Assert.DoesNotContain("<dt>Documento</dt>", html);
    }

    [Fact]
    public async Task Registros_NoContienenLosDatosDelCiudadano()
    {
        var c = await CrearCiudadanoAsync();
        var dni = DniNuevo();
        await c.Cliente.PutAsJsonAsync("/api/cuentas/yo/perfil", Completo(dni));

        foreach (var dato in new[] { dni, "Jr. Ficticio 123", "911222333", "Marta Ficticia" })
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato));
    }
}
