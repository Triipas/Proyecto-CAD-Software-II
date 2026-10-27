using Cad.Contratos.Casos;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Bloque «Ahora» (27/10/2026): el núcleo del dashboard. Una foto del momento, los tiempos de cada etapa del caso con p50 y p90
// (también por prioridad), el embudo de los reportes y el índice de casos falsos. Reutiliza las marcas que ya registran los
// casos (eventos, presentación y apertura en la central) y los catálogos del 27/10/2026.

// ── Tiempos por etapa ─────────────────────────────────────────────────────────────────────────────
public sealed record EtapaTiempo(string Etapa, int Casos, double? P50Minutos, double? P90Minutos);

// Prioridad nula: todas las prioridades juntas. «SinRegistrar»: casos sin prioridad (anteriores o aún sin clasificar).
public sealed record GrupoTiempos(string Origen, string? Prioridad, IReadOnlyList<EtapaTiempo> Etapas);

public sealed class ConsultarTiemposPorEtapa(CadDbContext db)
{
    public const string Sos = "SOS";
    public const string Reporte = "Reporte";
    public const string SinRegistrar = "SinRegistrar";

    // El primer momento de cada marca del caso (nulo si no pasó).
    public sealed record Marcas(Guid Id, OrigenCaso Origen, string? Prioridad, DateTimeOffset Registro, DateTimeOffset? Presentado, DateTimeOffset? Abierto,
        DateTimeOffset? Contacto, DateTimeOffset? Revision, DateTimeOffset? Decision, DateTimeOffset? Admision, DateTimeOffset? Asignacion,
        DateTimeOffset? EnCamino, DateTimeOffset? Atencion, DateTimeOffset? Cierre);

    private static readonly (string Etapa, Func<Marcas, DateTimeOffset?> Desde, Func<Marcas, DateTimeOffset?> Hasta)[] EtapasSos =
    [
        ("Hasta aparecer en la central", m => m.Registro, m => m.Presentado),
        ("Hasta que la central lo abre", m => m.Registro, m => m.Abierto),
        ("Hasta el primer contacto", m => m.Registro, m => m.Contacto),
        ("Hasta el despacho", m => m.Registro, m => m.Asignacion),
        ("Del despacho a «En camino»", m => m.Asignacion, m => m.EnCamino),
        ("De «En camino» a la atención", m => m.EnCamino, m => m.Atencion),
        ("De la atención al cierre", m => m.Atencion, m => m.Cierre),
        ("Respuesta (registro → atención)", m => m.Registro, m => m.Atencion),
    ];

    private static readonly (string Etapa, Func<Marcas, DateTimeOffset?> Desde, Func<Marcas, DateTimeOffset?> Hasta)[] EtapasReporte =
    [
        ("Hasta que la central lo abre", m => m.Registro, m => m.Abierto),
        ("Hasta iniciar la revisión", m => m.Registro, m => m.Revision),
        ("De la revisión a la decisión", m => m.Revision, m => m.Decision),
        ("Hasta la decisión", m => m.Registro, m => m.Decision),
        ("De la admisión al despacho", m => m.Admision, m => m.Asignacion),
        ("Del despacho a «En camino»", m => m.Asignacion, m => m.EnCamino),
        ("De «En camino» a la atención", m => m.EnCamino, m => m.Atencion),
        ("De la atención al cierre", m => m.Atencion, m => m.Cierre),
    ];

