using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// Bloque B, propuesta 8 — tiempo de respuesta por zona (27/10/2026): dónde tardamos más, no solo dónde pasan más cosas. Con
// las marcas que ya usa HU28, medidas desde el registro del caso: despacho (primera asignación) y respuesta (primer inicio de
// atención). En minutos, con p50 y p90 en lugar del promedio. Por defecto, los casos que se atienden como un SOS (DP19); con
// el origen «Reporte», los reportes (su ubicación es la del hecho).
public sealed record TiemposZona(int Casos, int Despachados, int Atendidos,
    double? DespachoP50, double? DespachoP90, double? RespuestaP50, double? RespuestaP90);

public sealed record ZonaRespuesta(double Sur, double Oeste, double Norte, double Este, TiemposZona Tiempos);

// Agrupadas: las zonas con menos casos que el mínimo de privacidad, juntas (sin límites). Total: todo el periodo.
public sealed record RespuestaPorZona(IReadOnlyList<ZonaRespuesta> Zonas, TiemposZona? Agrupadas, int ZonasAgrupadas, TiemposZona Total, int SinUbicacion);

public sealed class ConsultarRespuestaPorZona(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    // Un caso cancelado por su titular o descartado por la central no se atendió: sus tiempos no son de respuesta.
    private static readonly EstadoCaso[] Excluidos = [EstadoCaso.Cancelada, EstadoCaso.Descartada];

    public async Task<RespuestaPorZona> EjecutarAsync(PeriodoAnalitica periodo, bool reportes, CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora;
        var casos = (await CasosAnalitica.LeerAsync(db, periodo.Desde, fin, ct))
            .Where(c => !Excluidos.Contains(c.Estado) && (reportes ? c.Origen == OrigenCaso.Reporte : c.Origen != OrigenCaso.Reporte))
            .ToList();

        var ids = casos.Select(c => c.Id).ToList();
        var marcas = await db.EventosCaso.AsNoTracking()
            .Where(e => ids.Contains(e.CasoId) && (e.Tipo == TipoEventoCaso.CasoAsignado || e.Tipo == TipoEventoCaso.AtencionIniciada))
            .GroupBy(e => new { e.CasoId, e.Tipo })
            .Select(g => new { g.Key.CasoId, g.Key.Tipo, Primera = g.Min(e => e.OcurridoEn) })
            .ToListAsync(ct);
        var despacho = marcas.Where(m => m.Tipo == TipoEventoCaso.CasoAsignado).ToDictionary(m => m.CasoId, m => m.Primera);
        var atencion = marcas.Where(m => m.Tipo == TipoEventoCaso.AtencionIniciada).ToDictionary(m => m.CasoId, m => m.Primera);

        TiemposZona Tiempos(IReadOnlyCollection<CasosAnalitica.Fila> grupo)
        {
            var minutosDespacho = grupo.Where(c => despacho.ContainsKey(c.Id)).Select(c => (despacho[c.Id] - c.RegistradoEn).TotalMinutes).ToList();
            var minutosRespuesta = grupo.Where(c => atencion.ContainsKey(c.Id)).Select(c => (atencion[c.Id] - c.RegistradoEn).TotalMinutes).ToList();
            return new TiemposZona(grupo.Count, minutosDespacho.Count, minutosRespuesta.Count,
                Estadistica.Percentil(minutosDespacho, 0.5), Estadistica.Percentil(minutosDespacho, 0.9),
                Estadistica.Percentil(minutosRespuesta, 0.5), Estadistica.Percentil(minutosRespuesta, 0.9));
        }

        var porZona = casos.Where(c => c.Zona is not null).GroupBy(c => c.Zona!).ToList();
        var visibles = porZona.Where(g => g.Count() >= o.MinimoCasosPorZona)
            .Select(g => new ZonaRespuesta(g.Key.Sur, g.Key.Oeste, g.Key.Norte, g.Key.Este, Tiempos(g.ToList())))
            .OrderByDescending(z => z.Tiempos.RespuestaP90 ?? double.MinValue) // primero donde peor es lo normal
            .ToList();
        var agrupadas = porZona.Where(g => g.Count() < o.MinimoCasosPorZona).ToList();
        return new RespuestaPorZona(visibles,
            agrupadas.Count == 0 ? null : Tiempos(agrupadas.SelectMany(g => g).ToList()),
            agrupadas.Count, Tiempos(casos), casos.Count(c => c.Zona is null));
    }
}
