using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Periodo del análisis: desde (incluido) hasta (excluido), como el filtro de HU29. La zona horaria es la de la central: las
// horas del día y los días de la semana se cuentan en hora local.
public sealed record PeriodoAnalitica(DateTimeOffset Desde, DateTimeOffset Hasta, TimeZoneInfo Zona)
{
    public bool Valido => Hasta > Desde;
}

// Una zona es una celda de la cuadrícula del mapa de concentración (HU29): así todos los mapas de la analítica hablan de las
// mismas zonas. Los límites van en grados.
public sealed record Zona(long Fila, long Columna)
{
    public static Zona De(double latitud, double longitud) =>
        new((long)Math.Floor(latitud / Consultas.ConsultarConcentracion.TamanoCelda), (long)Math.Floor(longitud / Consultas.ConsultarConcentracion.TamanoCelda));

    public double Sur => Fila * Consultas.ConsultarConcentracion.TamanoCelda;
    public double Oeste => Columna * Consultas.ConsultarConcentracion.TamanoCelda;
    public double Norte => (Fila + 1) * Consultas.ConsultarConcentracion.TamanoCelda;
    public double Este => (Columna + 1) * Consultas.ConsultarConcentracion.TamanoCelda;
    public double LatitudCentro => (Sur + Norte) / 2;
    public double LongitudCentro => (Oeste + Este) / 2;
}

public static class Estadistica
{
    // Percentil con interpolación lineal entre los dos valores vecinos (el de las hojas de cálculo). Sin valores, nulo.
    // Se usan p50 y p90 en lugar del promedio: un caso muy lento no distorsiona la mediana, y el p90 dice cuánto tarda lo
    // peor del servicio normal.
    public static double? Percentil(IReadOnlyList<double> valores, double p)
    {
        if (valores.Count == 0)
            return null;
        var ordenados = valores.Order().ToArray();
        var posicion = (ordenados.Length - 1) * p;
        var abajo = (int)Math.Floor(posicion);
        var arriba = (int)Math.Ceiling(posicion);
        return ordenados[abajo] + (ordenados[arriba] - ordenados[abajo]) * (posicion - abajo);
    }

