using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Notificaciones;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Serenos;

// HU21 — Recibir y consultar un despacho (S2-T09). Notificación por el canal del sereno y por push (ADR-07, con un
// doble de FCM); datos limitados por DP06. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class DespachoTests(FabricaCad fabrica)
{
    private const string Telefono = "987654321";

    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App, string Jwt);

    private async Task<SerenoDePrueba> SerenoAsync(bool operativo = true)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        if (operativo)
            Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app, app.DefaultRequestHeaders.Authorization!.Parameter!);
    }

    private async Task<Guid> SosAsync(bool conUbicacion = true)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano, telefono: Telefono));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            conUbicacion ? new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1)) : null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<HttpResponseMessage> AsignarAsync(HttpClient operador, Guid casoId, Guid serenoId, Guid? idSolicitud = null)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        var respuesta = await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(),
            ["Asignacion.IdSolicitud"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return respuesta;
    }

    private static async Task RegistrarIntentoAsync(HttpClient operador, Guid casoId, string resultado, string? informacion, string? referencia)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        var campos = new Dictionary<string, string>
        {
            ["Formulario.IdSolicitud"] = Guid.NewGuid().ToString(), ["Formulario.Resultado"] = resultado,
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        };
        if (informacion is not null) campos["Formulario.InformacionHecho"] = informacion;
        if (referencia is not null) campos["Formulario.ReferenciaUbicacion"] = referencia;
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync(ruta, new FormUrlEncodedContent(campos))).StatusCode);
    }

    // Conexión de la app del sereno al canal en tiempo real, con el JWT en la cabecera Authorization.
    private async Task<(HubConnection Conexion, ConcurrentQueue<DateTimeOffset> Avisos)> ConectarAsync(string jwt)
    {
        var avisos = new ConcurrentQueue<DateTimeOffset>();
        var conexion = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/sereno", opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => fabrica.Server.CreateHandler();
                opciones.Transports = HttpTransportType.LongPolling;
                opciones.AccessTokenProvider = () => Task.FromResult<string?>(jwt);
            })
            .Build();
        conexion.On("DespachoAsignado", () => avisos.Enqueue(DateTimeOffset.UtcNow));
        await conexion.StartAsync();
        return (conexion, avisos);
    }

    private static string TokenFicticio() => $"token-ficticio-{Guid.NewGuid():N}";

    private static async Task RegistrarDispositivoAsync(SerenoDePrueba sereno, string token) =>
        Assert.Equal(HttpStatusCode.NoContent, (await sereno.App.PutAsJsonAsync("/api/sereno/dispositivo", new SolicitudRegistroDispositivo(token))).StatusCode);

    [Fact]
    public async Task CA01_AlAsignar_ElSerenoAsignadoRecibeElAvisoEnTiempoRealYLaPush_YOtroSerenoNo()
    {
        var casoId = await SosAsync();
        var asignado = await SerenoAsync();
        var otro = await SerenoAsync();
        var token = TokenFicticio();
        var tokenOtro = TokenFicticio();
        await RegistrarDispositivoAsync(asignado, token);
        await RegistrarDispositivoAsync(otro, tokenOtro);
        var (conexion, avisos) = await ConectarAsync(asignado.Jwt);
        var (conexionOtro, avisosOtro) = await ConectarAsync(otro.Jwt);
        await using var _ = conexion;
        await using var __ = conexionOtro;

        await AsignarAsync(await OperadorAsync(), casoId, asignado.Cuenta.Id);

        Assert.True(await CentralDePrueba.EsperarAsync(() => !avisos.IsEmpty), "El sereno asignado no recibió el aviso en tiempo real.");
        Assert.True(await CentralDePrueba.EsperarAsync(() => fabrica.Push.Enviadas.Contains(token)), "No se envió la push al sereno asignado.");
        await Task.Delay(300);
        Assert.Empty(avisosOtro);
        Assert.DoesNotContain(tokenOtro, fabrica.Push.Enviadas);
    }

    [Fact]
    public async Task CA01_LaMismaAsignacionReenviada_NoRepiteElAviso()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        var token = TokenFicticio();
        await RegistrarDispositivoAsync(sereno, token);
        var operador = await OperadorAsync();
        var idSolicitud = Guid.NewGuid();

        await AsignarAsync(operador, casoId, sereno.Cuenta.Id, idSolicitud);
        await AsignarAsync(operador, casoId, sereno.Cuenta.Id, idSolicitud);

        Assert.True(await CentralDePrueba.EsperarAsync(() => fabrica.Push.Enviadas.Contains(token)));
        await Task.Delay(300);
        Assert.Single(fabrica.Push.Enviadas, t => t == token);
    }

    [Fact]
    public async Task CA01_ADR07_SinDispositivoRegistrado_NoHayPushPeroElDespachoSeConsulta()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        var (conexion, avisos) = await ConectarAsync(sereno.Jwt);
        await using var _ = conexion;

        await AsignarAsync(await OperadorAsync(), casoId, sereno.Cuenta.Id);

        Assert.True(await CentralDePrueba.EsperarAsync(() => !avisos.IsEmpty));
        var despacho = await sereno.App.GetFromJsonAsync<DespachoSereno>("/api/sereno/despacho");
        Assert.Equal(casoId, despacho!.CasoId);
    }

    [Fact]
    public async Task ADR07_TokenQueFcmDaPorNoRegistrado_SeBorra()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        var token = TokenFicticio();
        fabrica.Push.TokensInvalidos[token] = true;
        await RegistrarDispositivoAsync(sereno, token);

        await AsignarAsync(await OperadorAsync(), casoId, sereno.Cuenta.Id);

        Assert.True(await CentralDePrueba.EsperarAsync(() => fabrica.Push.Enviadas.Contains(token)));
        Assert.True(await CentralDePrueba.EsperarAsync(() =>
            !fabrica.ConBaseDatosAsync(db => db.DispositivosSereno.AnyAsync(d => d.SerenoId == sereno.Cuenta.Id)).Result));
    }

    [Fact]
    public async Task CA02_ElSerenoAsignadoVeIdentificadorOrigenEstadoUbicacionEInformacion()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        var operador = await OperadorAsync();
        await RegistrarIntentoAsync(operador, casoId, "Contactado", "Dos personas discuten en la vía", "Frente al parque ficticio");
        await RegistrarIntentoAsync(operador, casoId, "SinRespuesta", null, null); // sin información: no se muestra
        await AsignarAsync(operador, casoId, sereno.Cuenta.Id);

        var despacho = await sereno.App.GetFromJsonAsync<DespachoSereno>("/api/sereno/despacho");
        var mismoCaso = await sereno.App.GetFromJsonAsync<DespachoSereno>($"/api/sereno/casos/{casoId}");

        Assert.NotNull(despacho);
        Assert.Equal(casoId, despacho.CasoId);
        Assert.Equal("SOS", despacho.Origen);
        Assert.Equal("Asignada", despacho.Estado);
        Assert.Equal(-12.0464, despacho.Ubicacion!.Latitud);
        Assert.Equal(-77.0428, despacho.Ubicacion.Longitud);
        Assert.Equal(10, despacho.Ubicacion.PrecisionMetros);
        var informacion = Assert.Single(despacho.Informacion);
        Assert.Equal("Dos personas discuten en la vía", informacion.InformacionHecho);
        Assert.Equal("Frente al parque ficticio", informacion.ReferenciaUbicacion);
        Assert.Equal(despacho.CasoId, mismoCaso!.CasoId);
    }

    [Fact]
    public async Task CA03_CasoSinUbicacion_NoTieneCoordenadas_YLaReferenciaVaAparte()
    {
        var casoId = await SosAsync(conUbicacion: false);
        var sereno = await SerenoAsync();
        var operador = await OperadorAsync();
        await RegistrarIntentoAsync(operador, casoId, "Contactado", null, "Esquina de la calle ficticia 123");
        await AsignarAsync(operador, casoId, sereno.Cuenta.Id);

        var json = await sereno.App.GetStringAsync("/api/sereno/despacho");
        var despacho = System.Text.Json.JsonSerializer.Deserialize<DespachoSereno>(json, System.Text.Json.JsonSerializerOptions.Web);

        Assert.Null(despacho!.Ubicacion);
        Assert.Contains("\"ubicacion\":null", json);
        Assert.DoesNotContain("latitud", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Esquina de la calle ficticia 123", Assert.Single(despacho.Informacion).ReferenciaUbicacion);
    }

    [Fact]
    public async Task CA04_OtroSereno_NoConsultaElCasoNiLoVeComoSuDespacho()
    {
        var casoId = await SosAsync();
        var asignado = await SerenoAsync();
        var otro = await SerenoAsync();
        await AsignarAsync(await OperadorAsync(), casoId, asignado.Cuenta.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await otro.App.GetAsync($"/api/sereno/casos/{casoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await otro.App.GetAsync("/api/sereno/despacho")).StatusCode);
        // Un caso pendiente (sin asignar) tampoco es consultable por un sereno.
        var pendiente = await SosAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await asignado.App.GetAsync($"/api/sereno/casos/{pendiente}")).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Operador)]
    public async Task CA04_OtrosRolesConSuToken_NoConsultanElDespacho(string rol)
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        await AsignarAsync(await OperadorAsync(), casoId, sereno.Cuenta.Id);
        var otraApp = rol == Roles.Ciudadano ? await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol)) : await OperadorAsync();

        var respuesta = await otraApp.GetAsync($"/api/sereno/casos/{casoId}");

        Assert.Contains(respuesta.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    [Fact]
    public async Task CA05_DP06_ElDespachoNoIncluyeDatosDelCiudadanoNiDelOperadorNiElResultado()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoAsync();
        var operador = await OperadorAsync();
        await RegistrarIntentoAsync(operador, casoId, "Contactado", "Hecho ficticio", "Referencia ficticia");
        await AsignarAsync(operador, casoId, sereno.Cuenta.Id);

        var json = await sereno.App.GetStringAsync("/api/sereno/despacho");

        foreach (var prohibido in new[] { Telefono, "Prueba", "Ciudadano", "Operador", "Contactado", "resultado", "telefono", "nombre", "ciudadanoId" })
            Assert.DoesNotContain(prohibido, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SinDespacho_Responde204()
    {
        var sereno = await SerenoAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await sereno.App.GetAsync("/api/sereno/despacho")).StatusCode);
    }

    [Fact]
    public async Task Dispositivo_ElMismoTelefonoPasaAlUltimoSerenoQueIngresa_YSalirLoQuita()
    {
        var primero = await SerenoAsync(operativo: false);
        var segundo = await SerenoAsync(operativo: false);
        var token = TokenFicticio();

        await RegistrarDispositivoAsync(primero, token);
        await RegistrarDispositivoAsync(segundo, token);

        var dispositivos = await fabrica.ConBaseDatosAsync(db => db.DispositivosSereno.AsNoTracking().Where(d => d.Token == token).ToListAsync());
        Assert.Equal(segundo.Cuenta.Id, Assert.Single(dispositivos).SerenoId);

        Assert.Equal(HttpStatusCode.NoContent, (await segundo.App.DeleteAsync("/api/sereno/dispositivo")).StatusCode);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.DispositivosSereno.AnyAsync(d => d.Token == token)));
    }

    [Fact]
    public async Task Dispositivo_RegistrosSimultaneosDelMismoSereno_DejanUnaFila()
    {
        var sereno = await SerenoAsync(operativo: false);
        var token = TokenFicticio();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            sereno.App.PutAsJsonAsync("/api/sereno/dispositivo", new SolicitudRegistroDispositivo(token))));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.DispositivosSereno.CountAsync(d => d.SerenoId == sereno.Cuenta.Id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Dispositivo_TokenVacio_SeRechaza(string token)
    {
        var sereno = await SerenoAsync(operativo: false);

        var respuesta = await sereno.App.PutAsJsonAsync("/api/sereno/dispositivo", new SolicitudRegistroDispositivo(token));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public void ADR07_LaNotificacionPushNoLlevaDatosDelCaso()
    {
        var mensaje = EnviadorPushFcm.CrearMensaje("token-ficticio");

        Assert.Equal("Nuevo despacho", mensaje.Notification.Title);
        Assert.Equal("Tienes un caso asignado. Abre la app para ver los datos.", mensaje.Notification.Body);
        Assert.Equal(new Dictionary<string, string> { ["tipo"] = "despacho" }, mensaje.Data);
        Assert.Equal(EnviadorPushFcm.CanalAndroid, mensaje.Android.Notification.ChannelId);
    }
}
