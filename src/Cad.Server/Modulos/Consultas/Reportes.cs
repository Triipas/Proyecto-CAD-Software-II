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

// HU15–HU17: la decisión registrada sobre el reporte.
public sealed record DecisionVista(TipoDecisionReporte Tipo, string Motivo, string? Destino, string? Accion, string Operador, DateTimeOffset En,
    string? MotivoCodigo = null, string? Prioridad = null, string? Heridos = null)
{
    // 27/10/2026: los textos de los catálogos (motivo del descarte; prioridad y heridos de la admisión). Nulos si la decisión
    // es anterior a los catálogos.
    public string? TextoMotivoDescarte => Cad.Contratos.Casos.Catalogo.Texto(Cad.Contratos.Casos.MotivosDescarte.Todos, MotivoCodigo);
    public string? TextoPrioridad => Cad.Contratos.Casos.Catalogo.Texto(Cad.Contratos.Casos.PrioridadesCaso.Todas, Prioridad);
    public string? TextoHeridos => Cad.Contratos.Casos.Catalogo.Texto(Cad.Contratos.Casos.PersonasHeridas.Todas, Heridos);

    public string Texto => Tipo switch
    {
        TipoDecisionReporte.Admision => "Admitido para atención",
        TipoDecisionReporte.Derivacion => "Derivado",
        _ => "Descartado",
    };
}

// HU15-CA02 y HU20-CA07: reporte admitido, pendiente de asignación en la central. La ubicación es la del hecho.
public sealed record ReporteAdmitido(
    Guid Id,
    DateTimeOffset RegistradoEn,
    DateTimeOffset AdmitidoEn,
    string Ciudadano,
    string Categoria,
    double? Latitud,
    double? Longitud,
    string? Referencia,
    // Registrado por la central (23/10/2026): su lugar no lo indicó ningún ciudadano.
    bool DeLaCentral = false)
{
    public bool ConPunto => Latitud is not null && Longitud is not null;
}

public sealed record RetiroVista(string Motivo, DateTimeOffset En);

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
    IReadOnlyList<CorreccionVista> Correcciones,
    DecisionVista? Decision = null,
    // Reporte registrado por la central (23/10/2026): quién avisó y su teléfono, si los dio.
    bool DeLaCentral = false,
    string? ContactoNombre = null,
    string? ContactoTelefono = null,
    // DP08 ampliada (26/10/2026): cuántas fotos adjuntó el ciudadano (se piden una por una, solo para el operador).
    int Fotos = 0,
    // Decisión del PO del 26/10/2026: el ciudadano retiró el reporte antes de la decisión, con su motivo.
    RetiroVista? Retiro = null)
{
    public bool PuedeIniciarRevision => Estado == EstadoCaso.Recibido;
    public bool PuedeCorregirCategoria => Estado == EstadoCaso.EnRevision;
    public bool PuedeDecidir => Estado == EstadoCaso.EnRevision;
    // Tras la admisión sigue el flujo de un caso: pendiente de asignación, asignado, en camino, en atención, cerrado.
    public bool EnAtencionOperativa => Estado is EstadoCaso.Pendiente or EstadoCaso.Asignada or EstadoCaso.EnCamino or EstadoCaso.EnAtencion or EstadoCaso.Cerrada;
    public bool ConPunto => LatitudHecho is not null && LongitudHecho is not null;
}

