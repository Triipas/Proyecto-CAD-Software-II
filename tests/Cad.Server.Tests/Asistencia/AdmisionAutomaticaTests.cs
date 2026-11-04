using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Asistencia;

// HU47 (S6-T16; plan de asistencia con IA, 7.3 a 7.8) — la admisión automática con un asistente falso: «Sombra» calcula sin
// actuar, «Activa» admite pasado el retraso con el usuario del sistema, el operador que toma la revisión gana, la pausa la detiene,
// cada condición por separado impide admitir y el freno pasa a «Sombra». El ajuste es global: cada prueba lo deja «Apagada».
// Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public sealed class AdmisionAutomaticaTests(FabricaCad fabrica) : IAsyncLifetime
{
    private readonly AsistenteFalso falso = new();
    private WebApplicationFactory<Program> servidor = null!;

    private const string Descripcion = "Dos sujetos en moto me arrebataron el celular en la esquina del parque hace un momento";

    public async Task InitializeAsync()
    {
        servidor = fabrica.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ia:Habilitada", "true");
            b.UseSetting("Ia:IntervaloAdmisionSegundos", "1");
            b.ConfigureTestServices(s => s.AddScoped<IAsistenteReportes>(_ => falso));
        });
        _ = servidor.Server;
        await AjustarAsync("Apagada");
    }

    public async Task DisposeAsync()
    {
        await AjustarAsync("Apagada");
        await servidor.DisposeAsync();
    }

    // Directo en la base: permite retrasos y cupos por debajo de los rangos del formulario, para que la prueba no espere.
    private Task AjustarAsync(string modo, int retraso = 30, int maximoPorHora = 60, bool corregir = false, int frenoMinimo = 20, int frenoPorcentaje = 5) =>
        fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlAsync($"""
            UPDATE "AjustesIa" SET "Modo" = {modo}, "RetrasoSegundos" = {retraso}, "MaximoPorHora" = {maximoPorHora},
              "PermitirCorregirCategoria" = {corregir}, "FrenoMinimoAdmisiones" = {frenoMinimo}, "FrenoPorcentajeDescartes" = {frenoPorcentaje},
              "LongitudMinimaDescripcion" = 40
            WHERE "Id" = 1
            """));

    private static ResultadoAsistente Analisis(Guid? categoria = null, DecisionSugerida decision = DecisionSugerida.Admitir,
        NivelConfianza confianza = NivelConfianza.Alta, bool urgencia = false, SenalTriaje[]? senales = null, string modelo = "modelo-de-prueba") =>
        new(new AnalisisReporte(categoria ?? CategoriaIncidencia.RoboOHurto.Id, null, confianza, "Describe un arrebato de celular.", decision,
            "Hecho de seguridad ciudadana con ubicación.", decision == DecisionSugerida.Derivar ? DestinosDerivacion.Bomberos : null,
            decision == DecisionSugerida.Derivar ? "Se informó a Bomberos." : null,
            decision == DecisionSugerida.Descartar ? MotivosDescarte.FalsoOMalintencionado : null,
            decision == DecisionSugerida.Admitir ? PrioridadesCaso.Alta : null, urgencia, urgencia ? "Menciona un arma." : null, null,
            senales ?? []), modelo);

    // Por defecto: primera y segunda opinión coinciden en admitir un robo con confianza alta.
    private void Coinciden(Guid? categoria = null)
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(categoria));
        falso.Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(categoria, modelo: "verificador-de-prueba"));
    }

    private async Task<Guid> RegistrarAsync(string descripcion = Descripcion, Guid? categoria = null, bool conPunto = true) =>
        await ReportesDePrueba.RegistrarAsync(await ReportesDePrueba.CiudadanoAsync(fabrica, servidor), descripcion,
            categoria ?? CategoriaIncidencia.RoboOHurto.Id, conPunto);

    private async Task<EvaluacionAdmision> EvaluacionAsync(Guid id)
    {
        EvaluacionAdmision? evaluacion = null;
        Assert.True(await CentralDePrueba.EsperarAsync(() => (evaluacion = fabrica.ConBaseDatosAsync(db =>
            db.EvaluacionesAdmision.AsNoTracking().SingleOrDefaultAsync(e => e.CasoId == id)).Result) is not null), "No se evaluó la admisión.");
        return evaluacion!;
    }

    private Task<Caso> CasoAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<DecisionReporte?> DecisionAsync(Guid id) =>
        fabrica.ConBaseDatosAsync(db => db.DecisionesReporte.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == id));

    // Fuerza que el retraso haya pasado, sin esperarlo.
    private Task VencerAsync(Guid id) => fabrica.ConBaseDatosAsync(db =>
        db.Database.ExecuteSqlAsync($"""UPDATE "EvaluacionesAdmision" SET "AdmitirDesde" = now() - interval '1 second' WHERE "CasoId" = {id}"""));

    private Task<int> ProcesarAsync() =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<AdmitirAutomaticamente>().ProcesarAsync(CancellationToken.None));

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    [Fact]
    public async Task CA01_Apagada_NoSeEvaluaNiSeAdmite()
    {
        Coinciden();
        var id = await RegistrarAsync();

        Assert.True(await CentralDePrueba.EsperarAsync(() => falso.Recibidas.Any(e => e.CasoId == id)));
        await Task.Delay(1500);

        Assert.False(await fabrica.ConBaseDatosAsync(db => db.EvaluacionesAdmision.AnyAsync(e => e.CasoId == id)));
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task CA02_Sombra_CalculaQueHabriaAdmitidoSinCambiarElEstado()
    {
        await AjustarAsync("Sombra");
        Coinciden();
        var id = await RegistrarAsync();

        var evaluacion = await EvaluacionAsync(id);
        await Task.Delay(1500);

        Assert.Equal((ModoAdmision.Sombra, true, (CondicionAdmision?)null, true, "verificador-de-prueba"),
            (evaluacion.Modo, evaluacion.Elegible, evaluacion.Condicion, evaluacion.VerificadorCoincide, evaluacion.ModeloVerificador));
        Assert.Null(evaluacion.AdmitirDesde);
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
        Assert.Null(await DecisionAsync(id));
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{id}"));
        Assert.Contains("En modo «Sombra», el sistema lo habría admitido solo.", html);
    }

    [Fact]
    public async Task CA03_Activa_AdmitePasadoElRetrasoConElSistemaYLaMarcaVisible()
    {
        await AjustarAsync("Activa", retraso: 1);
        Coinciden();
        var id = await RegistrarAsync();

        Assert.True(await CentralDePrueba.EsperarAsync(() => CasoAsync(id).Result.Estado == EstadoCaso.Pendiente, 20), "No se admitió solo.");

        var decision = (await DecisionAsync(id))!;
        Assert.Equal((TipoDecisionReporte.Admision, UsuarioSistema.Id), (decision.Tipo, decision.OperadorId));
        Assert.StartsWith(EvaluarAdmision.PrefijoMotivo, decision.Motivo);
        var caso = await CasoAsync(id);
        Assert.Equal((PrioridadesCaso.Alta, PersonasHeridas.NoSeSabe), (caso.Prioridad, caso.Heridos));
        var actores = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == id && (e.Tipo == TipoEventoCaso.RevisionIniciada || e.Tipo == TipoEventoCaso.ReporteAdmitido))
            .Select(e => e.ActorId).ToListAsync());
        Assert.Equal(2, actores.Count);
        Assert.All(actores, a => Assert.Equal(UsuarioSistema.Id, a));
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{id}"));
        Assert.Contains("data-admitido-automaticamente", html);
        Assert.Contains("Sistema CAD (automático)", html);
    }

    [Fact]
    public async Task CA04_SiUnOperadorIniciaLaRevisionDuranteElRetraso_ElSistemaNoInterviene()
    {
        await AjustarAsync("Activa", retraso: 600);
        Coinciden();
        var id = await RegistrarAsync();
        var evaluacion = await EvaluacionAsync(id);
        Assert.NotNull(evaluacion.AdmitirDesde);
        var operador = await OperadorAsync();

        // Mientras corre el retraso, la lista y el detalle lo avisan.
        Assert.Contains("data-admision-programada", await operador.GetStringAsync("/panel/reportes"));
        Assert.Contains("El sistema lo admitirá solo a las", WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/reportes/{id}")));

        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoRevision.Registrado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<RevisarReporte>().IniciarAsync(id, cuenta.Id, CancellationToken.None)));
        await VencerAsync(id);
        await ProcesarAsync();

        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
        Assert.Null(await DecisionAsync(id));
    }

    [Fact]
    public async Task CA01_PausadaDuranteElRetraso_NoAdmite()
    {
        await AjustarAsync("Activa", retraso: 600);
        Coinciden();
        var id = await RegistrarAsync();
        await EvaluacionAsync(id);
        var operador = await fabrica.CrearAsync(Roles.Operador);

        await fabrica.ConServiciosAsync(s => s.GetRequiredService<CambiarAjusteIa>().PausarAsync(operador.Id, CancellationToken.None));
        await VencerAsync(id);

        Assert.Equal(0, await ProcesarAsync());
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
    }

    public static TheoryData<string> Escenarios() => ["urgencia", "confianzaMedia", "derivar", "descartar", "duplicado", "otraCategoria",
        "sinVerificador", "verificadorDeriva", "verificadorOtraCategoria", "descripcionCorta", "sinPunto"];

    [Theory]
    [MemberData(nameof(Escenarios))]
    public async Task CA05_CA10_CadaCondicionQueNoSeCumple_ImpideAdmitirSolo(string escenario)
    {
        await AjustarAsync("Sombra");
        Coinciden();
        var (descripcion, conPunto, esperada) = (Descripcion, true, CondicionAdmision.VerificadorDiscrepa);
        switch (escenario)
        {
            case "urgencia":
                falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(urgencia: true));
                esperada = CondicionAdmision.PosibleUrgencia;
                break;
            case "confianzaMedia":
                falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(confianza: NivelConfianza.Media));
                esperada = CondicionAdmision.ConfianzaNoAlta;
                break;
            case "derivar":
                falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(decision: DecisionSugerida.Derivar));
                esperada = CondicionAdmision.DecisionNoAdmitir;
                break;
            case "descartar":
                falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(decision: DecisionSugerida.Descartar));
                esperada = CondicionAdmision.DecisionNoAdmitir;
                break;
            case "duplicado":
                falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(senales: [SenalTriaje.Duplicado]));
                esperada = CondicionAdmision.Duplicado;
                break;
            case "otraCategoria":
                Coinciden(CategoriaIncidencia.Iniciales[7].Id);
                esperada = CondicionAdmision.CategoriaDistinta;
                break;
            case "sinVerificador":
                falso.Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(null);
                esperada = CondicionAdmision.SinVerificacion;
                break;
            case "verificadorDeriva":
                falso.Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(decision: DecisionSugerida.Derivar));
                break;
            case "verificadorOtraCategoria":
                falso.Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(Analisis(CategoriaIncidencia.Iniciales[7].Id));
                break;
            case "descripcionCorta":
                descripcion = "Me robaron el celular";
                esperada = CondicionAdmision.DescripcionCorta;
                break;
            case "sinPunto":
                conPunto = false;
                esperada = CondicionAdmision.SinPunto;
                break;
        }

        var id = await RegistrarAsync(descripcion, conPunto: conPunto);
        var evaluacion = await EvaluacionAsync(id);

        Assert.False(evaluacion.Elegible);
        Assert.Equal(esperada, evaluacion.Condicion);
        Assert.Null(evaluacion.AdmitirDesde);
    }

    [Fact]
    public async Task CupoPorHora_PasadoElMaximo_NoEsElegible()
    {
        var desde = DateTimeOffset.UtcNow.AddHours(-1);
        var previas = await fabrica.ConBaseDatosAsync(db => db.EvaluacionesAdmision.CountAsync(e => e.Elegible && e.Modo == ModoAdmision.Sombra && e.EvaluadaEn >= desde));
        await AjustarAsync("Sombra", maximoPorHora: previas + 1);
        Coinciden();

        var primera = await EvaluacionAsync(await RegistrarAsync());
        var segunda = await EvaluacionAsync(await RegistrarAsync());

        Assert.True(primera.Elegible);
        Assert.Equal(CondicionAdmision.CupoAgotado, segunda.Condicion);
    }

    [Fact]
    public async Task PermitirCorregirCategoria_ConLasDosOpinionesDeAcuerdo_AdmiteYCorrigeConElSistema()
    {
        await AjustarAsync("Activa", retraso: 1, corregir: true);
        Coinciden(CategoriaIncidencia.RoboOHurto.Id);
        var id = await RegistrarAsync(categoria: CategoriaIncidencia.Otros.Id);

        Assert.True(await CentralDePrueba.EsperarAsync(() => CasoAsync(id).Result.Estado == EstadoCaso.Pendiente, 20), "No se admitió solo.");

        Assert.Equal(CategoriaIncidencia.RoboOHurto.Id, (await CasoAsync(id)).CategoriaId);
        var correccion = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.CorreccionesCategoria.AsNoTracking().Where(c => c.CasoId == id).ToListAsync()));
        Assert.Equal(UsuarioSistema.Id, correccion.OperadorId);
    }

    [Fact]
    public async Task CA08_Freno_ConDemasiadosDescartesPasaASombraYLoAvisa()
    {
        // Dos admisiones automáticas nuevas; la central descarta una.
        await AjustarAsync("Activa", retraso: 1, frenoMinimo: 1000);
        Coinciden();
        var primera = await RegistrarAsync();
        var segunda = await RegistrarAsync();
        Assert.True(await CentralDePrueba.EsperarAsync(() => CasoAsync(primera).Result.Estado == EstadoCaso.Pendiente
            && CasoAsync(segunda).Result.Estado == EstadoCaso.Pendiente, 20), "No se admitieron solos.");
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoDescarte.Descartado>(await fabrica.ConServiciosAsync(s => s.GetRequiredService<DescartarCaso>()
            .EjecutarAsync(segunda, MotivosDescarte.FalsoOMalintencionado, "No era un robo.", operador.Id, CancellationToken.None)));

        // Con menos admisiones que el mínimo, no frena.
        Assert.False(await fabrica.ConServiciosAsync(s => s.GetRequiredService<AdmitirAutomaticamente>().FrenarSiCorrespondeAsync(CancellationToken.None)));

        // El servicio en segundo plano también revisa el freno en cada vuelta: vale que frene él o esta llamada.
        await AjustarAsync("Activa", frenoMinimo: 2, frenoPorcentaje: 1);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<AdmitirAutomaticamente>().FrenarSiCorrespondeAsync(CancellationToken.None));

        Assert.True(await CentralDePrueba.EsperarAsync(() =>
            fabrica.ConBaseDatosAsync(db => db.AjustesIa.AsNoTracking().SingleAsync()).Result.Modo == ModoAdmision.Sombra), "No frenó.");
        var cambio = await fabrica.ConBaseDatosAsync(db => db.CambiosAjusteIa.AsNoTracking().OrderByDescending(c => c.En).FirstAsync());
        Assert.Equal((ModoAdmision.Activa, ModoAdmision.Sombra, UsuarioSistema.Id), (cambio.ModoAnterior, cambio.ModoNuevo, cambio.ActorId));
        Assert.StartsWith("Freno automático:", cambio.Motivo);
        var reportes = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/reportes"));
        Assert.Contains("Pasó sola a «Sombra». Freno automático:", reportes);
    }
}

