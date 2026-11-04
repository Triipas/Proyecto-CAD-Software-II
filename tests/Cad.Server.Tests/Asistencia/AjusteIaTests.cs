using System.Net;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Asistencia;

// HU45 (S6-T15; plan de asistencia con IA, 7.1, 7.2 y 7.8) — el interruptor de la admisión automática: el administrador cambia el
// modo con un motivo, el operador solo puede pausarlo, cada cambio queda en el historial y un aviso muestra el modo vigente en
// «Reportes» y «Central». El ajuste es global: cada prueba lo deja «Apagada» al terminar. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public sealed class AjusteIaTests(FabricaCad fabrica) : IAsyncLifetime
{
    public Task InitializeAsync() => VolverAApagadaAsync();

    public Task DisposeAsync() => VolverAApagadaAsync();

    private Task VolverAApagadaAsync() => fabrica.ConBaseDatosAsync(async db =>
        await db.Database.ExecuteSqlRawAsync("UPDATE \"AjustesIa\" SET \"Modo\" = 'Apagada', \"RetrasoSegundos\" = 30 WHERE \"Id\" = 1"));

    private Task<AjusteIa> AjusteAsync() => fabrica.ConBaseDatosAsync(db => db.AjustesIa.AsNoTracking().SingleAsync());

    private Task<List<CambioAjusteIa>> CambiosDeAsync(Guid actor) =>
        fabrica.ConBaseDatosAsync(db => db.CambiosAjusteIa.AsNoTracking().Where(c => c.ActorId == actor).ToListAsync());

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> ConSesionAsync(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> CambiarAsync(HttpClient navegador, string modo, string? motivo, string retraso = "30")
    {
        var campos = new Dictionary<string, string>
        {
            ["Formulario.Modo"] = modo,
            ["Formulario.RetrasoSegundos"] = retraso,
            ["Formulario.MaximoPorHora"] = "60",
            ["Formulario.LongitudMinimaDescripcion"] = "40",
            ["Formulario.FrenoMinimoAdmisiones"] = "20",
            ["Formulario.FrenoPorcentajeDescartes"] = "5",
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/admin/ia"),
        };
        if (motivo is not null) campos["Formulario.Motivo"] = motivo;
        return await navegador.PostAsync("/panel/admin/ia", new FormUrlEncodedContent(campos));
    }

    private static async Task<HttpResponseMessage> PausarAsync(HttpClient navegador) =>
        await navegador.PostAsync("/panel/reportes?handler=Pausar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/reportes"),
        }));

    [Fact]
    public async Task CA01_PorDefecto_ApagadaYSinAvisoEnReportesNiCentral()
    {
        Assert.Equal(ModoAdmision.Apagada, (await AjusteAsync()).Modo);
        var (operador, _) = await ConSesionAsync(Roles.Operador);

        Assert.DoesNotContain("data-admision-automatica", await operador.GetStringAsync("/panel/reportes"));
        Assert.DoesNotContain("data-admision-automatica", await operador.GetStringAsync("/panel/central"));
    }

    [Fact]
    public async Task CA06_CA07_ElAdministradorCambiaElModoConMotivoYQuedaEnElHistorial()
    {
        var (administrador, cuenta) = await ConSesionAsync(Roles.Administrador);
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await CambiarAsync(administrador, "Sombra", "  Medir la precisión antes de activarla  ", retraso: "45");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var ajuste = await AjusteAsync();
        Assert.Equal((ModoAdmision.Sombra, 45), (ajuste.Modo, ajuste.RetrasoSegundos));
        var cambio = Assert.Single(await CambiosDeAsync(cuenta.Id));
        Assert.Equal((ModoAdmision.Apagada, ModoAdmision.Sombra, "Medir la precisión antes de activarla"), (cambio.ModoAnterior, cambio.ModoNuevo, cambio.Motivo));
        Assert.InRange(cambio.En, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Contains("retraso 45 s", cambio.Parametros);

        var pagina = WebUtility.HtmlDecode(await administrador.GetStringAsync("/panel/admin/ia"));
        Assert.Contains("data-modo-vigente=\"Sombra\"", pagina);
        Assert.Contains("Medir la precisión antes de activarla", pagina);
        Assert.Contains("Prueba Administrador", pagina);
    }

    [Fact]
    public async Task CA07_SinMotivoOConValoresFueraDeRango_NoCambiaNada()
    {
        var (administrador, cuenta) = await ConSesionAsync(Roles.Administrador);

        var sinMotivo = WebUtility.HtmlDecode(await (await CambiarAsync(administrador, "Activa", null)).Content.ReadAsStringAsync());
        var fueraDeRango = WebUtility.HtmlDecode(await (await CambiarAsync(administrador, "Activa", "Probar", retraso: "0")).Content.ReadAsStringAsync());

        Assert.Contains("Indica el motivo del cambio.", sinMotivo);
        Assert.Contains("Indica un valor entre 5 y 600 segundos.", fueraDeRango);
        Assert.Equal(ModoAdmision.Apagada, (await AjusteAsync()).Modo);
        Assert.Empty(await CambiosDeAsync(cuenta.Id));
    }

    [Fact]
    public async Task CA06_ElOperadorVeElAvisoYSoloPuedePausar()
    {
        var (administrador, _) = await ConSesionAsync(Roles.Administrador);
        await CambiarAsync(administrador, "Activa", "Prueba de la demostración");
        var (operador, cuenta) = await ConSesionAsync(Roles.Operador);

        var reportes = WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/reportes"));
        Assert.Contains("data-admision-automatica=\"Activa\"", reportes);
        Assert.Contains("Admisión automática: «Activa»", reportes);
        Assert.Contains("data-pausar-admision", reportes);
        Assert.Contains("data-admision-automatica=\"Activa\"", await operador.GetStringAsync("/panel/central"));

        var respuesta = await PausarAsync(operador);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(ModoAdmision.Apagada, (await AjusteAsync()).Modo);
        var pausa = Assert.Single(await CambiosDeAsync(cuenta.Id));
        Assert.Equal((ModoAdmision.Activa, ModoAdmision.Apagada, CambiarAjusteIa.MotivoPausaOperador), (pausa.ModoAnterior, pausa.ModoNuevo, pausa.Motivo));
        Assert.DoesNotContain("data-admision-automatica", await operador.GetStringAsync("/panel/reportes"));

        // Pausar otra vez no registra un segundo cambio; y el operador no puede reactivarla (la página del administrador lo rechaza).
        await PausarAsync(operador);
        Assert.Single(await CambiosDeAsync(cuenta.Id));
        var reactivar = await operador.GetAsync("/panel/admin/ia");
        Assert.Equal(HttpStatusCode.Redirect, reactivar.StatusCode);
        Assert.Contains("/panel/acceso-denegado", reactivar.Headers.Location!.ToString());
    }
}