    public async Task<IReadOnlyList<GrupoTiempos>> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var marcas = await MarcasAsync(db, periodo, ct);
        var grupos = new List<GrupoTiempos>();
        foreach (var (origen, etapas, delOrigen) in new[]
        {
            (Sos, EtapasSos, marcas.Where(m => OrigenesCaso.SeAtiendeComoSos(m.Origen)).ToList()),
            (Reporte, EtapasReporte, marcas.Where(m => m.Origen == OrigenCaso.Reporte).ToList()),
        })
        {
            grupos.Add(new GrupoTiempos(origen, null, Medir(etapas, delOrigen)));
            foreach (var prioridad in PrioridadesCaso.Todas.Select(p => p.Codigo).Append(SinRegistrar))
            {
                var deLaPrioridad = delOrigen.Where(m => (m.Prioridad ?? SinRegistrar) == prioridad).ToList();
                if (deLaPrioridad.Count > 0)
                    grupos.Add(new GrupoTiempos(origen, prioridad, Medir(etapas, deLaPrioridad)));
            }
        }
        return grupos;
    }

    private static List<EtapaTiempo> Medir((string Etapa, Func<Marcas, DateTimeOffset?> Desde, Func<Marcas, DateTimeOffset?> Hasta)[] etapas,
        List<Marcas> casos) =>
        etapas.Select(e =>
        {
            var minutos = casos.Where(m => e.Desde(m) is not null && e.Hasta(m) is { } h && h >= e.Desde(m))
                .Select(m => (e.Hasta(m)!.Value - e.Desde(m)!.Value).TotalMinutes).ToList();
            return new EtapaTiempo(e.Etapa, minutos.Count, Estadistica.Percentil(minutos, 0.5), Estadistica.Percentil(minutos, 0.9));
        }).ToList();

    // Las marcas de los casos registrados en el periodo (también la usan el embudo y el índice de falsos).
    internal static async Task<List<Marcas>> MarcasAsync(CadDbContext db, PeriodoAnalitica periodo, CancellationToken ct)
    {
        var casos = await db.Casos.AsNoTracking().Where(c => c.RegistradoEn >= periodo.Desde && c.RegistradoEn < periodo.Hasta)
            .Select(c => new { c.Id, c.Origen, c.Prioridad, c.RegistradoEn, c.PresentadoEnCentralEn, c.AbiertoEnCentralEn })
            .ToListAsync(ct);
        var ids = casos.Select(c => c.Id).ToList();
        var primeros = (await db.EventosCaso.AsNoTracking().Where(e => ids.Contains(e.CasoId))
                .GroupBy(e => new { e.CasoId, e.Tipo })
                .Select(g => new { g.Key.CasoId, g.Key.Tipo, Primero = g.Min(e => e.OcurridoEn) })
                .ToListAsync(ct))
            .ToDictionary(e => (e.CasoId, e.Tipo), e => e.Primero);
        DateTimeOffset? Primero(Guid caso, params TipoEventoCaso[] tipos) =>
            tipos.Select(t => primeros.TryGetValue((caso, t), out var en) ? en : (DateTimeOffset?)null).Where(en => en is not null).Min();

        return casos.Select(c => new Marcas(c.Id, c.Origen, c.Prioridad, c.RegistradoEn, c.PresentadoEnCentralEn, c.AbiertoEnCentralEn,
            Primero(c.Id, TipoEventoCaso.IntentoContactoRegistrado),
            Primero(c.Id, TipoEventoCaso.RevisionIniciada),
            Primero(c.Id, TipoEventoCaso.ReporteAdmitido, TipoEventoCaso.ReporteDerivado, TipoEventoCaso.ReporteDescartado),
            Primero(c.Id, TipoEventoCaso.ReporteAdmitido),
            Primero(c.Id, TipoEventoCaso.CasoAsignado),
            Primero(c.Id, TipoEventoCaso.DesplazamientoConfirmado),
            Primero(c.Id, TipoEventoCaso.AtencionIniciada),
            Primero(c.Id, TipoEventoCaso.CasoCerrado))).ToList();
    }
}

// ── Embudo de reportes ────────────────────────────────────────────────────────────────────────────
public sealed record ConteoCodigo(string Codigo, string Texto, int Veces);

