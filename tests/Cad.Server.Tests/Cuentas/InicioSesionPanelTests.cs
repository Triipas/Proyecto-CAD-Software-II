using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Cuentas;

// HU02 — Iniciar sesión (S1-T07), canal del panel web (cookie). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class InicioSesionPanelTests(FabricaCad fabrica)
{
    private static bool TieneCookieDeSesion(HttpResponseMessage respuesta) =>
        respuesta.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("cad.panel="));

    [Fact]
    public async Task CA01_Operador_EntraASuInterfazDeCentral()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();

        var ingreso = await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.Redirect, ingreso.StatusCode);
        Assert.Equal("/panel/central", ingreso.Headers.Location?.OriginalString);
        var cookie = Assert.Single(ingreso.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cad.panel="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var central = await navegador.GetAsync("/panel/central");
        Assert.Equal(HttpStatusCode.OK, central.StatusCode);
        Assert.Contains("Central de monitoreo", await central.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CA01_AdministradorInicialSembrado_EntraASuInterfazDeAdministracion()
    {
        var navegador = fabrica.CrearNavegador();

        var ingreso = await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);

        Assert.Equal(HttpStatusCode.Redirect, ingreso.StatusCode);
        Assert.Equal("/panel/admin", ingreso.Headers.Location?.OriginalString);
        var admin = await navegador.GetAsync("/panel/admin");
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Contains("Administración", await admin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CA01_Operador_NoEntraALaInterfazDelAdministrador()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var admin = await navegador.GetAsync("/panel/admin");

        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.StartsWith("https://localhost/panel/acceso-denegado", admin.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA01_RolDeLaApp_NoEntraAlPanel(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);

        var ingreso = await fabrica.CrearNavegador().IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        var html = await ingreso.Content.ReadAsStringAsync();
        // Mensaje completo: la página incluye además una nota general que menciona la aplicación móvil.
        Assert.Contains("Esta cuenta debe ingresar desde la aplicación móvil.", html);
        Assert.Contains("aviso--informacion", html);
        Assert.DoesNotContain("aria-invalid", html);
        Assert.False(TieneCookieDeSesion(ingreso));
    }

    [Fact]
    public async Task CA01_SinSesion_LasPaginasDelPanelRedirigenAlIngreso()
    {
        var respuesta = await fabrica.CrearNavegador().GetAsync("/panel/central");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.StartsWith("https://localhost/panel/ingresar", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CA01_CookieDelPanel_PermiteConsultarLaApiConSuRol()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var perfil = await navegador.GetFromJsonAsync<PerfilUsuario>("/api/cuentas/yo");

        Assert.Equal(Roles.Operador, perfil!.Rol);
    }

    [Fact]
    public async Task CA02_ContrasenaIncorrecta_NoConcedeAcceso()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();

        var ingreso = await navegador.IngresarAlPanelAsync(cuenta.Correo, "ClaveEquivocada1");

        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        var html = await ingreso.Content.ReadAsStringAsync();
        Assert.Contains("Correo o contraseña incorrectos.", html);
        Assert.DoesNotContain("ClaveEquivocada1", html);
        // El error no depende solo del color: aviso con tipo y campos marcados y enlazados al mensaje.
        Assert.Contains("aviso--error", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Equal(2, CuentasDePrueba.Contar(html, "aria-invalid=\"true\""));
        Assert.Equal(2, CuentasDePrueba.Contar(html, "aria-describedby=\"aviso-ingreso\""));
        Assert.False(TieneCookieDeSesion(ingreso));
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.GetAsync("/panel/central")).StatusCode);
    }

    [Fact]
    public async Task CA02_EnvioSinTokenAntifalsificacion_SeRechaza()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);

        var ingreso = await fabrica.CrearNavegador().PostAsync("/panel/ingresar", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Formulario.Correo"] = cuenta.Correo, ["Formulario.Contrasena"] = cuenta.Clave }));

        Assert.Equal(HttpStatusCode.BadRequest, ingreso.StatusCode);
        Assert.False(TieneCookieDeSesion(ingreso));
    }

    [Fact]
    public async Task CA03_OperadorDesactivado_NoConcedeAcceso()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador, activa: false);

        var ingreso = await fabrica.CrearNavegador().IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = await ingreso.Content.ReadAsStringAsync();
        Assert.Contains("La cuenta está desactivada.", html);
        Assert.Contains("aviso--advertencia", html);
        Assert.DoesNotContain("aria-invalid", html);
        Assert.False(TieneCookieDeSesion(ingreso));
    }

    [Theory]
    [InlineData("/css/tokens.css", "text/css")]
    [InlineData("/css/panel.css", "text/css")]
    [InlineData("/js/formulario.js", "text/javascript")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    public async Task Rediseno_RecursosDelPanel_SeSirvenSinSesion(string ruta, string tipo)
    {
        var respuesta = await fabrica.CrearNavegador().GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(tipo, respuesta.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Rediseno_PaginaDeIngreso_EnlazaEstilosScriptYFavicon()
    {
        var html = await fabrica.CrearNavegador().GetStringAsync("/panel/ingresar");

        Assert.Contains("/css/tokens.css", html);
        Assert.Contains("/css/panel.css", html);
        Assert.Contains("/js/formulario.js", html);
        Assert.Contains("/favicon.svg", html);
        Assert.Contains("no es un canal oficial de emergencias", html);
    }

    [Fact]
    public async Task Salir_CierraLaSesionDelPanel()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/central");

        var salida = await navegador.PostAsync("/panel/salir",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, salida.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.GetAsync("/panel/central")).StatusCode);
    }

    [Fact]
    public async Task CA04_IngresosDelPanel_NingunaContrasenaEnLosRegistros()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var claveIncorrecta = $"Incorrecta-{Guid.NewGuid():N}";
        var navegador = fabrica.CrearNavegador();

        await navegador.IngresarAlPanelAsync(cuenta.Correo, claveIncorrecta);
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        foreach (var secreto in new[] { cuenta.Clave, claveIncorrecta, fabrica.AdminClave, cuenta.Correo })
            Assert.DoesNotContain(fabrica.Registros.Entradas, entrada => entrada.Contains(secreto, StringComparison.OrdinalIgnoreCase));
    }
}
