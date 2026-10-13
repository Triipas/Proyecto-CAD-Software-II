using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU11 — datos de la sección urgente de la central. Solo lectura (Plan de Arquitectura, 3.3: módulo Consultas).
public sealed record SosPendiente(
    Guid Id,
    string Origen,
    string Estado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros)
{
    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;
}

public sealed class ConsultarSosPendientes(CadDbContext db)
{
    // Los más recientes primero: un SOS que acaba de llegar queda visible sin desplazarse (HU11-CA02).
    public async Task<IReadOnlyList<SosPendiente>> EjecutarAsync(CancellationToken ct) =>
        await (from caso in db.Casos.AsNoTracking()
               join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
               where caso.Origen == OrigenCaso.Sos && caso.Estado == EstadoCaso.Pendiente
               orderby caso.RegistradoEn descending
               select new SosPendiente(caso.Id, "SOS", "Pendiente", caso.RegistradoEn,
                   ciudadano.Nombres + " " + ciudadano.Apellidos,
                   caso.Latitud, caso.Longitud, caso.PrecisionMetros))
              .ToListAsync(ct);
}