// Los reportes registrados en el periodo, paso a paso. La central registra los suyos ya admitidos (no pasan por la revisión).
// SinDecidir: aún «Recibido» o «En revisión». Los desgloses usan los catálogos del 27/10/2026 («Sin registrar» antes).
public sealed record EmbudoReportes(int Recibidos, int DeLaApp, int DeLaCentral, int Revisados, int Decididos, int Admitidos, int Derivados,
    int Descartados, int Retirados, int SinDecidir, int Asignados, int Atendidos, int Cerrados,
    IReadOnlyList<ConteoCodigo> Destinos, IReadOnlyList<ConteoCodigo> MotivosDescarte, IReadOnlyList<ConteoCodigo> ResultadosCierre);

public sealed class ConsultarEmbudoReportes(CadDbContext db)
{
    public const string SinRegistrar = "SinRegistrar";

    public async Task<EmbudoReportes> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var marcas = (await ConsultarTiemposPorEtapa.MarcasAsync(db, periodo, ct)).Where(m => m.Origen == OrigenCaso.Reporte).ToList();
        var ids = marcas.Select(m => m.Id).ToList();
        var filas = await (from caso in db.Casos.AsNoTracking()
                           where ids.Contains(caso.Id)
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                           join decision in db.DecisionesReporte.AsNoTracking() on caso.Id equals decision.CasoId into decisiones
                           from decision in decisiones.DefaultIfEmpty()
                           join cierre in db.CierresCaso.AsNoTracking() on caso.Id equals cierre.CasoId into cierres
                           from cierre in cierres.DefaultIfEmpty()
                           select new
                           {
                               caso.Estado,
                               DeLaCentral = detalle.RegistradoPorOperadorId != null,
                               Tipo = decision == null ? (TipoDecisionReporte?)null : decision.Tipo,
                               DestinoCodigo = decision == null ? null : decision.DestinoCodigo,
                               MotivoCodigo = decision == null ? null : decision.MotivoCodigo,
                               ResultadoAtencion = cierre == null ? null : cierre.ResultadoAtencion,
                               Cerrado = cierre != null,
                           })
                          .ToListAsync(ct);
        var deLaApp = filas.Where(f => !f.DeLaCentral).ToList();

        static IReadOnlyList<ConteoCodigo> Contar(IEnumerable<string?> codigos, IReadOnlyList<OpcionCatalogo> catalogo) =>
            codigos.GroupBy(c => c ?? SinRegistrar)
                .Select(g => new ConteoCodigo(g.Key, Catalogo.Texto(catalogo, g.Key) ?? "Sin registrar", g.Count()))
                .OrderByDescending(c => c.Veces).ToList();

        return new EmbudoReportes(
            filas.Count, deLaApp.Count, filas.Count - deLaApp.Count,
            marcas.Count(m => m.Revision is not null),
            deLaApp.Count(f => f.Tipo is not null),
            filas.Count(f => f.Tipo == TipoDecisionReporte.Admision),
            filas.Count(f => f.Tipo == TipoDecisionReporte.Derivacion),
            filas.Count(f => f.Tipo == TipoDecisionReporte.Descarte),
            filas.Count(f => f.Estado == EstadoCaso.Cancelada),
            filas.Count(f => f.Estado is EstadoCaso.Recibido or EstadoCaso.EnRevision),
            marcas.Count(m => m.Asignacion is not null),
            marcas.Count(m => m.Atencion is not null),
            filas.Count(f => f.Cerrado),
            Contar(filas.Where(f => f.Tipo == TipoDecisionReporte.Derivacion).Select(f => f.DestinoCodigo), DestinosDerivacion.Todos),
            Contar(filas.Where(f => f.Tipo == TipoDecisionReporte.Descarte).Select(f => f.MotivoCodigo), MotivosDescarte.Todos),
            Contar(filas.Where(f => f.Cerrado).Select(f => f.ResultadoAtencion), ResultadosAtencion.Todos));
    }
}

