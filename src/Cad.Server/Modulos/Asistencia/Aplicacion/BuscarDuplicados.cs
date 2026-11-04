using Cad.Calles;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

public sealed record DuplicadoPosible(Guid CasoId, string Categoria, string Descripcion, EstadoCaso Estado, int Metros, int Minutos,
    DateTimeOffset RegistradoEn);

// HU47 (S6-T10) — candidatos a duplicado, sin IA: otros reportes no cancelados ni descartados registrados a ≤ 150 m y ≤ 2 h, con
// punto en ambos, hasta 5 y los más cercanos primero. No se exige la misma categoría: el ciudadano pudo equivocarse. Se muestran
// aunque la IA esté apagada (CA05); confirmar que describen el mismo hecho le corresponde al modelo.
public sealed class BuscarDuplicados(CadDbContext db, IOptions<OpcionesIa> opciones)
{
    private const double MetrosPorGrado = 111_320;

    public async Task<IReadOnlyList<DuplicadoPosible>> EjecutarAsync(Guid casoId, CancellationToken ct)
    {
        var limites = opciones.Value.Duplicados;
        var propio = await (from caso in db.Casos.AsNoTracking()
                            join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                            where caso.Id == casoId
                            select new { caso.RegistradoEn, detalle.LatitudHecho, detalle.LongitudHecho })
            .SingleOrDefaultAsync(ct);
        if (propio is not { LatitudHecho: { } latitud, LongitudHecho: { } longitud })
            return [];

        // Primero un recuadro en grados (usa índices y descarta casi todo); la distancia real se calcula después.
        var dLat = limites.Metros / MetrosPorGrado;
        var dLon = limites.Metros / (MetrosPorGrado * Math.Max(0.1, Math.Cos(latitud * Math.PI / 180)));
        var desde = propio.RegistradoEn.AddMinutes(-limites.Minutos);
        var hasta = propio.RegistradoEn.AddMinutes(limites.Minutos);
        var filas = await (from caso in db.Casos.AsNoTracking()
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id
                           where caso.Id != casoId && caso.Origen == OrigenCaso.Reporte
                                 && caso.Estado != EstadoCaso.Cancelada && caso.Estado != EstadoCaso.Descartada
                                 && caso.RegistradoEn >= desde && caso.RegistradoEn <= hasta
                                 && detalle.LatitudHecho != null && detalle.LongitudHecho != null
                                 && detalle.LatitudHecho >= latitud - dLat && detalle.LatitudHecho <= latitud + dLat
                                 && detalle.LongitudHecho >= longitud - dLon && detalle.LongitudHecho <= longitud + dLon
                           select new { caso.Id, Categoria = categoria.Nombre, detalle.Descripcion, caso.Estado, caso.RegistradoEn,
                               Latitud = detalle.LatitudHecho!.Value, Longitud = detalle.LongitudHecho!.Value })
            .ToListAsync(ct);

        return filas
            .Select(f => new DuplicadoPosible(f.Id, f.Categoria, f.Descripcion, f.Estado,
                (int)Math.Round(Geo.DistanciaMetros(latitud, longitud, f.Latitud, f.Longitud)),
                (int)Math.Round(Math.Abs((f.RegistradoEn - propio.RegistradoEn).TotalMinutes)), f.RegistradoEn))
            .Where(d => d.Metros <= limites.Metros)
            .OrderBy(d => d.Metros).ThenBy(d => d.Minutos)
            .Take(limites.Candidatos)
            .ToList();
    }
}
