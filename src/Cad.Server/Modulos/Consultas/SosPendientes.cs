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
    double? PrecisionMetros,
    string? Tipo)
{
    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;

    // HU13-CA03: un SOS sin clasificar se muestra como «Tipo por determinar».
    public const string TipoPorDeterminar = "Tipo por determinar";
}

public sealed class ConsultarSosPendientes(CadDbContext db)
{
    // Los más recientes primero: un SOS que acaba de llegar queda visible sin desplazarse (HU11-CA02).
    public async Task<IReadOnlyList<SosPendiente>> EjecutarAsync(CancellationToken ct) =>
        await (from caso in db.Casos.AsNoTracking()
               join ciudadano in db.Users.AsNoTracking() on caso.CiudadanoId equals ciudadano.Id
               join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id into categorias
               from categoria in categorias.DefaultIfEmpty()
               where caso.Origen == OrigenCaso.Sos && caso.Estado == EstadoCaso.Pendiente
               orderby caso.RegistradoEn descending
               select new SosPendiente(caso.Id, "SOS", "Pendiente", caso.RegistradoEn,
                   ciudadano.Nombres + " " + ciudadano.Apellidos,
                   caso.Latitud, caso.Longitud, caso.PrecisionMetros,
                   categoria == null ? null : categoria.Nombre))
              .ToListAsync(ct);
}
