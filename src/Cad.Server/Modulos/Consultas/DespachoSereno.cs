using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU21 — lo que ve el sereno de su despacho. Solo lectura y solo el caso con su asignación vigente (CA04, CA05).
// Proyección cerrada a DP06: nunca se leen los datos personales del ciudadano, el resultado del contacto ni el operador.
public sealed class ConsultarDespacho(CadDbContext db)
{
    // Despacho vigente del sereno, o null si no tiene ninguno.
    public async Task<DespachoSereno?> VigenteAsync(Guid serenoId, CancellationToken ct)
    {
        var casoId = await db.Asignaciones.AsNoTracking()
            .Where(a => a.SerenoId == serenoId && a.Vigente)
            .Select(a => (Guid?)a.CasoId)
            .SingleOrDefaultAsync(ct);
        return casoId is { } id ? await DelCasoAsync(serenoId, id, ct) : null;
    }

    // CA04: null si el caso no existe o no está asignado (vigente) a este sereno; no se distingue entre ambos.
    public async Task<DespachoSereno?> DelCasoAsync(Guid serenoId, Guid casoId, CancellationToken ct)
    {
        var despacho = await (from asignacion in db.Asignaciones.AsNoTracking()
                              join caso in db.Casos.AsNoTracking() on asignacion.CasoId equals caso.Id
                              where asignacion.CasoId == casoId && asignacion.SerenoId == serenoId && asignacion.Vigente
                              select new
                              {
                                  caso.Id, caso.Origen, caso.Estado, caso.RegistradoEn, asignacion.AsignadaEn,
                                  caso.Latitud, caso.Longitud, caso.PrecisionMetros, caso.UbicacionObtenidaEn,
                              })
                             .SingleOrDefaultAsync(ct);
        if (despacho is null)
            return null;

        // Solo lo que el operador registró como información del hecho o referencia, de lo más reciente a lo más antiguo.
        var informacion = await db.IntentosContacto.AsNoTracking()
            .Where(i => i.CasoId == casoId && (i.InformacionHecho != null || i.ReferenciaUbicacion != null))
            .OrderByDescending(i => i.RegistradoEn).ThenByDescending(i => i.Id)
            .Select(i => new InformacionDespacho(i.RegistradoEn, i.InformacionHecho, i.ReferenciaUbicacion))
            .ToListAsync(ct);

        // HU22/HU23: horas de los avances de esta asignación (si el caso se reasignó tras una cancelación, DP03, no se
        // mezclan los avances del sereno anterior).
        var avances = await db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == casoId && e.OcurridoEn >= despacho.AsignadaEn
                && (e.Tipo == TipoEventoCaso.DesplazamientoConfirmado || e.Tipo == TipoEventoCaso.AtencionIniciada))
            .Select(e => new { e.Tipo, e.OcurridoEn })
            .ToListAsync(ct);

        // CA03: sin ubicación del dispositivo no se envían coordenadas.
        var ubicacion = despacho.Latitud is { } latitud && despacho.Longitud is { } longitud
            ? new UbicacionDespacho(latitud, longitud, despacho.PrecisionMetros!.Value, despacho.UbicacionObtenidaEn!.Value)
            : null;

        // HU20-CA07: un reporte admitido lleva el lugar del hecho y, desde la decisión del PO del 16/10/2026 sobre DP06,
        // la descripción del ciudadano. Nunca sus datos personales.
        var reporte = despacho.Origen == OrigenCaso.Reporte
            ? await db.DetallesReporte.AsNoTracking().Where(d => d.CasoId == casoId)
                .Select(d => new { Lugar = new LugarHechoDespacho(d.LatitudHecho, d.LongitudHecho, d.PrecisionHechoMetros, d.ReferenciaHecho), d.Descripcion })
                .SingleOrDefaultAsync(ct)
            : null;

        return new DespachoSereno(despacho.Id, despacho.Origen.ToString().ToUpperInvariant(), EstadosCaso.Texto(despacho.Estado),
            despacho.RegistradoEn, despacho.AsignadaEn, ubicacion, informacion,
            avances.FirstOrDefault(a => a.Tipo == TipoEventoCaso.DesplazamientoConfirmado)?.OcurridoEn,
            avances.FirstOrDefault(a => a.Tipo == TipoEventoCaso.AtencionIniciada)?.OcurridoEn,
            reporte?.Lugar, reporte?.Descripcion);
    }
}