    // Distancia sobre la superficie de la Tierra (haversine), en metros.
    public static double Metros(double latitud1, double longitud1, double latitud2, double longitud2)
    {
        const double Radio = 6_371_000;
        static double Rad(double grados) => grados * Math.PI / 180;
        var dLat = Rad(latitud2 - latitud1);
        var dLon = Rad(longitud2 - longitud1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(latitud1)) * Math.Cos(Rad(latitud2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * Radio * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

public readonly record struct Intervalo(DateTimeOffset Desde, DateTimeOffset Hasta)
{
    public double Segundos => (Hasta - Desde).TotalSeconds;
}

// Cuándo estuvo operativo y cuándo disponible cada sereno en un periodo (bloque B). Operativo: según el historial de su estado
// (CambiosEstadoSereno, desde el 27/10/2026). Disponible: operativo y sin una asignación vigente, la misma regla que HU19-CA02.
public sealed class LineaDeTiempoSerenos
{
    private readonly Dictionary<Guid, List<Intervalo>> operativo;
    private readonly Dictionary<Guid, List<Intervalo>> disponible;

    private LineaDeTiempoSerenos(Dictionary<Guid, List<Intervalo>> operativo, Dictionary<Guid, List<Intervalo>> disponible) =>
        (this.operativo, this.disponible) = (operativo, disponible);

    public IEnumerable<Guid> Serenos => operativo.Keys;

    public static async Task<LineaDeTiempoSerenos> CargarAsync(CadDbContext db, DateTimeOffset desde, DateTimeOffset hasta, DateTimeOffset ahora,
        CancellationToken ct)
    {
        // El estado al empezar el periodo es el del último cambio anterior; por eso se leen también los cambios previos.
        var cambios = await db.CambiosEstadoSereno.AsNoTracking()
            .Where(c => c.CambiadoEn < hasta)
            .OrderBy(c => c.SerenoId).ThenBy(c => c.CambiadoEn)
            .Select(c => new { c.SerenoId, c.Operativo, c.CambiadoEn })
            .ToListAsync(ct);
        var asignaciones = await db.Asignaciones.AsNoTracking()
            .Where(a => a.AsignadaEn < hasta && (a.FinalizadaEn == null || a.FinalizadaEn > desde))
            .Select(a => new { a.SerenoId, a.AsignadaEn, a.FinalizadaEn })
            .ToListAsync(ct);

        var fin = hasta < ahora ? hasta : ahora; // el futuro no se cuenta
        var operativo = new Dictionary<Guid, List<Intervalo>>();
        foreach (var grupo in cambios.GroupBy(c => c.SerenoId))
        {
            var intervalos = new List<Intervalo>();
            DateTimeOffset? inicio = null;
            foreach (var cambio in grupo)
            {
                if (cambio.Operativo && inicio is null)
                    inicio = cambio.CambiadoEn;
                else if (!cambio.Operativo && inicio is { } abierto)
                {
                    Agregar(intervalos, abierto, cambio.CambiadoEn, desde, fin);
                    inicio = null;
                }
            }
            if (inicio is { } sinCerrar)
                Agregar(intervalos, sinCerrar, fin, desde, fin);
            operativo[grupo.Key] = intervalos;
        }

        var ocupado = asignaciones.GroupBy(a => a.SerenoId).ToDictionary(g => g.Key,
            g => g.Select(a => new Intervalo(a.AsignadaEn, a.FinalizadaEn ?? ahora)).OrderBy(i => i.Desde).ToList());
        var disponible = operativo.ToDictionary(o => o.Key, o => Restar(o.Value, ocupado.GetValueOrDefault(o.Key) ?? []));
        return new LineaDeTiempoSerenos(operativo, disponible);
    }

    public bool EstaDisponible(Guid sereno, DateTimeOffset momento) =>
        disponible.TryGetValue(sereno, out var intervalos) && intervalos.Any(i => i.Desde <= momento && momento < i.Hasta);

    public double SegundosOperativos(DateTimeOffset desde, DateTimeOffset hasta) => Sumar(operativo, desde, hasta);
    public double SegundosDisponibles(DateTimeOffset desde, DateTimeOffset hasta) => Sumar(disponible, desde, hasta);

    private static double Sumar(Dictionary<Guid, List<Intervalo>> intervalos, DateTimeOffset desde, DateTimeOffset hasta) =>
        intervalos.Values.SelectMany(l => l).Sum(i =>
        {
            var a = i.Desde > desde ? i.Desde : desde;
            var b = i.Hasta < hasta ? i.Hasta : hasta;
            return b > a ? (b - a).TotalSeconds : 0;
        });

    private static void Agregar(List<Intervalo> intervalos, DateTimeOffset a, DateTimeOffset b, DateTimeOffset desde, DateTimeOffset hasta)
    {
        var inicio = a > desde ? a : desde;
        var fin = b < hasta ? b : hasta;
        if (fin > inicio)
            intervalos.Add(new Intervalo(inicio, fin));
    }

    // Lo operativo menos lo ocupado (ambas listas ordenadas por inicio).
    private static List<Intervalo> Restar(List<Intervalo> base_, List<Intervalo> quitar)
    {
        var resultado = new List<Intervalo>();
        foreach (var intervalo in base_)
        {
            var inicio = intervalo.Desde;
            foreach (var q in quitar.Where(q => q.Hasta > intervalo.Desde && q.Desde < intervalo.Hasta))
            {
                if (q.Desde > inicio)
                    resultado.Add(new Intervalo(inicio, q.Desde));
                if (q.Hasta > inicio)
                    inicio = q.Hasta;
            }
            if (intervalo.Hasta > inicio)
                resultado.Add(new Intervalo(inicio, intervalo.Hasta));
        }
        return resultado;
    }
}

// Casos del análisis con su zona, origen y momento de registro. Ubicación: la del dispositivo para un SOS o un caso urgente
// (HU08), el punto del hecho para un reporte (DP10), como en HU29.
internal static class CasosAnalitica
{
    // Las incidencias (como en HU29): sin los casos cancelados por su titular ni los descartados por la central.
    public static bool EsIncidencia(Fila caso) => !Consultas.ConsultarConcentracion.EstadosExcluidos.Contains(caso.Estado);

    public sealed record Fila(Guid Id, OrigenCaso Origen, EstadoCaso Estado, DateTimeOffset RegistradoEn, double? Latitud, double? Longitud,
        Guid? CategoriaId = null)
    {
        public Zona? Zona => Latitud is { } lat && Longitud is { } lon ? Analitica.Zona.De(lat, lon) : null;
    }

    public static Task<List<Fila>> LeerAsync(CadDbContext db, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct) =>
        (from caso in db.Casos.AsNoTracking()
         where caso.RegistradoEn >= desde && caso.RegistradoEn < hasta
         join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId into detalles
         from detalle in detalles.DefaultIfEmpty()
         select new Fila(caso.Id, caso.Origen, caso.Estado, caso.RegistradoEn,
             caso.Origen == OrigenCaso.Reporte ? (detalle == null ? null : detalle.LatitudHecho) : caso.Latitud,
             caso.Origen == OrigenCaso.Reporte ? (detalle == null ? null : detalle.LongitudHecho) : caso.Longitud,
             caso.CategoriaId))
        .ToListAsync(ct);
}
