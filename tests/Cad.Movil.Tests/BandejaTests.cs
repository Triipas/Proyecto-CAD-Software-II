using Cad.Contratos.Casos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU31 — bandeja de notificaciones del ciudadano (lado de la app, S3-T14, S3-T15). Datos ficticios.
public class BandejaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Caso = Guid.NewGuid();

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };
    private readonly NavegacionFalsa navegacion = new();
    private readonly RelojFijo reloj = new(Ahora);

    private static NotificacionCiudadanoVista Notificacion(bool leida, int minutos = 0) =>
        new(Guid.NewGuid(), Caso, "Tu reporte pasó a «En revisión».", Ahora.AddMinutes(-minutos), leida);

    private NotificacionesVistaModelo Bandeja() => new(api, sesiones, navegacion, reloj, Lima);

    private InicioCiudadanoVistaModelo Inicio() => new(new EmisorSos(api, new UbicacionFalsa(), sesiones, reloj),
        new PreparacionUbicacion(new UbicacionFalsa()), sesiones, navegacion, () => { }, new ServicioBandeja(api, sesiones, reloj));

    [Fact]
    public async Task CA01_CA02_LaBandeja_MuestraLasNotificacionesGuardadas()
    {
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(1,
            [Notificacion(leida: false), Notificacion(leida: true, minutos: 30)]));
        var vm = Bandeja();

        await vm.AlAparecerAsync();

        Assert.Equal(2, vm.Notificaciones.Count);
        Assert.Equal("1 notificación sin leer.", vm.Resumen);
        Assert.Equal(("Nueva", true), (vm.Notificaciones[0].Estado, vm.Notificaciones[0].NoLeida));
        Assert.Equal("Leída", vm.Notificaciones[1].Estado);
        Assert.Equal("16/10/2026 10:00", vm.Notificaciones[0].Fecha);
        Assert.False(vm.SinNotificaciones);
    }

    [Fact]
    public async Task CA01_AlAbrirUnaNoLeida_SeMarcaYSeMuestraElCasoVinculado()
    {
        var nueva = Notificacion(leida: false);
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(1, [nueva]));
        var vm = Bandeja();
        await vm.AlAparecerAsync();

        await vm.Notificaciones[0].Abrir.EjecutarAsync();

        Assert.Equal([nueva.Id], api.NotificacionesLeidas);
        Assert.Equal([$"{Rutas.MiCaso}?id={Caso}"], navegacion.Rutas);
    }

    [Fact]
    public async Task AlAbrirUnaYaLeida_NoSeVuelveAMarcar()
    {
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(0, [Notificacion(leida: true)]));
        var vm = Bandeja();
        await vm.AlAparecerAsync();

        await vm.Notificaciones[0].Abrir.EjecutarAsync();

        Assert.Empty(api.NotificacionesLeidas);
        Assert.Single(navegacion.Rutas);
    }

    [Fact]
    public async Task SinNotificaciones_SeIndica()
    {
        var vm = Bandeja();

        await vm.AlAparecerAsync();

        Assert.True(vm.SinNotificaciones);
        Assert.Equal("No tienes notificaciones sin leer.", vm.Resumen);
    }

    [Fact]
    public async Task SinRespuesta_SeAvisaYNoSeDaLaBandejaPorVacia()
    {
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.SinRespuesta("sin red");
        var vm = Bandeja();

        await vm.AlAparecerAsync();

        Assert.True(vm.HayMensaje);
        Assert.False(vm.SinNotificaciones);
    }

    [Fact]
    public async Task ConLaSesionVencida_NoSeConsulta()
    {
        sesiones.Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddMinutes(-1));
        var vm = Bandeja();

        await vm.AlAparecerAsync();

        Assert.Equal(0, api.ConsultasBandeja);
        Assert.True(vm.SesionTerminada);
    }

    // ----- Aviso en el inicio -----

    [Fact]
    public async Task ElInicio_MuestraCuantasNotificacionesHaySinLeer_YSeActualizaAlVolver()
    {
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(2, []));
        var inicio = Inicio();

        await inicio.AlAparecerAsync();
        Assert.True(inicio.HayNoLeidas);
        Assert.Equal("Notificaciones (2 sin leer)", inicio.TextoNotificaciones);

        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(0, []));
        await inicio.AlAparecerAsync(); // al volver de la bandeja

        Assert.False(inicio.HayNoLeidas);
        Assert.Equal("Notificaciones", inicio.TextoNotificaciones);
        Assert.Equal(2, api.ConsultasBandeja);
    }

    [Fact]
    public async Task ElAvisoDelInicio_SeIdentificaPorLaNotificacionMasReciente_NoPorElNumero()
    {
        // Una sin leer; se lee; llega otra: el número es el mismo, pero el aviso es otro (si se quitó el primero, este se ve).
        NotificacionCiudadanoVista Aviso(bool leida) => new(Guid.NewGuid(), Guid.NewGuid(), "Tu SOS pasó a «En atención».", DateTimeOffset.UtcNow, leida);
        var primera = Aviso(false);
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(1, [primera]));
        var inicio = Inicio();
        await inicio.AlAparecerAsync();
        var clavePrimera = inicio.ClaveNotificaciones;

        var segunda = Aviso(false);
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(1, [segunda, primera with { Leida = true }]));
        await inicio.AlAparecerAsync();

        Assert.Equal(primera.Id.ToString(), clavePrimera);
        Assert.Equal(segunda.Id.ToString(), inicio.ClaveNotificaciones);
        Assert.Equal("Notificaciones (1 sin leer)", inicio.TextoNotificaciones);
    }

    [Fact]
    public async Task ElInicio_SinRespuesta_NoMuestraUnNumero()
    {
        api.RespuestaBandeja = () => new ResultadoApi<BandejaNotificaciones>.SinRespuesta("sin red");
        var inicio = Inicio();

        await inicio.AlAparecerAsync();

        Assert.False(inicio.HayNoLeidas);
        Assert.Equal("Notificaciones", inicio.TextoNotificaciones);
    }

    [Fact]
    public async Task DesdeElInicio_SeAbreLaBandeja()
    {
        var inicio = Inicio();

        await inicio.VerNotificaciones.EjecutarAsync();

        Assert.Equal([Rutas.Notificaciones], navegacion.Rutas);
    }
}
