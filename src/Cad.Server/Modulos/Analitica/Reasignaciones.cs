using Cad.Contratos.Casos;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// «¿Se reasigna mucho?» (29/10/2026): con qué frecuencia la central le quita un caso a un sereno para darlo a otro, y por qué. Una
// reasignación solo se hace mientras el sereno aún no llega («Asignada» o «En camino»), así que cada una es tiempo que el
// ciudadano esperó de más. Se mide sobre lo que ya guardan Asignaciones y Reasignaciones; no guarda nada.
//
// Como el resto de la analítica, todo es agregado: no sale un sereno ni un operador con nombre. La concentración dice cuántos
// serenos pierden casos y cuántos pierde el que más, no quiénes son.
public sealed record MotivoReasignacionContado(string Codigo, string Texto, int Veces);

public sealed record ResumenReasignaciones(
    int CasosAsignados, int CasosReasignados, int Reasignaciones, int CasosReasignadosVariasVeces,
    IReadOnlyList<MotivoReasignacionContado> Motivos, int RetirosPorDesactivacion,
    int SerenosConAsignacion, int SerenosConRetiros, int MaximoRetirosDeUnSereno,
    int Esperas, double? EsperaP50Minutos, double? EsperaP90Minutos)
{
    // Sobre los casos que se asignaron en el periodo (cada caso una vez, aunque se reasigne varias). Sin casos asignados no hay
    // porcentaje: no un 0 inventado.
    public double? PorcentajeReasignados => CasosAsignados == 0 ? null : 100.0 * CasosReasignados / CasosAsignados;
}

public sealed class ConsultarReasignaciones(CadDbContext db)
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

        var retirosPorSereno = reasignaciones.GroupBy(r => r.SerenoAnteriorId).Select(g => g.Count()).ToList();
        return new ResumenReasignaciones(
            asignaciones.Select(a => a.CasoId).Distinct().Count(),
            reasignaciones.Select(r => r.CasoId).Distinct().Count(),
            reasignaciones.Count,
            reasignaciones.GroupBy(r => r.CasoId).Count(g => g.Count() > 1),
            motivos, porDesactivacion,
            asignaciones.Select(a => a.SerenoId).Distinct().Count(),
            retirosPorSereno.Count,
            retirosPorSereno.DefaultIfEmpty(0).Max(),
            esperas.Count, Estadistica.Percentil(esperas, 0.5), Estadistica.Percentil(esperas, 0.9));
    }
}
