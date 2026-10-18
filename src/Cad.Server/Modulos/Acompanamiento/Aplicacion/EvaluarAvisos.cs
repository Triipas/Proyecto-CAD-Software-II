using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

// HU42 — Avisos técnicos y de verificación del acompañamiento (S5-T07). Los evalúa el servidor, solo sobre sesiones activas
// (CA06): un cierre voluntario o por vencimiento no genera ninguno (CA05).
//  - CA01: más de 60 s (DP12) sin una posición nueva → «Ubicación desactualizada». La referencia es la hora de obtención de la
//    última posición o, si aún no llegó ninguna, el inicio de la sesión.
//  - CA02: una confirmación de bienestar presentada y sin respuesta durante 2 minutos → aviso de verificación.
// CA03: la clave de la condición (la última posición o la confirmación) es única por sesión y regla, también en la base de datos:
// mientras la condición persiste no se repite el aviso; una posición nueva que vuelva a quedar desactualizada es otra condición.
// CA04: un aviso no es un SOS: no entra en la sección urgente ni genera despacho.
public sealed class EvaluarAvisos(CadDbContext db, IOptions<OpcionesAcompanamiento> opciones, ILogger<EvaluarAvisos> registro)
{
    public TimeSpan Umbral => TimeSpan.FromSeconds(Math.Max(1, opciones.Value.UmbralDesactualizadaSegundos));
    public TimeSpan PlazoConfirmacion => TimeSpan.FromSeconds(Math.Max(1, opciones.Value.PlazoConfirmacionSegundos));

    // Devuelve los avisos generados en esta evaluación.
    public async Task<IReadOnlyList<AvisoAcompanamiento>> EjecutarAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var activas = await db.SesionesAcompanamiento.AsNoTracking()
            .Where(s => s.Estado == EstadoSesion.Activa && s.VenceEn > ahora)
            .Select(s => new
            {
                Sesion = s,
                UltimaPosicion = db.PosicionesAcompanamiento.Where(p => p.SesionId == s.Id).Max(p => (DateTimeOffset?)p.ObtenidaEn),
            })
            .ToListAsync(ct);

        var nuevos = new List<AvisoAcompanamiento>();
        foreach (var activa in activas)
        {
            var referencia = activa.UltimaPosicion ?? activa.Sesion.IniciadaEn;
            if (ahora - referencia > Umbral)
                nuevos.Add(AvisoAcompanamiento.UbicacionDesactualizada(activa.Sesion, activa.UltimaPosicion, ahora));
        }

        var limite = ahora - PlazoConfirmacion;
        var sinRespuesta = await (from c in db.ConfirmacionesBienestar.AsNoTracking()
                                  join s in db.SesionesAcompanamiento.AsNoTracking() on c.SesionId equals s.Id
                                  where c.Respuesta == null && c.PresentadaEn != null && c.PresentadaEn < limite
                                        && s.Estado == EstadoSesion.Activa && s.VenceEn > ahora
                                  select new { Confirmacion = c, Sesion = s })
                                 .ToListAsync(ct);
        nuevos.AddRange(sinRespuesta.Select(x => AvisoAcompanamiento.ConfirmacionSinRespuesta(x.Sesion, x.Confirmacion, ahora)));

        var generados = new List<AvisoAcompanamiento>();
        foreach (var aviso in nuevos)
        {
            // CA03: si ya existe para esta condición, no se repite.
            if (await db.AvisosAcompanamiento.AnyAsync(a => a.SesionId == aviso.SesionId && a.Regla == aviso.Regla && a.Clave == aviso.Clave, ct))
                continue;
            db.AvisosAcompanamiento.Add(aviso);
            try
            {
                await db.SaveChangesAsync(ct);
                generados.Add(aviso);
                registro.LogInformation("Aviso {AvisoId} generado en el acompañamiento {SesionId}: {Regla}.", aviso.Id, aviso.SesionId, aviso.Regla);
            }
            catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
            {
                // Otra evaluación simultánea lo generó primero.
                db.ChangeTracker.Clear();
            }
        }
        return generados;
    }
}

// Plan de Arquitectura 9.9: tarea periódica del servidor (como el vencimiento). Cada 10 s por defecto.
public sealed class EvaluacionAvisos(IServiceScopeFactory alcances, TimeProvider reloj, IOptions<OpcionesAcompanamiento> opciones,
    ILogger<EvaluacionAvisos> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, opciones.Value.IntervaloAvisosSegundos)), reloj);
        do
        {
            try
            {
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<EvaluarAvisos>().EjecutarAsync(reloj.GetUtcNow(), detener);
            }
            catch (Exception ex) when (!detener.IsCancellationRequested)
            {
                registro.LogError(ex, "No se pudieron evaluar los avisos del acompañamiento; se reintentará.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(detener));
    }
}
