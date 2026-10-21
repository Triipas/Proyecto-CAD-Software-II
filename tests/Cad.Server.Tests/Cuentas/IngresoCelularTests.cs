using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Cuentas;

// ADR-08 — registro e ingreso del ciudadano con celular verificado y PIN (HU01 v2, HU02 v2). Números y datos ficticios;
// los códigos son siempre simulados.
[Collection(ColeccionServidor.Nombre)]
public class IngresoCelularTests(FabricaCad fabrica)
{
    private const string Pin = "482915";

    // Números ficticios distintos en cada prueba: 9 dígitos que empiezan por 9.
    private static string NumeroNuevo() => "9" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();

    private static async Task<T> LeerAsync<T>(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<T>())!;

    private static async Task<string> TituloAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<ProblemDetails>())!.Title!;

    private static Task<HttpResponseMessage> PedirCodigoAsync(HttpClient cliente, string celular) =>
        cliente.PostAsJsonAsync("/api/auth/celular/codigo", new SolicitudCodigoCelular(celular));

    private static Task<HttpResponseMessage> VerificarAsync(HttpClient cliente, string celular, string codigo) =>
        cliente.PostAsJsonAsync("/api/auth/celular/verificar", new SolicitudVerificacionCelular(celular, codigo));

    private static async Task<RespuestaVerificacionCelular> VerificarNumeroAsync(HttpClient cliente, string celular)
    {
        var codigo = await PedirCodigoAsync(cliente, celular);
        Assert.Equal(HttpStatusCode.OK, codigo.StatusCode);
        var enviado = await LeerAsync<RespuestaCodigoCelular>(codigo);
        var verificacion = await VerificarAsync(cliente, celular, enviado.CodigoDePrueba!);
        Assert.Equal(HttpStatusCode.OK, verificacion.StatusCode);
        return await LeerAsync<RespuestaVerificacionCelular>(verificacion);
    }

    private static async Task<RespuestaIngresoCelular> RegistrarAsync(HttpClient cliente, string celular, string pin = Pin)
    {
        var verificacion = await VerificarNumeroAsync(cliente, celular);
        var registro = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia Quispe", pin, AceptaTerminos: true));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        return await LeerAsync<RespuestaIngresoCelular>(registro);
    }

    private static Task<HttpResponseMessage> IngresarAsync(HttpClient cliente, string celular, string pin, string? llave, string? token = null) =>
        cliente.PostAsJsonAsync("/api/auth/celular/ingresar", new SolicitudIngresoCelular(celular, pin, llave, token));

    private static async Task<HttpStatusCode> ConsultarPerfilAsync(HttpClient cliente, string token)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/api/cuentas/yo");
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await cliente.SendAsync(solicitud)).StatusCode;
    }

    [Fact]
    public async Task Registro_NumeroNuevo_CreaCiudadanoSinCorreoConCelularVerificadoYSesion()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();

        var verificacion = await VerificarNumeroAsync(cliente, $"+51 {celular[..3]} {celular[3..6]} {celular[6..]}");
        Assert.False(verificacion.CuentaExiste);
        Assert.False(verificacion.PuedeRegistrarDeNuevo);

        var registro = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, " Rosa ", "Ficticia Quispe", Pin, AceptaTerminos: true));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);
        var sesion = await LeerAsync<RespuestaIngresoCelular>(registro);

        Assert.Equal(Roles.Ciudadano, sesion.Rol);
        Assert.False(string.IsNullOrEmpty(sesion.LlaveDispositivo));
        Assert.True(sesion.ExpiraEn > DateTimeOffset.UtcNow.AddDays(29)); // sesión larga del ciudadano
        Assert.Equal(HttpStatusCode.OK, await ConsultarPerfilAsync(cliente, sesion.Token));

        var cuenta = await fabrica.ConBaseDatosAsync(db => db.Users.SingleAsync(u => u.UserName == celular));
        Assert.Null(cuenta.Email);
        Assert.Null(cuenta.PasswordHash);
        Assert.Equal(celular, cuenta.PhoneNumber);
        Assert.True(cuenta.PhoneNumberConfirmed);
        Assert.Equal("Rosa", cuenta.Nombres);
        Assert.NotNull(cuenta.TerminosAceptadosEn);
        Assert.False(string.IsNullOrEmpty(cuenta.TerminosVersion));
        Assert.DoesNotContain(Pin, cuenta.PinHash);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("812345678")]
    [InlineData("abc")]
    public async Task Codigo_CelularInvalido_SeRechaza(string celular)
    {
        var respuesta = await PedirCodigoAsync(fabrica.CreateClient(), celular);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await LeerAsync<ValidationProblemDetails>(respuesta);
        Assert.Contains("celular", problema.Errors.Keys);
    }

    [Fact]
    public async Task Codigo_ReenvioAntesDeLaEspera_SeRechazaConElTiempoQueFalta()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        Assert.Equal(HttpStatusCode.OK, (await PedirCodigoAsync(cliente, celular)).StatusCode);

        var segundo = await PedirCodigoAsync(cliente, celular);

        Assert.Equal(HttpStatusCode.TooManyRequests, segundo.StatusCode);
        Assert.StartsWith("Espera ", await TituloAsync(segundo));
    }

    [Fact]
    public async Task Verificar_CodigoIncorrecto_DescuentaIntentosYAlAgotarlosLoAnula()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var enviado = await LeerAsync<RespuestaCodigoCelular>(await PedirCodigoAsync(cliente, celular));
        var incorrecto = enviado.CodigoDePrueba == "000000" ? "000001" : "000000";

        var primero = await VerificarAsync(cliente, celular, incorrecto);
        Assert.Equal(HttpStatusCode.BadRequest, primero.StatusCode);
        Assert.Equal("Código incorrecto. Te quedan 4 intentos.", await TituloAsync(primero));

        for (var i = 0; i < 4; i++)
            await VerificarAsync(cliente, celular, incorrecto);

        // Agotados los intentos, ni el código correcto sirve: hay que pedir otro.
        var correcto = await VerificarAsync(cliente, celular, enviado.CodigoDePrueba!);
        Assert.Equal(HttpStatusCode.Gone, correcto.StatusCode);
    }

    [Fact]
    public async Task Registro_TokenDeVerificacion_SoloSirveUnaVez()
    {
        var cliente = fabrica.CreateClient();
        var verificacion = await VerificarNumeroAsync(cliente, NumeroNuevo());
        var solicitud = new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia", Pin, true);
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/auth/celular/registro", solicitud)).StatusCode);

        var repetido = await cliente.PostAsJsonAsync("/api/auth/celular/registro", solicitud);

        Assert.Equal(HttpStatusCode.Gone, repetido.StatusCode);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("654321")]
    [InlineData("111111")]
    [InlineData("12345")]
    [InlineData("12a456")]
    public async Task Registro_PinObvioOMalFormado_SeRechazaSinGastarElToken(string pin)
    {
        var cliente = fabrica.CreateClient();
        var verificacion = await VerificarNumeroAsync(cliente, NumeroNuevo());

        var respuesta = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia", pin, true));
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("pin", (await LeerAsync<ValidationProblemDetails>(respuesta)).Errors.Keys);

        // Con un PIN válido, el mismo token aún sirve.
        var corregido = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia", Pin, true));
        Assert.Equal(HttpStatusCode.Created, corregido.StatusCode);
    }

    [Fact]
    public async Task Registro_SinAceptarTerminos_SeRechaza()
    {
        var cliente = fabrica.CreateClient();
        var verificacion = await VerificarNumeroAsync(cliente, NumeroNuevo());

        var respuesta = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia", Pin, AceptaTerminos: false));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("aceptaTerminos", (await LeerAsync<ValidationProblemDetails>(respuesta)).Errors.Keys);
    }

    [Fact]
    public async Task Registro_NumeroConCuenta_EnLaPresentacion_SeRechaza()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        await RegistrarAsync(cliente, celular);
        await fabrica.ConBaseDatosAsync(db => db.VerificacionesCelular.Where(v => v.Celular == celular).ExecuteDeleteAsync());

        var verificacion = await VerificarNumeroAsync(cliente, celular);
        Assert.True(verificacion.CuentaExiste);
        Assert.False(verificacion.PuedeRegistrarDeNuevo);

        var repetido = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Otra", "Persona", Pin, true));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Fact]
    public async Task Registro_NumeroConCuenta_EnDesarrollo_LiberaElNumeroYDesactivaLaCuentaAnterior()
    {
        using var desarrollo = fabrica.WithWebHostBuilder(b => b.UseSetting("Cuentas:PermitirReRegistroCelular", "true"));
        var cliente = desarrollo.CreateClient();
        var celular = NumeroNuevo();
        var primera = await RegistrarAsync(cliente, celular);
        await fabrica.ConBaseDatosAsync(db => db.VerificacionesCelular.Where(v => v.Celular == celular).ExecuteDeleteAsync());

        var verificacion = await VerificarNumeroAsync(cliente, celular);
        Assert.True(verificacion.PuedeRegistrarDeNuevo);
        var segunda = await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Nueva", "Cuenta", "730591", true));

        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ConsultarPerfilAsync(cliente, primera.Token));
        var cuentas = await fabrica.ConBaseDatosAsync(db => db.Users
            .Where(u => u.PhoneNumber == celular).OrderBy(u => u.CreadoEn).ToListAsync());
        Assert.Equal(2, cuentas.Count);
        Assert.False(cuentas[0].Activo);
        Assert.StartsWith("liberado-", cuentas[0].UserName);
        Assert.True(cuentas[1].Activo);
        Assert.Equal(celular, cuentas[1].UserName);
    }

    [Fact]
    public async Task Ingresar_MismoTelefonoConPinCorrecto_EntraSinCodigo()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var registro = await RegistrarAsync(cliente, celular);

        var ingreso = await IngresarAsync(cliente, celular, Pin, registro.LlaveDispositivo);

        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        var sesion = await LeerAsync<RespuestaIngresoCelular>(ingreso);
        Assert.Null(sesion.LlaveDispositivo); // el teléfono ya estaba vinculado
        Assert.Equal(HttpStatusCode.OK, await ConsultarPerfilAsync(cliente, sesion.Token));
    }

    [Fact]
    public async Task Ingresar_SinLlaveNiCodigo_PideVerificarElTelefono()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        await RegistrarAsync(cliente, celular);

        Assert.Equal(HttpStatusCode.PreconditionRequired, (await IngresarAsync(cliente, celular, Pin, null)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await IngresarAsync(cliente, celular, Pin, "llave-inventada")).StatusCode);
    }

    [Fact]
    public async Task Ingresar_PinIncorrecto_CincoVeces_BloqueaInclusoElPinCorrecto()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var registro = await RegistrarAsync(cliente, celular);

        var primero = await IngresarAsync(cliente, celular, "000999", registro.LlaveDispositivo);
        Assert.Equal(HttpStatusCode.Unauthorized, primero.StatusCode);
        Assert.Equal("PIN incorrecto. Te quedan 4 intentos.", await TituloAsync(primero));
        for (var i = 0; i < 3; i++)
            await IngresarAsync(cliente, celular, "000999", registro.LlaveDispositivo);
        Assert.Equal(HttpStatusCode.Locked, (await IngresarAsync(cliente, celular, "000999", registro.LlaveDispositivo)).StatusCode);

        Assert.Equal(HttpStatusCode.Locked, (await IngresarAsync(cliente, celular, Pin, registro.LlaveDispositivo)).StatusCode);
    }

    [Fact]
    public async Task RestablecerPin_ConCodigo_DesbloqueaVinculaElTelefonoYCierraSesionesAnteriores()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var registro = await RegistrarAsync(cliente, celular);
        for (var i = 0; i < Usuario.MaximoIntentosPin; i++)
            await IngresarAsync(cliente, celular, "000999", registro.LlaveDispositivo);
        await fabrica.ConBaseDatosAsync(db => db.VerificacionesCelular.Where(v => v.Celular == celular).ExecuteDeleteAsync());

        var verificacion = await VerificarNumeroAsync(cliente, celular);
        var restablecido = await cliente.PostAsJsonAsync("/api/auth/celular/pin",
            new SolicitudRestablecerPin(verificacion.TokenVerificacion, "305718"));

        Assert.Equal(HttpStatusCode.OK, restablecido.StatusCode);
        var sesion = await LeerAsync<RespuestaIngresoCelular>(restablecido);
        Assert.Equal(HttpStatusCode.Unauthorized, await ConsultarPerfilAsync(cliente, registro.Token));
        Assert.Equal(HttpStatusCode.OK, (await IngresarAsync(cliente, celular, "305718", sesion.LlaveDispositivo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await IngresarAsync(cliente, celular, Pin, sesion.LlaveDispositivo)).StatusCode);
    }

    [Fact]
    public async Task Ingresar_DesdeOtroTelefono_LoVinculaYCierraLaSesionDelAnterior()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var primerTelefono = await RegistrarAsync(cliente, celular);
        await fabrica.ConBaseDatosAsync(db => db.VerificacionesCelular.Where(v => v.Celular == celular).ExecuteDeleteAsync());

        var verificacion = await VerificarNumeroAsync(cliente, celular);
        Assert.True(verificacion.CuentaExiste);
        var ingreso = await IngresarAsync(cliente, celular, Pin, null, verificacion.TokenVerificacion);

        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        var segundoTelefono = await LeerAsync<RespuestaIngresoCelular>(ingreso);
        Assert.False(string.IsNullOrEmpty(segundoTelefono.LlaveDispositivo));
        // DP24: un solo teléfono. El anterior pierde su sesión y su llave.
        Assert.Equal(HttpStatusCode.Unauthorized, await ConsultarPerfilAsync(cliente, primerTelefono.Token));
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await IngresarAsync(cliente, celular, Pin, primerTelefono.LlaveDispositivo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ConsultarPerfilAsync(cliente, segundoTelefono.Token));
    }

    [Fact]
    public async Task Ingresar_ConTokenDeOtroNumero_SeRechaza()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        await RegistrarAsync(cliente, celular);
        var otro = await VerificarNumeroAsync(cliente, NumeroNuevo());

        var respuesta = await IngresarAsync(cliente, celular, Pin, null, otro.TokenVerificacion);

        Assert.Equal(HttpStatusCode.Gone, respuesta.StatusCode);
    }

    [Fact]
    public async Task Codigo_SinSimuladoNiSmsConfigurado_NoEstaDisponible()
    {
        using var publicado = fabrica.WithWebHostBuilder(b => b.UseSetting("Codigos:PermitirSimulado", "false"));

        var respuesta = await PedirCodigoAsync(publicado.CreateClient(), NumeroNuevo());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
    }

    [Fact]
    public async Task Codigo_ConSmsReal_NoDevuelveElCodigoYLoValidaElProveedor_SalvoLosPrefijosSimulados()
    {
        var sms = new SmsDePrueba();
        using var conSms = fabrica.WithWebHostBuilder(b =>
        {
            b.UseSetting("Codigos:PrefijosSimulados:0", "90000");
            b.ConfigureTestServices(s => s.AddSingleton<IVerificacionSms>(sms));
        });
        var cliente = conSms.CreateClient();
        var celular = NumeroNuevo().Replace("90000", "91000");

        var enviado = await LeerAsync<RespuestaCodigoCelular>(await PedirCodigoAsync(cliente, celular));
        Assert.Null(enviado.CodigoDePrueba);
        Assert.Equal($"+51{celular}", Assert.Single(sms.Enviados));
        Assert.Equal(HttpStatusCode.OK, (await VerificarAsync(cliente, celular, SmsDePrueba.CodigoValido)).StatusCode);

        // Los números del emulador (prefijo simulado) no gastan SMS.
        var emulador = "90000" + Random.Shared.Next(1000, 9999);
        var simulado = await LeerAsync<RespuestaCodigoCelular>(await PedirCodigoAsync(cliente, emulador));
        Assert.NotNull(simulado.CodigoDePrueba);
        Assert.Single(sms.Enviados);
    }

    [Fact]
    public async Task Registros_NoContienenCelularCodigoPinNiTokens()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        var enviado = await LeerAsync<RespuestaCodigoCelular>(await PedirCodigoAsync(cliente, celular));
        var verificacion = await LeerAsync<RespuestaVerificacionCelular>(await VerificarAsync(cliente, celular, enviado.CodigoDePrueba!));
        var registro = await LeerAsync<RespuestaIngresoCelular>(await cliente.PostAsJsonAsync("/api/auth/celular/registro",
            new SolicitudRegistroCelular(verificacion.TokenVerificacion, "Rosa", "Ficticia", Pin, true)));
        await IngresarAsync(cliente, celular, "000999", registro.LlaveDispositivo);

        foreach (var dato in new[] { celular, enviado.CodigoDePrueba!, Pin, verificacion.TokenVerificacion, registro.LlaveDispositivo!, registro.Token })
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato));
    }

    [Fact]
    public async Task Pin_SeGuardaConElHashDeIdentity()
    {
        var cliente = fabrica.CreateClient();
        var celular = NumeroNuevo();
        await RegistrarAsync(cliente, celular);

        var verificado = await fabrica.ConServiciosAsync(async s =>
        {
            var usuario = await s.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(celular);
            return s.GetRequiredService<IPasswordHasher<Usuario>>().VerifyHashedPassword(usuario!, usuario!.PinHash!, Pin);
        });
        Assert.NotEqual(PasswordVerificationResult.Failed, verificado);
    }

    private sealed class SmsDePrueba : IVerificacionSms
    {
        public const string CodigoValido = "246810";
        public List<string> Enviados { get; } = [];
        public bool Configurada => true;

        public Task<bool> EnviarAsync(string celularE164, CancellationToken ct)
        {
            lock (Enviados) Enviados.Add(celularE164);
            return Task.FromResult(true);
        }

        public Task<bool> ComprobarAsync(string celularE164, string codigo, CancellationToken ct) =>
            Task.FromResult(codigo == CodigoValido);
    }
}
