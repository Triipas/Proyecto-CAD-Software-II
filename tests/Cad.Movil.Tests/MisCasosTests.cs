using Cad.Contratos.Casos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU30 — Consultar mis casos (lado de la app, S3-T13). Datos ficticios.
public class MisCasosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };
    private readonly NavegacionFalsa navegacion = new();
    private readonly RelojFijo reloj = new(Ahora);

    private MisCasosVistaModelo Lista() => new(api, sesiones, navegacion, reloj, Lima);
    private MiCasoVistaModelo Detalle() => new(api, sesiones, navegacion, reloj, Lima);

    private static readonly Guid Sos = Guid.NewGuid(), Reporte = Guid.NewGuid();

    private static List<MiCasoResumen> Casos(string estadoReporte = EstadosPublicos.Recibido) =>
    [
        new(Reporte, "Reporte", Ahora.AddHours(-1), estadoReporte, null),
        new(Sos, "SOS", Ahora.AddHours(-3), EstadosPublicos.Cerrado, "Se recuperó el objeto"),
    ];

    [Fact]
    public async Task CA01_AlAbrir_MuestraCadaCasoConOrigenFechaYEstado()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(Casos());
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.Equal(["Reporte · Recibido", "SOS · Cerrado"], vm.Casos.Select(c => c.Titulo));
        Assert.Equal("16/10/2026 09:00", vm.Casos[0].Fecha); // hora local (UTC-5)
        Assert.False(vm.SinCasos);
        Assert.False(vm.HayMensaje);
    }

    [Fact]
    public async Task CA03_UnCasoFinalizado_MuestraSuResultadoEnLaLista()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(Casos());
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.False(vm.Casos[0].HayResultado);
        Assert.True(vm.Casos[1].HayResultado);
        Assert.Equal("Se recuperó el objeto", vm.Casos[1].Resultado);
    }

    [Fact]
    public async Task CA02_AlActualizar_SeVeElEstadoActual()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(Casos());
        var vm = Lista();
        await vm.AlAparecerAsync();

        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(Casos(EstadosPublicos.EnRevision));
        await vm.Actualizar.EjecutarAsync();

        Assert.Equal("Reporte · En revisión", vm.Casos[0].Titulo);
        Assert.Equal(2, api.ConsultasMisCasos);
    }

    [Fact]
    public async Task CA01_SinCasos_SeIndica()
    {
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.True(vm.SinCasos);
        Assert.Empty(vm.Casos);
    }

    [Fact]
    public async Task AlTocarUnCaso_SeAbreSuDetalle()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(Casos());
        var vm = Lista();
        await vm.AlAparecerAsync();

        await vm.Casos[1].Abrir.EjecutarAsync();

        Assert.Equal([$"{Rutas.MiCaso}?id={Sos}"], navegacion.Rutas);
    }

    [Fact]
    public async Task SinRespuesta_SeAvisaYNoSeDaLaListaPorVacia()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.SinRespuesta("sin red");
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.True(vm.HayMensaje);
        Assert.Contains("No hay respuesta del servidor", vm.Mensaje);
        Assert.False(vm.SinCasos);
    }

    [Fact]
    public async Task ConLaSesionVencida_NoSeConsultaYSeOfreceVolverAIngresar()
    {
        sesiones.Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddMinutes(-1));
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.Equal(0, api.ConsultasMisCasos);
        Assert.True(vm.SesionTerminada);
        await vm.VolverAIngresar.EjecutarAsync();
        Assert.Null(sesiones.Sesion);
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }

    [Fact]
    public async Task UnRechazo401_TerminaLaSesion()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Rechazado(401, "", new Dictionary<string, string[]>());
        var vm = Lista();

        await vm.AlAparecerAsync();

        Assert.True(vm.SesionTerminada);
    }

    // ----- Detalle -----

    [Fact]
    public async Task CA01_CA03_ElDetalle_MuestraLosAvancesPublicosEnOrdenYElResultado()
    {
        api.RespuestaMiCaso = id => new ResultadoApi<MiCasoDetalle>.Correcto(new MiCasoDetalle(id, "Reporte", Ahora.AddHours(-3),
            EstadosPublicos.Derivado, "Derivado a Comisaría ficticia",
            [
                new(Ahora.AddHours(-3), EstadosPublicos.Recibido, null),
                new(Ahora.AddHours(-2), EstadosPublicos.EnRevision, null),
                new(Ahora.AddHours(-1), EstadosPublicos.Derivado, "Derivado a Comisaría ficticia"),
            ]));
        var vm = Detalle();

        await vm.CargarAsync(Reporte);

        Assert.Equal([Reporte], api.CasosConsultados);
        Assert.Equal("Reporte · Derivado", vm.Titulo);
        Assert.Equal("Registrado el 16/10/2026 07:00", vm.Registrado);
        Assert.Contains(Reporte.ToString(), vm.Identificador);
        Assert.True(vm.HayResultado);
        Assert.Equal([EstadosPublicos.Recibido, EstadosPublicos.EnRevision, EstadosPublicos.Derivado], vm.Avances.Select(a => a.Estado));
        Assert.Equal("16/10/2026 09:00", vm.Avances[2].Hora);
        Assert.True(vm.Avances[2].HayDetalle);
        Assert.False(vm.Avances[0].HayDetalle);
    }

    [Fact]
    public async Task CA04_UnCasoAjenoOInexistente_NoSeMuestra()
    {
        var vm = Detalle(); // el doble responde 404

        await vm.CargarAsync(Guid.NewGuid());

        Assert.False(vm.HayCaso);
        Assert.Equal("No se encontró el caso.", vm.Mensaje);
        Assert.Empty(vm.Avances);
    }

    [Fact]
    public async Task DesdeElInicio_SeAbrenMisCasos()
    {
        var inicio = new InicioCiudadanoVistaModelo(new EmisorSos(api, new UbicacionFalsa(), sesiones, reloj),
            new PreparacionUbicacion(new UbicacionFalsa()), sesiones, navegacion, () => { });

        await inicio.VerMisCasos.EjecutarAsync();

        Assert.Equal([Rutas.MisCasos], navegacion.Rutas);
    }
}
