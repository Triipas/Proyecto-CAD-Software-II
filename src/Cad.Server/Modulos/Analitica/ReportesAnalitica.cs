using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Bloque C, propuesta 11 — demora en reportar (27/10/2026): del momento aproximado del hecho (lo indica el ciudadano, HU10) al
// registro del reporte, por categoría, en horas con p50 y p90, y qué parte se reportó en menos de una hora. Un reporte que se
// hace días después dice que la app no se usa en el momento. Una hora del hecho posterior al registro cuenta como cero.
public sealed record DemoraCategoria(string Categoria, int Reportes, double? HorasP50, double? HorasP90, double PorcentajeEnLaPrimeraHora);

// Bloque C, propuesta 12 — ¿las fotos ayudan? Reportes decididos en el periodo (sin los que registró la central, que entran ya
// admitidos), con y sin fotos: cuántos se admitieron, derivaron o descartaron, y cuántos descartes fueron por falsos (HU17 con
// el motivo del catálogo, desde el 27/10/2026).
public sealed record DecisionesSegunFotos(int Decididos, int Admitidos, int Derivados, int Descartados, int DescartadosPorFalsos)
{
    public double PorcentajeAdmitidos => Decididos == 0 ? 0 : 100.0 * Admitidos / Decididos;
    public double PorcentajeFalsos => Decididos == 0 ? 0 : 100.0 * DescartadosPorFalsos / Decididos;
}

public sealed record ReportesAnalitica(IReadOnlyList<DemoraCategoria> DemoraPorCategoria, DemoraCategoria DemoraTotal,
    DecisionesSegunFotos ConFotos, DecisionesSegunFotos SinFotos);

public sealed class ConsultarReportesAnalitica(CadDbContext db)
{
    public const string Todas = "Todas las categorías";

    public async Task<ReportesAnalitica> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var categorias = await Categorias.NombresAsync(db, ct);

        // Demora: los reportes registrados en el periodo (la categoría vigente, la que dejó la revisión).
        var registrados = await (from caso in db.Casos.AsNoTracking()
                                 join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                                 where caso.Origen == OrigenCaso.Reporte && caso.RegistradoEn >= periodo.Desde && caso.RegistradoEn < periodo.Hasta
                                 select new { caso.CategoriaId, caso.RegistradoEn, detalle.OcurridoAproximadamenteEn })
                                .ToListAsync(ct);
        DemoraCategoria Demora(string nombre, IReadOnlyCollection<double> horas) => new(nombre, horas.Count,
            Estadistica.Percentil([.. horas], 0.5), Estadistica.Percentil([.. horas], 0.9),
            horas.Count == 0 ? 0 : 100.0 * horas.Count(h => h < 1) / horas.Count);
        var conHoras = registrados
            .Select(r => (Categoria: r.CategoriaId is { } id ? categorias.GetValueOrDefault(id, Categorias.SinClasificar) : Categorias.SinClasificar,
                Horas: Math.Max(0, (r.RegistradoEn - r.OcurridoAproximadamenteEn).TotalHours)))
            .ToList();
        var porCategoria = conHoras.GroupBy(r => r.Categoria)
            .Select(g => Demora(g.Key, g.Select(r => r.Horas).ToList()))
            .OrderByDescending(d => d.HorasP50)
            .ToList();

        // Fotos: las decisiones tomadas en el periodo.
        var decisiones = await (from decision in db.DecisionesReporte.AsNoTracking()
                                join detalle in db.DetallesReporte.AsNoTracking() on decision.CasoId equals detalle.CasoId
                                where decision.DecididaEn >= periodo.Desde && decision.DecididaEn < periodo.Hasta && detalle.RegistradoPorOperadorId == null
                                select new { decision.Tipo, decision.MotivoCodigo, ConFotos = db.FotosReporte.Any(f => f.CasoId == decision.CasoId) })
                               .ToListAsync(ct);
        DecisionesSegunFotos Contar(bool conFotos)
        {
            var grupo = decisiones.Where(d => d.ConFotos == conFotos).ToList();
            return new DecisionesSegunFotos(grupo.Count, grupo.Count(d => d.Tipo == TipoDecisionReporte.Admision),
                grupo.Count(d => d.Tipo == TipoDecisionReporte.Derivacion), grupo.Count(d => d.Tipo == TipoDecisionReporte.Descarte),
                grupo.Count(d => d.Tipo == TipoDecisionReporte.Descarte && d.MotivoCodigo == MotivosDescarte.FalsoOMalintencionado));
        }

        return new ReportesAnalitica(porCategoria, Demora(Todas, conHoras.Select(r => r.Horas).ToList()), Contar(true), Contar(false));
    }
}
