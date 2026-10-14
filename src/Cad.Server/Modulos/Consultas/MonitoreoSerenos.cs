using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU19 — Monitorear el estado y la disponibilidad de los serenos. Solo lectura (módulo Consultas).
// Latitud/Longitud solo se informan si el sereno está operativo: la ubicación se comparte únicamente
// mientras está activo (HU18), así que la última posición de un sereno no operativo no se expone.
public sealed record SerenoMonitoreo(
    Guid Id,
    string Nombre,
    bool Operativo,
    bool Disponible,
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros,
    DateTimeOffset? UltimaPosicionEn)
{
    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;
}

public sealed class ConsultarSerenos(CadDbContext db)
{
    private static readonly Guid RolSereno = Roles.Todos.Single(r => r.Nombre == Roles.Sereno).Id;

    public async Task<IReadOnlyList<SerenoMonitoreo>> EjecutarAsync(CancellationToken ct)
    {
        var filas = await (from usuario in db.Users.AsNoTracking()
                           where usuario.Activo && db.UserRoles.Any(r => r.UserId == usuario.Id && r.RoleId == RolSereno)
                           let estado = db.EstadosSereno.FirstOrDefault(e => e.SerenoId == usuario.Id)
                           let operativo = estado != null && estado.Operativo
                           // Solo posiciones recibidas desde la activación vigente (reloj del servidor): la última
                           // posición de un turno anterior no se presenta como actual.
                           let ultima = db.PosicionesSereno
                               .Where(p => p.SerenoId == usuario.Id && estado != null && p.RecibidaEn >= estado.CambiadoEn)
                               .OrderByDescending(p => p.ObtenidaEn)
                               .FirstOrDefault()
                           orderby operativo descending, usuario.Nombres, usuario.Apellidos
                           select new Fila
                           {
                               Id = usuario.Id,
                               Nombre = usuario.Nombres + " " + usuario.Apellidos,
                               Operativo = operativo,
                               Latitud = ultima == null ? null : ultima.Latitud,
                               Longitud = ultima == null ? null : ultima.Longitud,
                               PrecisionMetros = ultima == null ? null : ultima.PrecisionMetros,
                               RecibidaEn = ultima == null ? null : ultima.RecibidaEn,
                           })
                          .ToListAsync(ct);

        return filas.Select(f => new SerenoMonitoreo(
                f.Id, f.Nombre, f.Operativo,
                // HU19-CA02, regla del MVP: operativo y sin asignación vigente. Las asignaciones llegan con HU20
                // (bloque 5); hasta entonces ningún sereno tiene asignación vigente.
                Disponible: f.Operativo,
                f.Operativo ? f.Latitud : null,
                f.Operativo ? f.Longitud : null,
                f.Operativo ? f.PrecisionMetros : null,
                f.Operativo ? f.RecibidaEn : null))
            .ToList();
    }

    private sealed class Fila
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = "";
        public bool Operativo { get; init; }
        public double? Latitud { get; init; }
        public double? Longitud { get; init; }
        public double? PrecisionMetros { get; init; }
        public DateTimeOffset? RecibidaEn { get; init; }
    }
}
