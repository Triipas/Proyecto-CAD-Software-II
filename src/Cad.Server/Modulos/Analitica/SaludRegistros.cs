using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Marcas de la central sobre un caso (27/10/2026, salud del sistema): la primera presentación y la primera apertura. Cada una se
// escribe una sola vez, con una actualización condicional (solo si sigue vacía): dos paneles a la vez no se pisan. Si falla, el
// operador sigue trabajando: la analítica nunca bloquea la atención.
public sealed class MarcasCentral(CadDbContext db, TimeProvider reloj, ILogger<MarcasCentral> registro)
{
    public Task PresentadoAsync(Guid casoId, CancellationToken ct) => Marcar(casoId, presentado: true, ct);
    public Task AbiertoAsync(Guid casoId, CancellationToken ct) => Marcar(casoId, presentado: false, ct);

    private async Task Marcar(Guid casoId, bool presentado, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        try
        {
            if (presentado)
                await db.Casos.Where(c => c.Id == casoId && c.PresentadoEnCentralEn == null)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.PresentadoEnCentralEn, ahora), ct);
            else
                await db.Casos.Where(c => c.Id == casoId && c.AbiertoEnCentralEn == null)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.AbiertoEnCentralEn, ahora), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogWarning(ex, "No se pudo anotar la {Marca} del caso {CasoId}.", presentado ? "presentación" : "apertura", casoId);
        }
    }
}

// Un intento de notificación push del despacho al sereno (ADR-07), con su resultado: si llegó a FCM, si el dispositivo ya no
// existía o si no se pudo enviar; o si el sereno no tenía dispositivo. Sin el token ni el contenido. Solo inserción.
public sealed class EnvioPush
{
    public const string SinDispositivo = "SinDispositivo";

    public Guid Id { get; private set; }
    public Guid SerenoId { get; private set; }
    public DateTimeOffset EnviadoEn { get; private set; }
    // «Enviada», «TokenInvalido», «NoEnviada» (Compartido.ResultadoPush) o «SinDispositivo».
    public string Resultado { get; private set; } = "";

    private EnvioPush() { } // EF Core

    public static EnvioPush Registrar(Guid serenoId, string resultado, DateTimeOffset ahora) =>
        new() { Id = Guid.CreateVersion7(), SerenoId = serenoId, EnviadoEn = ahora, Resultado = resultado };
}
