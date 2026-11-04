using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Asistencia;
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

// Asistente de prueba: devuelve lo que diga cada prueba y guarda lo que recibió (para comprobar que llega anonimizado).
public sealed class AsistenteFalso : IAsistenteReportes
{
    public Func<EntradaAnalisis, CancellationToken, Task<ResultadoAsistente?>> Responder { get; set; } =
        (_, _) => Task.FromResult<ResultadoAsistente?>(null);

    public List<EntradaAnalisis> Recibidas { get; } = [];

    public bool Disponible => true;

    public Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct)
    {
        lock (Recibidas)
            Recibidas.Add(entrada);
        return Responder(entrada, ct);
    }

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) => Task.FromResult<ResultadoAsistente?>(null);
}

// HU44 (S6-T12) y HU45 (S6-T10, S6-T11, S6-T13) — la sugerencia del asistente sobre un reporte, de punta a punta: cola en segundo
// plano, tabla, aviso en vivo, tarjeta del detalle, etiqueta de la lista y candidatos a duplicado. Con un asistente falso: las
// pruebas nunca llaman a un proveedor externo (plan, 11). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public sealed class SugerenciaReporteTests(FabricaCad fabrica) : IAsyncLifetime
{
    private readonly AsistenteFalso falso = new();
    private WebApplicationFactory<Program> servidor = null!;

    public Task InitializeAsync()
    {
        servidor = fabrica.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ia:Habilitada", "true");
            b.UseSetting("Ia:TiempoMaximoSegundos", "1");
            b.ConfigureTestServices(s => s.AddScoped<IAsistenteReportes>(_ => falso));
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await servidor.DisposeAsync();

    private static ResultadoAsistente Sugerir(Guid categoria, DecisionSugerida decision = DecisionSugerida.Admitir,
        NivelConfianza confianza = NivelConfianza.Alta, string motivo = "Hecho de seguridad ciudadana con ubicación para atenderlo.",
        bool urgencia = false, Guid? duplicado = null, string justificacion = "Describe un arrebato de celular en la vía pública.") =>
        new(new AnalisisReporte(categoria, null, confianza, justificacion, decision, motivo,
            decision == DecisionSugerida.Derivar ? DestinosDerivacion.Bomberos : null,
            decision == DecisionSugerida.Derivar ? "Se informó del hecho a Bomberos." : null,
            decision == DecisionSugerida.Descartar ? MotivosDescarte.FalsoOMalintencionado : null,
            decision == DecisionSugerida.Admitir ? PrioridadesCaso.Media : null,
            urgencia, urgencia ? "Menciona un arma." : null, duplicado, []), "modelo-de-prueba");

    // Un punto al azar en Arequipa, para que los reportes de otras pruebas no caigan cerca.
    private static (double Lat, double Lon) PuntoAlAzar() => (-16.45 + Random.Shared.NextDouble() * 0.1, -71.60 + Random.Shared.NextDouble() * 0.1);

    private async Task<(Guid Id, HttpClient App)> RegistrarAsync(string descripcion, Guid categoria, (double Lat, double Lon)? punto = null,
        WebApplicationFactory<Program>? en = null, HttpClient? app = null)
    {
        en ??= servidor;
        if (app is null)
        {
            var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
            var token = await (await en.CreateClient().PostAsJsonAsync("/api/auth/token",
                new Cad.Contratos.Cuentas.SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
            app = en.CreateClient();
            app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);
        }
        var (lat, lon) = punto ?? PuntoAlAzar();
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), categoria, descripcion,
            DateTimeOffset.UtcNow.AddMinutes(-10), new UbicacionHecho(FuentesUbicacionHecho.Mapa, lat, lon, null, "Frente al parque")));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return ((await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id, app);
    }

    private Task<List<SugerenciaReporte>> SugerenciasAsync(Guid id) =>
        fabrica.ConBaseDatosAsync(db => db.SugerenciasReporte.AsNoTracking().Where(s => s.CasoId == id).ToListAsync());

    private async Task<SugerenciaReporte> EsperarSugerenciaAsync(Guid id)
    {
        SugerenciaReporte? sugerencia = null;
        Assert.True(await CentralDePrueba.EsperarAsync(() => (sugerencia = SugerenciasAsync(id).Result.SingleOrDefault()) is not null),
            "El análisis no guardó la sugerencia.");
        return sugerencia!;
    }

    private async Task<HttpClient> OperadorAsync(WebApplicationFactory<Program>? en = null)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = (en ?? servidor).CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private Task<ResultadoRevision> IniciarRevisionAsync(Guid id, Guid operadorId) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operadorId, CancellationToken.None));

    // ----- HU44 -----

    [Fact]
    public async Task HU44_CA01_AlRegistrarse_ElAnalisisGuardaLaSugerenciaYAvisaALaCentral()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id));
        await using var central = await fabrica.OperadorEnCentralAsync(servidor);

        var (id, _) = await RegistrarAsync("Me arrebataron el celular, mi número es 987654321", CategoriaIncidencia.Otros.Id);

        var sugerencia = await EsperarSugerenciaAsync(id);
        Assert.Equal((CategoriaIncidencia.RoboOHurto.Id, DecisionSugerida.Admitir, NivelConfianza.Alta, "modelo-de-prueba"),
            (sugerencia.CategoriaSugeridaId, sugerencia.Decision, sugerencia.Confianza, sugerencia.Modelo));
        Assert.Equal("2026-11-a", sugerencia.VersionPrompt);
        // El aviso llega dos veces: por el registro y por la sugerencia.
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Count(c => c == id) >= 2), "La central no recibió el aviso de la sugerencia.");
        // Plan, 10: al asistente no le llega el teléfono.
        var recibida = falso.Recibidas.Single(e => e.CasoId == id);
        Assert.DoesNotContain("987654321", recibida.Descripcion);
        Assert.Contains("[dato]", recibida.Descripcion);
    }

    [Fact]
    public async Task HU44_CA02_ConSugerenciaDistinta_UsarEstaCategoriaSoloEnRevisionYRegistraLaCorreccionComoHoy()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id));
        var (id, _) = await RegistrarAsync("Me arrebataron la cartera en el paradero", CategoriaIncidencia.Otros.Id);
        await EsperarSugerenciaAsync(id);
        var operador = await OperadorAsync();

        // «Recibido»: la tarjeta pide iniciar la revisión (HU45-CA06) y no ofrece el botón.
        var recibido = WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/reportes/{id}"));
        Assert.Contains("data-tarjeta-sugerencia", recibido);
        Assert.Contains("Sugerencia automática · datos simulados · revísala antes de decidir", recibido); // CA06
        Assert.Contains("Describe un arrebato de celular en la vía pública.", recibido);
        Assert.Contains("Confianza alta", recibido);
        Assert.Contains("data-usar-categoria-inactivo", recibido);
        Assert.DoesNotContain("data-usar-categoria ", recibido);
        Assert.Contains("data-iniciar-para-decidir", recibido);

        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        Assert.IsType<ResultadoRevision.Registrado>(await IniciarRevisionAsync(id, cuenta.Id));
        var enRevision = WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/reportes/{id}"));
        Assert.Contains("data-usar-categoria ", enRevision);
        Assert.Contains("data-usar-motivo", enRevision);

        var respuesta = await navegador.PostAsync($"/panel/reportes/{id}?handler=Corregir", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CategoriaId"] = CategoriaIncidencia.RoboOHurto.Id.ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, $"/panel/reportes/{id}"),
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var correccion = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.CorreccionesCategoria.AsNoTracking().Where(c => c.CasoId == id).ToListAsync()));
        Assert.Equal((CategoriaIncidencia.Otros.Id, CategoriaIncidencia.RoboOHurto.Id, cuenta.Id),
            (correccion.CategoriaAnteriorId, correccion.CategoriaNuevaId, correccion.OperadorId));
        Assert.Contains("data-ya-corregida", WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/reportes/{id}")));
    }

    [Fact]
    public async Task HU44_CA03_SiElAsistenteFallaOTarda_ElReporteSeRegistraIgualYNoHaySugerencia()
    {
        falso.Responder = (_, _) => throw new HttpRequestException("sin red");
        var cronometro = Stopwatch.StartNew();
        var (conError, _) = await RegistrarAsync("Me robaron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(5), "El registro esperó al asistente.");

        falso.Responder = async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return null; };
        var (lento, _) = await RegistrarAsync("Me robaron la bicicleta", CategoriaIncidencia.RoboOHurto.Id);

        // El análisis termina (por error o por tiempo agotado) sin guardar nada.
        Assert.True(await CentralDePrueba.EsperarAsync(() => falso.Recibidas.Any(e => e.CasoId == lento)));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Empty(await SugerenciasAsync(conError));
        Assert.Empty(await SugerenciasAsync(lento));
        var html = await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{conError}");
        Assert.DoesNotContain("data-tarjeta-sugerencia", html);
    }

    [Fact]
    public async Task HU44_CA03_ConLaIaApagada_NoSeAnalizaNiSeMuestraLaTarjeta()
    {
        // El servidor compartido tiene Ia:Habilitada = false (valor por defecto).
        var (id, _) = await RegistrarAsync("Me robaron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id, en: fabrica);

        await Task.Delay(500);
        Assert.Empty(await SugerenciasAsync(id));
        Assert.DoesNotContain(falso.Recibidas, e => e.CasoId == id);
        Assert.DoesNotContain("data-tarjeta-sugerencia", await (await OperadorAsync(fabrica)).GetStringAsync($"/panel/reportes/{id}"));
    }

    [Fact]
    public async Task HU44_CA05_RespuestaConCategoriaFueraDelCatalogo_SeDescarta()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(Guid.NewGuid()));
        var (id, _) = await RegistrarAsync("Me robaron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id);

        Assert.True(await CentralDePrueba.EsperarAsync(() => falso.Recibidas.Any(e => e.CasoId == id)));
        await Task.Delay(500);
        Assert.Empty(await SugerenciasAsync(id));
    }

    [Fact]
    public async Task HU44_CA07_NadaDeLaSugerenciaApareceEnLoQueVeElCiudadano()
    {
        const string justificacion = "Justificación interna exclusiva de esta prueba.";
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id, justificacion: justificacion,
            motivo: "Motivo borrador exclusivo de esta prueba."));
        var (id, app) = await RegistrarAsync("Me arrebataron la cartera", CategoriaIncidencia.Otros.Id);
        await EsperarSugerenciaAsync(id);

        var mio = await app.GetStringAsync($"/api/casos/mios/{id}");
        var lista = await app.GetStringAsync("/api/casos/mios");

        foreach (var texto in new[] { mio, lista })
        {
            Assert.DoesNotContain(justificacion, texto);
            Assert.DoesNotContain("Motivo borrador exclusivo", texto);
            Assert.DoesNotContain("Robo o hurto", texto); // la categoría sugerida no se aplica sola
        }
    }

    [Fact]
    public async Task HU44_EnLaListaDeReportes_LaCategoriaSugeridaApareceSoloSiDifiere()
    {
        falso.Responder = (e, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id));
        var (distinta, _) = await RegistrarAsync("Me arrebataron la mochila", CategoriaIncidencia.Otros.Id);
        var (igual, _) = await RegistrarAsync("Me robaron la mochila", CategoriaIncidencia.RoboOHurto.Id);
        await EsperarSugerenciaAsync(distinta);
        await EsperarSugerenciaAsync(igual);

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/reportes"));

        string Fila(Guid id)
        {
            var inicio = html.IndexOf($"data-reporte=\"{id}\"", StringComparison.Ordinal);
            return html[inicio..html.IndexOf("</tr>", inicio, StringComparison.Ordinal)];
        }
        Assert.Contains("Sugerida: Robo o hurto", Fila(distinta));
        Assert.DoesNotContain("data-categoria-sugerida", Fila(igual));
    }

    [Fact]
    public async Task Idempotencia_DosAnalisisDelMismoReporte_UnaSolaSugerencia()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id));
        var (id, _) = await RegistrarAsync("Me robaron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id);
        await EsperarSugerenciaAsync(id);

        servidor.Services.GetRequiredService<Cad.Server.Compartido.IColaAnalisisReportes>().Encolar(id);
        using (var alcance = servidor.Services.CreateScope())
            await alcance.ServiceProvider.GetRequiredService<AnalizarReporte>().EjecutarAsync(id, CancellationToken.None);
        await Task.Delay(500);

        Assert.Single(await SugerenciasAsync(id));
    }

    // ----- HU45 -----

    [Fact]
    public async Task HU45_CA01_CA02_DecisionSugerida_UsarEsteMotivoSoloRellenaElFormulario()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.Otros.Id, DecisionSugerida.Derivar,
            NivelConfianza.Media, "El hecho no es de seguridad ciudadana y corresponde a Bomberos (116)."));
        var (id, _) = await RegistrarAsync("Sale humo de un techo, parece un incendio", CategoriaIncidencia.Otros.Id);
        await EsperarSugerenciaAsync(id);
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoRevision.Registrado>(await IniciarRevisionAsync(id, cuenta.Id));
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/reportes/{id}"));

        Assert.Contains("data-sugerencia-decision=\"Derivar\"", html);
        Assert.Contains("El hecho no es de seguridad ciudadana y corresponde a Bomberos (116).", html);
        Assert.Contains("data-sugerencia-tipo=\"Derivacion\"", html);
        Assert.Contains("data-sugerencia-destino=\"Bomberos\"", html);
        Assert.Contains("data-sugerencia-accion=\"Se informó del hecho a Bomberos.\"", html);
        Assert.Contains("Bomberos (116)", html);
        // CA02: mostrar la sugerencia no decide nada.
        Assert.Equal(EstadoCaso.EnRevision, (await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id))).Estado);
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.DecisionesReporte.AsNoTracking().Where(d => d.CasoId == id).ToListAsync()));
    }

    [Fact]
    public async Task HU45_PosibleUrgencia_AvisoDestacadoYNuncaDescartar()
    {
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id, DecisionSugerida.Descartar,
            urgencia: true));
        var (id, _) = await RegistrarAsync("Un sujeto con cuchillo está asaltando ahora en el paradero", CategoriaIncidencia.RoboOHurto.Id);

        var sugerencia = await EsperarSugerenciaAsync(id);
        Assert.Equal(DecisionSugerida.RevisarConCuidado, sugerencia.Decision); // CA04
        Assert.True(sugerencia.PosibleUrgencia);
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{id}"));
        Assert.Contains("data-sugerencia-urgencia", html);
        Assert.Contains("Posible emergencia en curso", html);
        Assert.DoesNotContain("data-usar-motivo", html); // «Revisar con cuidado» no tiene un motivo que copiar
    }

    [Fact]
    public async Task HU45_CA05_CandidatosADuplicado_SinIa_CercanosDeCualquierCategoriaYNoDescartados()
    {
        var (lat, lon) = PuntoAlAzar();
        var desplazado = (double metros) => (lat + metros / 111_320, lon);
        // En el servidor compartido, con la IA apagada.
        var (original, app) = await RegistrarAsync("Me arrebataron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id, (lat, lon), en: fabrica);
        var (cercano, _) = await RegistrarAsync("Un tipo en moto le quitó el teléfono a una chica", CategoriaIncidencia.Otros.Id, desplazado(60), en: fabrica);
        var (lejano, _) = await RegistrarAsync("Me robaron en otra calle", CategoriaIncidencia.RoboOHurto.Id, desplazado(1200), en: fabrica);
        var (descartado, _) = await RegistrarAsync("Robo en la misma esquina", CategoriaIncidencia.RoboOHurto.Id, desplazado(30), en: fabrica);
        var operadorId = (await fabrica.CrearAsync(Roles.Operador)).Id;
        Assert.IsType<ResultadoRevision.Registrado>(await IniciarRevisionAsync(descartado, operadorId));
        Assert.IsType<ResultadoDecision.Registrado>(await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(descartado,
            new DatosDecision(TipoDecisionReporte.Descarte, "Duplicado.", MotivoDescarte: MotivosDescarte.Duplicado), operadorId, CancellationToken.None)));

        var candidatos = await fabrica.ConServiciosAsync(s => s.GetRequiredService<BuscarDuplicados>().EjecutarAsync(original, CancellationToken.None));

        var candidato = Assert.Single(candidatos);
        Assert.Equal(cercano, candidato.CasoId);
        Assert.InRange(candidato.Metros, 55, 65);
        Assert.Equal("Otros", candidato.Categoria);
        var html = WebUtility.HtmlDecode(await (await OperadorAsync(fabrica)).GetStringAsync($"/panel/reportes/{original}"));
        Assert.Contains("Posibles duplicados", html);
        Assert.Contains($"data-duplicado=\"{cercano}\"", html);
        Assert.DoesNotContain($"data-duplicado=\"{lejano}\"", html);
        Assert.DoesNotContain("Sugerencia automática", html); // sin IA no hay sugerencia, solo los candidatos

        // Sin punto no hay candidatos.
        var sinPunto = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.RoboOHurto.Id,
            "Me robaron cerca del mercado", DateTimeOffset.UtcNow.AddMinutes(-5), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Mercado San Camilo")));
        var idSinPunto = (await sinPunto.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        Assert.Empty(await fabrica.ConServiciosAsync(s => s.GetRequiredService<BuscarDuplicados>().EjecutarAsync(idSinPunto, CancellationToken.None)));
    }

    [Fact]
    public async Task HU45_ElModeloConfirmaUnDuplicadoDeLosCandidatos_SeMarcaEnLaTarjeta()
    {
        var (lat, lon) = PuntoAlAzar();
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(null);
        var (original, _) = await RegistrarAsync("Me arrebataron el celular en la esquina", CategoriaIncidencia.RoboOHurto.Id, (lat, lon));
        falso.Responder = (e, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id, DecisionSugerida.Descartar,
            NivelConfianza.Media, "Duplicado de un reporte anterior del mismo hecho.", duplicado: e.Candidatos.FirstOrDefault()?.CasoId));
        var (repetido, _) = await RegistrarAsync("Dos en moto le quitaron el celular a un joven", CategoriaIncidencia.Otros.Id, (lat + 0.0003, lon));

        var sugerencia = await EsperarSugerenciaAsync(repetido);
        Assert.Equal(original, sugerencia.DuplicadoDeId);
        Assert.Contains(original, falso.Recibidas.Single(e => e.CasoId == repetido).Candidatos.Select(c => c.CasoId));
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{repetido}"));
        Assert.Contains("data-mismo-hecho", html);
        Assert.Contains("Posible duplicado", html);
    }

    [Fact]
    public async Task RNF07_NiLaDescripcionNiLaSugerenciaSeRegistran()
    {
        var sello = Guid.NewGuid().ToString("N")[..12];
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(Sugerir(CategoriaIncidencia.RoboOHurto.Id,
            justificacion: $"Justificación {sello}", motivo: $"Motivo {sello}"));
        var (id, _) = await RegistrarAsync($"Me robaron el celular {sello} en la esquina", CategoriaIncidencia.RoboOHurto.Id);
        await EsperarSugerenciaAsync(id);

        Assert.Contains(fabrica.Registros.Entradas, e => e.Contains(id.ToString()) && e.Contains("sugerencia"));
        Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(sello));
    }
}
