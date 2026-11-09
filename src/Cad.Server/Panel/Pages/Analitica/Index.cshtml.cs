using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Cad.Server.Panel.Pages.Analitica;

// Dashboard de analítica, «Preguntas del analista» (27/10/2026). Cada pregunta abre con la respuesta escrita en una frase y su
// evidencia debajo; los cálculos son los de Modulos/Analitica. Solo operadores (decisión del usuario: sin rol nuevo por ahora).
// La selección (pregunta, periodo, prioridad) va en la dirección, como en «Casos» y el mapa de concentración.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(IServiceProvider servicios, CacheAnalitica cache, IOptions<OpcionesAnalitica> opciones, IConfiguration configuracion,
    TimeProvider reloj, ILogger<IndexModel> registro) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Pregunta { get; set; }
    [BindProperty(SupportsGet = true)] public string? Periodo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public string? Prioridad { get; set; }
    // «Rendimiento de los serenos»: el sereno elegido; sin él, la lista de todos.
    [BindProperty(SupportsGet = true)] public Guid? Sereno { get; set; }

    public Preguntas.Definicion Actual { get; private set; } = Preguntas.Buscar(null)!;
    public PeriodoAnalitica Rango { get; private set; } = null!;
    public PeriodoAnalitica Anterior { get; private set; } = null!;
    public string TextoPeriodo { get; private set; } = "";
    public string TextoAnterior { get; private set; } = "";
    public OpcionesAnalitica Opciones => opciones.Value;
    public Dictionary<string, Resumen> Resumenes { get; } = [];
    public string? ErrorPeriodo { get; private set; }

    // Lo que se calculó para la pregunta abierta; nulo si falló (Error lo explica).
    public object? Vista { get; private set; }
    public string? Error { get; private set; }
    public Zonas Zonas => new(configuracion);
    public TimeZoneInfo ZonaHoraria => TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public sealed record Resumen(string Texto, string Tono = "");

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Preguntas.Buscar(Pregunta) is not { } definicion)
            return NotFound();
        Actual = definicion;
        ResolverPeriodo();
        await CargarAsync(ct);
        await ResumirAsync(ct);
        return Page();
    }

    // Bloque F: el informe del periodo, en CSV (Excel en es-PE). Mismo periodo que la página.
    public async Task<IActionResult> OnGetInformeAsync(CancellationToken ct)
    {
        ResolverPeriodo();
        var informe = await servicios.GetRequiredService<GenerarInformePeriodo>().EjecutarAsync(Rango, ct);
        var desde = TimeZoneInfo.ConvertTime(Rango.Desde, ZonaHoraria).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return File(informe.ACsv(), "text/csv; charset=utf-8", $"informe-analitica-{desde}.csv");
    }

    // Enlaces de la página: misma selección, otra pregunta u otro periodo. Un periodo que la otra pregunta no ofrece (12 meses de
    // «Comparación por bloques» en «Tiempos de respuesta») no viaja: esa pregunta abre con el suyo.
    public string Enlace(string? pregunta = null, string? periodo = null)
    {
        var destino = pregunta ?? Actual.Clave;
        var p = periodo ?? PeriodoPara(destino);
        return Url.Page("/Analitica/Index", new
        {
            pregunta = destino,
            periodo = p,
            desde = p == "rango" ? Desde : null,
            hasta = p == "rango" ? Hasta : null,
            prioridad = destino == "tiempo" ? Prioridad : null,
            sereno = destino == "serenos" ? Sereno : null,
        })!;
    }

    private string? PeriodoPara(string pregunta) =>
        Periodo == "rango" || PeriodosDe(pregunta).Any(p => p.Valor == Periodo) ? Periodo : null;

    // Un sereno (o, sin él, la lista de todos) en el mismo periodo.
    public string EnlaceSereno(Guid? sereno) =>
        Url.Page("/Analitica/Index", new
        {
            pregunta = "serenos",
            periodo = Periodo,
            desde = Periodo == "rango" ? Desde : null,
            hasta = Periodo == "rango" ? Hasta : null,
            sereno,
        })!;

    // Los periodos que ofrece la pregunta abierta. «Comparación por bloques» mira meses (la evolución es de 12): 30 días, 3 meses o
    // 12 meses, y 12 meses de entrada; las demás, hoy, 7 o 30 días.
    public IReadOnlyList<(string Valor, string Texto)> Periodos => PeriodosDe(Actual.Clave);

    public static IReadOnlyList<(string Valor, string Texto)> PeriodosDe(string pregunta) => pregunta == "bloques"
        ? [("30", "30 días"), ("90", "3 meses"), ("365", "12 meses")]
        : [("hoy", "Hoy"), ("7", "7 días"), ("30", "30 días")];

    // El periodo de los datos de la columna de preguntas: el mismo con que abre cada una (Enlace). Con 3 o 12 meses, las demás
    // preguntas abren con sus 7 días; calcularlas sobre un año solo para la columna haría lenta la página (puntos críticos).
    private PeriodoAnalitica RangoResumen { get; set; } = null!;

    public string EnlaceInforme() => Url.Page("/Analitica/Index", "Informe", new { periodo = Periodo, desde = Desde, hasta = Hasta })!;

    public string EnlaceConcentracion() =>
        $"/panel/mapa-concentracion?Desde={Dia(Rango.Desde)}&Hasta={Dia(Rango.Hasta.AddTicks(-1))}";

    private string Dia(DateTimeOffset momento) => TimeZoneInfo.ConvertTime(momento, ZonaHoraria).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void ResolverPeriodo()
    {
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), ZonaHoraria);
        var hoy = DateOnly.FromDateTime(ahora.DateTime);
        var porDefecto = Actual.Clave == "bloques" ? "365" : "7";
        // Se acepta cualquier periodo conocido (el informe se pide sin pregunta); los botones muestran solo los de la pregunta.
        Periodo = Periodo is "hoy" or "7" or "30" or "90" or "365" or "rango" ? Periodo : porDefecto;
        (DateOnly desde, DateOnly hasta) = Periodo switch
        {
            "hoy" => (hoy, hoy),
            "30" => (hoy.AddDays(-29), hoy),
            "90" => (hoy.AddDays(-89), hoy),
            "365" => (hoy.AddDays(-364), hoy),
            "rango" => (Leer(Desde) ?? hoy.AddDays(-6), Leer(Hasta) ?? hoy),
            _ => (hoy.AddDays(-6), hoy),
        };
        if (desde > hasta)
        {
            ErrorPeriodo = "La fecha final es anterior a la inicial: se muestran los últimos 7 días.";
            (Periodo, desde, hasta) = ("7", hoy.AddDays(-6), hoy);
        }
        // Un rango de años haría cada cálculo muy costoso: se acota al último año del rango.
        if (hasta.DayNumber - desde.DayNumber > 365)
        {
            ErrorPeriodo = "El rango máximo es de un año: se muestra el último año del rango.";
            desde = hasta.AddDays(-365);
        }
        Desde = desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Hasta = hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Rango = new PeriodoAnalitica(Inicio(desde), Inicio(hasta.AddDays(1)), ZonaHoraria);
        var dias = hasta.DayNumber - desde.DayNumber + 1;
        Anterior = new PeriodoAnalitica(Inicio(desde.AddDays(-dias)), Inicio(desde), ZonaHoraria);
        RangoResumen = Periodo is "90" or "365" ? new PeriodoAnalitica(Inicio(hoy.AddDays(-6)), Inicio(hoy.AddDays(1)), ZonaHoraria) : Rango;
        var peru = CultureInfo.GetCultureInfo("es-PE");
        TextoPeriodo = Periodo switch
        {
            "hoy" => "el día de hoy",
            "7" => "los últimos 7 días",
            "30" => "los últimos 30 días",
            "90" => "los últimos 3 meses",
            "365" => "los últimos 12 meses",
            _ => desde == hasta ? $"el {desde.ToString("d 'de' MMMM", peru)}" : $"el periodo del {desde.ToString("d/MM", peru)} al {hasta.ToString("d/MM", peru)}",
        };
        TextoAnterior = Periodo switch
        {
            "hoy" => "ayer", "7" => "la semana anterior", "30" => "los 30 días anteriores", "90" => "los 3 meses anteriores",
            "365" => "los 12 meses anteriores", _ => "el periodo anterior",
        };
    }

    private static DateOnly? Leer(string? texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private DateTimeOffset Inicio(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ZonaHoraria.GetUtcOffset(local)).ToUniversalTime();
    }

    private T S<T>() where T : notnull => servicios.GetRequiredService<T>();
    private string Clave(string que, PeriodoAnalitica p) => $"analitica:{que}:{p.Desde:O}:{p.Hasta:O}";

    // Cada pregunta se calcula aparte: si una falla, solo ella muestra el error y las demás siguen.
    private async Task CargarAsync(CancellationToken ct)
    {
        try
        {
            Vista = Actual.Clave switch
            {
                "bloques" => await BloquesAsync(ct),
                "ahora" => await AhoraAsync(ct),
                "tiempo" => await TiempoAsync(ct),
                "falsos" => await FalsosAsync(ct),
                "cobertura" => await CoberturaAsync(ct),
                "reasigna" => await ReasignaAsync(ct),
                "serenos" => await SerenosAsync(ct),
                "repite" => await RepiteAsync(ct),
                "ciudadano" => await CiudadanoAsync(ct),
                _ => await SistemaAsync(ct),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            registro.LogError(ex, "No se pudo calcular la pregunta {Pregunta} de la analítica.", Actual.Clave);
            Error = HttpContext.TraceIdentifier;
        }
    }

    // ── Cada pregunta ─────────────────────────────────────────────────────────────────────────
    public sealed record VistaBloques(ComparacionBloques Datos);

    private async Task<VistaBloques> BloquesAsync(CancellationToken ct) =>
        new(await cache.ObtenerAsync(Clave("bloques", Rango), () => S<ConsultarComparacionBloques>().EjecutarAsync(Rango, ct)));

    public sealed record VistaAhora(FotoAhora Foto, CasosAtascados Casos);

    private async Task<VistaAhora> AhoraAsync(CancellationToken ct)
    {
        var foto = await S<ConsultarAhora>().EjecutarAsync(ct);
        var casos = await S<ConsultarCasosAtascados>().EjecutarAsync(ct);
        return new VistaAhora(foto, casos);
    }

    public sealed record VistaTiempo(IReadOnlyList<GrupoTiempos> Grupos, RespuestaPorZona Zonas, TiemposZona TotalAnterior,
        IReadOnlyList<double> Respuestas, CasosAtascados Casos, string? Prioridad);

    private async Task<VistaTiempo> TiempoAsync(CancellationToken ct)
    {
        var grupos = await cache.ObtenerAsync(Clave("etapas", Rango), () => S<ConsultarTiemposPorEtapa>().EjecutarAsync(Rango, ct));
        var zonas = await cache.ObtenerAsync(Clave("respuesta", Rango), () => S<ConsultarRespuestaPorZona>().EjecutarAsync(Rango, false, ct));
        var anterior = await cache.ObtenerAsync(Clave("respuesta", Anterior), () => S<ConsultarRespuestaPorZona>().EjecutarAsync(Anterior, false, ct));
        var prioridad = Prioridad is { Length: > 0 } && Cad.Contratos.Casos.Catalogo.Contiene(Cad.Contratos.Casos.PrioridadesCaso.Todas, Prioridad) ? Prioridad : null;
        Prioridad = prioridad;
        // Cada respuesta del periodo: con pocos casos se dibuja cada una en lugar de medianas. Sin cancelados ni descartados,
        // como el mapa por zona y la comparación con el periodo anterior (ConsultarRespuestaPorZona).
        var marcas = await ConsultarTiemposPorEtapa.MarcasAsync(HttpContext.RequestServices.GetRequiredService<Persistencia.CadDbContext>(), Rango, ct);
        var respuestas = marcas
            .Where(m => Modulos.Casos.Dominio.OrigenesCaso.SeAtiendeComoSos(m.Origen) && m.Atencion is not null && (prioridad is null || m.Prioridad == prioridad)
                && !ConsultarRespuestaPorZona.Excluidos.Contains(m.Estado))
            .Select(m => (m.Atencion!.Value - m.Registro).TotalMinutes).Order().ToList();
        return new VistaTiempo(grupos, zonas, anterior.Total, respuestas, await S<ConsultarCasosAtascados>().EjecutarAsync(ct), prioridad);
    }

    public sealed record VistaFalsos(IndiceFalsos Actual, IndiceFalsos Anterior, EmbudoReportes Embudo, ReportesAnalitica Reportes);

    private async Task<VistaFalsos> FalsosAsync(CancellationToken ct) => new(
        await cache.ObtenerAsync(Clave("falsos", Rango), () => S<ConsultarIndiceFalsos>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("falsos", Anterior), () => S<ConsultarIndiceFalsos>().EjecutarAsync(Anterior, ct)),
        await cache.ObtenerAsync(Clave("embudo", Rango), () => S<ConsultarEmbudoReportes>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("reportes", Rango), () => S<ConsultarReportesAnalitica>().EjecutarAsync(Rango, ct)));

    public sealed record VistaCobertura(Cobertura Cobertura, DemandaYOferta Demanda, IReadOnlyList<SerenoMonitoreo> Serenos);

    private async Task<VistaCobertura> CoberturaAsync(CancellationToken ct) => new(
        await cache.ObtenerAsync(Clave("cobertura", Rango), () => S<ConsultarCobertura>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("demanda", Rango), () => S<ConsultarDemandaYOferta>().EjecutarAsync(Rango, ct)),
        await S<ConsultarSerenos>().EjecutarAsync(ct));

    public sealed record VistaReasigna(ResumenReasignaciones Actual, ResumenReasignaciones Anterior);

    private async Task<VistaReasigna> ReasignaAsync(CancellationToken ct) => new(
        await cache.ObtenerAsync(Clave("reasigna", Rango), () => S<ConsultarReasignaciones>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("reasigna", Anterior), () => S<ConsultarReasignaciones>().EjecutarAsync(Anterior, ct)));

    // Elegido: el sereno pedido, con su periodo anterior (nulo si ese periodo no tuvo turnos ni casos). Pedido sin Elegido: el
    // sereno no estuvo en el periodo.
    public sealed record VistaSerenos(RendimientoEquipo Actual, RendimientoEquipo Anterior, RendimientoSereno? Elegido, RendimientoSereno? ElegidoAntes,
        Guid? Pedido);

    private async Task<VistaSerenos> SerenosAsync(CancellationToken ct)
    {
        var actual = await cache.ObtenerAsync(Clave("serenos", Rango), () => S<ConsultarRendimientoSerenos>().EjecutarAsync(Rango, ct));
        var anterior = await cache.ObtenerAsync(Clave("serenos", Anterior), () => S<ConsultarRendimientoSerenos>().EjecutarAsync(Anterior, ct));
        return new VistaSerenos(actual, anterior,
            actual.Serenos.FirstOrDefault(s => s.SerenoId == Sereno), anterior.Serenos.FirstOrDefault(s => s.SerenoId == Sereno), Sereno);
    }

    public sealed record VistaRepite(IReadOnlyList<PuntoCritico> Puntos, IReadOnlyList<ZonaEnAlza> Alzas, DateTimeOffset FinSemana);

    private async Task<VistaRepite> RepiteAsync(CancellationToken ct)
    {
        var fin = Rango.Hasta < reloj.GetUtcNow() ? Rango.Hasta : reloj.GetUtcNow();
        return new VistaRepite(
            await cache.ObtenerAsync(Clave("puntos", Rango), () => S<ConsultarPuntosCriticos>().EjecutarAsync(Rango, ct)),
            await S<ConsultarZonasEnAlza>().EjecutarAsync(fin, ct), fin);
    }

    public sealed record VistaCiudadano(EvaluacionesAnalitica Evaluaciones, EvaluacionesAnalitica Anterior, Adopcion Adopcion, ReportesAnalitica Reportes);

    private async Task<VistaCiudadano> CiudadanoAsync(CancellationToken ct) => new(
        await cache.ObtenerAsync(Clave("evaluaciones", Rango), () => S<ConsultarEvaluaciones>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("evaluaciones", Anterior), () => S<ConsultarEvaluaciones>().EjecutarAsync(Anterior, ct)),
        await cache.ObtenerAsync(Clave("adopcion", Rango), () => S<ConsultarAdopcion>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("reportes", Rango), () => S<ConsultarReportesAnalitica>().EjecutarAsync(Rango, ct)));

    public sealed record VistaSistema(SaludSistema Salud, SaludSistema Anterior);

    private async Task<VistaSistema> SistemaAsync(CancellationToken ct) => new(
        await cache.ObtenerAsync(Clave("salud", Rango), () => S<ConsultarSaludSistema>().EjecutarAsync(Rango, ct)),
        await cache.ObtenerAsync(Clave("salud", Anterior), () => S<ConsultarSaludSistema>().EjecutarAsync(Anterior, ct)));

    // ── La columna de preguntas: el dato de cada una (lo que ya está calculado o es barato) ─────────────────────────
    private async Task ResumirAsync(CancellationToken ct)
    {
        async Task Intentar(string clave, Func<Task<Resumen>> resumir)
        {
            try { Resumenes[clave] = await resumir(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogWarning(ex, "No se pudo resumir la pregunta {Pregunta}.", clave);
                Resumenes[clave] = new Resumen("No se pudo calcular", "aviso");
            }
        }
        var minimo = Opciones.MinimoParaPercentiles;
        Resumenes["bloques"] = new Resumen($"{BloquesDistrito.Todos.Count} bloques · {BloquesDistrito.TotalZonas} zonas");
        await Intentar("ahora", async () =>
        {
            var atascados = (await S<ConsultarCasosAtascados>().EjecutarAsync(ct)).Atascados.Count;
            return atascados > 0 ? new Resumen($"▲ {atascados} sobre el tiempo esperado", "aviso") : new Resumen("✓ Todo a tiempo", "bien");
        });
        await Intentar("tiempo", async () =>
        {
            var total = (await cache.ObtenerAsync(Clave("respuesta", RangoResumen), () => S<ConsultarRespuestaPorZona>().EjecutarAsync(RangoResumen, false, ct))).Total;
            return total.Atendidos == 0 ? new Resumen("Sin SOS atendidos")
                : total.Atendidos < minimo ? new Resumen($"{total.Atendidos} SOS · pocos para medianas")
                : new Resumen($"Mitad atendida · {Formato.Minutos(total.RespuestaP50)}");
        });
        await Intentar("falsos", async () =>
        {
            var indice = await cache.ObtenerAsync(Clave("falsos", RangoResumen), () => S<ConsultarIndiceFalsos>().EjecutarAsync(RangoResumen, ct));
            return indice.PorcentajeReportesFalsos is { } p ? new Resumen($"Reportes falsos · {Formato.Porcentaje(p)}") : new Resumen("Sin reportes decididos");
        });
        Resumenes["cobertura"] = cache.SiExiste<Cobertura>(Clave("cobertura", RangoResumen)) is { } cobertura
            ? cobertura.Zonas.Count(z => z.PorcentajeCubierto < 50) is var sinCubrir and > 0
                ? new Resumen($"▲ {sinCubrir} {(sinCubrir == 1 ? "zona" : "zonas")} sin cubrir", "aviso")
                : cobertura.Zonas.Count == 0 ? new Resumen("Sin zonas con 3+ casos") : new Resumen("✓ Zonas cubiertas", "bien")
            : new Resumen("Zonas, turnos y serenos");
        await Intentar("reasigna", async () =>
        {
            var r = await cache.ObtenerAsync(Clave("reasigna", RangoResumen), () => S<ConsultarReasignaciones>().EjecutarAsync(RangoResumen, ct));
            return r.PorcentajeReasignados is not { } p ? new Resumen("Sin casos asignados")
                : r.CasosReasignados == 0 ? new Resumen("✓ Ninguno reasignado", "bien")
                : p > Opciones.ReferenciaReasignadosPorcentaje ? new Resumen($"▲ {Formato.Porcentaje(p, 0)} reasignados", "aviso")
                : new Resumen($"{Formato.Porcentaje(p, 0)} reasignados");
        });
        // Sin nombres en la columna (se ve desde todas las preguntas), y sin calcularla desde otra pregunta: lee las posiciones de
        // todos los serenos. Solo si ya está en la caché, como la cobertura.
        Resumenes["serenos"] = cache.SiExiste<RendimientoEquipo>(Clave("serenos", RangoResumen)) is { } equipo
            ? equipo.Serenos.Count == 0 ? new Resumen("Sin serenos en el periodo")
                : equipo.Serenos.Count(s => Rendimiento.Senales(s, equipo, Opciones).Count > 0) is var mirar and > 0
                    ? new Resumen($"▲ {mirar} {(mirar == 1 ? "sereno" : "serenos")} con señales", "aviso")
                    : new Resumen("✓ Ninguno fuera de lo normal", "bien")
            : new Resumen("Llegada, notas y zonas");
        await Intentar("repite", async () =>
        {
            var puntos = (await cache.ObtenerAsync(Clave("puntos", RangoResumen), () => S<ConsultarPuntosCriticos>().EjecutarAsync(RangoResumen, ct))).Count;
            return new Resumen(puntos == 0 ? "Sin puntos críticos" : $"{puntos} {(puntos == 1 ? "punto crítico" : "puntos críticos")}");
        });
        await Intentar("ciudadano", async () =>
        {
            var e = await cache.ObtenerAsync(Clave("evaluaciones", RangoResumen), () => S<ConsultarEvaluaciones>().EjecutarAsync(RangoResumen, ct));
            return e.Evaluados == 0 ? new Resumen("Sin evaluaciones")
                : new Resumen($"{Formato.Decimal(Ciudadano.Promedio(e))} de 5 · {Formato.Porcentaje(e.PorcentajeEvaluados, 0)} evalúa");
        });
        await Intentar("sistema", async () =>
        {
            var salud = await cache.ObtenerAsync(Clave("salud", RangoResumen), () => S<ConsultarSaludSistema>().EjecutarAsync(RangoResumen, ct));
            var alertas = Sistema.Senales(salud).Count(s => !s.Bien);
            return alertas == 0 ? new Resumen("✓ Todo dentro de lo aceptable", "bien") : new Resumen($"▲ {alertas} fuera de lo aceptable", "aviso");
        });
    }
}
