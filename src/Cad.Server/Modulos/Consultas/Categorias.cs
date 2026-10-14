using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// DP07: categorías del catálogo vigente, en el orden en que se presentan para elegir (HU13-CA04).
public sealed record CategoriaVista(Guid Id, string Nombre);

public sealed class ConsultarCategorias(CadDbContext db)
{
    public async Task<IReadOnlyList<CategoriaVista>> EjecutarAsync(CancellationToken ct) =>
        await db.CategoriasIncidencia.AsNoTracking()
            .OrderBy(c => c.Orden)
            .Select(c => new CategoriaVista(c.Id, c.Nombre))
            .ToListAsync(ct);
}
