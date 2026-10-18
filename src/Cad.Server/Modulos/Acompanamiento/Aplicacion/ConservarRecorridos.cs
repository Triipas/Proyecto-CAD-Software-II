using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

public abstract record ResultadoConservacion
{
    public sealed record Registrada(ConservacionRecorrido Conservacion) : ResultadoConservacion;
    // Ya estaba conservada: se devuelve la conservación existente (un reenvío no la duplica ni la cambia).
    public sealed record YaRegistrada(ConservacionRecorrido Conservacion) : ResultadoConservacion;
    public sealed record Invalida(Dictionary<string, string[]> Errores) : ResultadoConservacion;
    // Las posiciones ya se eliminaron por plazo: no hay nada que conservar.
    public sealed record SinPosiciones : ResultadoConservacion;
    public sealed record NoEncontrada : ResultadoConservacion;
}

// HU39-B — conservación y eliminación de recorridos (S4-T14, S4-T15; RNF13). DP22, decisión del equipo de desarrollo delegada
// por el PO el 16/10/2026: las posiciones de una sesión terminada se eliminan a los 30 días de su cierre (CA04), salvo las
// vinculadas con un caso con una finalidad explícita (CA05). Los datos de la sesión (inicio, vencimiento, cierre) se conservan
// para el historial de HU39-A.
public sealed class ConservarRecorridos(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAcompanamiento> opciones,
    ILogger<ConservarRecorridos> registro)
{
    public int ConservacionDias => Math.Max(1, opciones.Value.ConservacionDias);

    // CA05: conservación excepcional con finalidad y registro explícitos; el caso debe existir.
    public async Task<ResultadoConservacion> ConservarAsync(Guid sesionId, Guid? casoId, string? finalidad, Guid operadorId, CancellationToken ct)
    {
        var sesion = await db.SesionesAcompanamiento.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sesionId, ct);
        if (sesion is null)
            return new ResultadoConservacion.NoEncontrada();
        if (await db.ConservacionesRecorrido.AsNoTracking().SingleOrDefaultAsync(c => c.SesionId == sesionId, ct) is { } previa)
            return new ResultadoConservacion.YaRegistrada(previa);
        if (sesion.PosicionesEliminadasEn is not null)
            return new ResultadoConservacion.SinPosiciones();

        var errores = new Dictionary<string, string[]>();
        if (casoId is not { } caso || caso == Guid.Empty || !await db.Casos.AnyAsync(c => c.Id == caso, ct))
            errores["casoId"] = ["Indica el identificador de un caso existente (SOS o reporte)."];
        if (string.IsNullOrWhiteSpace(finalidad))
            errores["finalidad"] = ["Indica la finalidad de conservar el recorrido."];
        else if (finalidad.Trim().Length > ConservacionRecorrido.MaximoFinalidad)
            errores["finalidad"] = [$"La finalidad admite hasta {ConservacionRecorrido.MaximoFinalidad} caracteres."];
        if (errores.Count > 0)
            return new ResultadoConservacion.Invalida(errores);

        var conservacion = ConservacionRecorrido.Registrar(sesionId, casoId!.Value, finalidad!, operadorId, reloj.GetUtcNow());
        db.ConservacionesRecorrido.Add(conservacion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Dos operadores a la vez: prevalece la primera.
            db.ChangeTracker.Clear();
            return new ResultadoConservacion.YaRegistrada(await db.ConservacionesRecorrido.AsNoTracking().SingleAsync(c => c.SesionId == sesionId, ct));
        }
        registro.LogInformation("Recorrido del acompañamiento {SesionId} conservado vinculado al caso {CasoId}.", sesionId, conservacion.CasoId);
        return new ResultadoConservacion.Registrada(conservacion);
    }

    // CA04: elimina las posiciones de las sesiones terminadas hace más del plazo, sin conservación excepcional, y anota en la
    // sesión cuándo se eliminaron. Devuelve las sesiones afectadas.
    public async Task<IReadOnlyList<Guid>> EliminarVencidasAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var limite = ahora.AddDays(-ConservacionDias);
        var sesiones = await db.SesionesAcompanamiento
            .Where(s => s.Estado != EstadoSesion.Activa && s.CerradaEn != null && s.CerradaEn <= limite && s.PosicionesEliminadasEn == null
                && !db.ConservacionesRecorrido.Any(c => c.SesionId == s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (sesiones.Count == 0)
            return sesiones;

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var posiciones = await db.PosicionesAcompanamiento.Where(p => sesiones.Contains(p.SesionId)).ExecuteDeleteAsync(ct);
        await db.SesionesAcompanamiento.Where(s => sesiones.Contains(s.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PosicionesEliminadasEn, (DateTimeOffset?)ahora), ct);
        await transaccion.CommitAsync(ct);
        // Sin coordenadas ni identificadores de ciudadanos: solo cuántas.
        registro.LogInformation("Conservación de recorridos: {Posiciones} posiciones de {Sesiones} sesiones eliminadas al cumplirse {Dias} días.",
            posiciones, sesiones.Count, ConservacionDias);
        return sesiones;
    }
}

// HU39-B (Plan de Arquitectura 9.9): la eliminación por plazo se revisa al arrancar y cada hora (parámetro).
public sealed class EliminacionRecorridos(IServiceScopeFactory alcances, TimeProvider reloj, IOptions<OpcionesAcompanamiento> opciones,
    ILogger<EliminacionRecorridos> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, opciones.Value.IntervaloEliminacionMinutos)), reloj);
        do
        {
            try
            {
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<ConservarRecorridos>().EliminarVencidasAsync(detener);
            }
            catch (Exception ex) when (!detener.IsCancellationRequested)
            {
                registro.LogError(ex, "No se pudo revisar la conservación de los recorridos; se reintentará.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(detener));
    }
}
