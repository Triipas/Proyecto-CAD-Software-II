using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.Tests;

// HU07 — Emitir un SOS con una única activación — y HU08 — ubicación actual del SOS (lado de la app).
public class EmisorSosTests
{
    private static readonly DateTimeOffset Activacion = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private readonly UbicacionFalsa ubicacion = new();
    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new();
    private readonly RelojFijo reloj = new(Activacion);

    private EmisorSos Emisor() => new(api, ubicacion, sesiones, reloj) { EsperasEntreIntentos = [TimeSpan.Zero, TimeSpan.Zero] };

    private static ResultadoApi<RespuestaSos> SinRespuesta() => new ResultadoApi<RespuestaSos>.SinRespuesta("sin red");

    [Fact]
    public async Task HU07_CA01_UnaActivacion_EnviaUnSoloSosSinPedirNadaMas()
    {
        var emisor = Emisor();

        await emisor.ActivarAsync();

        var enviado = Assert.Single(api.SosEnviados);
        Assert.NotEqual(Guid.Empty, enviado.IdSolicitud);
        Assert.Equal(Activacion, enviado.ActivadoEn);
        Assert.IsType<EstadoSos.Confirmado>(emisor.Estado);
    }

    [Fact]
    public async Task HU07_CA01_SegundoToqueMientrasSeEnvia_NoCreaOtroSos()
    {
        var respuesta = new TaskCompletionSource<ResultadoApi<RespuestaSos>>();
        api.RespuestasSos.Enqueue(_ => respuesta.Task);
        var emisor = Emisor();

        var primero = emisor.ActivarAsync();
        var segundo = emisor.ActivarAsync(); // doble toque mientras el primero espera al servidor
        Assert.True(segundo.IsCompleted); // se ignora al instante, sin esperar al servidor
        respuesta.SetResult(ApiFalsa.Registrado(api.SosEnviados.First()));
        await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(api.SosEnviados);
    }

    [Fact]
    public async Task HU07_CA03_NoSeConfirmaHastaQueRespondeElServidor()
    {
        var respuesta = new TaskCompletionSource<ResultadoApi<RespuestaSos>>();
        api.RespuestasSos.Enqueue(_ => respuesta.Task);
        var emisor = Emisor();
        var estados = new List<EstadoSos>();
        emisor.Cambio += estados.Add;

        var envio = emisor.ActivarAsync();
        Assert.IsType<EstadoSos.Enviando>(emisor.Estado);
        Assert.DoesNotContain(estados, e => e is EstadoSos.Confirmado);

        respuesta.SetResult(ApiFalsa.Registrado(api.SosEnviados.Single()));
        await envio.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<EstadoSos.Confirmado>(emisor.Estado);
    }

    [Fact]
    public async Task HU07_CA04_CA07_SinRespuesta_ReintentaConElMismoIdYNoConfirma()
    {
        api.RespuestasSos.Enqueue(_ => Task.FromResult(SinRespuesta()));
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Equal(3, api.SosEnviados.Count); // 1 intento + 2 reintentos técnicos
        Assert.Single(api.SosEnviados.Select(s => s.IdSolicitud).Distinct());
        Assert.IsType<EstadoSos.NoConfirmado>(emisor.Estado);
        Assert.True(emisor.PuedeReintentar);
    }

    [Fact]
    public async Task HU07_CA07_ReintentoManual_EnviaExactamenteLaMismaSolicitud()
    {
        api.RespuestasSos.Enqueue(_ => Task.FromResult(SinRespuesta()));
        api.RespuestasSos.Enqueue(_ => Task.FromResult(SinRespuesta()));
        api.RespuestasSos.Enqueue(_ => Task.FromResult(SinRespuesta()));
        api.RespuestasSos.Enqueue(s => Task.FromResult(ApiFalsa.Registrado(s)));
        var emisor = Emisor();
        await emisor.ActivarAsync();

        await emisor.ReintentarAsync();

        Assert.IsType<EstadoSos.Confirmado>(emisor.Estado);
        Assert.Single(api.SosEnviados.Distinct()); // misma solicitud: mismo id, momento y ubicación
        Assert.False(emisor.PuedeReintentar);
    }

