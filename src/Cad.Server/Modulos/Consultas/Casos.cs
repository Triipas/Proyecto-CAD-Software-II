using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU26 — Buscar y filtrar casos (S3-T03, S3-T04). Solo lectura; la autorización (solo operador, CA06) la aplica la página.
// Todos los filtros son opcionales y se combinan (CA01). DP17: las fechas se aplican a la fecha de registro; los más
// recientes primero, en páginas de 50.
public sealed record FiltroCasos(
    DateTimeOffset? Desde = null,
    DateTimeOffset? Hasta = null,
    OrigenCaso? Origen = null,
    // Categoría vigente del catálogo, o SinTipo para «Tipo por determinar» (CA02).
    Guid? CategoriaId = null,
    bool SinTipo = false,
    EstadoCaso? Estado = null,
    // Un caso «tiene» al sereno si estuvo asignado a él, también si la asignación ya terminó (casos cerrados).
    Guid? SerenoId = null);

public sealed record CasoEncontrado(
    Guid Id,
    OrigenCaso Origen,
    string Estado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    // null = «Tipo por determinar» (CA02).
    string? Tipo,
    // Último sereno asignado, si lo hubo.
    string? Sereno,
    // SOS: ubicación del dispositivo; reporte: punto del hecho. Sin coordenadas inventadas (CA05).
    double? Latitud,
    double? Longitud)
{
    public const string TipoPorDeterminar = "Tipo por determinar";
    public bool ConUbicacion => Latitud is not null && Longitud is not null;
    public string TextoOrigen => Origen == OrigenCaso.Sos ? "SOS" : "Reporte";
    public string TextoTipo => Tipo ?? TipoPorDeterminar;
}

public sealed record PaginaCasos(IReadOnlyList<CasoEncontrado> Casos, int Total, int Pagina, int TamanoPagina)
{
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina));
}

public sealed record SerenoParaFiltro(Guid Id, string Nombre);

public sealed class ConsultarCasos(CadDbContext db)
{
    public const int TamanoPagina = 50; // DP17

    private static readonly Guid RolSereno = Roles.Todos.Single(r => r.Nombre == Roles.Sereno).Id;

    public async Task<PaginaCasos> EjecutarAsync(FiltroCasos filtro, int pagina, CancellationToken ct)
    {
        var casos = db.Casos.AsNoTracking().AsQueryable();
        if (filtro.Desde is { } desde)
            casos = casos.Where(c => c.RegistradoEn >= desde);
        if (filtro.Hasta is { } hasta)
            casos = casos.Where(c => c.RegistradoEn < hasta);
        if (filtro.Origen is { } origen)
            casos = casos.Where(c => c.Origen == origen);
        if (filtro.SinTipo)
            casos = casos.Where(c => c.CategoriaId == null);
        else if (filtro.CategoriaId is { } categoria)
            casos = casos.Where(c => c.CategoriaId == categoria);
        if (filtro.Estado is { } estado)
            casos = casos.Where(c => c.Estado == estado);
        if (filtro.SerenoId is { } sereno)
            casos = casos.Where(c => db.Asignaciones.Any(a => a.CasoId == c.Id && a.SerenoId == sereno));

        var total = await casos.CountAsync(ct);
        pagina = Math.Clamp(pagina, 1, Math.Max(1, (int)Math.Ceiling(total / (double)TamanoPagina)));

        var filas = await (from caso in casos
                           join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id into categorias
                           from categoria in categorias.DefaultIfEmpty()
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId into detalles
                           from detalle in detalles.DefaultIfEmpty()
                           let ultimoSereno = (from asignacion in db.Asignaciones
                                               join usuario in db.Users on asignacion.SerenoId equals usuario.Id
                                               where asignacion.CasoId == caso.Id
                                               orderby asignacion.AsignadaEn descending
                                               select usuario.Nombres + " " + usuario.Apellidos).FirstOrDefault()
                           orderby caso.RegistradoEn descending, caso.Id descending
                           select new
                           {
                               caso.Id, caso.Origen, caso.Estado, caso.RegistradoEn,
                               Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos,
                               Tipo = categoria == null ? null : categoria.Nombre,
                               Sereno = ultimoSereno,
                               Latitud = caso.Origen == OrigenCaso.Sos ? caso.Latitud : (detalle == null ? null : detalle.LatitudHecho),
                               Longitud = caso.Origen == OrigenCaso.Sos ? caso.Longitud : (detalle == null ? null : detalle.LongitudHecho),
                           })
                          .Skip((pagina - 1) * TamanoPagina)
                          .Take(TamanoPagina)
                          .ToListAsync(ct);

        return new PaginaCasos(
            filas.Select(f => new CasoEncontrado(f.Id, f.Origen, EstadosCaso.Texto(f.Estado), f.RegistradoEn, f.Ciudadano, f.Tipo, f.Sereno,
                f.Latitud, f.Longitud)).ToList(),
            total, pagina, TamanoPagina);
    }

    // Serenos para el filtro: todas las cuentas de sereno, también las desactivadas (pueden tener casos anteriores).
    public async Task<IReadOnlyList<SerenoParaFiltro>> SerenosAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == RolSereno))
            .OrderBy(u => u.Nombres).ThenBy(u => u.Apellidos)
            .Select(u => new SerenoParaFiltro(u.Id, u.Nombres + " " + u.Apellidos))
            .ToListAsync(ct);
}
