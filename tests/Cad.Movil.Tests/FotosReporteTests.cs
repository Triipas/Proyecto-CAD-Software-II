using Cad.Contratos.Reportes;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// DP08 ampliada (26/10/2026) — fotos del reporte en la app: hasta tres, opcionales, enviadas en la misma solicitud y
// reenviadas tal cual en un reintento. Imágenes ficticias (la preparación real del JPEG es de Android y se verifica en el emulador).
public class FotosReporteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = Guid.NewGuid();

    private readonly ApiFalsa api = new() { Categorias = [new(Robo, "Robo")] };
    private readonly SesionesFalsas sesiones = new();
    private readonly RelojFijo reloj = new(Ahora);
    private readonly FotosFalsas fotos = new();

    private RegistradorReporte Registrador() => new(api, sesiones, reloj) { EsperasEntreIntentos = [TimeSpan.Zero, TimeSpan.Zero] };

    private static byte[] Foto(byte sello) => [0xFF, 0xD8, 0xFF, 0xE0, sello, sello, 0xFF, 0xD9];

    private static DatosReporte Validos(IReadOnlyList<byte[]>? adjuntas) =>
        new(Robo, "Robo de una bicicleta", Ahora.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Esquina del mercado"), adjuntas);

    private async Task<ReporteVistaModelo> FormularioAsync(RegistradorReporte? registrador = null)
    {
        var vm = new ReporteVistaModelo(api, registrador ?? Registrador(), new UbicacionFalsa(), sesiones, new NavegacionFalsa(), reloj, Lima, fotos: fotos);
        await vm.AlAparecerAsync();
        await vm.ElegirCategoria.EjecutarAsync(vm.Opciones[0]);
        vm.Descripcion = "Robo de una bicicleta";
        vm.Referencia = "Esquina del mercado";
        return vm;
    }

    [Fact]
    public async Task ConFotos_ViajanEnLaMismaSolicitud()
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos([Foto(1), Foto(2)]));

        var enviado = Assert.Single(api.ReportesEnviados);
        Assert.Equal([Foto(1), Foto(2)], enviado.Fotos!);
    }

    [Fact]
    public async Task SinFotos_LaSolicitudNoLasLleva()
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos([]));

        Assert.Null(Assert.Single(api.ReportesEnviados).Fotos);
    }

    [Fact]
    public async Task ElReintento_ReenviaLasMismasFotosConElMismoIdSolicitud()
    {
        for (var i = 0; i < 3; i++)
            api.RespuestasReporte.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaReporte>>(new ResultadoApi<RespuestaReporte>.SinRespuesta("sin red")));
        api.RespuestasReporte.Enqueue(s => Task.FromResult(ApiFalsa.ReporteRegistrado(s)));
        var registrador = Registrador();
        await registrador.EnviarAsync(Validos([Foto(7)]));
        Assert.IsType<EstadoReporte.NoConfirmado>(registrador.Estado);

        await registrador.ReintentarAsync();

        var enviados = api.ReportesEnviados.ToList();
        Assert.Equal(4, enviados.Count);
        Assert.Single(enviados.Select(e => e.IdSolicitud).Distinct());
        Assert.All(enviados, e => Assert.Equal([Foto(7)], e.Fotos!));
        Assert.IsType<EstadoReporte.Registrado>(registrador.Estado);
    }

    [Fact]
    public async Task MasDeTresFotos_OUnaDeMasDeUnMega_NoSeEnvia()
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos([Foto(1), Foto(2), Foto(3), Foto(4)]));
        Assert.Contains("fotos", Assert.IsType<EstadoReporte.Rechazado>(registrador.Estado).Errores.Keys);
        await registrador.EnviarAsync(Validos([new byte[LimitesFotosReporte.MaximoBytes + 1]]));
        Assert.Contains("fotos", Assert.IsType<EstadoReporte.Rechazado>(registrador.Estado).Errores.Keys);

        Assert.Empty(api.ReportesEnviados);
    }

    [Fact]
    public async Task LaGaleria_PideSoloLasQueFaltanYNoPasaDeTres()
    {
        var vm = await FormularioAsync();
        fotos.Elegidas.Enqueue([Foto(1), Foto(2)]);
        await vm.ElegirFotos.EjecutarAsync();
        // Android no respeta el límite del selector: llegan dos, pero queda lugar para una.
        fotos.Elegidas.Enqueue([Foto(3), Foto(4)]);
        await vm.ElegirFotos.EjecutarAsync();

        Assert.Equal([3, 1], fotos.MaximosPedidos);
        Assert.Equal(3, vm.Fotos.Count);
        Assert.Equal("3 de 3", vm.ContadorFotos);
        Assert.False(vm.PuedeAgregarFotos);
        Assert.False(vm.ElegirFotos.CanExecute(null));
        Assert.False(vm.TomarFoto.CanExecute(null));
        Assert.True(vm.HayMensajeFotos);
    }

    [Fact]
    public async Task QuitarUnaFoto_RenumeraYSeEnvianLasQueQuedan()
    {
        var vm = await FormularioAsync();
        fotos.Elegidas.Enqueue([Foto(1), Foto(2), Foto(3)]);
        await vm.ElegirFotos.EjecutarAsync();

        await vm.QuitarFoto.EjecutarAsync(vm.Fotos[0]);
        await vm.Enviar.EjecutarAsync();

        Assert.Equal([1, 2], vm.Fotos.Select(f => f.Numero));
        Assert.Equal("Foto 1", vm.Fotos[0].DescripcionAccesible);
        Assert.True(vm.PuedeAgregarFotos);
        Assert.Equal([Foto(2), Foto(3)], Assert.Single(api.ReportesEnviados).Fotos!);
    }

    [Fact]
    public async Task LaCamara_AgregaLaFotoTomada_YCancelarNoAgregaNada()
    {
        var vm = await FormularioAsync();
        fotos.Tomadas.Enqueue(Foto(9));
        fotos.Tomadas.Enqueue(null);

        await vm.TomarFoto.EjecutarAsync();
        await vm.TomarFoto.EjecutarAsync();

        Assert.Equal(Foto(9), Assert.Single(vm.Fotos).Contenido);
        Assert.False(vm.HayMensajeFotos);
    }

    [Fact]
    public async Task UnaFotoQueNoSePuedeUsar_ExplicaPorQueYElReporteSigueEditable()
    {
        var vm = await FormularioAsync();
        fotos.Error = new FotoNoUsableException("Ese archivo no es una foto que se pueda adjuntar.");

        await vm.ElegirFotos.EjecutarAsync();

        Assert.Empty(vm.Fotos);
        Assert.Equal("Ese archivo no es una foto que se pueda adjuntar.", vm.MensajeFotos);
        Assert.True(vm.Editable);
        Assert.True(vm.Enviar.CanExecute(null));
    }

    [Fact]
    public async Task SinCamara_NoSeOfreceTomarFoto()
    {
        fotos.PuedeTomar = false;
        var vm = await FormularioAsync();

        Assert.False(vm.PuedeTomarFoto);
        Assert.False(vm.TomarFoto.CanExecute(null));
        Assert.True(vm.ElegirFotos.CanExecute(null));
    }

    [Fact]
    public async Task ElRechazoDelServidorPorUnaFoto_SeMuestraJuntoALasFotos()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaReporte>>(new ResultadoApi<RespuestaReporte>.Rechazado(400,
            "El reporte no es válido.", new Dictionary<string, string[]> { ["fotos[0]"] = ["La foto debe ser una imagen JPEG."] })));
        var vm = await FormularioAsync();
        fotos.Elegidas.Enqueue([Foto(1)]);
        await vm.ElegirFotos.EjecutarAsync();

        await vm.Enviar.EjecutarAsync();

        Assert.Equal("La foto debe ser una imagen JPEG.", vm.ErrorFotos);
        Assert.True(vm.HayErrorFotos);
    }

    private sealed class FotosFalsas : IFotosReporte
    {
        public bool PuedeTomar { get; set; } = true;
        public Queue<byte[]?> Tomadas { get; } = new();
        public Queue<IReadOnlyList<byte[]>> Elegidas { get; } = new();
        public List<int> MaximosPedidos { get; } = [];
        public Exception? Error { get; set; }

        public Task<byte[]?> TomarAsync() => Error is { } e ? Task.FromException<byte[]?>(e) : Task.FromResult(Tomadas.Dequeue());

        public Task<IReadOnlyList<byte[]>> ElegirAsync(int maximo)
        {
            MaximosPedidos.Add(maximo);
            return Error is { } e ? Task.FromException<IReadOnlyList<byte[]>>(e) : Task.FromResult(Elegidas.Dequeue());
        }
    }
}
