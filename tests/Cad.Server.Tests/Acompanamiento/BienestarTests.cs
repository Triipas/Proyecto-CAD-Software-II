using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Acompanamiento;

// HU40 — Solicitar una confirmación de bienestar (S5-T04) y HU41 — Responderla (S5-T05, S5-T06). DP20 (PO, 16/10/2026):
// una sola confirmación pendiente por sesión. SOS vigente (PO, 16/10/2026): Pendiente, Asignada, En camino o En atención.
// Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class BienestarTests(FabricaCad fabrica)
{
    private sealed record Ciudadano(HttpClient App, CuentaDePrueba Cuenta, Guid SesionId);

    private async Task<Ciudadano> AcompanamientoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321");
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var respuesta = await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return new(app, cuenta, (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!.Id);
    }

    private Task<ResultadoSolicitudBienestar> SolicitarAsync(Guid sesionId, Guid operadorId, Guid? idSolicitud = null) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<ConfirmarBienestar>().SolicitarAsync(sesionId, operadorId, idSolicitud ?? Guid.NewGuid(), CancellationToken.None));

    private async Task<Guid> ConfirmacionAsync(Guid sesionId) =>
        Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await SolicitarAsync(sesionId, (await fabrica.CrearAsync(Roles.Operador)).Id)).Confirmacion.Id;

    private Task<List<ConfirmacionBienestar>> ConfirmacionesAsync(Guid sesionId) =>
        fabrica.ConBaseDatosAsync(db => db.ConfirmacionesBienestar.AsNoTracking().Where(c => c.SesionId == sesionId).ToListAsync());

    private Task<List<Caso>> SosDeAsync(Guid ciudadanoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.CiudadanoId == ciudadanoId).ToListAsync());

    private static SolicitudEmisionSos Sos(Guid? idSolicitud = null)
    {
        var ahora = DateTimeOffset.UtcNow;
        return new(idSolicitud ?? Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 12, ahora.AddSeconds(1)));
    }

    private static Task<HttpResponseMessage> ResponderAsync(HttpClient app, Guid confirmacionId, string? respuesta, SolicitudEmisionSos? sos = null) =>
        app.PostAsJsonAsync($"/api/acompanamiento/confirmaciones/{confirmacionId}/respuesta", new SolicitudRespuestaBienestar(respuesta, sos));

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> SolicitarEnPanelAsync(HttpClient navegador, Guid sesionId, Guid? idSolicitud = null)
    {
        var ruta = $"/panel/acompanamientos/{sesionId}";
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, ruta);
        return await navegador.PostAsync($"{ruta}?handler=SolicitarConfirmacion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["IdSolicitudConfirmacion"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = token,
        }));
    }

    // ----- HU40 -----

    [Fact]
    public async Task HU40_CA01_SesionActiva_ElOperadorSolicitaYQuedaRegistradaConOperadorFechaYHora()
    {
        var ciudadano = await AcompanamientoAsync();
        var (operador, cuenta) = await PanelAsync();
        var antes = DateTimeOffset.UtcNow;
        var idSolicitud = Guid.NewGuid();

        var respuesta = await SolicitarEnPanelAsync(operador, ciudadano.SesionId, idSolicitud);
        var reenvio = await SolicitarEnPanelAsync(operador, ciudadano.SesionId, idSolicitud);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, reenvio.StatusCode); // el mismo formulario reenviado no crea otra
        var confirmacion = Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId));
        Assert.Equal((cuenta.Id, ciudadano.Cuenta.Id), (confirmacion.OperadorId, confirmacion.CiudadanoId));
        Assert.InRange(confirmacion.SolicitadaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(confirmacion.Pendiente);
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}"));
        Assert.Contains("Pendiente: aún no se mostró al ciudadano", html);
        Assert.Contains("data-confirmacion-en-espera", html);
        Assert.Contains("data-confirmacion=\"Pendiente\"", await operador.GetStringAsync("/panel/acompanamientos?handler=Lista"));
    }

    [Fact]
    public async Task HU40_CA02_SesionCerradaVencidaOInexistente_SeRechaza()
    {
        var finalizada = await AcompanamientoAsync();
        Assert.Equal(HttpStatusCode.OK, (await finalizada.App.PostAsync($"/api/acompanamiento/{finalizada.SesionId}/finalizacion", null)).StatusCode);
        var vencida = await AcompanamientoAsync();
        await fabrica.ConBaseDatosAsync(db => db.SesionesAcompanamiento.Where(s => s.Id == vencida.SesionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IniciadaEn, DateTimeOffset.UtcNow.AddSeconds(-1801)).SetProperty(x => x.VenceEn, DateTimeOffset.UtcNow.AddSeconds(-1))));
        var operador = await fabrica.CrearAsync(Roles.Operador);

        Assert.IsType<ResultadoSolicitudBienestar.SesionNoActiva>(await SolicitarAsync(finalizada.SesionId, operador.Id));
        Assert.IsType<ResultadoSolicitudBienestar.SesionNoActiva>(await SolicitarAsync(vencida.SesionId, operador.Id));
        Assert.IsType<ResultadoSolicitudBienestar.NoEncontrada>(await SolicitarAsync(Guid.NewGuid(), operador.Id));
        Assert.Empty(await ConfirmacionesAsync(finalizada.SesionId));
        Assert.Empty(await ConfirmacionesAsync(vencida.SesionId));

        // En el panel: el aviso y ningún registro.
        var (navegador, _) = await PanelAsync();
        var html = System.Net.WebUtility.HtmlDecode(await (await SolicitarEnPanelAsync(navegador, finalizada.SesionId)).Content.ReadAsStringAsync());
        Assert.Contains("La sesión ya no está activa", html);
        Assert.DoesNotContain("Solicitar confirmación</button>", html);
    }

    [Fact]
    public async Task HU40_CA03_LaSolicitudNoCreaUnSosNiUnDespacho()
    {
        var ciudadano = await AcompanamientoAsync();
        var casosAntes = await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync());
        var asignacionesAntes = await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync());

        await ConfirmacionAsync(ciudadano.SesionId);

        Assert.Empty(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.Equal(casosAntes, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync()));
        Assert.Equal(asignacionesAntes, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync()));
    }

    [Fact]
    public async Task HU40_CA04_DP20_ConUnaConfirmacionPendiente_OtraSolicitudSeRechaza()
    {
        var ciudadano = await AcompanamientoAsync();
        var primera = await ConfirmacionAsync(ciudadano.SesionId);
        var otroOperador = await fabrica.CrearAsync(Roles.Operador);

        var segunda = await SolicitarAsync(ciudadano.SesionId, otroOperador.Id);

        Assert.Equal(primera, Assert.IsType<ResultadoSolicitudBienestar.OtraPendiente>(segunda).Pendiente.Id);
        Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId));
        // Respondida la anterior, se puede pedir otra.
        Assert.Equal(HttpStatusCode.OK, (await ResponderAsync(ciudadano.App, primera, RespuestasBienestar.EstoyBien)).StatusCode);
        Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await SolicitarAsync(ciudadano.SesionId, otroOperador.Id));
        Assert.Equal(2, (await ConfirmacionesAsync(ciudadano.SesionId)).Count);
    }

    [Fact]
    public async Task HU40_CA04_DP20_SolicitudesSimultaneas_QuedaUnaSolaPendiente()
    {
        var ciudadano = await AcompanamientoAsync();
        var operadores = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => fabrica.CrearAsync(Roles.Operador)));

        var resultados = await Task.WhenAll(operadores.Select(o => SolicitarAsync(ciudadano.SesionId, o.Id)));

        Assert.Single(resultados, r => r is ResultadoSolicitudBienestar.Solicitada);
        Assert.All(resultados.Where(r => r is not ResultadoSolicitudBienestar.Solicitada), r => Assert.IsType<ResultadoSolicitudBienestar.OtraPendiente>(r));
        Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId));
    }

    [Fact]
    public async Task HU40_LaBaseDeDatosTampocoAdmiteDosPendientesEnLaMismaSesion()
    {
        var ciudadano = await AcompanamientoAsync();
        await ConfirmacionAsync(ciudadano.SesionId);
        var operador = await fabrica.CrearAsync(Roles.Operador);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ConfirmacionesBienestar\" (\"Id\", \"SesionId\", \"CiudadanoId\", \"OperadorId\", \"IdSolicitud\", \"SolicitadaEn\") VALUES ({Guid.NewGuid()}, {ciudadano.SesionId}, {ciudadano.Cuenta.Id}, {operador.Id}, {Guid.NewGuid()}, now())")));
        Assert.Equal("IX_ConfirmacionesBienestar_Pendiente", error.ConstraintName);
    }

    [Theory]
    [InlineData(Roles.Administrador)]
    public async Task HU40_OtroPerfilDelPanel_NoPuedeSolicitar(string rol)
    {
        var ciudadano = await AcompanamientoAsync();
        var (navegador, _) = await PanelAsync(rol);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/admin/cuentas");

        var respuesta = await navegador.PostAsync($"/panel/acompanamientos/{ciudadano.SesionId}?handler=SolicitarConfirmacion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["IdSolicitudConfirmacion"] = Guid.NewGuid().ToString(), ["__RequestVerificationToken"] = token }));

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("acceso-denegado", respuesta.Headers.Location?.ToString() ?? "acceso-denegado");
        Assert.Empty(await ConfirmacionesAsync(ciudadano.SesionId));
    }

    // ----- HU41 -----

    [Fact]
    public async Task HU41_CA01_LaAppObtieneLaConfirmacionPendienteYRegistraLaPresentacionUnaVez()
    {
        var ciudadano = await AcompanamientoAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await ciudadano.App.GetAsync($"/api/acompanamiento/{ciudadano.SesionId}/confirmacion")).StatusCode);
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        var pendiente = (await ciudadano.App.GetFromJsonAsync<ConfirmacionBienestarVista>($"/api/acompanamiento/{ciudadano.SesionId}/confirmacion"))!;
        var primera = await ciudadano.App.PostAsync($"/api/acompanamiento/confirmaciones/{id}/presentacion", null);
        var presentada = (await primera.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!.PresentadaEn;
        var segunda = (await (await ciudadano.App.PostAsync($"/api/acompanamiento/confirmaciones/{id}/presentacion", null))
            .Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;

        Assert.Equal(id, pendiente.Id);
        Assert.Null(pendiente.Respuesta);
        Assert.NotNull(presentada);
        Assert.Equal(presentada!.Value.ToUnixTimeMilliseconds(), segunda.PresentadaEn!.Value.ToUnixTimeMilliseconds());
        Assert.Contains("mostrada al ciudadano, sin respuesta", System.Net.WebUtility.HtmlDecode(
            await (await PanelAsync()).Navegador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}")));
    }

    [Fact]
    public async Task HU41_CA01_OtroCiudadano_NoVeNiPresentaLaConfirmacion()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"/api/acompanamiento/{ciudadano.SesionId}/confirmacion")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.PostAsync($"/api/acompanamiento/confirmaciones/{id}/presentacion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ResponderAsync(otro, id, RespuestasBienestar.NecesitoAyuda, Sos())).StatusCode);
        var confirmacion = Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId));
        Assert.Null(confirmacion.PresentadaEn);
        Assert.True(confirmacion.Pendiente);
    }

    [Fact]
    public async Task HU41_CA02_EstoyBien_SeRegistraYLaSesionSigueActiva()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        var respuesta = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.EstoyBien);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;
        Assert.Equal((RespuestasBienestar.EstoyBien, (Guid?)null), (vista.Respuesta, vista.CasoId));
        Assert.NotNull(vista.RespondidaEn);
        Assert.NotNull(vista.PresentadaEn); // responder implica que se mostró
        Assert.Equal(EstadosAcompanamiento.Activa, (await ciudadano.App.GetFromJsonAsync<SesionAcompanamientoVista>("/api/acompanamiento/actual"))!.Estado);
        Assert.Empty(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await ciudadano.App.GetAsync($"/api/acompanamiento/{ciudadano.SesionId}/confirmacion")).StatusCode);
    }

    [Fact]
    public async Task HU41_CA03_NecesitoAyudaSinSosVigente_EmiteUnSosConSuUbicacionYAvisaALaCentral()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        await using var central = await fabrica.OperadorEnCentralAsync();

        var respuesta = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, Sos());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;
        var sos = Assert.Single(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.Equal(sos.Id, vista.CasoId);
        Assert.Equal((OrigenCaso.Sos, EstadoCaso.Pendiente), (sos.Origen, sos.Estado));
        Assert.True(sos.UbicacionDisponible); // HU08: con la ubicación actual que envió la app
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.Avisos.Contains(sos.Id)), "La central no recibió el SOS.");
        // La sesión sigue activa: pedir ayuda no la finaliza.
        Assert.Equal(EstadosAcompanamiento.Activa, (await ciudadano.App.GetFromJsonAsync<SesionAcompanamientoVista>("/api/acompanamiento/actual"))!.Estado);
        Assert.Contains($"/panel/central/sos/{sos.Id}", await (await PanelAsync()).Navegador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}"));
    }

    // 24/10/2026: si el ciudadano cancela el SOS de «Necesito ayuda», la tarjeta y el detalle lo dicen y dejan de mostrarlo como
    // una petición de ayuda en curso (rojo); la central recibe el aviso para actualizarse.
    [Fact]
    public async Task NecesitoAyuda_ConElSosCancelado_YaNoSeMuestraComoAlertaEnCurso()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        var vista = (await (await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, Sos())).Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;
        var (navegador, _) = await PanelAsync();
        string Tarjeta(string lista) => System.Text.RegularExpressions.Regex.Match(lista,
            $"data-acompanamiento=\"{ciudadano.SesionId}\".*?</li>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;

        var antes = Tarjeta(await navegador.GetStringAsync("/panel/acompanamientos?handler=Lista"));
        Assert.Contains("aviso aviso--error", antes);
        Assert.Contains("data-sos-en-curso=\"true\"", antes);

        await using var central = await fabrica.OperadorEnCentralAsync();
        Assert.Equal(HttpStatusCode.OK, (await ciudadano.App.PostAsJsonAsync($"/api/casos/mios/{vista.CasoId}/cancelacion",
            new SolicitudCancelacionSos("Ya estoy a salvo."))).StatusCode);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCancelaciones.Contains(vista.CasoId!.Value)), "aviso de la cancelación");

        var despues = System.Net.WebUtility.HtmlDecode(Tarjeta(await navegador.GetStringAsync("/panel/acompanamientos?handler=Lista")));
        Assert.DoesNotContain("aviso aviso--error", despues);
        Assert.Contains("aviso aviso--advertencia", despues);
        Assert.Contains("El ciudadano canceló el SOS.", despues);
        Assert.Contains("El ciudadano canceló el SOS.", System.Net.WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}")));
    }

    [Theory]
    [InlineData(EstadoCaso.Pendiente)]
    [InlineData(EstadoCaso.Asignada)]
    [InlineData(EstadoCaso.EnCamino)]
    [InlineData(EstadoCaso.EnAtencion)]
    public async Task HU41_CA04_NecesitoAyudaConSosVigente_SeVinculaSinCrearOtro(EstadoCaso estado)
    {
        var ciudadano = await AcompanamientoAsync();
        var existente = (await (await ciudadano.App.PostAsJsonAsync("/api/sos", Sos())).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        if (estado != EstadoCaso.Pendiente)
            await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == existente).ExecuteUpdateAsync(c => c.SetProperty(x => x.Estado, estado)));
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        var respuesta = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, Sos());

        Assert.Equal(existente, (await respuesta.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!.CasoId);
        Assert.Equal(existente, Assert.Single(await SosDeAsync(ciudadano.Cuenta.Id)).Id);
    }

    [Theory]
    [InlineData(EstadoCaso.Cerrada)]
    [InlineData(EstadoCaso.Cancelada)]
    public async Task HU41_CA03_ConUnSosQueYaNoEstaVigente_SeEmiteUnoNuevo(EstadoCaso estado)
    {
        var ciudadano = await AcompanamientoAsync();
        var anterior = (await (await ciudadano.App.PostAsJsonAsync("/api/sos", Sos())).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == anterior).ExecuteUpdateAsync(c => c.SetProperty(x => x.Estado, estado)));
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        var vista = (await (await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, Sos())).Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;

        Assert.NotEqual(anterior, vista.CasoId);
        Assert.Equal(2, (await SosDeAsync(ciudadano.Cuenta.Id)).Count);
    }

    [Fact]
    public async Task HU41_CA05_SinRespuesta_NoSeEmiteSosNiDespachoAutomatico()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        // Pasa el tiempo sin respuesta (la solicitud se lleva 20 minutos atrás) y se consulta como lo hacen la app y el panel.
        await fabrica.ConBaseDatosAsync(db => db.ConfirmacionesBienestar.Where(c => c.Id == id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.SolicitadaEn, DateTimeOffset.UtcNow.AddMinutes(-20))));
        Assert.Equal(HttpStatusCode.OK, (await ciudadano.App.GetAsync($"/api/acompanamiento/{ciudadano.SesionId}/confirmacion")).StatusCode);
        await (await PanelAsync()).Navegador.GetStringAsync("/panel/acompanamientos?handler=Lista");

        Assert.Empty(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.True(Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId)).Pendiente);
    }

    [Fact]
    public async Task HU41_CA07_S5T06_RespuestaRepetidaSimultanea_NoCreaMasDeUnSos()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        var sos = Sos(); // el mismo identificador en cada reintento

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, sos)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var caso = Assert.Single(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.All(await Task.WhenAll(respuestas.Select(r => r.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())), v => Assert.Equal(caso.Id, v!.CasoId));
        Assert.Equal(caso.Id, Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId)).CasoId);
    }

    [Fact]
    public async Task HU41_CA07_ReenvioTrasRespuesta_DevuelveLaMismaYOtraOpcionSeRechaza()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        var sos = Sos();
        var primera = (await (await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, sos)).Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;

        var reenvio = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, sos);
        var otra = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.EstoyBien);

        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        var repetida = (await reenvio.Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!;
        Assert.Equal((primera.Id, primera.Respuesta, primera.CasoId), (repetida.Id, repetida.Respuesta, repetida.CasoId));
        Assert.Equal(primera.RespondidaEn!.Value.ToUnixTimeMilliseconds(), repetida.RespondidaEn!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Single(await SosDeAsync(ciudadano.Cuenta.Id));
        Assert.Equal(RespuestaBienestar.NecesitoAyuda, Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId)).Respuesta);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Quizas")]
    public async Task HU41_RespuestaInvalida_NoSeRegistra(string? respuesta)
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(ciudadano.App, id, respuesta)).StatusCode);
        // «Necesito ayuda» necesita la solicitud del SOS (para que un reintento no lo duplique).
        Assert.Equal(HttpStatusCode.BadRequest, (await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda)).StatusCode);
        Assert.True(Assert.Single(await ConfirmacionesAsync(ciudadano.SesionId)).Pendiente);
        Assert.Empty(await SosDeAsync(ciudadano.Cuenta.Id));
    }

    [Fact]
    public async Task HU41_LaRespuestaSeAceptaAunqueLaSesionHayaTerminadoMientrasTanto()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);
        Assert.Equal(HttpStatusCode.OK, (await ciudadano.App.PostAsync($"/api/acompanamiento/{ciudadano.SesionId}/finalizacion", null)).StatusCode);

        var respuesta = await ResponderAsync(ciudadano.App, id, RespuestasBienestar.NecesitoAyuda, Sos());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Single(await SosDeAsync(ciudadano.Cuenta.Id));
    }

    [Fact]
    public async Task HU41_LaBaseDeDatosExigeElSosSoloConNecesitoAyuda()
    {
        var ciudadano = await AcompanamientoAsync();
        var id = await ConfirmacionAsync(ciudadano.SesionId);

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ConfirmacionesBienestar\" SET \"Respuesta\" = 'NecesitoAyuda', \"RespondidaEn\" = now() WHERE \"Id\" = {id}")));
        Assert.Equal("CK_ConfirmacionesBienestar_Respuesta", error.ConstraintName);
    }
}
