using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU12 — datos que el operador necesita para verificar un SOS. Solo lectura (módulo Consultas).
public sealed record DetalleSos(
    Guid Id,
    string Estado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    string? Telefono,
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros,
    DateTimeOffset? UbicacionObtenidaEn,
    IReadOnlyList<IntentoContactoVista> Intentos)
{
    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;
}

public sealed record IntentoContactoVista(
    ResultadoContacto Resultado,
    string Operador,
    DateTimeOffset RegistradoEn,
    string? InformacionHecho,
    string? ReferenciaUbicacion);

public sealed class ConsultarDetalleSos(CadDbContext db)
{
    public async Task<DetalleSos?> EjecutarAsync(Guid casoId, CancellationToken ct)
    {
        var sos = await (from caso in db.Casos.AsNoTracking()
                         join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
                         where caso.Id == casoId && caso.Origen == OrigenCaso.Sos
                         select new
                         {
                             caso.Id, caso.Estado, caso.RegistradoEn,
                             Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos,
                             ciudadano.PhoneNumber,
                             caso.Latitud, caso.Longitud, caso.PrecisionMetros, caso.UbicacionObtenidaEn,
                         })
                        .SingleOrDefaultAsync(ct);
        if (sos is null)
            return null;

        // CA07: todos los intentos, del más reciente al más antiguo.
        var intentos = await (from intento in db.IntentosContacto.AsNoTracking()
                              join operador in db.Users.AsNoTracking() on intento.OperadorId equals operador.Id
                              where intento.CasoId == casoId
                              orderby intento.RegistradoEn descending, intento.Id descending
                              select new IntentoContactoVista(intento.Resultado, operador.Nombres + " " + operador.Apellidos,
                                  intento.RegistradoEn, intento.InformacionHecho, intento.ReferenciaUbicacion))
                             .ToListAsync(ct);

        return new DetalleSos(sos.Id, sos.Estado.ToString(), sos.RegistradoEn, sos.Ciudadano, sos.PhoneNumber,
            sos.Latitud, sos.Longitud, sos.PrecisionMetros, sos.UbicacionObtenidaEn, intentos);
    }
}