public sealed class ConsultarReportes(CadDbContext db)
{
    // Los más recientes primero, como el listado de casos (DP17).
    public async Task<IReadOnlyList<ReporteEnBandeja>> EjecutarAsync(CancellationToken ct)
    {
        var filas = await (from caso in db.Casos.AsNoTracking()
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                           from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id
                           where caso.Origen == OrigenCaso.Reporte && (caso.Estado == EstadoCaso.Recibido || caso.Estado == EstadoCaso.EnRevision)
                           orderby caso.RegistradoEn descending
                           select new
                           {
                               caso.Id, caso.Estado, caso.RegistradoEn, Ciudadano = ciudadano == null ? (detalle.ContactoNombre == null ? AutorReporte.DeLaCentral : detalle.ContactoNombre + AutorReporte.SufijoContacto) : ciudadano.Nombres + " " + ciudadano.Apellidos,
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
                          from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                          where caso.Id == casoId && caso.Origen == OrigenCaso.Reporte
                          select new { caso, detalle, Ciudadano = ciudadano == null ? (detalle.ContactoNombre == null ? AutorReporte.DeLaCentral : detalle.ContactoNombre + AutorReporte.SufijoContacto) : ciudadano.Nombres + " " + ciudadano.Apellidos })
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
        var decision = await (from registro in db.DecisionesReporte.AsNoTracking()
                              join operador in db.Users.AsNoTracking() on registro.OperadorId equals operador.Id
                              join caso in db.Casos.AsNoTracking() on registro.CasoId equals caso.Id
                              where registro.CasoId == casoId
                              select new DecisionVista(registro.Tipo, registro.Motivo, registro.Destino, registro.Accion,
                                  operador.Nombres + " " + operador.Apellidos, registro.DecididaEn, registro.MotivoCodigo, caso.Prioridad, caso.Heridos))
            .SingleOrDefaultAsync(ct);

        var fotos = await db.FotosReporte.AsNoTracking().CountAsync(f => f.CasoId == casoId, ct);
        var retiro = await db.CancelacionesSos.AsNoTracking().Where(c => c.CasoId == casoId)
            .Select(c => new RetiroVista(c.Motivo, c.CanceladaEn)).SingleOrDefaultAsync(ct);

        var (c, d) = (fila.caso, fila.detalle);
        return new DetalleReporteVista(c.Id, c.Estado, EstadosCaso.Texto(c.Origen, c.Estado), c.RegistradoEn, fila.Ciudadano, d.Descripcion,
            d.OcurridoAproximadamenteEn, d.FuenteUbicacion, d.LatitudHecho, d.LongitudHecho, d.PrecisionHechoMetros,
            d.ReferenciaHecho, c.CategoriaId!.Value, categorias[c.CategoriaId!.Value], categorias[d.CategoriaOriginalId],
            revision?.Operador, revision?.OcurridoEn, correcciones, decision, d.DeLaCentral, d.ContactoNombre, d.ContactoTelefono, fotos, retiro);
    }
}

// DP08 ampliada: el contenido de una foto del reporte (1–3), o null si no existe. Solo la usa el detalle del panel (operador).
public sealed class ConsultarFotoReporte(CadDbContext db)
{
    public Task<byte[]?> EjecutarAsync(Guid casoId, int numero, CancellationToken ct) =>
        db.FotosReporte.AsNoTracking().Where(f => f.CasoId == casoId && f.Numero == numero).Select(f => f.Contenido).SingleOrDefaultAsync(ct);
}

// Reporte registrado por la central (23/10/2026): no tiene ciudadano; se nombra por quien avisó, si lo dio.
public static class AutorReporte
{
    public const string DeLaCentral = "Registrado por la central";
    public const string SufijoContacto = " (avisó a la central)";
}

// HU15-CA02: los reportes admitidos que esperan asignación, los más recientes primero, como los SOS pendientes.
public sealed class ConsultarReportesAdmitidos(CadDbContext db)
{
    public async Task<IReadOnlyList<ReporteAdmitido>> EjecutarAsync(CancellationToken ct) =>
        await (from caso in db.Casos.AsNoTracking()
               join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
               join decision in db.DecisionesReporte.AsNoTracking() on caso.Id equals decision.CasoId
               from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
               join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id
               where caso.Origen == OrigenCaso.Reporte && caso.Estado == EstadoCaso.Pendiente
               orderby caso.RegistradoEn descending
               select new ReporteAdmitido(caso.Id, caso.RegistradoEn, decision.DecididaEn,
                   ciudadano == null ? (detalle.ContactoNombre == null ? AutorReporte.DeLaCentral : detalle.ContactoNombre + AutorReporte.SufijoContacto) : ciudadano.Nombres + " " + ciudadano.Apellidos,
                   categoria.Nombre, detalle.LatitudHecho, detalle.LongitudHecho, detalle.ReferenciaHecho, detalle.RegistradoPorOperadorId != null))
              .ToListAsync(ct);
}
