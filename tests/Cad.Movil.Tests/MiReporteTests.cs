using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// Decisión del PO del 26/10/2026 — en «Mi caso», el ciudadano consulta en solo lectura lo que dio en su reporte (con sus fotos)
// y puede retirarlo, con motivo, mientras la central no lo haya decidido («Recibido» o «En revisión»). Datos ficticios.
public class MiReporteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Id = Guid.NewGuid();

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };

    private static MiReporte Datos(int fotos = 2, string fuente = FuentesUbicacionHecho.Mapa) =>
        new("Daños o vandalismo", "Pintas en la banca del parque (ficticio).", Ahora.AddHours(-2), fuente,
            fuente == FuentesUbicacionHecho.Texto ? null : -12.0631, fuente == FuentesUbicacionHecho.Texto ? null : -77.0365,
            "Frente a la pileta", fotos);

    private static MiCasoDetalle Caso(string estado, MiReporte? reporte, string? resultado = null) =>
        new(Id, "Reporte", Ahora.AddHours(-1), estado, resultado, [new AvancePublico(Ahora.AddHours(-1), EstadosPublicos.Recibido, null)],
            null, reporte);

    private async Task<MiCasoVistaModelo> AbiertoAsync(string estado = EstadosPublicos.Recibido, MiReporte? reporte = null)
    {
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Caso(estado, reporte ?? Datos()));
        var vm = new MiCasoVistaModelo(api, sesiones, new NavegacionFalsa(), new RelojFijo(Ahora), Lima);
        await vm.CargarAsync(Id);
        return vm;
    }

    [Fact]
    public async Task MuestraLoQueDioEnSoloLecturaConSusFotos()
    {
        var vm = await AbiertoAsync();

        Assert.True(vm.HayReporte);
        Assert.Equal("Daños o vandalismo", vm.ReporteCategoria);
        Assert.Equal("Pintas en la banca del parque (ficticio).", vm.ReporteDescripcion);
        Assert.Equal("26/10/2026 08:00", vm.ReporteOcurrido);
        Assert.Equal("Punto marcado en el mapa (-12.06310, -77.03650)", vm.ReporteLugar);
        Assert.Equal("Frente a la pileta", vm.ReporteReferencia);
        Assert.Equal("2 fotos adjuntas", vm.TextoFotosReporte);
        Assert.Equal([1, 2], vm.FotosReporte.Select(f => f.Numero));
        Assert.Equal([(Id, 1), (Id, 2)], api.FotosConsultadas);
        // Solo lectura: la vista modelo no expone nada para cambiar lo reportado.
        Assert.All(typeof(MiCasoVistaModelo).GetProperties().Where(p => p.Name.StartsWith("Reporte", StringComparison.Ordinal)),
            p => Assert.Null(p.SetMethod));
    }

    [Fact]
    public async Task DosCargasALaVez_NoDuplicanLasFotos()
    {
        // Abrir la página y deslizar para actualizar casi a la vez (observado en el emulador el 26/10/2026).
        api.DemoraFoto = TimeSpan.FromMilliseconds(30);
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Caso(EstadosPublicos.Recibido, Datos(fotos: 1)));
        var vm = new MiCasoVistaModelo(api, sesiones, new NavegacionFalsa(), new RelojFijo(Ahora), Lima);

        await Task.WhenAll(vm.CargarAsync(Id), vm.Actualizar.EjecutarAsync());

        Assert.Equal([1], vm.FotosReporte.Select(f => f.Numero));
    }

    [Fact]
    public async Task SinFotosConReferencia_LoDice_YNoPideFotos()
    {
        var vm = await AbiertoAsync(reporte: Datos(fotos: 0, fuente: FuentesUbicacionHecho.Texto));

        Assert.Equal("Indicado con una referencia", vm.ReporteLugar);
        Assert.Equal("No adjuntaste fotos.", vm.TextoFotosReporte);
        Assert.False(vm.HayFotosReporte);
        Assert.Empty(api.FotosConsultadas);
    }

    [Fact]
    public async Task UnaFotoQueNoLlega_NoImpideVerElReporte()
    {
        api.RespuestaFotoMiCaso = (_, n) => n == 1
            ? new ResultadoApi<byte[]>.SinRespuesta("sin red")
            : new ResultadoApi<byte[]>.Correcto([0xFF, 0xD8, 0xFF, 0xD9]);

        var vm = await AbiertoAsync();

        Assert.Equal([2], vm.FotosReporte.Select(f => f.Numero));
        Assert.Equal("Daños o vandalismo", vm.ReporteCategoria);
        Assert.False(vm.HayMensaje);
    }

    [Theory]
    [InlineData(EstadosPublicos.Recibido)]
    [InlineData(EstadosPublicos.EnRevision)]
    public async Task SinDecidir_SeOfreceRetirarConMotivo(string estado)
    {
        var vm = await AbiertoAsync(estado);
        api.RespuestaCancelacion = (_, s) => new ResultadoApi<MiCasoDetalle>.Correcto(
            Caso(EstadosPublicos.Cancelado, Datos(), $"Cancelado por ti. Motivo: {s.Motivo}"));

        Assert.True(vm.OfrecerCancelacion);
        Assert.Equal("Retirar este reporte", vm.TextoCancelar);
        await vm.MostrarCancelacion.EjecutarAsync();
        await vm.ConfirmarCancelacion.EjecutarAsync();
        Assert.Equal("Indica el motivo del retiro.", vm.ErrorMotivo);
        Assert.Empty(api.Cancelaciones);

        vm.MotivoCancelacion = "Ya lo resolvieron los vecinos.";
        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal([(Id, "Ya lo resolvieron los vecinos.")], api.Cancelaciones);
        Assert.Equal("Retiraste el reporte. La central fue avisada.", vm.Confirmacion);
        Assert.Equal("Reporte · Cancelado", vm.Titulo);
        Assert.False(vm.OfrecerCancelacion);
        Assert.True(vm.HayReporte); // lo que dio sigue a la vista
    }

    [Fact]
    public async Task SiLaCentralDecidioMientrasTanto_MuestraElRechazo()
    {
        var vm = await AbiertoAsync(EstadosPublicos.EnRevision);
        api.RespuestaCancelacion = (_, _) => new ResultadoApi<MiCasoDetalle>.Rechazado(409,
            "La central ya decidió sobre el reporte: no se puede retirar.", new Dictionary<string, string[]>());
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Ya no hace falta.";
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Caso(EstadosPublicos.Admitido, Datos()));

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal("La central ya decidió sobre el reporte: no se puede retirar.", vm.Mensaje);
        Assert.Equal("Reporte · Admitido", vm.Titulo);
        Assert.False(vm.OfrecerCancelacion);
    }
}
