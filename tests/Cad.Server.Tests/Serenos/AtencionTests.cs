using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Serenos;

// HU22 — Confirmar el desplazamiento (S2-T10) y HU23 — Registrar el inicio de atención (S2-T11). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AtencionTests(FabricaCad fabrica)
{
    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App);

    private async Task<SerenoDePrueba> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app);
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    // Un SOS asignado al sereno indicado.
    private async Task<Guid> CasoAsignadoAsync(SerenoDePrueba sereno, HttpClient operador)
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var ruta = $"/panel/central/sos/{sos!.Id}";
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = token,
        }))).StatusCode);
        return sos.Id;
    }

    private static Task<HttpResponseMessage> DesplazamientoAsync(SerenoDePrueba sereno, Guid casoId) =>
        sereno.App.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null);

    private static Task<HttpResponseMessage> AtencionAsync(SerenoDePrueba sereno, Guid casoId) =>
        sereno.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null);

    private static async Task<HttpResponseMessage> AtencionDesdeElPanelAsync(HttpClient operador, Guid casoId, string? token = null)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        token ??= await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        return await operador.PostAsync($"{ruta}?handler=IniciarAtencion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    private Task<List<EventoCaso>> EventosAsync(Guid casoId, TipoEventoCaso tipo) =>
        fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.CasoId == casoId && e.Tipo == tipo).ToListAsync());

    // ---------------- HU22 ----------------

    [Fact]
    public async Task HU22_CA01_ElSerenoAsignadoConfirma_PasaAEnCaminoConResponsableYHora_YLaCentralLoRefleja()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await using var central = await fabrica.OperadorEnCentralAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await DesplazamientoAsync(sereno, casoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var despacho = await respuesta.Content.ReadFromJsonAsync<DespachoSereno>();
        Assert.Equal(DespachoSereno.EstadoEnCamino, despacho!.Estado);
        Assert.NotNull(despacho.EnCaminoEn);
        Assert.Equal(EstadoCaso.EnCamino, (await CasoAsync(casoId)).Estado);
        var evento = Assert.Single(await EventosAsync(casoId, TipoEventoCaso.DesplazamientoConfirmado));
        Assert.Equal(sereno.Cuenta.Id, evento.ActorId);
        Assert.InRange(evento.OcurridoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)));
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Contains(sereno.Cuenta.Id)));
        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains("SOS En camino", detalle);
        Assert.Contains("data-avances", detalle);
        Assert.Contains("data-estado-caso=\"En camino\"", await operador.GetStringAsync("/panel/central?handler=Serenos"));
    }

    [Fact]
    public async Task HU22_CA02_UnCasoQueNoEstaAsignada_SeRechaza()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        // Estado que no es «Asignada» sin haber confirmado antes (no se alcanza por la API; se fuerza en la base).
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Casos\" SET \"Estado\" = 'Pendiente' WHERE \"Id\" = {casoId}"));

        var respuesta = await DesplazamientoAsync(sereno, casoId);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("no está «Asignada»", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await EventosAsync(casoId, TipoEventoCaso.DesplazamientoConfirmado));
    }

    [Fact]
    public async Task HU22_CA02_Dominio_SoloUnCasoAsignadoPasaAEnCamino()
    {
        var caso = Caso.EmitirSos(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(caso.ConfirmarDesplazamiento);
        Assert.Throws<InvalidOperationException>(caso.IniciarAtencion);
        caso.Asignar();
        Assert.Throws<InvalidOperationException>(caso.IniciarAtencion);
        caso.ConfirmarDesplazamiento();
        Assert.Throws<InvalidOperationException>(caso.ConfirmarDesplazamiento);
        caso.IniciarAtencion();
        Assert.Equal(EstadoCaso.EnAtencion, caso.Estado);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HU22_CA03_OtroSereno_SeRechazaSinCambios()
    {
        var asignado = await SerenoAsync();
        var otro = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(asignado, operador);

        Assert.Equal(HttpStatusCode.NotFound, (await DesplazamientoAsync(otro, casoId)).StatusCode);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task HU22_CA04_UnOperador_NoPuedeConfirmarElDesplazamiento()
    {
        var sereno = await SerenoAsync();
        var (operador, cuentaOperador) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);

        // Por la API del sereno con su sesión del panel: rechazado por la política.
        var porApi = await operador.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null);
        // Y el caso de uso lo rechaza aunque se le llame con un operador.
        var porCasoDeUso = await fabrica.ConServiciosAsync(s => s.GetRequiredService<AvanzarCaso>()
            .EjecutarAsync(casoId, Avance.ConfirmarDesplazamiento, cuentaOperador.Id, ActorAvance.Operador, CancellationToken.None));

        Assert.Contains(porApi.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(AvanzarCaso.MotivoSoloSereno, Assert.IsType<ResultadoAvance.Rechazado>(porCasoDeUso).Motivo);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task HU22_CA05_LaMismaConfirmacionReenviada_NoDuplicaNiCambiaElEstado()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => DesplazamientoAsync(sereno, casoId)));
        var otraVez = await DesplazamientoAsync(sereno, casoId);

        Assert.All(respuestas.Append(otraVez), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await EventosAsync(casoId, TipoEventoCaso.DesplazamientoConfirmado));
        Assert.Equal(EstadoCaso.EnCamino, (await CasoAsync(casoId)).Estado);
    }

    // ---------------- HU23 ----------------

    [Fact]
    public async Task HU23_CA01_CA03_ElSerenoRegistraElInicio_PasaAEnAtencion_YLaCentralLoRefleja()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await DesplazamientoAsync(sereno, casoId);
        await using var central = await fabrica.OperadorEnCentralAsync();
        while (central.AvisosCasos.TryDequeue(out _)) { }

        var respuesta = await AtencionAsync(sereno, casoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var despacho = await respuesta.Content.ReadFromJsonAsync<DespachoSereno>();
        Assert.Equal(DespachoSereno.EstadoEnAtencion, despacho!.Estado);
        Assert.NotNull(despacho.EnAtencionEn);
        Assert.Equal(sereno.Cuenta.Id, Assert.Single(await EventosAsync(casoId, TipoEventoCaso.AtencionIniciada)).ActorId);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)), "La central no recibió el aviso.");
        Assert.Contains("SOS En atención", await operador.GetStringAsync($"/panel/central/sos/{casoId}"));
        Assert.Contains("data-estado-caso=\"En atención\"", await operador.GetStringAsync("/panel/central?handler=Serenos"));
    }

    [Fact]
    public async Task HU23_CA02_UnOperadorRegistraElInicio_QuedaComoResponsable_YLaAppDelSerenoRecibeElAviso()
    {
        var sereno = await SerenoAsync();
        var (operador, cuentaOperador) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await DesplazamientoAsync(sereno, casoId);
        var avisos = new ConcurrentQueue<bool>();
        await using var canal = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/sereno", o =>
            {
                o.HttpMessageHandlerFactory = _ => fabrica.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(sereno.App.DefaultRequestHeaders.Authorization!.Parameter);
            })
            .Build();
        canal.On("DespachoActualizado", () => avisos.Enqueue(true));
        await canal.StartAsync();

        var respuesta = await AtencionDesdeElPanelAsync(operador, casoId);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.EnAtencion, (await CasoAsync(casoId)).Estado);
        Assert.Equal(cuentaOperador.Id, Assert.Single(await EventosAsync(casoId, TipoEventoCaso.AtencionIniciada)).ActorId);
        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains("Inicio de atención registrado.", detalle);
        Assert.Contains("· Prueba Operador", detalle);
        Assert.True(await CentralDePrueba.EsperarAsync(() => !avisos.IsEmpty), "La app del sereno no recibió el aviso.");
    }

    [Fact]
    public async Task HU23_CA04_UnCasoQueNoEstaEnCamino_SeRechaza_DesdeLaAppYDesdeElPanel()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador); // «Asignada»

        var desdeApp = await AtencionAsync(sereno, casoId);
        var desdePanel = await AtencionDesdeElPanelAsync(operador, casoId);

        Assert.Equal(HttpStatusCode.Conflict, desdeApp.StatusCode);
        Assert.Contains("no está «En camino»", await desdeApp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, desdePanel.StatusCode);
        Assert.Contains("No se pudo registrar el inicio de atención", await desdePanel.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
        Assert.Empty(await EventosAsync(casoId, TipoEventoCaso.AtencionIniciada));
    }

    [Fact]
    public async Task HU23_CA05_OtroSereno_SeRechazaSinCambios()
    {
        var asignado = await SerenoAsync();
        var otro = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(asignado, operador);
        await DesplazamientoAsync(asignado, casoId);

        Assert.Equal(HttpStatusCode.NotFound, (await AtencionAsync(otro, casoId)).StatusCode);
        Assert.Equal(EstadoCaso.EnCamino, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task HU23_CA06_SerenoYOperadorALaVez_UnSoloCambio()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await DesplazamientoAsync(sereno, casoId);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, $"/panel/central/sos/{casoId}");

        var respuestas = await Task.WhenAll(AtencionAsync(sereno, casoId), AtencionDesdeElPanelAsync(operador, casoId, token));

        Assert.Single(await EventosAsync(casoId, TipoEventoCaso.AtencionIniciada));
        Assert.Equal(EstadoCaso.EnAtencion, (await CasoAsync(casoId)).Estado);
        // Uno de los dos se aplica; el otro informa de que ya estaba registrado.
        var aplicados = (respuestas[0].StatusCode == HttpStatusCode.OK ? 1 : 0) + (respuestas[1].StatusCode == HttpStatusCode.Redirect ? 1 : 0);
        Assert.Equal(1, aplicados);
    }

    [Fact]
    public async Task HU23_CA06_LaMismaSolicitudReenviada_UnSoloRegistro()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await DesplazamientoAsync(sereno, casoId);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AtencionAsync(sereno, casoId)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await EventosAsync(casoId, TipoEventoCaso.AtencionIniciada));
    }

    [Fact]
    public async Task HU23_UnAdministradorConTokenValido_NoRegistraElInicio()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await OperadorAsync();
        var casoId = await CasoAsignadoAsync(sereno, operador);
        await DesplazamientoAsync(sereno, casoId);
        var (administrador, _) = await OperadorAsync(Roles.Administrador);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(administrador, "/panel/admin/cuentas/nueva");

        var respuesta = await AtencionDesdeElPanelAsync(administrador, casoId, token);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.Equal(EstadoCaso.EnCamino, (await CasoAsync(casoId)).Estado);
    }
}