// S6-T05 (plan, 7.5 y CA09) — el usuario del sistema, actor de las acciones automáticas.
[Collection(ColeccionServidor.Nombre)]
public class UsuarioSistemaTests(FabricaCad fabrica)
{
    [Fact]
    public async Task ExisteSinContrasenaNiRolYDesactivado()
    {
        var usuario = await fabrica.ConBaseDatosAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == UsuarioSistema.Id));
        var roles = await fabrica.ConBaseDatosAsync(db => db.UserRoles.AsNoTracking().CountAsync(r => r.UserId == UsuarioSistema.Id));

        Assert.Equal((UsuarioSistema.Nombres, UsuarioSistema.Apellidos), (usuario.Nombres, usuario.Apellidos));
        Assert.Null(usuario.PasswordHash);
        Assert.Null(usuario.PinHash);
        Assert.Null(usuario.Email);
        Assert.False(usuario.Activo);
        Assert.Equal(0, roles);
    }

    [Fact]
    public async Task NoPuedeIniciarSesion()
    {
        var conCredenciales = await fabrica.ConServiciosAsync(async s =>
        {
            var usuarios = s.GetRequiredService<UserManager<Usuario>>();
            var usuario = await usuarios.FindByIdAsync(UsuarioSistema.Id.ToString());
            return await usuarios.CheckPasswordAsync(usuario!, "");
        });
        var panel = await fabrica.CrearNavegador().IngresarAlPanelAsync(UsuarioSistema.NombreUsuario, "cualquier-cosa-1");

        Assert.False(conCredenciales);
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode); // vuelve a mostrar el formulario con el error; no hay redirección a una sesión
        Assert.DoesNotContain(panel.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("cad.panel="));
    }

    [Fact]
    public async Task NoApareceEnLasListasDeCuentasNiDeSerenos()
    {
        var cuentas = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarCuentasInstitucionales>().ListarAsync(CancellationToken.None));
        var serenos = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarSerenos>().EjecutarAsync(CancellationToken.None));

        Assert.DoesNotContain(cuentas, c => c.Id == UsuarioSistema.Id);
        Assert.DoesNotContain(serenos, s => s.Id == UsuarioSistema.Id);
    }
}