// S6-T09 (plan, 5.4) — los ejemplos del prompt: decisiones parecidas ya tomadas, por búsqueda de texto completo.
[Collection(ColeccionServidor.Nombre)]
public sealed class RecuperarEjemplosTests(FabricaCad fabrica)
{
    private async Task<Guid> DecididoAsync(HttpClient app, string descripcion, TipoDecisionReporte tipo, string motivo, Guid? actor = null)
    {
        var id = await ReportesDePrueba.RegistrarAsync(app, descripcion, CategoriaIncidencia.RoboOHurto.Id);
        var responsable = actor ?? (await fabrica.CrearAsync(Roles.Operador)).Id;
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, responsable, CancellationToken.None));
        var datos = tipo switch
        {
            TipoDecisionReporte.Derivacion => new DatosDecision(tipo, motivo, DestinosDerivacion.Bomberos, "Se informó a Bomberos."),
            TipoDecisionReporte.Descarte => new DatosDecision(tipo, motivo, MotivoDescarte: MotivosDescarte.FalsoOMalintencionado),
            _ => new DatosDecision(tipo, motivo, Prioridad: PrioridadesCaso.Media, Heridos: PersonasHeridas.No),
        };
        Assert.IsType<ResultadoDecision.Registrado>(await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>()
            .EjecutarAsync(id, datos, responsable, CancellationToken.None)));
        return id;
    }

    [Fact]
    public async Task LosMasParecidosPrimero_ConLasTresDecisiones_SinElPropioNiLasAutomaticas_YAnonimizados()
    {
        var sello = "zarquimbo" + Guid.NewGuid().ToString("N")[..6]; // palabra que solo usan los reportes de esta prueba
        var app = await ReportesDePrueba.CiudadanoAsync(fabrica, fabrica);
        await DecididoAsync(app, $"Me robaron la mochila {sello} en el paradero, llamen al 987654321", TipoDecisionReporte.Admision, $"Admitido {sello} parecido.");
        await DecididoAsync(app, $"Hay humo y fuego en un techo {sello}", TipoDecisionReporte.Derivacion, $"Derivado {sello}.");
        await DecididoAsync(app, $"asdf prueba {sello}", TipoDecisionReporte.Descarte, $"Descartado {sello}.");
        await DecididoAsync(app, $"Robo de mochila {sello} admitido por el sistema", TipoDecisionReporte.Admision, $"Automático {sello}.", UsuarioSistema.Id);
        var propio = await ReportesDePrueba.RegistrarAsync(app, $"Robaron una mochila {sello} cerca del paradero", CategoriaIncidencia.RoboOHurto.Id);

        var ejemplos = await fabrica.ConServiciosAsync(s => s.GetRequiredService<IRecuperadorEjemplos>()
            .RecuperarAsync(propio, $"Robaron una mochila {sello} cerca del paradero", 6, CancellationToken.None));

        Assert.InRange(ejemplos.Count, 3, 6);
        Assert.Contains("mochila", ejemplos[0].Descripcion); // el más parecido primero
        Assert.Equal(DecisionSugerida.Admitir, ejemplos[0].Decision);
        Assert.Contains(ejemplos, e => e.Decision == DecisionSugerida.Derivar);
        Assert.Contains(ejemplos, e => e.Decision == DecisionSugerida.Descartar);
        Assert.DoesNotContain(ejemplos, e => e.Descripcion.Contains("cerca del paradero")); // el propio caso
        Assert.DoesNotContain(ejemplos, e => e.Motivo.Contains("Automático")); // el sistema no aprende de sí mismo
        Assert.DoesNotContain(ejemplos, e => e.Descripcion.Contains("987654321"));
    }
}
