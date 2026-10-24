using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// Rango de atención en la app (24/10/2026): «Llegaste» dentro del rango y, fuera, el motivo antes de iniciar la atención.
// Puntos ficticios en Arequipa.
public class RangoAtencionTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 20, 15, 0, 0, TimeSpan.Zero);
    private const double LatCaso = -16.3988, LonCaso = -71.5369;
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };

    private static DespachoSereno EnCamino(UbicacionDespacho? punto) =>
        new(Guid.NewGuid(), "SOS", DespachoSereno.EstadoEnCamino, Inicio, Inicio.AddMinutes(1), punto, []);

    // Un sereno operativo en (lat, lon), con un caso «En camino».
    private async Task<InicioSerenoVistaModelo> SerenoEnAsync(double lat, double lon, bool casoConPunto = true)
    {
        var despacho = EnCamino(casoConPunto ? new UbicacionDespacho(LatCaso, LonCaso, 10, Inicio) : null);
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(despacho);
        api.EstadoServidor = new RespuestaEstadoOperativo(true, Inicio, 15);
        ubicacion.Posicion = () => new UbicacionSos(lat, lon, 8, Inicio);
        var reloj = new FakeTimeProvider(Inicio.AddSeconds(5));
        var vm = new InicioSerenoVistaModelo(new ServicioTurnoSereno(api, ubicacion, sesiones, reloj),
            new ServicioDespacho(api, sesiones, new CanalFalso(), TimeProvider.System),
            new ServicioNotificaciones(api, sesiones, new ServicioPushNoDisponible(), TimeProvider.System), sesiones, new NavegacionFalsa(), () => { },
            reloj: reloj)
        { HiloPrincipal = a => { a(); return Task.CompletedTask; } };
        await vm.AlAparecerAsync();
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (vm.PosicionPropia is null && DateTime.UtcNow < limite)
            await Task.Delay(10);
        return vm;
    }

    [Fact]
    public async Task DentroDelRango_SeAvisaLlegaste_YSeIniciaConLaPosicion()
    {
        var vm = await SerenoEnAsync(LatCaso - 0.0004, LonCaso); // ~45 m

        Assert.True(vm.EnElLugar);
        Assert.True(vm.MostrarLlegaste);
        await vm.IniciarAtencion.EjecutarAsync();

        var solicitud = Assert.Single(api.InicioSolicitudes);
        Assert.Equal(LatCaso - 0.0004, solicitud.Posicion!.Latitud);
        Assert.Null(solicitud.Motivo);
        Assert.False(vm.MostrarMotivoInicio);
    }

    [Fact]
    public async Task ElAvisoDeLlegada_SeDisparaUnaVezPorCaso()
    {
        var llegadas = 0;
        var vm = await SerenoEnAsync(LatCaso + 0.03, LonCaso); // lejos al principio
        vm.Llegada += () => Interlocked.Increment(ref llegadas);
        Assert.False(vm.MostrarLlegaste);

        // Se acerca: el turno envía la nueva posición y la pantalla se actualiza.
        ubicacion.Posicion = () => new UbicacionSos(LatCaso, LonCaso + 0.0005, 8, Inicio);
        await vm.Alternar.EjecutarAsync(); // con un caso en curso, pide confirmación…
        await vm.ConfirmarDesactivacion.EjecutarAsync(); // …y se desactiva
        await vm.Alternar.EjecutarAsync(); // vuelve a activar y lee la posición
        // Se espera al aviso, no a MostrarLlegaste: esa propiedad se calcula al vuelo y puede verse antes de que el
        // modelo de vista termine de revisar la llegada en el otro hilo.
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref llegadas) == 0 && DateTime.UtcNow < limite)
            await Task.Delay(10);
        await Task.Delay(50); // por si llegara un segundo aviso

        Assert.True(vm.MostrarLlegaste);
        Assert.Equal(1, Volatile.Read(ref llegadas));
    }

    [Fact]
    public async Task FueraDelRango_PrimeroSePideElMotivo_YSeEnviaConEl()
    {
        var vm = await SerenoEnAsync(LatCaso - 0.01, LonCaso); // ~1,1 km

        await vm.IniciarAtencion.EjecutarAsync();

        Assert.Empty(api.InicioSolicitudes); // todavía no se envía nada
        Assert.True(vm.MostrarMotivoInicio);
        Assert.False(vm.MostrarBotonIniciar);
        Assert.StartsWith("Estás a 1,1 km del punto del caso (el rango es de 150 m).", vm.TextoFueraDeRango);
        Assert.DoesNotContain(vm.OpcionesMotivo, o => o.Clave == MotivosInicioFueraDeRango.SinPosicion); // tiene posición

        await vm.ConfirmarInicioFueraDeRango.EjecutarAsync();
        Assert.Equal("Elige un motivo.", vm.ErrorMotivoInicio);
        vm.MotivoInicio = vm.OpcionesMotivo.Single(o => o.Clave == MotivosInicioFueraDeRango.Otro);
        Assert.True(vm.PideDetalleMotivo);
        await vm.ConfirmarInicioFueraDeRango.EjecutarAsync();
        Assert.Equal("Cuenta brevemente el motivo.", vm.ErrorMotivoInicio);
        Assert.Empty(api.InicioSolicitudes);

        vm.MotivoInicio = vm.OpcionesMotivo.Single(o => o.Clave == MotivosInicioFueraDeRango.CiudadanoSeMovio);
        await vm.ConfirmarInicioFueraDeRango.EjecutarAsync();

        var solicitud = Assert.Single(api.InicioSolicitudes);
        Assert.Equal(MotivosInicioFueraDeRango.CiudadanoSeMovio, solicitud.Motivo);
        Assert.NotNull(solicitud.Posicion);
        Assert.False(vm.MostrarMotivoInicio);
    }

    [Fact]
    public async Task SiElServidorLoVeFueraDelRango_SeAbreElMotivoConSuMensaje()
    {
        var vm = await SerenoEnAsync(LatCaso - 0.0004, LonCaso); // la app lo ve dentro
        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.Rechazado(422,
            "Estás a 600 m del punto del caso (el rango es de 150 m). Indica por qué inicias la atención aquí.",
            new Dictionary<string, string[]> { [RangoAtencion.ErrorFueraDeRango] = ["600", "150"] });

        await vm.IniciarAtencion.EjecutarAsync();

        Assert.True(vm.MostrarMotivoInicio);
        Assert.StartsWith("Estás a 600 m", vm.TextoFueraDeRango);
        Assert.False(vm.HayErrorDespacho); // no es un error: falta el motivo
    }

    [Fact]
    public async Task SinPuntoDelCaso_NoHayRango_YSeIniciaDirectamente()
    {
        var vm = await SerenoEnAsync(LatCaso - 0.01, LonCaso, casoConPunto: false);

        await vm.IniciarAtencion.EjecutarAsync();

        Assert.False(vm.MostrarLlegaste);
        Assert.False(vm.MostrarMotivoInicio);
        Assert.Single(api.InicioSolicitudes);
    }

    // 24/10/2026 (DP16-b sigue vigente): con un caso en curso, desactivarse se confirma antes.
    [Fact]
    public async Task ConUnCasoEnCurso_DesactivarsePideConfirmacion()
    {
        var vm = await SerenoEnAsync(LatCaso - 0.01, LonCaso);
        var antes = api.CambiosEstado.Count;

        await vm.Alternar.EjecutarAsync();
        Assert.True(vm.MostrarConfirmarDesactivacion);
        Assert.False(vm.MostrarBotonAlternar);
        Assert.Equal(antes, api.CambiosEstado.Count); // todavía no se desactivó

        await vm.SeguirOperativo.EjecutarAsync();
        Assert.False(vm.MostrarConfirmarDesactivacion);
        Assert.True(vm.Operativo);

        await vm.Alternar.EjecutarAsync();
        await vm.ConfirmarDesactivacion.EjecutarAsync();
        Assert.False(vm.MostrarConfirmarDesactivacion);
        Assert.False(Assert.Single(api.CambiosEstado.Skip(antes)).Operativo);
    }
}
