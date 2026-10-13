using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cad.Server.Tests.Cuentas;

// HU02 — Iniciar sesión (S1-T07), canal de la app móvil (JWT). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class InicioSesionAppTests(FabricaCad fabrica)
{
    private const string RutaToken = "/api/auth/token";
    private const string RutaPerfil = "/api/cuentas/yo";

    private async Task<HttpResponseMessage> PedirTokenAsync(string correo, string clave) =>
        await fabrica.CreateClient().PostAsJsonAsync(RutaToken, new SolicitudInicioSesion(correo, clave));

    private async Task<HttpResponseMessage> ConsultarPerfilAsync(string? token)
    {
        var cliente = fabrica.CreateClient();
        if (token is not null)
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await cliente.GetAsync(RutaPerfil);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA01_CredencialesValidas_EmiteTokenYAccedeConSuRol(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);

        var respuesta = await PedirTokenAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var token = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>();
        Assert.Equal(rol, token!.Rol);
        Assert.True(token.ExpiraEn > DateTimeOffset.UtcNow);

        var perfil = await ConsultarPerfilAsync(token.Token);
        Assert.Equal(HttpStatusCode.OK, perfil.StatusCode);
        var datos = await perfil.Content.ReadFromJsonAsync<PerfilUsuario>();
        Assert.Equal(cuenta.Id, datos!.Id);
        Assert.Equal(rol, datos.Rol);
    }

    [Fact]
    public async Task CA01_CiudadanoRegistradoPorHU01_PuedeIniciarSesion()
    {
        var correo = $"nuevo.{Guid.NewGuid():N}@correo.test";
        var registro = await fabrica.CreateClient().PostAsJsonAsync("/api/cuentas/registro",
            new SolicitudRegistroCiudadano("Luis", "Mamani", correo, "955444333", "MiClave2026"));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        // El correo se normaliza igual que en el registro.
        var respuesta = await PedirTokenAsync($" {correo.ToUpperInvariant()} ", "MiClave2026");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Administrador)]
    public async Task CA01_RolDelPanel_NoObtieneTokenDeLaApp(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);

        var respuesta = await PedirTokenAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("panel de central", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CA02_ContrasenaIncorrecta_NoConcedeAcceso()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);

        var respuesta = await PedirTokenAsync(cuenta.Correo, cuenta.Clave + "x");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains("Correo o contraseña incorrectos.", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CA02_CorreoInexistente_MismoRechazoQueContrasenaIncorrecta()
    {
        var respuesta = await PedirTokenAsync($"nadie.{Guid.NewGuid():N}@correo.test", "Cualquiera123");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Contains("Correo o contraseña incorrectos.", await respuesta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("", "Clave12345")]
    [InlineData("alguien@correo.test", "")]
    public async Task CA02_CredencialesVacias_NoConcedeAcceso(string correo, string clave)
    {
        var respuesta = await PedirTokenAsync(correo, clave);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CA02_SinToken_LaApiRechazaConEstado401()
    {
        var respuesta = await ConsultarPerfilAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task CA02_TokenFirmadoConOtraClave_LaApiLoRechaza()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var falsificado = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "cad-servidor",
            Audience = "cad-movil",
            Subject = new ClaimsIdentity([new Claim("sub", cuenta.Id.ToString()), new Claim("role", Roles.Operador)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                SecurityAlgorithms.HmacSha256),
        });

        var respuesta = await ConsultarPerfilAsync(falsificado);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA03_CuentaDesactivada_NoConcedeAcceso(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol, activa: false);

        var respuesta = await PedirTokenAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("desactivada", cuerpo);
        Assert.DoesNotContain("token", cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CA04_IntentosValidosEInvalidos_NingunaContrasenaNiTokenEnLosRegistros()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var claveIncorrecta = $"Incorrecta-{Guid.NewGuid():N}";

        var valido = await PedirTokenAsync(cuenta.Correo, cuenta.Clave);
        var token = (await valido.Content.ReadFromJsonAsync<RespuestaToken>())!.Token;
        await PedirTokenAsync(cuenta.Correo, claveIncorrecta);
        await ConsultarPerfilAsync(token);

        foreach (var secreto in new[] { cuenta.Clave, claveIncorrecta, token, cuenta.Correo })
            Assert.DoesNotContain(fabrica.Registros.Entradas, entrada => entrada.Contains(secreto, StringComparison.OrdinalIgnoreCase));
    }
}
