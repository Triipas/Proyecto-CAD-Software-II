using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// 26/10/2026 — «¿Cómo vas?»: el ciudadano avisa a la central cómo va durante su acompañamiento, sin que se lo pidan (lado de la
// app). Coordenadas ficticias.
public class EstadoCiudadanoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider reloj = new(Inicio);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ciudadano", "Ciudadano", Inicio.AddHours(8)) };
    private readonly Guid sesionId = Guid.NewGuid();

    public EstadoCiudadanoTests()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, reloj.GetUtcNow());
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow(), id: sesionId));
    }

    private async Task<(ServicioAcompanamiento Servicio, AcompanamientoVistaModelo Vm)> ActivoAsync()
    {
        var servicio = new ServicioAcompanamiento(api, ubicacion, sesiones, new AlmacenLocalFalso(), reloj, new SegundoPlanoFalso());
        var vm = new AcompanamientoVistaModelo(servicio, ubicacion, new NavegacionFalsa(), () => { }, reloj) { HiloPrincipal = a => { a(); return Task.CompletedTask; } };
        await servicio.IniciarAsync(true);
        return (servicio, vm);
    }

    [Fact]
    public async Task ConLaSesionActivaYSinConfirmacionPendiente_SeMuestraComoVasConSusCuatroOpciones()
    {
        var (servicio, vm) = await ActivoAsync();

        Assert.True(vm.MostrarComoVas);
        Assert.All(new[] { vm.InformarEstoyBien, vm.InformarAlgoNoEstaBien, vm.InformarNecesitoAyuda, vm.InformarLlegue }, c => Assert.True(c.CanExecute(null)));
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task EstoyBienYAlgoNoEstaBien_SeEnvianSinSos_YElAcompanamientoSigueActivo()
    {
        var (servicio, vm) = await ActivoAsync();

        await servicio.InformarEstadoAsync(EstadosCiudadano.AlgoNoEstaBien);

        var (id, solicitud) = Assert.Single(api.EstadosInformados);
        Assert.Equal((sesionId, EstadosCiudadano.AlgoNoEstaBien), (id, solicitud.Estado));
        Assert.Null(solicitud.Sos);
        Assert.Contains("te tiene en seguimiento", vm.ResultadoEstado);
        Assert.Equal("exito", vm.TipoResultadoEstado);
        Assert.True(servicio.Activo);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task NecesitoAyuda_LlevaElSosConLaUbicacion_YSinRespuestaElReintentoReutilizaLaMismaSolicitud()
    {
        var (servicio, vm) = await ActivoAsync();
        api.RespuestaEstado = _ => new ResultadoApi<EstadoInformadoVista>.SinRespuesta("HttpRequestException");

        await servicio.InformarEstadoAsync(EstadosCiudadano.NecesitoAyuda);
        Assert.Equal("error", vm.TipoResultadoEstado);
        Assert.Contains("NO está confirmado", vm.ResultadoEstado);
        api.RespuestaEstado = null;
        await servicio.InformarEstadoAsync(EstadosCiudadano.NecesitoAyuda);

        var envios = api.EstadosInformados.Select(e => e.Solicitud).ToList();
        Assert.Equal(2, envios.Count);
        Assert.Equal(envios[0], envios[1]); // misma solicitud y mismo SOS: el servidor no duplica nada
        Assert.NotNull(envios[0].Sos?.Ubicacion);
        Assert.Equal("Pedido de ayuda registrado: la central recibió tu SOS.", vm.ResultadoEstado);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task Llegue_AvisaLaLlegada_YFinalizaElAcompanamiento()
    {
        var (servicio, vm) = await ActivoAsync();

        await servicio.InformarEstadoAsync(EstadosCiudadano.Llegue);

        Assert.Equal(EstadosCiudadano.Llegue, Assert.Single(api.EstadosInformados).Solicitud.Estado);
        Assert.Contains(sesionId, api.Finalizaciones);
        Assert.Equal(EstadoAcompanamiento.Finalizado, servicio.Estado);
        Assert.False(vm.MostrarComoVas);
    }

    [Fact]
    public async Task Llegue_DejaDeCompartirLaUbicacionAntesDeEsperarLaRespuesta()
    {
        var (servicio, _) = await ActivoAsync();
        bool? transmitiaAlAvisar = null;
        api.RespuestaEstado = s =>
        {
            transmitiaAlAvisar = servicio.Transmitiendo;
            return new ResultadoApi<EstadoInformadoVista>.SinRespuesta("TaskCanceledException");
        };

        await servicio.InformarEstadoAsync(EstadosCiudadano.Llegue);

        Assert.False(transmitiaAlAvisar);
        Assert.Contains(sesionId, api.Finalizaciones);
    }

    [Fact]
    public async Task UnAvisoSinConfirmar_NoSeReutilizaEnOtraSesion()
    {
        var (servicio, _) = await ActivoAsync();
        api.RespuestaEstado = _ => new ResultadoApi<EstadoInformadoVista>.SinRespuesta("HttpRequestException");
        await servicio.InformarEstadoAsync(EstadosCiudadano.NecesitoAyuda);
        await servicio.FinalizarAsync();
        var otra = Guid.NewGuid();
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow(), id: otra));
        await servicio.IniciarAsync(true);

        await servicio.InformarEstadoAsync(EstadosCiudadano.NecesitoAyuda);

        var envios = api.EstadosInformados.ToList();
        Assert.Equal((sesionId, otra), (envios[0].SesionId, envios[1].SesionId));
        Assert.NotEqual(envios[0].Solicitud.IdSolicitud, envios[1].Solicitud.IdSolicitud);
        Assert.NotEqual(envios[0].Solicitud.Sos!.IdSolicitud, envios[1].Solicitud.Sos!.IdSolicitud);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task Llegue_SinConexion_DejaDeCompartirLaUbicacionIgual()
    {
        var (servicio, vm) = await ActivoAsync();
        api.RespuestaEstado = _ => new ResultadoApi<EstadoInformadoVista>.SinRespuesta("HttpRequestException");

        await servicio.InformarEstadoAsync(EstadosCiudadano.Llegue);

        Assert.False(servicio.Transmitiendo);
        Assert.False(servicio.Activo);
        Assert.Contains("tu llegada no llegó a la central", vm.ResultadoEstado);
    }

    [Fact]
    public async Task ConUnaConfirmacionPendiente_SeMuestraLaConfirmacionEnLugarDeComoVas()
    {
        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(
            new ConfirmacionBienestarVista(Guid.NewGuid(), Inicio, null, null, null, null));
        var (servicio, vm) = await ActivoAsync();
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (servicio.Confirmacion is null && DateTime.UtcNow < limite)
            await Task.Delay(10);

        Assert.True(vm.HayConfirmacion);
        Assert.False(vm.MostrarComoVas);
        await servicio.DetenerAsync();
    }
}