// ── Índice de falsos ──────────────────────────────────────────────────────────────────────────────
// Reportes: de los decididos en el periodo (sin los de la central), cuántos se descartaron como falsos o malintencionados (HU17
// con su motivo del catálogo), en total y por categoría. SOS (y casos urgentes): descartados por falsos, activaciones
// accidentales (descarte por ese motivo o cancelación «Lo envié por error») y falsas alarmas al cerrar. Los motivos de descarte y
// de cancelación, desglosados. Un descarte o una cancelación anterior a los catálogos cuenta como «Sin registrar», no como falso.
public sealed record FalsosCategoria(string Categoria, int Decididos, int Falsos)
{
    public double Porcentaje => Decididos == 0 ? 0 : 100.0 * Falsos / Decididos;
}

public sealed record IndiceFalsos(int ReportesDecididos, int ReportesFalsos, IReadOnlyList<FalsosCategoria> PorCategoria,
    int Sos, int SosDescartadosPorFalsos, int SosActivacionAccidental, int SosFalsaAlarmaAlCerrar,
    IReadOnlyList<ConteoCodigo> MotivosDescarte, IReadOnlyList<ConteoCodigo> MotivosCancelacion)
{
    public double? PorcentajeReportesFalsos => ReportesDecididos == 0 ? null : 100.0 * ReportesFalsos / ReportesDecididos;
    public double? PorcentajeSosFalsos => Sos == 0 ? null : 100.0 * SosDescartadosPorFalsos / Sos;
    public double? PorcentajeSosAccidentales => Sos == 0 ? null : 100.0 * SosActivacionAccidental / Sos;
}

public sealed class ConsultarIndiceFalsos(CadDbContext db)
{
    public async Task<IndiceFalsos> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var categorias = await Categorias.NombresAsync(db, ct);
        var decisiones = await (from d in db.DecisionesReporte.AsNoTracking()
                                join detalle in db.DetallesReporte.AsNoTracking() on d.CasoId equals detalle.CasoId
                                join caso in db.Casos.AsNoTracking() on d.CasoId equals caso.Id
                                where d.DecididaEn >= periodo.Desde && d.DecididaEn < periodo.Hasta && detalle.RegistradoPorOperadorId == null
                                select new { Falso = d.Tipo == TipoDecisionReporte.Descarte && d.MotivoCodigo == MotivosDescarte.FalsoOMalintencionado, caso.CategoriaId })
                               .ToListAsync(ct);

        var sos = await db.Casos.AsNoTracking()
            .Where(c => c.RegistradoEn >= periodo.Desde && c.RegistradoEn < periodo.Hasta && c.Origen != OrigenCaso.Reporte)
            .Select(c => c.Id).ToListAsync(ct);
        var descartes = await db.DescartesCaso.AsNoTracking().Where(d => sos.Contains(d.CasoId)).Select(d => d.MotivoCodigo).ToListAsync(ct);
        var cancelaciones = await db.CancelacionesSos.AsNoTracking().Where(c => sos.Contains(c.CasoId)).Select(c => c.MotivoCodigo).ToListAsync(ct);
        var falsasAlarmas = await db.CierresCaso.AsNoTracking()
            .CountAsync(c => sos.Contains(c.CasoId) && c.ResultadoAtencion == ResultadosAtencion.FalsaAlarma, ct);

        static IReadOnlyList<ConteoCodigo> Contar(IEnumerable<string?> codigos, IReadOnlyList<OpcionCatalogo> catalogo) =>
            codigos.GroupBy(c => c ?? ConsultarEmbudoReportes.SinRegistrar)
                .Select(g => new ConteoCodigo(g.Key, Catalogo.Texto(catalogo, g.Key) ?? "Sin registrar", g.Count()))
                .OrderByDescending(c => c.Veces).ToList();

