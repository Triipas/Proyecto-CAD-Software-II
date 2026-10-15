using Cad.Contratos.Serenos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU21-CA01 (ADR-07) — notificaciones push del despacho en la app. Token ficticio.
public class NotificacionesTests
{
    private readonly ApiFalsa api = new();
    private readonly PushFalso push = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };

    private ServicioNotificaciones Servicio() => new(api, sesiones, push, TimeProvider.System);

    private InicioSerenoVistaModelo VistaModelo(ServicioNotificaciones notificaciones) =>
        new(new ServicioTurnoSereno(api, new UbicacionFalsa(), sesiones, new FakeTimeProvider(DateTimeOffset.UtcNow)),
            new ServicioDespacho(api, sesiones, new CanalFalso(), TimeProvider.System), notificaciones, sesiones, new NavegacionFalsa(), () => { })
        { HiloPrincipal = a => { a(); return Task.CompletedTask; } };

    [Fact]
    public async Task AlAbrir_SinHaberDecidido_NoPideElPermisoNiRegistra_YExplicaPrimero()
    {
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.Equal(0, push.Solicitudes);
        Assert.Empty(api.DispositivosRegistrados);
        Assert.True(vm.MostrarExplicacionNotificaciones);
        Assert.False(vm.NotificacionesActivas);
    }

    [Fact]
    public async Task AlPermitir_SePideElPermiso_YSeRegistraElToken()
    {
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();

        await vm.PermitirNotificaciones.EjecutarAsync();

        Assert.Equal(1, push.Solicitudes);
        Assert.Equal(["token-fcm-ficticio"], api.DispositivosRegistrados);
        Assert.True(vm.NotificacionesActivas);
        Assert.False(vm.MostrarExplicacionNotificaciones);
    }

    [Fact]
    public async Task ConElPermisoYaConcedido_AlAbrirSeRegistraSinPreguntar()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        var notificaciones = Servicio();

        await notificaciones.PrepararAsync();

        Assert.Equal(0, push.Solicitudes);
        Assert.Single(api.DispositivosRegistrados);
        Assert.True(notificaciones.Registrado);
    }

    [Fact]
    public async Task SiSeDeniega_NoSeRegistra_YSeOrientaALosAjustes()
    {
        push.RespuestaSolicitud = EstadoPermisoNotificaciones.Denegado;
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();

        await vm.PermitirNotificaciones.EjecutarAsync();

        Assert.Empty(api.DispositivosRegistrados);
        Assert.True(vm.MostrarAjustesNotificaciones);
        Assert.False(vm.MostrarExplicacionNotificaciones);
    }

    [Fact]
    public async Task SinFirebaseOSinGooglePlay_NoSeMuestraNadaNiSePide()
    {
        push.Disponible = false;
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.False(vm.MostrarExplicacionNotificaciones);
        Assert.False(vm.MostrarAjustesNotificaciones);
        Assert.Equal(0, push.Solicitudes);
        Assert.Empty(api.DispositivosRegistrados);
    }

    [Fact]
    public async Task SiNoHayToken_SeAvisa_YNoSeRegistra()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        push.Token = null;
        var notificaciones = Servicio();

        await notificaciones.PrepararAsync();

        Assert.False(notificaciones.Registrado);
        Assert.Empty(api.DispositivosRegistrados);
        Assert.Contains("Los verás al abrir la app", notificaciones.Aviso);
    }

    [Fact]
    public async Task SinConexion_SeAvisa_YSeReintentaAlVolverAAbrir()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        api.RespuestaDispositivo = () => new ResultadoApi<SinContenido>.SinRespuesta("No hay conexión con el servidor.");
        var notificaciones = Servicio();

        await notificaciones.PrepararAsync();
        Assert.False(notificaciones.Registrado);
        Assert.NotNull(notificaciones.Aviso);

        api.RespuestaDispositivo = () => new ResultadoApi<SinContenido>.Correcto(new SinContenido());
        await notificaciones.PrepararAsync();

        Assert.True(notificaciones.Registrado);
        Assert.Null(notificaciones.Aviso);
    }

    [Fact]
    public async Task CuandoFcmRenuevaElToken_SeVuelveARegistrar()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        var notificaciones = Servicio();
        await notificaciones.PrepararAsync();

        push.Renovar("token-fcm-nuevo");

        await Task.Delay(100);
        Assert.Equal(["token-fcm-ficticio", "token-fcm-nuevo"], api.DispositivosRegistrados);
    }

    [Fact]
    public async Task AlSalir_SeQuitaElDispositivoAntesDeCerrarLaSesion()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();
        Assert.True(vm.NotificacionesActivas);

        await vm.Salir.EjecutarAsync();

        Assert.Equal(1, api.DispositivosQuitados);
        Assert.Null(sesiones.Sesion);
        Assert.False(vm.NotificacionesActivas);
    }

    [Fact]
    public async Task SinSesion_NoSeRegistraNada()
    {
        push.Permiso = EstadoPermisoNotificaciones.Concedido;
        sesiones.Sesion = null;

        await Servicio().PrepararAsync();

        Assert.Empty(api.DispositivosRegistrados);
    }
}
