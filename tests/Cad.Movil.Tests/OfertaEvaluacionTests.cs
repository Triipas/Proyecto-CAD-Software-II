using Cad.Contratos.Casos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU32 (29/10/2026) — al cerrarse el caso, la app ofrece sola la evaluación: una vez, opcional y sin cambiar el caso. Datos ficticios.
public class OfertaEvaluacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Caso = Guid.NewGuid();

    private readonly ApiFalsa api = new();
    private readonly AlmacenLocalFalso almacen = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };
    private readonly NavegacionFalsa navegacion = new();
    private readonly RelojFijo reloj = new(Ahora);
    private readonly OfertaEvaluacion oferta;

    // Estado del caso en el servidor, que cada prueba cambia entre consultas.
    private string estado = EstadosPublicos.EnAtencion;
    private DateTimeOffset cerradoEn = Ahora.AddMinutes(-2);
    private MiEvaluacion? evaluacion;

    public OfertaEvaluacionTests()
    {
        oferta = new OfertaEvaluacion(api, almacen, reloj);
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Detalle(Caso));
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(
            [new MiCasoResumen(Caso, "SOS", Ahora.AddHours(-1), estado, null)]);
    }

    private MiCasoDetalle Detalle(Guid id) => new(id, "SOS", Ahora.AddHours(-1), estado, null,
        estado == EstadosPublicos.Cerrado
            ? [new AvancePublico(Ahora.AddHours(-1), EstadosPublicos.Recibido, null), new AvancePublico(cerradoEn, EstadosPublicos.Cerrado, null)]
            : [new AvancePublico(Ahora.AddHours(-1), EstadosPublicos.Recibido, null)],
        evaluacion);

    private InicioCiudadanoVistaModelo Inicio() => new(new EmisorSos(api, new UbicacionFalsa(), sesiones, reloj),
        new PreparacionUbicacion(new UbicacionFalsa()), sesiones, navegacion, () => { }, api: api, oferta: oferta)
    { HiloPrincipal = accion => { accion(); return Task.CompletedTask; } };

    private MiCasoVistaModelo MiCaso() => new(api, sesiones, navegacion, reloj, Lima, oferta);

    [Fact]
    public async Task AlCerrarseElCaso_ElInicioAbreSolaLaEvaluacion()
    {
        var inicio = Inicio();
        await inicio.RevisarCasosAsync();
        Assert.Empty(navegacion.Rutas); // en atención: todavía nada que calificar

        estado = EstadosPublicos.Cerrado;
        await inicio.RevisarCasosAsync();

        Assert.Equal([$"{Rutas.MiCaso}?id={Caso}&{Rutas.ParametroCalificar}=1"], navegacion.Rutas);
    }

    [Fact]
    public async Task SiYaSeOfrecio_NoSeVuelveAAbrirEnLasSiguientesConsultas()
    {
        estado = EstadosPublicos.Cerrado;
        var inicio = Inicio();

        await inicio.RevisarCasosAsync();
        await inicio.RevisarCasosAsync();
        await Inicio().RevisarCasosAsync(); // otra apertura de la app: el recuerdo está en el teléfono

        Assert.Single(navegacion.Rutas);
        Assert.True(oferta.YaOfrecida(Caso));
    }

    [Fact]
    public async Task SiLaAppEstabaCerrada_AlAbrirlaDespuesSeOfreceSiNoSeCalifico()
    {
        estado = EstadosPublicos.Cerrado; // se cerró mientras la app no estaba abierta
        cerradoEn = Ahora.AddHours(-5);

        await Inicio().RevisarCasosAsync();

        Assert.Single(navegacion.Rutas);
    }

    [Fact]
    public async Task UnCasoYaEvaluado_NoSeOfrece()
    {
        estado = EstadosPublicos.Cerrado;
        evaluacion = new MiEvaluacion(5, null, Ahora.AddMinutes(-1));

        await Inicio().RevisarCasosAsync();

        Assert.Empty(navegacion.Rutas);
        Assert.True(oferta.YaOfrecida(Caso)); // visto: no se vuelve a consultar su detalle en cada revisión
    }

    [Fact]
    public async Task UnCierreAntiguo_NoSeOfreceSolo()
    {
        estado = EstadosPublicos.Cerrado;
        cerradoEn = Ahora - OfertaEvaluacion.Vigencia - TimeSpan.FromHours(1);

        await Inicio().RevisarCasosAsync();

        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task ConVariosCerrados_SoloSeOfreceElMasReciente()
    {
        var anterior = Guid.NewGuid();
        estado = EstadosPublicos.Cerrado;
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(
        [
            new MiCasoResumen(anterior, "Reporte", Ahora.AddHours(-30), EstadosPublicos.Cerrado, null),
            new MiCasoResumen(Caso, "SOS", Ahora.AddHours(-1), EstadosPublicos.Cerrado, null),
        ]);
        var inicio = Inicio();

        await inicio.RevisarCasosAsync();
        await inicio.RevisarCasosAsync();

        Assert.Equal([$"{Rutas.MiCaso}?id={Caso}&{Rutas.ParametroCalificar}=1"], navegacion.Rutas);
        Assert.True(oferta.YaOfrecida(anterior));
    }

    [Fact]
    public async Task SinRespuestaDelServidor_NoSeMarcaNiSeAbreNada_YSeReintenta()
    {
        estado = EstadosPublicos.Cerrado;
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Rechazado(503, "Sin servicio", new Dictionary<string, string[]>());
        var inicio = Inicio();

        await inicio.RevisarCasosAsync();
        Assert.Empty(navegacion.Rutas);
        Assert.False(oferta.YaOfrecida(Caso));

        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Detalle(Caso));
        await inicio.RevisarCasosAsync();
        Assert.Single(navegacion.Rutas);
    }

    [Fact]
    public async Task ConLaInvitacion_ElFormularioSeAbreConUnaNotaDeQueEsOpcional()
    {
        estado = EstadosPublicos.Cerrado;
        var vm = MiCaso();

        await vm.CargarAsync(Caso, ofrecerEvaluacion: true);

        Assert.True(vm.FormularioEvaluacion);
        Assert.True(vm.InvitacionEvaluacion);
        Assert.False(vm.OfrecerEvaluacion);
        Assert.Contains("opcional", vm.TextoInvitacionEvaluacion);
    }

    [Fact]
    public async Task AhoraNo_PlegaElFormularioSinEnviarNada_YSigueDisponibleDesdeElCaso()
    {
        estado = EstadosPublicos.Cerrado;
        var vm = MiCaso();
        await vm.CargarAsync(Caso, ofrecerEvaluacion: true);
        await vm.Opciones[3].Elegir.EjecutarAsync();

        await vm.OmitirEvaluacion.EjecutarAsync();

        Assert.Empty(api.Evaluaciones);
        Assert.False(vm.FormularioEvaluacion);
        Assert.False(vm.InvitacionEvaluacion);
        Assert.True(vm.PuedeEvaluar); // el caso no quedó en un estado raro: se puede calificar después
        Assert.True(vm.OfrecerEvaluacion);

        await vm.MostrarEvaluacion.EjecutarAsync();
        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.Single(api.Evaluaciones);
        Assert.True(vm.HayEvaluacion);
    }

    [Fact]
    public async Task AbiertoAMano_UnCasoCerradoOfreceCalificarSinAbrirElFormulario()
    {
        estado = EstadosPublicos.Cerrado;
        var vm = MiCaso();

        await vm.CargarAsync(Caso);

        Assert.True(vm.OfrecerEvaluacion);
        Assert.False(vm.FormularioEvaluacion);
        Assert.False(vm.InvitacionEvaluacion);
    }

    [Fact]
    public async Task ConElCasoAbierto_SiSeCierraEnLaRevision_SeOfreceSinSalirDeLaPantalla()
    {
        var vm = MiCaso();
        await vm.CargarAsync(Caso);
        Assert.True(vm.EnCurso);
        Assert.False(vm.PuedeEvaluar);

        estado = EstadosPublicos.Cerrado;
        await vm.RevisarAsync();

        Assert.False(vm.EnCurso);
        Assert.True(vm.FormularioEvaluacion);
        Assert.True(vm.InvitacionEvaluacion);
        Assert.True(oferta.YaOfrecida(Caso)); // al volver al inicio no se abre otra vez
        await Inicio().RevisarCasosAsync();
        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task UnaRevisionDeFondoQueFalla_NoMuestraErroresNiBorraElCaso()
    {
        var vm = MiCaso();
        await vm.CargarAsync(Caso);
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.SinRespuesta("sin red");

        await vm.RevisarAsync();

        Assert.True(vm.HayCaso);
        Assert.False(vm.HayMensaje);
    }
}
