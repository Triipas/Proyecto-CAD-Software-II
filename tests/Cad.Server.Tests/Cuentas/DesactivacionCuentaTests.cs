using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cad.Server.Tests.Cuentas;

// HU05 — Desactivar una cuenta institucional (S4-T12, S4-T13). Datos ficticios.
// CA04: decisión del PO del 16/10/2026 (DP03): la sesión abierta se cierra y la asignación vigente se cancela.
[Collection(ColeccionServidor.Nombre)]
public class DesactivacionCuentaTests(FabricaCad fabrica)
{
    private static string RutaEditar(Guid id) => $"/panel/admin/cuentas/{id}/editar";

    private async Task<HttpClient> AdministradorAsync()
    {
        var navegador = fabrica.CrearNavegador();
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave)).StatusCode);
        return navegador;
    }

    // Envía la confirmación de la desactivación con el token antifalsificación de la página de la cuenta.
    // paginaToken: para un perfil que no puede abrir la ficha, la página de la que toma un token válido de su propia sesión.
    private static async Task<HttpResponseMessage> DesactivarAsync(HttpClient navegador, Guid id, string? paginaToken = null) =>
        await navegador.PostAsync($"{RutaEditar(id)}?handler=Desactivar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, paginaToken ?? RutaEditar(id)),
        }));

    private Task<bool> ActivaAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Activo).SingleAsync());

    [Theory]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Sereno)]
    public async Task CA01_CuentaActivaDeOperadorOSereno_PasaADesactivada(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var admin = await AdministradorAsync();
        Assert.Contains("Confirmar la desactivación", await admin.GetStringAsync(RutaEditar(cuenta.Id)));

        var respuesta = await DesactivarAsync(admin, cuenta.Id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/panel/admin/cuentas", respuesta.Headers.Location?.OriginalString);
        Assert.False(await ActivaAsync(cuenta.Id));
        var listado = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/panel/admin/cuentas"));
        Assert.Contains("Se desactivó la cuenta", listado);
        var fila = listado[listado.IndexOf($"data-cuenta=\"{cuenta.Id}\"", StringComparison.Ordinal)..];
        Assert.Contains("Desactivada", fila[..fila.IndexOf("</tr>", StringComparison.Ordinal)]);
        // La ficha ya no ofrece desactivarla y muestra su estado.
        var ficha = await admin.GetStringAsync(RutaEditar(cuenta.Id));
        Assert.DoesNotContain("Confirmar la desactivación", ficha);
        Assert.Contains("Desactivada", ficha);
    }

    [Fact]
    public async Task CA01_DesactivarDeNuevo_NoFallaYLaCuentaSigueDesactivada()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var admin = await AdministradorAsync();
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(admin, RutaEditar(cuenta.Id));
        var formulario = () => new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

        // Un reenvío del mismo formulario y dos envíos a la vez: todos terminan bien y la cuenta queda desactivada.
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            admin.PostAsync($"{RutaEditar(cuenta.Id)}?handler=Desactivar", formulario())));
        var reenvio = await admin.PostAsync($"{RutaEditar(cuenta.Id)}?handler=Desactivar", formulario());

        Assert.All(respuestas.Append(reenvio), r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.False(await ActivaAsync(cuenta.Id));
    }

    // Otro administrador desactiva la misma cuenta entre la lectura y el guardado (cambia su sello de concurrencia).
    private sealed class DesactivacionDeOtroAdministrador(string conexion, Guid usuarioId) : SaveChangesInterceptor
    {
        private int restantes = 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (restantes-- > 0)
            {
                await using var otra = new NpgsqlConnection(conexion);
                await otra.OpenAsync(ct);
                await using var orden = new NpgsqlCommand(
                    "UPDATE \"AspNetUsers\" SET \"Activo\" = false, \"ConcurrencyStamp\" = gen_random_uuid()::text WHERE \"Id\" = @id", otra);
                orden.Parameters.AddWithValue("id", usuarioId);
                await orden.ExecuteNonQueryAsync(ct);
            }
            return resultado;
        }
    }

    [Fact]
    public async Task CA01_DesactivacionSimultanea_ElConflictoSeVuelveAEvaluarSinError()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var db = new Cad.Server.Persistencia.CadDbContext(new DbContextOptionsBuilder<Cad.Server.Persistencia.CadDbContext>()
            .UseNpgsql(conexion).AddInterceptors(new DesactivacionDeOtroAdministrador(conexion, cuenta.Id)).Options);

        var resultado = await fabrica.ConServiciosAsync(async s =>
        {
            var usuarios = new UserManager<Usuario>(
                new Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<Usuario, IdentityRole<Guid>, Cad.Server.Persistencia.CadDbContext, Guid>(db),
                s.GetRequiredService<IOptions<IdentityOptions>>(), s.GetRequiredService<IPasswordHasher<Usuario>>(),
                s.GetServices<IUserValidator<Usuario>>(), s.GetServices<IPasswordValidator<Usuario>>(),
                s.GetRequiredService<ILookupNormalizer>(), s.GetRequiredService<IdentityErrorDescriber>(), s,
                NullLogger<UserManager<Usuario>>.Instance);
            var desactivar = new Cad.Server.Modulos.Cuentas.Aplicacion.DesactivarCuentaInstitucional(db, usuarios,
                new Cad.Server.Modulos.Cuentas.Aplicacion.ConsultarCuentasInstitucionales(db), TimeProvider.System,
                s.GetRequiredService<Cad.Server.Compartido.INotificador>(),
                NullLogger<Cad.Server.Modulos.Cuentas.Aplicacion.DesactivarCuentaInstitucional>.Instance);
            return await desactivar.EjecutarAsync(cuenta.Id, Guid.Empty, CancellationToken.None);
        });

        Assert.IsType<Cad.Server.Modulos.Cuentas.Aplicacion.ResultadoCuentaInstitucional.Correcto>(resultado);
        Assert.False(await ActivaAsync(cuenta.Id));
    }

    [Fact]
    public async Task CA02_SerenoDesactivado_NoObtieneTokenConCredencialesCorrectas()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
            new SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).StatusCode);

        await DesactivarAsync(await AdministradorAsync(), cuenta.Id);
        var respuesta = await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token", new SolicitudInicioSesion(cuenta.Correo, cuenta.Clave));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("La cuenta está desactivada.", cuerpo);
        Assert.DoesNotContain("\"token\"", cuerpo);
    }

    [Fact]
    public async Task CA02_OperadorDesactivado_NoIngresaAlPanel()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        await DesactivarAsync(await AdministradorAsync(), cuenta.Id);

        var navegador = fabrica.CrearNavegador();
        var ingreso = await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        Assert.NotEqual(HttpStatusCode.Redirect, ingreso.StatusCode);
        Assert.Contains("La cuenta está desactivada.", System.Net.WebUtility.HtmlDecode(await ingreso.Content.ReadAsStringAsync()));
        Assert.False(ingreso.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("cad.panel=")));
        // Sin sesión: la central lo lleva al ingreso.
        Assert.Contains("/panel/ingresar", (await navegador.GetAsync("/panel/central")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CA03_SerenoConActuacionesPrevias_SusRegistrosSiguenDisponiblesEnElHistorial()
    {
        // Un sereno atiende y cierra un SOS (Sprint 2); después se desactiva su cuenta.
        var serenoCuenta = await fabrica.CrearAsync(Roles.Sereno);
        var apellido = $"Desactivado{Guid.NewGuid():N}"[..20];
        await fabrica.ConServiciosAsync(async s =>
        {
            var usuarios = s.GetRequiredService<UserManager<Usuario>>();
            var usuario = (await usuarios.FindByIdAsync(serenoCuenta.Id.ToString()))!;
            usuario.Apellidos = apellido;
            return await usuarios.UpdateAsync(usuario);
        });
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        var casoId = (await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            new UbicacionSos(-12.05, -77.03, 8, ahora.AddSeconds(1))))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var sereno = await fabrica.CrearClienteAppAsync(serenoCuenta);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var operadorCuenta = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<Cad.Server.Modulos.Casos.Aplicacion.ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<Cad.Server.Modulos.Casos.Aplicacion.AsignarCaso>().EjecutarAsync(casoId, serenoCuenta.Id, operadorCuenta.Id, Guid.NewGuid(), CancellationToken.None)));
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre",
            new SolicitudCierreCaso(CategoriaIncidencia.Iniciales[1].Id, "Se resolvió en el lugar (ficticio)", null))).StatusCode);
        var eventosAntes = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.ActorId == serenoCuenta.Id));

        await DesactivarAsync(await AdministradorAsync(), serenoCuenta.Id);

        // Ningún registro vinculado con la cuenta se borra ni cambia.
        Assert.Equal(eventosAntes, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.ActorId == serenoCuenta.Id)));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync(a => a.SerenoId == serenoCuenta.Id)));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.CierresCaso.CountAsync(c => c.ResponsableId == serenoCuenta.Id)));
        // El operador consulta el historial y la búsqueda de casos: las actuaciones del sereno siguen identificadas.
        var operador = fabrica.CrearNavegador();
        await operador.IngresarAlPanelAsync(operadorCuenta.Correo, operadorCuenta.Clave);
        var historial = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos/{casoId}/historial"));
        Assert.Contains($"Asignado al sereno Prueba {apellido}", historial);
        Assert.True(CuentasDePrueba.Contar(historial, $"Prueba {apellido}") >= 4, "Faltan actuaciones del sereno desactivado en el historial.");
        Assert.Contains(casoId.ToString(), await operador.GetStringAsync($"/panel/casos?Sereno={serenoCuenta.Id}"));
    }

    // Un SOS asignado a un sereno operativo, que ya confirmó que va en camino (HU20, HU22).
    private async Task<(Guid CasoId, CuentaDePrueba Sereno, HttpClient SerenoApp)> CasoEnCaminoAsync()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        var casoId = (await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            new UbicacionSos(-12.05, -77.03, 8, ahora.AddSeconds(1))))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var (serenoApp, sereno) = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<Cad.Server.Modulos.Casos.Aplicacion.ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<Cad.Server.Modulos.Casos.Aplicacion.AsignarCaso>().EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        return (casoId, sereno, serenoApp);
    }

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> SerenoOperativoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return (app, cuenta);
    }

    [Fact]
    public async Task CA04_SesionAbiertaEnLaApp_SeCierraAlDesactivar()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Sereno));
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/cuentas/yo")).StatusCode);

        await DesactivarAsync(await AdministradorAsync(), cuenta.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/cuentas/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/sereno/estado")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otro.GetAsync("/api/cuentas/yo")).StatusCode); // las demás sesiones siguen
    }

    [Fact]
    public async Task CA04_SesionAbiertaEnElPanel_SeCierraAlDesactivar()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var panel = fabrica.CrearNavegador();
        await panel.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        Assert.Equal(HttpStatusCode.OK, (await panel.GetAsync("/panel/central")).StatusCode);

        await DesactivarAsync(await AdministradorAsync(), cuenta.Id);

        var despues = await panel.GetAsync("/panel/central");
        Assert.Equal(HttpStatusCode.Redirect, despues.StatusCode);
        Assert.Contains("/panel/ingresar", despues.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await panel.GetAsync("/api/cuentas/yo")).StatusCode);
    }

    [Fact]
    public async Task CA04_AsignacionVigente_SeCancelaYElCasoVuelveAPendienteParaOtroSereno()
    {
        var (casoId, sereno, serenoApp) = await CasoEnCaminoAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();

        await DesactivarAsync(await AdministradorAsync(), sereno.Id);

        var asignacion = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AsNoTracking().Where(a => a.CasoId == casoId).ToListAsync()));
        Assert.False(asignacion.Vigente);
        Assert.True(asignacion.Cancelada);
        Assert.NotNull(asignacion.FinalizadaEn);
        Assert.Equal(EstadoCaso.Pendiente, (await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId))).Estado);
        var evento = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.AsignacionCancelada).ToListAsync()));
        Assert.Equal(VisibilidadEvento.Interna, evento.Visibilidad);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)), "La central no recibió el aviso del caso.");
        var historial = System.Net.WebUtility.HtmlDecode(await central.Navegador.GetStringAsync($"/panel/casos/{casoId}/historial"));
        Assert.Contains("Asignación cancelada: la cuenta del sereno se desactivó", historial);
        Assert.DoesNotContain("Asignación finalizada", historial);
        // El sereno desactivado ya no consulta su despacho; otro sereno recibe el caso sin los avances del anterior.
        Assert.Equal(HttpStatusCode.Unauthorized, (await serenoApp.GetAsync("/api/sereno/despacho")).StatusCode);
        var (otroApp, otro) = await SerenoOperativoAsync();
        Assert.IsType<Cad.Server.Modulos.Casos.Aplicacion.ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<Cad.Server.Modulos.Casos.Aplicacion.AsignarCaso>().EjecutarAsync(casoId, otro.Id, evento.ActorId, Guid.NewGuid(), CancellationToken.None)));
        var despacho = (await otroApp.GetFromJsonAsync<DespachoSereno>("/api/sereno/despacho"))!;
        Assert.Equal((DespachoSereno.EstadoAsignada, (DateTimeOffset?)null), (despacho.Estado, despacho.EnCaminoEn));
    }

    [Fact]
    public async Task CA04_SerenoDesactivadoSinAsignacion_NoCambiaNingunCaso()
    {
        var (app, sereno) = await SerenoOperativoAsync();
        var casosAntes = await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync(a => a.Vigente));

        await DesactivarAsync(await AdministradorAsync(), sereno.Id);

        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.ActorId == sereno.Id)));
        Assert.True(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync(a => a.Vigente)) <= casosAntes);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/sereno/estado")).StatusCode);
    }

    public static TheoryData<string?> PerfilesSinPermiso => new() { null, Roles.Ciudadano, Roles.Operador, Roles.Sereno };

    [Theory]
    [MemberData(nameof(PerfilesSinPermiso))]
    public async Task CA05_UsuarioQueNoEsAdministrador_NoPuedeDesactivar(string? perfil)
    {
        var victima = await fabrica.CrearAsync(Roles.Sereno);
        var navegador = fabrica.CrearNavegador();
        HttpResponseMessage respuesta;
        if (perfil is Roles.Operador)
        {
            // Un operador con sesión y un token antifalsificación válido (de su central) envía la acción directamente.
            var operador = await fabrica.CrearAsync(Roles.Operador);
            await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
            respuesta = await DesactivarAsync(navegador, victima.Id, "/panel/central");
            Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.OriginalString);
        }
        else
        {
            if (perfil is not null)
            {
                var cuenta = await fabrica.CrearAsync(perfil);
                navegador = await fabrica.CrearClienteAppAsync(cuenta);
            }
            respuesta = await navegador.PostAsync($"{RutaEditar(victima.Id)}?handler=Desactivar", new FormUrlEncodedContent([]));
            Assert.True(respuesta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Respondió {(int)respuesta.StatusCode}");
            if (respuesta.StatusCode == HttpStatusCode.Redirect)
                Assert.Matches("/panel/(ingresar|acceso-denegado)", respuesta.Headers.Location?.OriginalString);
        }

        Assert.True(await ActivaAsync(victima.Id));
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Administrador)]
    public async Task SoloCuentasInstitucionales_OtrasCuentasNoSeDesactivan(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var admin = await AdministradorAsync();

        var respuesta = await DesactivarAsync(admin, cuenta.Id, "/panel/admin/cuentas");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.True(await ActivaAsync(cuenta.Id));
    }

    [Fact]
    public async Task RegistroDeLaDesactivacion_SinCorreoNiNombre()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        await DesactivarAsync(await AdministradorAsync(), cuenta.Id);

        var entradas = fabrica.Registros.Entradas.Where(e => e.Contains(cuenta.Id.ToString())).ToList();
        Assert.Contains(entradas, e => e.Contains("desactivada"));
        Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(cuenta.Correo, StringComparison.OrdinalIgnoreCase));
    }
}
