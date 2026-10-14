using Cad.Contratos.Serenos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU21 — Recibir y consultar un despacho (lado de la app). Datos ficticios.
public class DespachoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly ApiFalsa api = new();
    private readonly CanalFalso canal = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };

    private static DespachoSereno Despacho(UbicacionDespacho? ubicacion = null, params InformacionDespacho[] informacion) =>
        new(Guid.NewGuid(), "SOS", "Asignada", Inicio, Inicio.AddMinutes(2), ubicacion, informacion);

    private ServicioDespacho Servicio() => new(api, sesiones, canal, TimeProvider.System);

    private InicioSerenoVistaModelo VistaModelo(ServicioDespacho despacho) =>
        new(new ServicioTurnoSereno(api, new UbicacionFalsa(), sesiones, new FakeTimeProvider(Inicio)), despacho, sesiones, new NavegacionFalsa(), () => { })
        { HiloPrincipal = a => { a(); return Task.CompletedTask; } };

    private static async Task Esperar(Func<bool> condicion, string descripcion)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException($"No se cumplió: {descripcion}");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task AlAbrir_SeConectaAlCanalYSeConsultaElDespacho()
    {
        var esperado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(esperado);
        var despacho = Servicio();

        await despacho.IniciarAsync();

        Assert.Equal(1, canal.Conexiones);
        Assert.Equal(esperado, despacho.Actual);
        Assert.False(despacho.AvisoNuevo);
    }

    [Fact]
    public async Task CA01_AlRecibirElAviso_VuelveAConsultarYMarcaUnDespachoNuevo()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        Assert.Null(despacho.Actual);
        Assert.True(despacho.Consultado);
        var nuevo = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(nuevo);

        canal.Avisar();

        await Esperar(() => despacho.Actual == nuevo, "el despacho nuevo se consulta");
        Assert.True(despacho.AvisoNuevo);
    }

    [Fact]
    public async Task ADR07_SinCanal_ElDespachoSeConsultaIgualmente()
    {
        canal.Conecta = false;
        var esperado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(esperado);
        var despacho = Servicio();

        await despacho.IniciarAsync();

        Assert.False(despacho.CanalConectado);
        Assert.Equal(esperado, despacho.Actual);
    }

    [Fact]
    public async Task AlReconectar_VuelveAConsultar_PorSiSePerdioUnAviso()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        var consultas = api.ConsultasDespacho;

        canal.Reconectar();

        await Esperar(() => api.ConsultasDespacho > consultas, "se consulta tras reconectar");
    }

    [Fact]
    public async Task SinConexion_ConservaElUltimoDespachoYAvisa()
    {
        var conocido = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(conocido);
        var despacho = Servicio();
        await despacho.IniciarAsync();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.SinRespuesta("No hay conexión con el servidor.");

        await despacho.ActualizarAsync();

        Assert.Equal(conocido, despacho.Actual);
        Assert.Contains("No se pudo consultar tu despacho", despacho.Error);
    }

    [Fact]
    public async Task AlSalir_NoSeHeredaElDespachoNiSeSigueEscuchando()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Despacho());
        var despacho = Servicio();
        await despacho.IniciarAsync();

        await despacho.ReiniciarAsync();
        var consultas = api.ConsultasDespacho;
        canal.Avisar();
        await Task.Delay(100);

        Assert.Null(despacho.Actual);
        Assert.False(despacho.Consultado);
        Assert.Equal(consultas, api.ConsultasDespacho);
    }

    [Fact]
    public async Task CA02_LaPantallaMuestraOrigenEstadoIdentificadorUbicacionEInformacion()
    {
        var d = Despacho(new UbicacionDespacho(-12.0464, -77.0428, 10, Inicio.AddSeconds(1)),
            new InformacionDespacho(Inicio.AddMinutes(1), "Dos personas discuten", "Frente al parque ficticio"));
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(d);
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.True(vm.HayDespacho);
        Assert.Equal("SOS · Asignada", vm.TextoDespacho);
        Assert.Contains(d.CasoId.ToString(), vm.TextoIdentificador);
        Assert.True(vm.HayUbicacionDespacho);
        Assert.StartsWith("-12.04640, -77.04280 · ± 10 m", vm.TextoUbicacionDespacho);
        var informacion = Assert.Single(vm.InformacionDespacho);
        Assert.Equal("Dos personas discuten", informacion.InformacionHecho);
        Assert.Equal("Frente al parque ficticio", informacion.ReferenciaUbicacion);
    }

    [Fact]
    public async Task CA03_SinUbicacion_NoHayCoordenadas_YLaReferenciaVaAparte()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(
            Despacho(null, new InformacionDespacho(Inicio, null, "Esquina ficticia")));
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.False(vm.HayUbicacionDespacho);
        Assert.True(vm.SinUbicacionDespacho);
        Assert.Equal("", vm.TextoUbicacionDespacho);
        var informacion = Assert.Single(vm.InformacionDespacho);
        Assert.True(informacion.HayReferencia);
        Assert.False(informacion.HayHecho);
    }

    [Fact]
    public async Task SinDespacho_LoDiceSoloTrasConsultar()
    {
        var despacho = Servicio();
        var vm = VistaModelo(despacho);
        Assert.False(vm.SinDespacho); // aún no se sabe

        await vm.AlAparecerAsync();

        Assert.True(vm.SinDespacho);
        Assert.False(vm.HayDespacho);
    }
}
