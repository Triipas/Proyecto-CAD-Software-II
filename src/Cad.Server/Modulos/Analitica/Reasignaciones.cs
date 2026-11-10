using Cad.Contratos.Casos;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// «Reasignaciones» (29/10/2026): con qué frecuencia la central le quita un caso a un sereno para darlo a otro, y por qué. Una
// reasignación solo se hace mientras el sereno aún no llega («Asignada» o «En camino»), así que cada una es tiempo que el
// ciudadano esperó de más. Se mide sobre lo que ya guardan Asignaciones y Reasignaciones; no guarda nada.
//
// Como el resto de la analítica, todo es agregado: no sale un sereno ni un operador con nombre. La concentración dice cuántos
// serenos pierden casos y cuántos pierde el que más, no quiénes son.
public sealed record MotivoReasignacionContado(string Codigo, string Texto, int Veces);

// Un tramo del periodo (16/11/2026, «Qué hay que arreglar»): sus reasignaciones por motivo, con la clave de Motivos. Los tramos
// son de 4 horas, un día, una o dos semanas o 30 días: los más cortos que dejan 10 columnas o menos.
public sealed record TramoReasignaciones(string Etiqueta, DateTimeOffset Desde, IReadOnlyDictionary<string, int> PorMotivo, DateTimeOffset Hasta = default)
{
    public int Total => PorMotivo.Values.Sum();
}

public sealed record ResumenReasignaciones(
    int CasosAsignados, int CasosReasignados, int Reasignaciones, int CasosReasignadosVariasVeces,
    IReadOnlyList<MotivoReasignacionContado> Motivos, int RetirosPorDesactivacion,
    int SerenosConAsignacion, int SerenosConRetiros, int MaximoRetirosDeUnSereno,
    int Esperas, double? EsperaP50Minutos, double? EsperaP90Minutos,
    IReadOnlyList<TramoReasignaciones>? Tramos = null, IReadOnlyList<int>? RetirosPorSerenoOrdenados = null)
{
    // Sobre los casos que se asignaron en el periodo (cada caso una vez, aunque se reasigne varias). Sin casos asignados no hay
    // porcentaje: no un 0 inventado.
    public double? PorcentajeReasignados => CasosAsignados == 0 ? null : 100.0 * CasosReasignados / CasosAsignados;
}

public sealed class ConsultarReasignaciones(CadDbContext db, TimeProvider reloj)
{
    // Reasignaciones anteriores al 27/10/2026, sin motivo del catálogo.
    public const string SinMotivo = "SinMotivo";

    public async Task<ResumenReasignaciones> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var asignaciones = await db.Asignaciones.AsNoTracking()
            .Where(a => a.AsignadaEn >= periodo.Desde && a.AsignadaEn < periodo.Hasta)
            .Select(a => new { a.CasoId, a.SerenoId })
            .ToListAsync(ct);
        var reasignaciones = await db.Reasignaciones.AsNoTracking()
            .Where(r => r.ReasignadoEn >= periodo.Desde && r.ReasignadoEn < periodo.Hasta)
            .Select(r => new { r.CasoId, r.SerenoAnteriorId, r.MotivoCodigo, r.ReasignadoEn })
            .ToListAsync(ct);
        // Una asignación cancelada deja de ser vigente por dos caminos: la reasignó la central (queda su fila en Reasignaciones, con
        // el mismo instante) o se desactivó la cuenta del sereno (no queda ninguna). Con ellas se mide cuánto tuvo el caso el sereno
        // anterior y cuántos retiros no fueron una decisión de la central.
        var canceladas = await db.Asignaciones.AsNoTracking()
            .Where(a => a.Cancelada && a.FinalizadaEn >= periodo.Desde && a.FinalizadaEn < periodo.Hasta)
            .Select(a => new { a.CasoId, a.SerenoId, a.AsignadaEn, FinalizadaEn = a.FinalizadaEn!.Value })
            .ToListAsync(ct);