        return new IndiceFalsos(decisiones.Count, decisiones.Count(d => d.Falso),
            decisiones.GroupBy(d => d.CategoriaId is { } id ? categorias.GetValueOrDefault(id, Categorias.SinClasificar) : Categorias.SinClasificar)
                .Select(g => new FalsosCategoria(g.Key, g.Count(), g.Count(d => d.Falso)))
                .OrderByDescending(f => f.Porcentaje).ThenByDescending(f => f.Decididos).ToList(),
            sos.Count,
            descartes.Count(m => m == MotivosDescarte.FalsoOMalintencionado),
            descartes.Count(m => m == MotivosDescarte.ActivacionAccidental) + cancelaciones.Count(m => m == MotivosCancelacion.PorError),
            falsasAlarmas,
            Contar(descartes, MotivosDescarte.Todos), Contar(cancelaciones, MotivosCancelacion.Todos));
    }
}

// ── Foto del momento ──────────────────────────────────────────────────────────────────────────────
// Lo que la central tiene entre manos ahora: SOS pendientes (el más antiguo y cuántos nadie abrió), reportes por revisar y
// admitidos sin asignar, casos en curso por etapa, serenos operativos y disponibles (la regla de HU19-CA02), acompañamientos
// activos con sus avisos sin atender, y cuántos casos están atascados (ConsultarCasosAtascados).
public sealed record FotoAhora(DateTimeOffset En, int SosPendientes, double? SosMasAntiguoMinutos, int SosSinAbrir,
    int ReportesPorRevisar, double? ReporteMasAntiguoMinutos, int ReportesAdmitidosSinAsignar,
    int Asignados, int EnCamino, int EnAtencion, int SerenosOperativos, int SerenosDisponibles,
    int AcompanamientosActivos, int AvisosSinAtender, int CasosAtascados);

public sealed class ConsultarAhora(CadDbContext db, TimeProvider reloj, Consultas.ConsultarSerenos serenos, ConsultarCasosAtascados atascados)
{
    public async Task<FotoAhora> EjecutarAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var abiertos = await db.Casos.AsNoTracking()
            .Where(c => c.Estado == EstadoCaso.Pendiente || c.Estado == EstadoCaso.Recibido || c.Estado == EstadoCaso.EnRevision
                || c.Estado == EstadoCaso.Asignada || c.Estado == EstadoCaso.EnCamino || c.Estado == EstadoCaso.EnAtencion)
            .Select(c => new { c.Origen, c.Estado, c.RegistradoEn, c.AbiertoEnCentralEn })
            .ToListAsync(ct);
        var sosPendientes = abiertos.Where(c => c.Estado == EstadoCaso.Pendiente && c.Origen != OrigenCaso.Reporte).ToList();
        var porRevisar = abiertos.Where(c => c.Estado is EstadoCaso.Recibido or EstadoCaso.EnRevision).ToList();
        double? MasAntiguo(IEnumerable<DateTimeOffset> registros) => registros.Any() ? (ahora - registros.Min()).TotalMinutes : null;

        var equipo = await serenos.EjecutarAsync(ct);
        var activos = await db.SesionesAcompanamiento.AsNoTracking().CountAsync(s => s.Estado == EstadoSesion.Activa && s.VenceEn > ahora, ct);
        var avisos = await db.AvisosAcompanamiento.AsNoTracking().CountAsync(a => a.Estado == EstadoAviso.Generado, ct);

        return new FotoAhora(ahora,
            sosPendientes.Count, MasAntiguo(sosPendientes.Select(c => c.RegistradoEn)), sosPendientes.Count(c => c.AbiertoEnCentralEn is null),
            porRevisar.Count, MasAntiguo(porRevisar.Select(c => c.RegistradoEn)),
            abiertos.Count(c => c.Estado == EstadoCaso.Pendiente && c.Origen == OrigenCaso.Reporte),
            abiertos.Count(c => c.Estado == EstadoCaso.Asignada), abiertos.Count(c => c.Estado == EstadoCaso.EnCamino),
            abiertos.Count(c => c.Estado == EstadoCaso.EnAtencion),
            equipo.Count(s => s.Operativo), equipo.Count(s => s.Disponible),
            activos, avisos, (await atascados.EjecutarAsync(ct)).Atascados.Count);
    }
}
