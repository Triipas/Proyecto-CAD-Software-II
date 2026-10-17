using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Acompanamiento.Consultas;

// HU42/HU43: un aviso con su regla, su momento, sus datos de origen, su estado y su caso vinculado, si lo tiene.
public sealed record AvisoVista(Guid Id, Guid SesionId, string Ciudadano, ReglaAviso Regla, DateTimeOffset GeneradoEn, DateTimeOffset ReferenciaEn,
    EstadoAviso Estado, string? Responsable, Guid? CasoId, bool CasoCreado, bool SinPosiciones = false)
{
    public string TextoRegla => Regla == ReglaAviso.UbicacionDesactualizada ? "Ubicación desactualizada" : "Confirmación sin respuesta";
    public string TextoTipo => Regla == ReglaAviso.UbicacionDesactualizada ? "Aviso técnico" : "Aviso de verificación";
    public string TextoEstado => Estado switch
    {
        EstadoAviso.Generado => "Sin revisar",
        EstadoAviso.EnRevision => "En revisión",
        _ => "Resuelto",
    };
}

public sealed record ActuacionVista(TipoActuacionAviso Tipo, string Descripcion, string Operador, DateTimeOffset RegistradaEn)
{
    public string TextoTipo => Tipo switch
    {
        TipoActuacionAviso.Contacto => "Contacto",
        TipoActuacionAviso.Accion => "Acción",
        _ => "Resultado",
    };
}

public sealed record DetalleAviso(AvisoVista Aviso, bool SesionActiva, string? Responsable, DateTimeOffset? RevisionIniciadaEn, string? ResueltoPor,
    DateTimeOffset? ResueltoEn, string? VinculadoPor, DateTimeOffset? VinculadoEn, string? JustificacionCaso, string? EstadoCaso,
    IReadOnlyList<ActuacionVista> Actuaciones);

// Solo lectura; la política OperadorAutorizado (DP04) la aplican las páginas.
public sealed class ConsultarAvisos(CadDbContext db)
{
    private IQueryable<AvisoVista> Vistas(IQueryable<AvisoAcompanamiento> avisos) =>
        from aviso in avisos
        join ciudadano in db.Users.AsNoTracking() on aviso.CiudadanoId equals ciudadano.Id
        join responsable in db.Users.AsNoTracking() on aviso.ResponsableId equals (Guid?)responsable.Id into responsables
        from responsable in responsables.DefaultIfEmpty()
        select new AvisoVista(aviso.Id, aviso.SesionId, ciudadano.Nombres + " " + ciudadano.Apellidos, aviso.Regla, aviso.GeneradoEn, aviso.ReferenciaEn,
            aviso.Estado, responsable == null ? null : responsable.Nombres + " " + responsable.Apellidos, aviso.CasoId, aviso.CasoCreado,
            aviso.Clave == "sin-posiciones");

    // Avisos sin resolver, los más recientes primero (página «Acompañamientos»).
    public async Task<IReadOnlyList<AvisoVista>> AbiertosAsync(CancellationToken ct) =>
        (await Vistas(db.AvisosAcompanamiento.AsNoTracking().Where(a => a.Estado != EstadoAviso.Resuelto))
            .ToListAsync(ct)).OrderByDescending(a => a.GeneradoEn).ToList();

    public async Task<IReadOnlyList<AvisoVista>> DeSesionAsync(Guid sesionId, CancellationToken ct) =>
        (await Vistas(db.AvisosAcompanamiento.AsNoTracking().Where(a => a.SesionId == sesionId))
            .ToListAsync(ct)).OrderByDescending(a => a.GeneradoEn).ToList();

    public async Task<DetalleAviso?> DetalleAsync(Guid avisoId, CancellationToken ct)
    {
        var aviso = await db.AvisosAcompanamiento.AsNoTracking().SingleOrDefaultAsync(a => a.Id == avisoId, ct);
        if (aviso is null)
            return null;
        var vista = await Vistas(db.AvisosAcompanamiento.AsNoTracking().Where(a => a.Id == avisoId)).SingleAsync(ct);

        async Task<string?> NombreAsync(Guid? id) => id is { } usuario
            ? await db.Users.AsNoTracking().Where(u => u.Id == usuario).Select(u => u.Nombres + " " + u.Apellidos).SingleOrDefaultAsync(ct)
            : null;

        var actuaciones = await (from actuacion in db.ActuacionesAviso.AsNoTracking()
                                 join operador in db.Users.AsNoTracking() on actuacion.OperadorId equals operador.Id
                                 where actuacion.AvisoId == avisoId
                                 orderby actuacion.RegistradaEn
                                 select new ActuacionVista(actuacion.Tipo, actuacion.Descripcion, operador.Nombres + " " + operador.Apellidos, actuacion.RegistradaEn))
                                .ToListAsync(ct);
        var sesionActiva = await db.SesionesAcompanamiento.AsNoTracking().AnyAsync(s => s.Id == aviso.SesionId && s.Estado == EstadoSesion.Activa, ct);
        var estadoCaso = aviso.CasoId is { } casoId
            ? await db.Casos.AsNoTracking().Where(c => c.Id == casoId).Select(c => (EstadoCaso?)c.Estado).SingleOrDefaultAsync(ct)
            : null;

        return new DetalleAviso(vista, sesionActiva, await NombreAsync(aviso.ResponsableId), aviso.RevisionIniciadaEn, await NombreAsync(aviso.ResueltoPor),
            aviso.ResueltoEn, await NombreAsync(aviso.VinculadoPor), aviso.VinculadoEn, aviso.JustificacionCaso,
            estadoCaso is { } e ? EstadosCaso.Texto(e) : null, actuaciones);
    }
}
