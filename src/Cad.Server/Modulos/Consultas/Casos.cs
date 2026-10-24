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
    // Tipo del catálogo, o SinTipo para «Tipo por determinar» (CA02). Decisión del PO (24/10/2026): en un caso cerrado
    // cuenta la tipificación final del cierre (lo que realmente pasó); en los demás, la clasificación vigente.
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
    // null = «Tipo por determinar» (CA02). En un caso cerrado, la tipificación final (ver FiltroCasos).
    string? Tipo,
    // Último sereno asignado, si lo hubo.
    string? Sereno,
    // SOS: ubicación del dispositivo; reporte: punto del hecho. Sin coordenadas inventadas (CA05).
    double? Latitud,
    double? Longitud,
    // HU28 (DP18): tiempos del caso como columnas de la búsqueda.
    TiemposCaso Tiempos)
{
    public const string TipoPorDeterminar = "Tipo por determinar";
    public bool ConUbicacion => Latitud is not null && Longitud is not null;
    public string TextoOrigen => OrigenesCaso.Texto(Origen);
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
            casos = casos.Where(c => c.CategoriaId == null && !db.CierresCaso.Any(x => x.CasoId == c.Id));
        else if (filtro.CategoriaId is { } categoria)
            casos = casos.Where(c => (db.CierresCaso.Where(x => x.CasoId == c.Id).Select(x => (Guid?)x.CategoriaId).FirstOrDefault() ?? c.CategoriaId) == categoria);
        if (filtro.Estado is { } estado)
            casos = casos.Where(c => c.Estado == estado);
        if (filtro.SerenoId is { } sereno)
            casos = casos.Where(c => db.Asignaciones.Any(a => a.CasoId == c.Id && a.SerenoId == sereno));

        var total = await casos.CountAsync(ct);
        pagina = Math.Clamp(pagina, 1, Math.Max(1, (int)Math.Ceiling(total / (double)TamanoPagina)));

        var filas = await (from caso in casos
                           from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                           let tipo = db.CierresCaso.Where(x => x.CasoId == caso.Id).Select(x => (Guid?)x.CategoriaId).FirstOrDefault() ?? caso.CategoriaId
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on tipo equals (Guid?)categoria.Id into categorias
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
                               Ciudadano = ciudadano == null
                                   ? (detalle != null && detalle.ContactoNombre != null ? detalle.ContactoNombre + AutorReporte.SufijoContacto : AutorReporte.DeLaCentral)
                                   : ciudadano.Nombres + " " + ciudadano.Apellidos,
                               Tipo = categoria == null ? null : categoria.Nombre,
                               Sereno = ultimoSereno,
                               Latitud = caso.Origen != OrigenCaso.Reporte ? caso.Latitud : (detalle == null ? null : detalle.LatitudHecho),
                               Longitud = caso.Origen != OrigenCaso.Reporte ? caso.Longitud : (detalle == null ? null : detalle.LongitudHecho),
                               DecididoEn = db.DecisionesReporte.Where(d => d.CasoId == caso.Id).Select(d => (DateTimeOffset?)d.DecididaEn).FirstOrDefault(),
                               Decision = db.DecisionesReporte.Where(d => d.CasoId == caso.Id).Select(d => (TipoDecisionReporte?)d.Tipo).FirstOrDefault(),
                               AtencionIniciadaEn = db.EventosCaso.Where(e => e.CasoId == caso.Id && e.Tipo == TipoEventoCaso.AtencionIniciada)
                                   .OrderBy(e => e.OcurridoEn).Select(e => (DateTimeOffset?)e.OcurridoEn).FirstOrDefault(),
                           })
                          .Skip((pagina - 1) * TamanoPagina)
                          .Take(TamanoPagina)
                          .ToListAsync(ct);

        return new PaginaCasos(
            filas.Select(f => new CasoEncontrado(f.Id, f.Origen, EstadosCaso.Texto(f.Estado), f.RegistradoEn, f.Ciudadano, f.Tipo, f.Sereno,
                f.Latitud, f.Longitud, new TiemposCaso(f.Origen, f.RegistradoEn, f.DecididoEn, f.Decision, f.AtencionIniciadaEn))).ToList(),
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