    [Fact]
    public async Task HU07_CA04_FalloInesperadoDeLaApi_NoCierraLaApp_YPermiteReintentar()
    {
        api.RespuestasSos.Enqueue(_ => throw new InvalidOperationException("fallo imprevisto"));
        var emisor = Emisor();

        await emisor.ActivarAsync(); // no debe lanzar

        Assert.Equal(3, api.SosEnviados.Count);
        Assert.Single(api.SosEnviados.Select(s => s.IdSolicitud).Distinct());
        Assert.IsType<EstadoSos.NoConfirmado>(emisor.Estado);
        Assert.True(emisor.PuedeReintentar);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task RechazoDelServidor_NoSeReintentaAutomaticamente(int estado)
    {
        api.RespuestasSos.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaSos>>(
            new ResultadoApi<RespuestaSos>.Rechazado(estado, "Rechazado", new Dictionary<string, string[]>())));
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Single(api.SosEnviados);
        Assert.Equal(estado, Assert.IsType<EstadoSos.Rechazado>(emisor.Estado).Estado);
    }

    [Fact]
    public async Task SesionVencida_NoEnviaYPideVolverAIngresar()
    {
        sesiones.Sesion = new SesionApp("token", "Ciudadano", Activacion.AddMinutes(-1));
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Empty(api.SosEnviados);
        Assert.Equal(401, Assert.IsType<EstadoSos.Rechazado>(emisor.Estado).Estado);
    }

    [Fact]
    public async Task DP14_TrasConfirmar_UnaNuevaActivacionEsUnSosNuevo()
    {
        var emisor = Emisor();

        await emisor.ActivarAsync();
        await emisor.ActivarAsync();

        Assert.Equal(2, api.SosEnviados.Select(s => s.IdSolicitud).Distinct().Count());
    }

    [Fact]
    public async Task HU08_CA01_ConPermiso_PideLaPosicionActualConEsperaMaximaDe10s()
    {
        var actual = new UbicacionSos(-12.0464, -77.0428, 8, Activacion.AddSeconds(2));
        ubicacion.Posicion = () => actual;
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Equal(actual, api.SosEnviados.Single().Ubicacion);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(ubicacion.Lecturas)); // DP11
    }

    [Fact]
    public async Task HU08_CA02_SinPosicion_ElSosSeEnviaIgualSinUbicacion()
    {
        ubicacion.Posicion = () => null;
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Null(api.SosEnviados.Single().Ubicacion);
        Assert.IsType<EstadoSos.Confirmado>(emisor.Estado);
    }

    [Fact]
    public async Task HU08_CA02_FalloDelGps_ElSosSeEnviaIgualSinUbicacion()
    {
        ubicacion.Falla = true;
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Null(api.SosEnviados.Single().Ubicacion);
    }

    [Fact]
    public async Task HU08_CA02_UbicacionQueNoResponde_ElSosSaleSinUbicacionALos10s()
    {
        // Hallazgo de SPIKE01: con la app en segundo plano, Android puede no entregar ninguna lectura.
        var relojSimulado = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Activacion);
        ubicacion.NoResponde = true;
        var emisor = new EmisorSos(api, ubicacion, sesiones, relojSimulado) { EsperasEntreIntentos = [TimeSpan.Zero, TimeSpan.Zero] };

        var envio = emisor.ActivarAsync();
        Assert.IsType<EstadoSos.ObteniendoUbicacion>(emisor.Estado);
        relojSimulado.Advance(TimeSpan.FromSeconds(9));
        Assert.Empty(api.SosEnviados);
        relojSimulado.Advance(TimeSpan.FromSeconds(1));
        await envio.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(Assert.Single(api.SosEnviados).Ubicacion);
        Assert.IsType<EstadoSos.Confirmado>(emisor.Estado);
    }

    [Fact]
    public async Task HU08_CA03_PosicionAnteriorALaActivacion_NoSeEnviaComoActual()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, Activacion.AddMinutes(-3));
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Null(api.SosEnviados.Single().Ubicacion);
    }

    [Theory]
    [InlineData(EstadoPermisoUbicacion.Desconocido)]
    [InlineData(EstadoPermisoUbicacion.Denegado)]
    public async Task HU08_CA04_SinPermiso_NoVuelveAPedirloNiLeeLaPosicion(EstadoPermisoUbicacion permiso)
    {
        ubicacion.Permiso = permiso;
        var emisor = Emisor();

        await emisor.ActivarAsync();

        Assert.Equal(0, ubicacion.Solicitudes);
        Assert.Empty(ubicacion.Lecturas);
        Assert.Null(api.SosEnviados.Single().Ubicacion); // el SOS sale igual (HU07-CA06)
    }
}
