using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// Bloque C, propuesta 9 — puntos críticos reincidentes (27/10/2026): lugares con MinimoCasosPuntoCritico o más incidencias a
// RadioPuntoCriticoMetros o menos unas de otras en el periodo (por defecto, los últimos 30 días), para el patrullaje
// preventivo. Agrupación codiciosa: se toma el caso con más vecinos, se forma su punto con ellos y se repite con el resto.
// El punto es el centro de sus casos (nunca el de uno solo).
public sealed record PuntoCritico(double Latitud, double Longitud, int Casos, int Sos, int Reportes, string? CategoriaFrecuente,
    DateTimeOffset UltimoCaso);

public sealed class ConsultarPuntosCriticos(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    public async Task<IReadOnlyList<PuntoCritico>> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var casos = (await CasosAnalitica.LeerAsync(db, periodo.Desde, periodo.Hasta < ahora ? periodo.Hasta : ahora, ct))
            .Where(c => CasosAnalitica.EsIncidencia(c) && c.Latitud is not null)
            .ToList();
        var categorias = await Categorias.NombresAsync(db, ct);

        // Los vecinos de cada caso se calculan una vez; en cada vuelta solo cuentan los que siguen sin punto.
        var vecinos = casos.ToDictionary(c => c, c => casos.Where(v => Estadistica.Metros(c.Latitud!.Value, c.Longitud!.Value,
            v.Latitud!.Value, v.Longitud!.Value) <= o.RadioPuntoCriticoMetros).ToList());
        var pendientes = new HashSet<CasosAnalitica.Fila>(casos);
        var puntos = new List<PuntoCritico>();
        while (true)
        {
            var mejor = pendientes
                .Select(c => (Caso: c, Vecinos: vecinos[c].Where(pendientes.Contains).ToList()))
                .OrderByDescending(x => x.Vecinos.Count).ThenBy(x => x.Caso.RegistradoEn)
                .FirstOrDefault();
            if (mejor.Vecinos is null || mejor.Vecinos.Count < o.MinimoCasosPuntoCritico)
                break;
            var grupo = mejor.Vecinos;
            pendientes.ExceptWith(grupo);
            var frecuente = grupo.Where(c => c.CategoriaId is not null).GroupBy(c => c.CategoriaId!.Value)
                .OrderByDescending(g => g.Count()).Select(g => categorias.GetValueOrDefault(g.Key)).FirstOrDefault();
            puntos.Add(new PuntoCritico(grupo.Average(c => c.Latitud!.Value), grupo.Average(c => c.Longitud!.Value), grupo.Count,
                grupo.Count(c => c.Origen != Casos.Dominio.OrigenCaso.Reporte), grupo.Count(c => c.Origen == Casos.Dominio.OrigenCaso.Reporte),
                frecuente, grupo.Max(c => c.RegistradoEn)));
        }
        return puntos;
    }
}

// Bloque C, propuesta 10 — zonas en alza (27/10/2026): incidencias de la última semana frente al promedio semanal de las cuatro
// anteriores, por zona y categoría (y por zona con todas las categorías: Categoria nula). Es alerta si la semana tiene al menos
// MinimoCasosPorZona casos y el aumento se sale de lo normal: (actual − promedio) / √promedio ≥ UmbralAlza, como en un conteo de
// Poisson (con un promedio menor que 1 se usa 1, para no alarmar por uno o dos casos en una zona tranquila).
public sealed record ZonaEnAlza(double Sur, double Oeste, double Norte, double Este, string? Categoria, int SemanaActual,
    double PromedioAnterior, double Desviacion, bool Alerta);

public sealed class ConsultarZonasEnAlza(CadDbContext db, IOptions<OpcionesAnalitica> opciones)
{
    public const int SemanasBase = 4;

    // hasta: fin de la semana analizada (excluido); la semana es [hasta − 7 días, hasta).
    public async Task<IReadOnlyList<ZonaEnAlza>> EjecutarAsync(DateTimeOffset hasta, CancellationToken ct)
    {
        var o = opciones.Value;
        var inicioSemana = hasta.AddDays(-7);
        var inicioBase = inicioSemana.AddDays(-7 * SemanasBase);
        var casos = (await CasosAnalitica.LeerAsync(db, inicioBase, hasta, ct))
            .Where(c => CasosAnalitica.EsIncidencia(c) && c.Zona is not null)
            .ToList();
        var categorias = await Categorias.NombresAsync(db, ct);

        ZonaEnAlza Comparar(Zona zona, Guid? categoria, List<CasosAnalitica.Fila> grupo)
        {
            var actual = grupo.Count(c => c.RegistradoEn >= inicioSemana);
            var promedio = grupo.Count(c => c.RegistradoEn < inicioSemana) / (double)SemanasBase;
            var desviacion = (actual - promedio) / Math.Sqrt(Math.Max(promedio, 1));
            return new ZonaEnAlza(zona.Sur, zona.Oeste, zona.Norte, zona.Este,
                categoria is { } id ? categorias.GetValueOrDefault(id, Categorias.SinClasificar) : null,
                actual, promedio, desviacion, actual >= o.MinimoCasosPorZona && desviacion >= o.UmbralAlza);
        }

        var porZona = casos.GroupBy(c => c.Zona!).Select(g => Comparar(g.Key, null, g.ToList()));
        var porCategoria = casos.GroupBy(c => (Zona: c.Zona!, Categoria: c.CategoriaId ?? Categorias.IdSinClasificar))
            .Select(g => Comparar(g.Key.Zona, g.Key.Categoria, g.ToList()));
        // Solo lo que subió; primero lo que más se sale de lo normal. Una zona con pocos casos en la semana no se muestra.
        return porZona.Concat(porCategoria)
            .Where(z => z.SemanaActual >= o.MinimoCasosPorZona && z.SemanaActual > z.PromedioAnterior)
            .OrderByDescending(z => z.Alerta).ThenByDescending(z => z.Desviacion)
            .ToList();
    }
}

internal static class Categorias
{
    // Un caso sin categoría (un SOS aún sin clasificar) se cuenta aparte.
    public static readonly Guid IdSinClasificar = Guid.Empty;
    public const string SinClasificar = "Sin clasificar";

    public static async Task<Dictionary<Guid, string>> NombresAsync(CadDbContext db, CancellationToken ct) =>
        await db.CategoriasIncidencia.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
}
