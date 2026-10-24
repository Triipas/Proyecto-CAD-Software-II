using System.Collections.Concurrent;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.Tests;

// Sonidos de la app (24/10/2026): cada uno acompaña a su aviso en pantalla. Datos ficticios.
public class SonidosTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly ApiFalsa api = new();
    private readonly CanalFalso canal = new();
    private readonly SonidosFalsos sonidos = new();
    private readonly SesionesFalsas sereno = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };

    private static DespachoSereno Despacho() => new(Guid.NewGuid(), "SOS", "Asignada", Inicio, Inicio.AddMinutes(2), null, []);

    private ServicioDespacho Servicio() => new(api, sereno, canal, TimeProvider.System, sonidos);

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
    public async Task UnDespachoNuevo_SuenaUnaVez()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        Assert.Empty(sonidos.Reproducidos); // abrir la app sin aviso no suena
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Despacho());

        canal.Avisar();

        await Esperar(() => !sonidos.Reproducidos.IsEmpty, "suena el despacho nuevo");
        Assert.Equal([Sonido.NuevoDespacho], sonidos.Reproducidos);
    }

    [Fact]
    public async Task UnAvanceRechazado_SuenaElError_YUnoAplicado_NoSuena()
    {
        var asignado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(asignado);
        var despacho = Servicio();
        await despacho.IniciarAsync();

        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.SinRespuesta("No hay conexión con el servidor.");
        Assert.False(await despacho.ConfirmarDesplazamientoAsync());
        Assert.Equal([Sonido.Error], sonidos.Reproducidos);

        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.Correcto(asignado with { Estado = DespachoSereno.EstadoEnCamino });
        Assert.True(await despacho.ConfirmarDesplazamientoAsync());
        Assert.Equal([Sonido.Error], sonidos.Reproducidos);
    }

    [Fact]
    public async Task UnSosConfirmado_SuenaComoRegistrado_YUnoSinRespuesta_ComoError()
    {
        var reloj = new RelojFijo(Inicio);
        var emisor = new EmisorSos(api, new UbicacionFalsa(), new SesionesFalsas(), reloj, sonidos) { EsperasEntreIntentos = [] };

        await emisor.ActivarAsync();
        Assert.Equal([Sonido.SosRegistrado], sonidos.Reproducidos);

        var otro = new EmisorSos(api, new UbicacionFalsa(), new SesionesFalsas(), reloj, sonidos) { EsperasEntreIntentos = [] };
        api.RespuestasSos.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaSos>>(new ResultadoApi<RespuestaSos>.SinRespuesta("sin red")));
        await otro.ActivarAsync();
        Assert.Equal([Sonido.SosRegistrado, Sonido.Error], sonidos.Reproducidos);
    }
}

public sealed class SonidosFalsos : IReproductorSonidos
{
    public ConcurrentQueue<Sonido> Reproducidos { get; } = new();
    public void Reproducir(Sonido sonido) => Reproducidos.Enqueue(sonido);
}
