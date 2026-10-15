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
    bool ConAsignacion,
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros,
    DateTimeOffset? UltimaPosicionEn,
    // HU23-CA03: caso de la asignación vigente y su estado (Asignada, En camino, En atención).
    Guid? CasoAsignadoId = null,
    string? EstadoCasoAsignado = null,
    // HU20-CA07: el caso asignado puede ser un reporte admitido; su detalle está en «Reportes».
    bool CasoAsignadoEsReporte = false)
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
                           let conAsignacion = db.Asignaciones.Any(a => a.SerenoId == usuario.Id && a.Vigente)
                           let casoAsignado = db.Asignaciones.Where(a => a.SerenoId == usuario.Id && a.Vigente).Select(a => (Guid?)a.CasoId).FirstOrDefault()
                           let estadoCaso = db.Casos.Where(c => c.Id == casoAsignado).Select(c => (Casos.Dominio.EstadoCaso?)c.Estado).FirstOrDefault()
                           let esReporte = db.Casos.Any(c => c.Id == casoAsignado && c.Origen == Casos.Dominio.OrigenCaso.Reporte)
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
                               ConAsignacion = conAsignacion,
                               CasoAsignado = casoAsignado,
                               EstadoCaso = estadoCaso,
                               EsReporte = esReporte,
                               Latitud = ultima == null ? null : ultima.Latitud,
                               Longitud = ultima == null ? null : ultima.Longitud,
                               PrecisionMetros = ultima == null ? null : ultima.PrecisionMetros,
                               RecibidaEn = ultima == null ? null : ultima.RecibidaEn,
                           })
                          .ToListAsync(ct);

        return filas.Select(f => new SerenoMonitoreo(
                f.Id, f.Nombre, f.Operativo,
                // HU19-CA02, regla del MVP: operativo y sin asignación vigente (HU20).
                Disponible: f.Operativo && !f.ConAsignacion,
                f.ConAsignacion,
                f.Operativo ? f.Latitud : null,
                f.Operativo ? f.Longitud : null,
                f.Operativo ? f.PrecisionMetros : null,
                f.Operativo ? f.RecibidaEn : null,
                f.CasoAsignado,
                f.EstadoCaso is { } estado ? Casos.Dominio.EstadosCaso.Texto(estado) : null,
                f.EsReporte))
            .ToList();
    }

    private sealed class Fila
    {
        public Guid Id { get; init; }
        public string Nombre { get; init; } = "";
        public bool Operativo { get; init; }
        public bool ConAsignacion { get; init; }
        public Guid? CasoAsignado { get; init; }
        public Casos.Dominio.EstadoCaso? EstadoCaso { get; init; }
        public bool EsReporte { get; init; }
        public double? Latitud { get; init; }
        public double? Longitud { get; init; }
        public double? PrecisionMetros { get; init; }
        public DateTimeOffset? RecibidaEn { get; init; }
    }
}
