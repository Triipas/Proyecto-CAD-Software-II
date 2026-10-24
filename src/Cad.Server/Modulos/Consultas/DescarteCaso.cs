using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// Descarte de un caso por la central (23/10/2026), para el detalle del caso en el panel: quién, cuándo, desde qué estado y por qué.
public sealed record DescarteVista(string Operador, string EstadoPrevio, string Motivo, DateTimeOffset DescartadoEn);

public sealed class ConsultarDescarte(CadDbContext db)
{
    public async Task<DescarteVista?> EjecutarAsync(Guid casoId, CancellationToken ct) =>
        await (from descarte in db.DescartesCaso.AsNoTracking()
               join operador in db.Users.AsNoTracking() on descarte.OperadorId equals operador.Id
               join caso in db.Casos.AsNoTracking() on descarte.CasoId equals caso.Id
               where descarte.CasoId == casoId
               select new { caso.Origen, descarte.EstadoPrevio, descarte.Motivo, descarte.DescartadoEn, Operador = operador.Nombres + " " + operador.Apellidos })
            .SingleOrDefaultAsync(ct) is { } d
            ? new DescarteVista(d.Operador, EstadosCaso.Texto(d.Origen, d.EstadoPrevio), d.Motivo, d.DescartadoEn)
            : null;
}
