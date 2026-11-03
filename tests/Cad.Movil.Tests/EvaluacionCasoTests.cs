using Cad.Contratos.Casos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU32 — Evaluar la atención de un caso cerrado (lado de la app, S5-T11). Datos ficticios.
public class EvaluacionCasoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Caso = Guid.NewGuid();

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };

    private static MiCasoDetalle Detalle(string estado, MiEvaluacion? evaluacion = null) =>
        new(Caso, "SOS", Ahora.AddHours(-2), estado, estado == EstadosPublicos.Cerrado ? "Se orientó al ciudadano" : null,
            [new AvancePublico(Ahora.AddHours(-2), EstadosPublicos.Recibido, null)], evaluacion);

    private async Task<MiCasoVistaModelo> AbiertoAsync(string estado = EstadosPublicos.Cerrado, MiEvaluacion? evaluacion = null)
    {
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Detalle(estado, evaluacion));
        var vm = new MiCasoVistaModelo(api, sesiones, new NavegacionFalsa(), new RelojFijo(Ahora), Lima);
        await vm.CargarAsync(Caso);
        return vm;
    }

    [Fact]
    public async Task CA01_CasoCerrado_SeEvaluaConPuntuacionYComentario()
    {
        var vm = await AbiertoAsync();
        Assert.True(vm.PuedeEvaluar);
        Assert.False(vm.HayEvaluacion);

        await vm.Opciones[3].Elegir.EjecutarAsync();
        vm.Comentario = "  El sereno llegó pronto.  ";
        await vm.EnviarEvaluacion.EjecutarAsync();

        var (casoId, solicitud) = Assert.Single(api.Evaluaciones);
        Assert.Equal(Caso, casoId);
        Assert.Equal((4, "El sereno llegó pronto."), (solicitud.Puntuacion, solicitud.Comentario));
        Assert.NotEqual(Guid.Empty, solicitud.IdSolicitud);
        Assert.True(vm.HayEvaluacion);
        Assert.False(vm.PuedeEvaluar);
        Assert.Equal("Tu evaluación: 4 de 5", vm.TextoEvaluacion);
        Assert.Equal("Comentario: El sereno llegó pronto.", vm.ComentarioRegistrado);
        Assert.Equal("Gracias por tu evaluación.", vm.Confirmacion);
    }

    [Fact]
    public async Task CA01_SinComentario_SeEnviaSoloLaPuntuacion()
    {
        var vm = await AbiertoAsync();

        vm.Puntuacion = 5;
        vm.Comentario = "   ";
        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.Null(Assert.Single(api.Evaluaciones).Solicitud.Comentario);
        Assert.Equal("Sin comentario.", vm.ComentarioRegistrado);
    }

    [Fact]
    public async Task CA01_LaOpcionElegidaQuedaMarcada()
    {
        var vm = await AbiertoAsync();

        await vm.Opciones[1].Elegir.EjecutarAsync();

        Assert.Equal(2, vm.Puntuacion);
        Assert.Equal([false, true, false, false, false], vm.Opciones.Select(o => o.Elegida));
        Assert.Equal("Puntuación 2 de 5, elegida", vm.Opciones[1].Descripcion);
    }

    [Fact]
    public async Task CA02_SinPuntuacion_NoSeEnvia()
    {
        var vm = await AbiertoAsync();
        vm.Comentario = "Solo comentario";

        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.Empty(api.Evaluaciones);
        Assert.Equal("Elige una puntuación de 1 a 5.", vm.ErrorEvaluacion);
        Assert.True(vm.PuedeEvaluar);
    }

    [Fact]
    public async Task CA02_ElServidorRechazaLosDatos_SeMuestraElError()
    {
        var vm = await AbiertoAsync();
        vm.Puntuacion = 3;
        api.RespuestaEvaluacion = (_, _) => new ResultadoApi<MiEvaluacion>.Rechazado(400, "No se registró la evaluación.",
            new Dictionary<string, string[]> { ["comentario"] = ["El comentario admite hasta 2000 caracteres."] });

        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.Equal("El comentario admite hasta 2000 caracteres.", vm.ErrorEvaluacion);
        Assert.True(vm.PuedeEvaluar);
    }

    [Fact]
    public async Task CA03_CasoYaEvaluado_MuestraLaEvaluacionYNoOfreceOtra()
    {
        var vm = await AbiertoAsync(evaluacion: new MiEvaluacion(2, null, Ahora));

        Assert.False(vm.PuedeEvaluar);
        Assert.True(vm.HayEvaluacion);
        Assert.Equal("Tu evaluación: 2 de 5", vm.TextoEvaluacion);
    }

    [Fact]
    public async Task CA03_EvaluadoDesdeOtroDispositivo_MuestraElRechazoYLaEvaluacionRegistrada()
    {
        var vm = await AbiertoAsync();
        vm.Puntuacion = 5;
        api.RespuestaEvaluacion = (_, _) => new ResultadoApi<MiEvaluacion>.Rechazado(409, "Ya evaluaste este caso: solo se admite una evaluación.",
            new Dictionary<string, string[]>());
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Detalle(EstadosPublicos.Cerrado, new MiEvaluacion(3, "Anterior", Ahora)));

        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.Equal("Ya evaluaste este caso: solo se admite una evaluación.", vm.Mensaje);
        Assert.Equal("Tu evaluación: 3 de 5", vm.TextoEvaluacion);
        Assert.False(vm.PuedeEvaluar);
    }

    [Fact]
    public async Task CA03_ReintentoTrasSinRespuesta_UsaElMismoIdentificador()
    {
        var vm = await AbiertoAsync();
        vm.Puntuacion = 4;
        api.RespuestaEvaluacion = (_, _) => new ResultadoApi<MiEvaluacion>.SinRespuesta("No hay conexión con el servidor.");

        await vm.EnviarEvaluacion.EjecutarAsync();
        Assert.Equal("No hay respuesta del servidor: la evaluación no se registró. Inténtalo de nuevo.", vm.Mensaje);
        Assert.True(vm.PuedeEvaluar);
        api.RespuestaEvaluacion = (_, s) => new ResultadoApi<MiEvaluacion>.Correcto(new MiEvaluacion(s.Puntuacion!.Value, null, Ahora));
        await vm.EnviarEvaluacion.EjecutarAsync();

        var solicitudes = api.Evaluaciones.Select(e => e.Solicitud.IdSolicitud).ToList();
        Assert.Equal(2, solicitudes.Count);
        Assert.Equal(solicitudes[0], solicitudes[1]);
        Assert.True(vm.HayEvaluacion);
        Assert.False(vm.HayMensaje);
    }

    [Theory]
    [InlineData(EstadosPublicos.Recibido)]
    [InlineData(EstadosPublicos.EnAtencion)]
    [InlineData(EstadosPublicos.Cancelado)]
    [InlineData(EstadosPublicos.Descartado)]
    public async Task CA04_CasoNoCerrado_NoOfreceEvaluar(string estado)
    {
        var vm = await AbiertoAsync(estado);

        Assert.False(vm.PuedeEvaluar);
        Assert.False(vm.HayEvaluacion);
    }

    [Fact]
    public async Task CA05_CasoAjeno_NoSeMuestra()
    {
        var vm = await AbiertoAsync();
        vm.Puntuacion = 5;
        api.RespuestaEvaluacion = (_, _) => new ResultadoApi<MiEvaluacion>.Rechazado(404, "No encontrado", new Dictionary<string, string[]>());

        await vm.EnviarEvaluacion.EjecutarAsync();

        Assert.False(vm.HayCaso);
        Assert.False(vm.PuedeEvaluar);
        Assert.Equal("No se encontró el caso.", vm.Mensaje);
    }

    [Fact]
    public async Task CA06_SinEvaluar_ElRestoDelDetalleSigueDisponible()
    {
        var vm = await AbiertoAsync();

        Assert.True(vm.HayResultado);
        Assert.NotEmpty(vm.Avances);
        await vm.Actualizar.EjecutarAsync();
        Assert.False(vm.HayMensaje);
        Assert.Empty(api.Evaluaciones);
    }

    // 27/10/2026: por qué evalúa así, opcional; la lista depende de la nota y lo que ya no aplica se quita al cambiarla.
    [Fact]
    public async Task Motivos_SeOfrecenSegunLaPuntuacion_YSeEnvianLosMarcados()
    {
        var vm = await AbiertoAsync();
        Assert.False(vm.HayOpcionesMotivoEvaluacion); // sin puntuación, todavía no

        vm.Puntuacion = 2;
        Assert.Equal("¿Qué falló? (opcional)", vm.TituloMotivosEvaluacion);
        Assert.Equal(MotivosEvaluacion.Negativos.Select(o => o.Codigo), vm.OpcionesMotivoEvaluacion.Select(o => o.Codigo));
        await vm.OpcionesMotivoEvaluacion.Single(o => o.Codigo == MotivosEvaluacion.Tardaron).Alternar.EjecutarAsync();
        Assert.True(vm.OpcionesMotivoEvaluacion.Single(o => o.Codigo == MotivosEvaluacion.Tardaron).Elegida);

        vm.Puntuacion = 5; // otra nota: «Tardaron en llegar» ya no aplica
        Assert.Equal("¿Qué estuvo bien? (opcional)", vm.TituloMotivosEvaluacion);
        Assert.DoesNotContain(vm.OpcionesMotivoEvaluacion, o => o.Elegida);
        await vm.OpcionesMotivoEvaluacion.Single(o => o.Codigo == MotivosEvaluacion.BuenTrato).Alternar.EjecutarAsync();
        await vm.OpcionesMotivoEvaluacion.Single(o => o.Codigo == MotivosEvaluacion.LlegaronRapido).Alternar.EjecutarAsync();
        await vm.EnviarEvaluacion.EjecutarAsync();

        // En el orden de la lista, no en el de los toques.
        Assert.Equal([MotivosEvaluacion.LlegaronRapido, MotivosEvaluacion.BuenTrato], Assert.Single(api.Evaluaciones).Solicitud.Motivos);
    }

    [Fact]
    public async Task Motivos_SinMarcarNinguno_NoSeEnvian_YLosRegistradosSeMuestran()
    {
        var vm = await AbiertoAsync();
        vm.Puntuacion = 3;
        await vm.EnviarEvaluacion.EjecutarAsync();
        Assert.Null(Assert.Single(api.Evaluaciones).Solicitud.Motivos);

        var evaluado = await AbiertoAsync(evaluacion: new MiEvaluacion(2, null, Ahora, [MotivosEvaluacion.Tardaron, MotivosEvaluacion.NoMeInformaron]));
        Assert.Equal("Motivos: Tardaron en llegar · No me informaron qué pasaba", evaluado.MotivosRegistrados);
    }
}
