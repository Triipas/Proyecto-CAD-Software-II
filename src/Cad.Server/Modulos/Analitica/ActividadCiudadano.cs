using System.Collections.Concurrent;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Bloque D, propuesta 15 — adopción y retención (27/10/2026). El ciudadano mantiene su sesión hasta 30 días, así que sus ingresos
// no dicen cuándo usa la app. Se anota, una vez por día, que el ciudadano usó la app (cualquier llamada con su sesión: abrir
// «Mis casos», mirar una notificación, emitir un SOS). Solo el día: ni qué hizo, ni a qué hora, ni dónde.
public sealed class ActividadCiudadano
{
    public Guid UsuarioId { get; private set; }
    // Día en hora local de la central.
    public DateOnly Dia { get; private set; }

    private ActividadCiudadano() { } // EF Core
}

// Una sola escritura por ciudadano y día: lo ya anotado hoy se recuerda en memoria. Si la escritura falla, la llamada del
// ciudadano sigue: la analítica nunca bloquea al usuario.
public sealed class RegistroActividadCiudadano(TimeProvider reloj, IConfiguration configuracion, ILogger<RegistroActividadCiudadano> registro)
{
    private readonly TimeZoneInfo zona = TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
    private readonly ConcurrentDictionary<Guid, DateOnly> anotados = new();

    public async Task AnotarAsync(CadDbContext db, Guid usuarioId, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime);
        if (anotados.TryGetValue(usuarioId, out var dia) && dia == hoy)
            return;
        try
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "ActividadesCiudadano" ("UsuarioId", "Dia") VALUES ({usuarioId}, {hoy}) ON CONFLICT DO NOTHING
                """, ct);
            anotados[usuarioId] = hoy;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogWarning(ex, "No se pudo anotar la actividad diaria de un ciudadano; se reintentará en su próxima llamada.");
        }
    }
}
