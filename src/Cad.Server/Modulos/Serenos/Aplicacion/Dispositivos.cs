using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Serenos.Aplicacion;

// ADR-07 — dispositivo del sereno para la notificación push del despacho. Nada de lo que se hace aquí escribe
// el token en los registros.
public sealed class GestionarDispositivo(CadDbContext db, TimeProvider reloj)
{
    public static Dictionary<string, string[]>? Validar(string? token) =>
        string.IsNullOrWhiteSpace(token) ? new() { ["token"] = ["Falta el token del dispositivo."] }
        : token.Length > DispositivoSereno.MaximoToken ? new() { ["token"] = ["El token del dispositivo es demasiado largo."] }
        : null;

    public async Task RegistrarAsync(Guid serenoId, string token, CancellationToken ct)
    {
        try
        {
            await IntentarAsync(serenoId, token, ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Dos registros simultáneos (p. ej., la app al abrirse dos veces): el segundo ve el primero y actualiza.
            db.ChangeTracker.Clear();
            await IntentarAsync(serenoId, token, ct);
        }
    }

    private async Task IntentarAsync(Guid serenoId, string token, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();

        // El teléfono lo usa quien ingresó por última vez: el token deja de pertenecer a cualquier otro sereno.
        await db.DispositivosSereno.Where(d => d.Token == token && d.SerenoId != serenoId).ExecuteDeleteAsync(ct);

        var dispositivo = await db.DispositivosSereno.SingleOrDefaultAsync(d => d.SerenoId == serenoId, ct);
        if (dispositivo is null)
            db.DispositivosSereno.Add(DispositivoSereno.Registrar(serenoId, token, ahora));
        else
            dispositivo.Actualizar(token, ahora);
        await db.SaveChangesAsync(ct);
    }

    // Al salir de la app: ese teléfono deja de recibir los avisos del sereno.
    public Task QuitarAsync(Guid serenoId, CancellationToken ct) =>
        db.DispositivosSereno.Where(d => d.SerenoId == serenoId).ExecuteDeleteAsync(ct);
}
