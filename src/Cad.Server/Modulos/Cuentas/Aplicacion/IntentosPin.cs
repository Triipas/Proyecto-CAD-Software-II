using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// ADR-08: los intentos de PIN se cuentan en la base de datos con una sola sentencia atómica, antes de comprobar el PIN.
// Así, peticiones simultáneas no pueden leer el mismo contador y probar más de MaximoIntentosPin PIN.
public static class IntentosPin
{
    // Reserva un intento: true si quedaba alguno (y ya está contado); false si el PIN está bloqueado.
    public static async Task<bool> ReservarAsync(CadDbContext db, Guid usuarioId, CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == usuarioId && u.IntentosPinFallidos < Usuario.MaximoIntentosPin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IntentosPinFallidos, u => u.IntentosPinFallidos + 1), ct) == 1;

    // Intentos ya usados (incluido el reservado).
    public static Task<int> UsadosAsync(CadDbContext db, Guid usuarioId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == usuarioId).Select(u => u.IntentosPinFallidos).SingleAsync(ct);

    // PIN correcto: el intento reservado no cuenta y el contador vuelve a cero.
    public static Task ReiniciarAsync(CadDbContext db, Usuario usuario, CancellationToken ct)
    {
        usuario.IntentosPinFallidos = 0;
        return db.Users.Where(u => u.Id == usuario.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IntentosPinFallidos, 0), ct);
    }
}
