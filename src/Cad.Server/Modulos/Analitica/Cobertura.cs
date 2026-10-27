using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// Bloque B, propuesta 6 — zonas sin cobertura (27/10/2026). Para cada zona con incidencias en el periodo: en qué parte del
// tiempo hubo al menos un sereno disponible a RadioCoberturaMetros o menos de su centro. Se revisa cada MuestreoMinutos con
// la última posición de cada sereno disponible (si no es más antigua que AntiguedadPosicionMinutos). Es una aproximación en
// línea recta: no considera calles ni tráfico.
public sealed record ZonaCobertura(double Sur, double Oeste, double Norte, double Este, int Casos, double PorcentajeCubierto, double HorasSinCobertura);

// PorcentajeConAlguno: parte del tiempo en que al menos un sereno disponible tenía ubicación conocida, en cualquier lugar.
// ZonasAgrupadas y CasosAgrupados: zonas con menos casos que el mínimo de privacidad, que no se muestran.
public sealed record Cobertura(IReadOnlyList<ZonaCobertura> Zonas, double PorcentajeConAlguno, int Muestras, double RadioMetros,
    int ZonasAgrupadas, int CasosAgrupados);

public sealed class ConsultarCobertura(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    // Las incidencias del mapa (como HU29): sin los casos cancelados ni descartados.
    private static readonly EstadoCaso[] Excluidos = Consultas.ConsultarConcentracion.EstadosExcluidos;

    public async Task<Cobertura> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora;

        var casosPorZona = (await CasosAnalitica.LeerAsync(db, periodo.Desde, fin, ct))
            .Where(c => !Excluidos.Contains(c.Estado) && c.Zona is not null)
            .GroupBy(c => c.Zona!)
            .ToDictionary(g => g.Key, g => g.Count());
        var visibles = casosPorZona.Where(z => z.Value >= o.MinimoCasosPorZona).ToList();
        var agrupadas = casosPorZona.Where(z => z.Value < o.MinimoCasosPorZona).ToList();

        var serenos = await LineaDeTiempoSerenos.CargarAsync(db, periodo.Desde, fin, ahora, ct);
        var posiciones = await PosicionesAsync(periodo.Desde.AddMinutes(-o.AntiguedadPosicionMinutos), fin, ct);

        var paso = TimeSpan.FromMinutes(Math.Max(1, o.MuestreoMinutos));
        var antiguedad = TimeSpan.FromMinutes(o.AntiguedadPosicionMinutos);
        var cubiertas = visibles.ToDictionary(z => z.Key, _ => 0);
        var muestras = 0;
        var conAlguno = 0;
        for (var momento = periodo.Desde; momento < fin; momento += paso)
        {
            muestras++;
            // Dónde estaba cada sereno disponible en este momento (su última posición reciente).
            var ubicados = serenos.Serenos
                .Where(s => serenos.EstaDisponible(s, momento))
                .Select(s => posiciones.TryGetValue(s, out var lista) ? Ultima(lista, momento, antiguedad) : null)
                .OfType<PosicionMuestra>()
                .ToList();
            if (ubicados.Count > 0)
                conAlguno++;
            foreach (var zona in cubiertas.Keys.ToList())
                if (ubicados.Any(p => Estadistica.Metros(zona.LatitudCentro, zona.LongitudCentro, p.Latitud, p.Longitud) <= o.RadioCoberturaMetros))
                    cubiertas[zona]++;
        }

        var horasPorMuestra = paso.TotalHours;
        var zonas = visibles
            .Select(z => new ZonaCobertura(z.Key.Sur, z.Key.Oeste, z.Key.Norte, z.Key.Este, z.Value,
                muestras == 0 ? 0 : 100.0 * cubiertas[z.Key] / muestras,
                (muestras - cubiertas[z.Key]) * horasPorMuestra))
            .OrderBy(z => z.PorcentajeCubierto).ThenByDescending(z => z.Casos) // primero las menos cubiertas
            .ToList();
        return new Cobertura(zonas, muestras == 0 ? 0 : 100.0 * conAlguno / muestras, muestras, o.RadioCoberturaMetros,
            agrupadas.Count, agrupadas.Sum(z => z.Value));
    }

    // Una posición por sereno y minuto (la última de cada minuto): basta para muestrear cada pocos minutos y evita leer
    // todas las posiciones (una cada 15 s por sereno operativo, DP15).
    private async Task<Dictionary<Guid, List<PosicionMuestra>>> PosicionesAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var filas = await db.Database.SqlQuery<PosicionMuestra>($"""
            SELECT DISTINCT ON ("SerenoId", date_trunc('minute', "ObtenidaEn"))
                   "SerenoId", "ObtenidaEn", "Latitud", "Longitud"
            FROM "PosicionesSereno"
            WHERE "ObtenidaEn" >= {desde} AND "ObtenidaEn" < {hasta}
            ORDER BY "SerenoId", date_trunc('minute', "ObtenidaEn"), "ObtenidaEn" DESC
            """).ToListAsync(ct);
        return filas.GroupBy(f => f.SerenoId).ToDictionary(g => g.Key, g => g.OrderBy(f => f.ObtenidaEn).ToList());
    }

    // La última posición obtenida hasta el momento, si no es más antigua que el límite (búsqueda binaria: la lista está ordenada).
    private static PosicionMuestra? Ultima(List<PosicionMuestra> lista, DateTimeOffset momento, TimeSpan antiguedad)
    {
        int bajo = 0, alto = lista.Count - 1, encontrada = -1;
        while (bajo <= alto)
        {
            var medio = (bajo + alto) / 2;
            if (lista[medio].ObtenidaEn <= momento) { encontrada = medio; bajo = medio + 1; }
            else alto = medio - 1;
        }
        return encontrada >= 0 && momento - lista[encontrada].ObtenidaEn <= antiguedad ? lista[encontrada] : null;
    }
}

// Fila de la consulta de posiciones (sin datos del sereno más que su identificador interno).
public sealed class PosicionMuestra
{
    public Guid SerenoId { get; set; }
    public DateTimeOffset ObtenidaEn { get; set; }
    public double Latitud { get; set; }
    public double Longitud { get; set; }
}
