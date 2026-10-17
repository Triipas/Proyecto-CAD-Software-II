using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU29 — mapa de concentración de incidencias (S5-T13). Periodo por fecha de registro (DP17) y origen.
public sealed record FiltroConcentracion(DateTimeOffset Desde, DateTimeOffset Hasta, OrigenCaso? Origen);

// Una celda de la cuadrícula con los casos de un origen; los límites, en grados. No lleva datos de ningún caso concreto.
public sealed record CeldaConcentracion(OrigenCaso Origen, double Sur, double Oeste, double Norte, double Este, int Casos);

public sealed record MapaConcentracion(IReadOnlyList<CeldaConcentracion> Celdas, int Sos, int Reportes, int SinUbicacion)
{
    public int MaximoPorCelda => Celdas.Count == 0 ? 0 : Celdas.Max(c => c.Casos);
}

public sealed class ConsultarConcentracion(CadDbContext db)
{
    // Tamaño de la celda: 0,005° (unos 550 m en Lima). Es una resolución de presentación, no una regla de negocio: agrupa
    // casos cercanos y evita mostrar el punto exacto de cada uno.
    public const double TamanoCelda = 0.005;

    // CA03 (y HU17-CA04): los casos cancelados y los reportes descartados no son incidencias del mapa.
    public static readonly EstadoCaso[] EstadosExcluidos = [EstadoCaso.Cancelada, EstadoCaso.Descartada];

    public async Task<MapaConcentracion> EjecutarAsync(FiltroConcentracion filtro, CancellationToken ct)
    {
        // CA01 y CA02: periodo y origen seleccionados. CA05: solo casos (nunca posiciones de acompañamiento ni de serenos).
        var casos = db.Casos.AsNoTracking()
            .Where(c => c.RegistradoEn >= filtro.Desde && c.RegistradoEn < filtro.Hasta && !EstadosExcluidos.Contains(c.Estado));
        // HU29 representa SOS y reportes (CA04); un caso urgente creado desde un aviso no entra (es una decisión del operador).
        casos = casos.Where(c => c.Origen == OrigenCaso.Sos || c.Origen == OrigenCaso.Reporte);
        if (filtro.Origen is { } origen)
            casos = casos.Where(c => c.Origen == origen);

        // CA02: ubicación válida. SOS: la del dispositivo (HU08); reporte: el punto del hecho (DP10), no una referencia en texto.
        var filas = await (from caso in casos
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId into detalles
                           from detalle in detalles.DefaultIfEmpty()
                           select new
                           {
                               caso.Origen,
                               Latitud = caso.Origen == OrigenCaso.Sos ? caso.Latitud : (detalle == null ? null : detalle.LatitudHecho),
                               Longitud = caso.Origen == OrigenCaso.Sos ? caso.Longitud : (detalle == null ? null : detalle.LongitudHecho),
                           })
                          .ToListAsync(ct);

        var conUbicacion = filas.Where(f => f.Latitud is not null && f.Longitud is not null).ToList();
        // CA04: una celda por origen, para diferenciar SOS y reportes.
        var celdas = conUbicacion
            .GroupBy(f => (f.Origen, Fila: Indice(f.Latitud!.Value), Columna: Indice(f.Longitud!.Value)))
            .Select(g => new CeldaConcentracion(g.Key.Origen,
                g.Key.Fila * TamanoCelda, g.Key.Columna * TamanoCelda, (g.Key.Fila + 1) * TamanoCelda, (g.Key.Columna + 1) * TamanoCelda, g.Count()))
            .OrderBy(c => c.Origen).ThenByDescending(c => c.Casos)
            .ToList();
        return new MapaConcentracion(celdas,
            conUbicacion.Count(f => f.Origen == OrigenCaso.Sos), conUbicacion.Count(f => f.Origen == OrigenCaso.Reporte),
            filas.Count - conUbicacion.Count);
    }

    private static long Indice(double grados) => (long)Math.Floor(grados / TamanoCelda);
}
