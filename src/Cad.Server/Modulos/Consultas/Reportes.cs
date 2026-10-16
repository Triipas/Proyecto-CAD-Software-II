using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// E5 (decisión del PO del 14/10/2026): la página «Reportes» del panel muestra los reportes recibidos y en revisión.
public sealed record ReporteEnBandeja(
    Guid Id,
    string Estado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    string Categoria,
    DateTimeOffset OcurridoAproximadamenteEn,
    string FuenteUbicacion);

public sealed record CorreccionVista(string Anterior, string Nueva, string Operador, DateTimeOffset En);

public sealed record DetalleReporteVista(
    Guid Id,
    EstadoCaso Estado,
    string TextoEstado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    string Descripcion,
    DateTimeOffset OcurridoAproximadamenteEn,
    string FuenteUbicacion,
    double? LatitudHecho,
    double? LongitudHecho,
    double? PrecisionHechoMetros,
    string? ReferenciaHecho,
    Guid CategoriaActualId,
    string CategoriaActual,
    string CategoriaOriginal,
    string? RevisionIniciadaPor,
    DateTimeOffset? RevisionIniciadaEn,
    IReadOnlyList<CorreccionVista> Correcciones)
{
    public bool PuedeIniciarRevision => Estado == EstadoCaso.Recibido;
    public bool PuedeCorregirCategoria => Estado == EstadoCaso.EnRevision;
    public bool ConPunto => LatitudHecho is not null && LongitudHecho is not null;
}

public sealed class ConsultarReportes(CadDbContext db)
{
    // Los más recientes primero, como el listado de casos (DP17).
    public async Task<IReadOnlyList<ReporteEnBandeja>> EjecutarAsync(CancellationToken ct)
    {
        var filas = await (from caso in db.Casos.AsNoTracking()
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                           join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id
                           where caso.Origen == OrigenCaso.Reporte && (caso.Estado == EstadoCaso.Recibido || caso.Estado == EstadoCaso.EnRevision)
                           orderby caso.RegistradoEn descending
                           select new
                           {
                               caso.Id, caso.Estado, caso.RegistradoEn, Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos,
                               Categoria = categoria.Nombre, detalle.OcurridoAproximadamenteEn, detalle.FuenteUbicacion,
                           }).ToListAsync(ct);
        return filas.Select(f => new ReporteEnBandeja(f.Id, EstadosCaso.Texto(f.Estado), f.RegistradoEn, f.Ciudadano, f.Categoria,
            f.OcurridoAproximadamenteEn, f.FuenteUbicacion)).ToList();
    }
}

public sealed class ConsultarDetalleReporte(CadDbContext db)
{
    public async Task<DetalleReporteVista?> EjecutarAsync(Guid casoId, CancellationToken ct)
    {
        var fila = await (from caso in db.Casos.AsNoTracking()
                          join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                          join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                          where caso.Id == casoId && caso.Origen == OrigenCaso.Reporte
                          select new { caso, detalle, Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos })
            .SingleOrDefaultAsync(ct);
        if (fila is null)
            return null;

        var categorias = await db.CategoriasIncidencia.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var revision = await (from evento in db.EventosCaso.AsNoTracking()
                              join operador in db.Users.AsNoTracking() on evento.ActorId equals operador.Id
                              where evento.CasoId == casoId && evento.Tipo == TipoEventoCaso.RevisionIniciada
                              select new { Operador = operador.Nombres + " " + operador.Apellidos, evento.OcurridoEn })
            .FirstOrDefaultAsync(ct);
        var correcciones = (await (from correccion in db.CorreccionesCategoria.AsNoTracking()
                                   join operador in db.Users.AsNoTracking() on correccion.OperadorId equals operador.Id
                                   where correccion.CasoId == casoId
                                   select new { correccion, Operador = operador.Nombres + " " + operador.Apellidos })
                .ToListAsync(ct))
            .OrderBy(c => c.correccion.CorregidaEn)
            .Select(c => new CorreccionVista(categorias[c.correccion.CategoriaAnteriorId], categorias[c.correccion.CategoriaNuevaId], c.Operador, c.correccion.CorregidaEn))
            .ToList();

        var (c, d) = (fila.caso, fila.detalle);
        return new DetalleReporteVista(c.Id, c.Estado, EstadosCaso.Texto(c.Estado), c.RegistradoEn, fila.Ciudadano, d.Descripcion,
            d.OcurridoAproximadamenteEn, d.FuenteUbicacion, d.LatitudHecho, d.LongitudHecho, d.PrecisionHechoMetros,
            d.ReferenciaHecho, c.CategoriaId!.Value, categorias[c.CategoriaId!.Value], categorias[d.CategoriaOriginalId],
            revision?.Operador, revision?.OcurridoEn, correcciones);
    }
}