        var esperas = new List<double>();
        var porReasignacion = new HashSet<(Guid, Guid, DateTimeOffset)>();
        foreach (var r in reasignaciones)
        {
            var anterior = canceladas.FirstOrDefault(a => a.CasoId == r.CasoId && a.SerenoId == r.SerenoAnteriorId && a.FinalizadaEn == r.ReasignadoEn);
            if (anterior is null)
                continue;
            porReasignacion.Add((anterior.CasoId, anterior.SerenoId, anterior.FinalizadaEn));
            esperas.Add((r.ReasignadoEn - anterior.AsignadaEn).TotalMinutes);
        }
        var porDesactivacion = canceladas.Count(a => !porReasignacion.Contains((a.CasoId, a.SerenoId, a.FinalizadaEn)));

        var motivos = MotivosReasignacion.Todos
            .Select(m => new MotivoReasignacionContado(m.Codigo, m.Texto, reasignaciones.Count(r => r.MotivoCodigo == m.Codigo)))
            .ToList();
        var sinMotivo = reasignaciones.Count(r => r.MotivoCodigo is null);
        if (sinMotivo > 0)
            motivos.Add(new MotivoReasignacionContado(SinMotivo, "Sin motivo registrado", sinMotivo));

        var retirosPorSereno = reasignaciones.GroupBy(r => r.SerenoAnteriorId).Select(g => g.Count()).OrderDescending().ToList();
        var tramos = Tramos(periodo, reloj.GetUtcNow())
            .Select(t => new TramoReasignaciones(t.Etiqueta, t.Desde, motivos.ToDictionary(m => m.Codigo,
                m => reasignaciones.Count(r => r.ReasignadoEn >= t.Desde && r.ReasignadoEn < t.Hasta && (r.MotivoCodigo ?? SinMotivo) == m.Codigo)), t.Hasta))
            .ToList();
        return new ResumenReasignaciones(
            asignaciones.Select(a => a.CasoId).Distinct().Count(),
            reasignaciones.Select(r => r.CasoId).Distinct().Count(),
            reasignaciones.Count,
            reasignaciones.GroupBy(r => r.CasoId).Count(g => g.Count() > 1),
            motivos, porDesactivacion,
            asignaciones.Select(a => a.SerenoId).Distinct().Count(),
            retirosPorSereno.Count,
            retirosPorSereno.DefaultIfEmpty(0).Max(),
            esperas.Count, Estadistica.Percentil(esperas, 0.5), Estadistica.Percentil(esperas, 0.9),
            tramos, retirosPorSereno);
    }

    // Los tramos del periodo hasta ahora (lo que aún no pasó no es una columna en cero). Se cuentan en hora local: un día empieza a
    // la medianoche de la central.
    public static IReadOnlyList<(string Etiqueta, DateTimeOffset Desde, DateTimeOffset Hasta)> Tramos(PeriodoAnalitica periodo, DateTimeOffset ahora)
    {
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora;
        if (fin <= periodo.Desde)
            return [];
        var cultura = System.Globalization.CultureInfo.GetCultureInfo("es-PE");
        var largo = (periodo.Hasta - periodo.Desde).TotalDays;
        (TimeSpan Paso, Func<DateTime, DateTime, string> Etiqueta) forma =
            largo <= 1.01 ? (TimeSpan.FromHours(4), (d, h) => $"{d:%H}–{(h.Hour == 0 ? 24 : h.Hour)} h")
            : largo <= 10 ? (TimeSpan.FromDays(1), (d, _) => d.ToString("ddd d", cultura).Replace(".", ""))
            : largo <= 70 ? (TimeSpan.FromDays(7), (d, _) => d.ToString("d MMM", cultura).Replace(".", ""))
            : largo <= 140 ? (TimeSpan.FromDays(14), (d, _) => d.ToString("d MMM", cultura).Replace(".", ""))
            : (TimeSpan.FromDays(30), (d, _) => d.ToString("MMM yyyy", cultura).Replace(".", ""));
        var tramos = new List<(string, DateTimeOffset, DateTimeOffset)>();
        for (var desde = periodo.Desde; desde < fin; desde += forma.Paso)
        {
            var hasta = desde + forma.Paso < periodo.Hasta ? desde + forma.Paso : periodo.Hasta;
            var local = TimeZoneInfo.ConvertTime(desde, periodo.Zona).DateTime;
            tramos.Add((forma.Etiqueta(local, TimeZoneInfo.ConvertTime(hasta, periodo.Zona).DateTime), desde, hasta));
        }
        return tramos;
    }
}
