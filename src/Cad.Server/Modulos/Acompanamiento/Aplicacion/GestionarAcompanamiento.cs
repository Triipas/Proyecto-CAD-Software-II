using Cad.Contratos.Acompanamiento;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

public sealed class OpcionesAcompanamiento
{
    public const string Seccion = "Acompanamiento";

    // HU35-CA01: la app intenta transmitir la última posición cada 15 segundos.
    public int IntervaloPosicionesSegundos { get; set; } = 15;

    // Plan de Arquitectura, 9.9: el temporizador revisa los vencimientos cada 5 s (parámetro).
    public int IntervaloVencimientoSegundos { get; set; } = 5;

    // DP12 (16/10/2026): una posición con más de 60 s es desactualizada. DP20: sin ningún envío durante ese mismo tiempo,
    // la sesión se muestra «Sin conexión».
    public int UmbralDesactualizadaSegundos { get; set; } = 60;

    // DP22 (decisión del equipo de desarrollo, 16/10/2026): las posiciones de una sesión terminada se conservan 30 días.
    public int ConservacionDias { get; set; } = 30;

    // Plan de Arquitectura 9.9: la eliminación por plazo se revisa al arrancar y cada hora.
    public int IntervaloEliminacionMinutos { get; set; } = 60;
}

public abstract record ResultadoInicioAcompanamiento
{
    public sealed record Iniciada(SesionAcompanamiento Sesion) : ResultadoInicioAcompanamiento;
    // Reiteración técnica de la misma pulsación: se devuelve la misma sesión.
    public sealed record YaIniciada(SesionAcompanamiento Sesion) : ResultadoInicioAcompanamiento;
    // HU34-CA06: ya hay otra sesión activa; no se crea una segunda.
    public sealed record OtraSesionActiva : ResultadoInicioAcompanamiento;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoInicioAcompanamiento;
}

public abstract record ResultadoPosicionAcompanamiento
{
    public sealed record Registrada : ResultadoPosicionAcompanamiento;
    // La misma posición reenviada (misma sesión y misma hora de obtención): no se duplica.
    public sealed record YaRegistrada : ResultadoPosicionAcompanamiento;
    // HU35-CA02, HU36-CA04, HU37-CA04: la sesión ya no está activa; la posición no se incorpora.
    public sealed record SesionCerrada(SesionAcompanamiento Sesion) : ResultadoPosicionAcompanamiento;
    public sealed record NoEncontrada : ResultadoPosicionAcompanamiento;
    public sealed record Invalida(Dictionary<string, string[]> Errores) : ResultadoPosicionAcompanamiento;
}

public abstract record ResultadoFinalizacion
{
    // La sesión queda cerrada (por esta finalización, por una anterior o por su vencimiento): HU36-CA07.
    public sealed record Cerrada(SesionAcompanamiento Sesion) : ResultadoFinalizacion;
    public sealed record NoEncontrada : ResultadoFinalizacion;
}

// HU34 — Activar el acompañamiento (S4-T01), HU35 — Compartir la posición (S4-T03), HU36 — Finalizarlo (S4-T05, S4-T06)
// y HU37 — Cierre automático (S4-T07, con VencimientoAcompanamientos). Solo el titular: la política Ciudadano la aplican
// los endpoints y la titularidad, cada consulta (una sesión ajena responde igual que una inexistente).
// Ni el inicio ni el cierre crean, cambian ni cancelan casos (HU34-CA05, HU36-CA05/CA06, HU37-CA05).
public sealed class GestionarAcompanamiento(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAcompanamiento> opciones,
    ILogger<GestionarAcompanamiento> registro)
{
    public int IntervaloPosicionesSegundos => opciones.Value.IntervaloPosicionesSegundos;

    public DateTimeOffset Ahora => reloj.GetUtcNow();

    public async Task<ResultadoInicioAcompanamiento> IniciarAsync(Guid ciudadanoId, SolicitudInicioAcompanamiento solicitud, CancellationToken ct)
    {
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoInicioAcompanamiento.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });

        // Una sesión vencida que el temporizador aún no cerró se cierra primero: no impide iniciar otra (HU37-CA03: el
        // ciudadano inicia una nueva) y un reintento de su inicio la devuelve ya vencida.
        var ahora = reloj.GetUtcNow();
        await VencerAsync(ahora, ciudadanoId, ct);
        if (await PorSolicitudAsync(ciudadanoId, solicitud.IdSolicitud, ct) is { } existente)
            return new ResultadoInicioAcompanamiento.YaIniciada(existente);

        // CA02 y CA03: sin la finalidad aceptada, o sin permiso ni ubicación disponible, no se crea la sesión.
        var errores = new Dictionary<string, string[]>();
        if (!solicitud.FinalidadAceptada)
            errores["finalidadAceptada"] = ["Para iniciar el acompañamiento debes aceptar compartir tu ubicación con la central durante el trayecto."];
        if (!solicitud.UbicacionDisponible)
            errores["ubicacionDisponible"] = ["Para iniciar el acompañamiento necesitas el permiso de ubicación y una posición disponible."];
        if (errores.Count > 0)
            return new ResultadoInicioAcompanamiento.Invalido(errores);

        if (await db.SesionesAcompanamiento.AsNoTracking().AnyAsync(s => s.CiudadanoId == ciudadanoId && s.Estado == EstadoSesion.Activa, ct))
            return new ResultadoInicioAcompanamiento.OtraSesionActiva();

        var sesion = SesionAcompanamiento.Iniciar(ciudadanoId, solicitud.IdSolicitud, ahora);
        db.SesionesAcompanamiento.Add(sesion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // CA06: dos inicios simultáneos. Los índices únicos dejan pasar uno; el otro es el mismo (reintento) o se rechaza.
            db.ChangeTracker.Clear();
            return await PorSolicitudAsync(ciudadanoId, solicitud.IdSolicitud, ct) is { } misma
                ? new ResultadoInicioAcompanamiento.YaIniciada(misma)
                : new ResultadoInicioAcompanamiento.OtraSesionActiva();
        }

        registro.LogInformation("Acompañamiento {SesionId} iniciado.", sesion.Id);
        return new ResultadoInicioAcompanamiento.Iniciada(sesion);
    }

    // HU34-CA08 y HU37-CA06: la sesión activa del ciudadano (null si no tiene ninguna).
    public async Task<SesionAcompanamiento?> ActivaAsync(Guid ciudadanoId, CancellationToken ct)
    {
        await VencerAsync(reloj.GetUtcNow(), ciudadanoId, ct);
        return await db.SesionesAcompanamiento.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CiudadanoId == ciudadanoId && s.Estado == EstadoSesion.Activa, ct);
    }

    // Una sesión concreta del titular (también cerrada: al reconectar, la app ve que terminó, HU37-CA06).
    public async Task<SesionAcompanamiento?> DelTitularAsync(Guid ciudadanoId, Guid sesionId, CancellationToken ct)
    {
        await VencerAsync(reloj.GetUtcNow(), ciudadanoId, ct);
        return await db.SesionesAcompanamiento.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sesionId && s.CiudadanoId == ciudadanoId, ct);
    }

    public async Task<ResultadoPosicionAcompanamiento> RegistrarPosicionAsync(Guid ciudadanoId, Guid sesionId,
        PosicionAcompanamientoEnviada enviada, CancellationToken ct)
    {
        // La sesión se bloquea hasta guardar la posición: una finalización simultánea espera, así que una posición solo se
        // incorpora si la sesión estaba activa al recibirla (regla de cierre compartida, S4-T06). Bloqueo de escritura y no
        // compartido: cada envío actualiza el último contacto de la sesión (DP20), y dos bloqueos compartidos que después
        // escriben la misma fila se interbloquean (lo encontraron las pruebas con envíos simultáneos).
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var sesion = await db.SesionesAcompanamiento
            .FromSql($"SELECT *, xmin FROM \"SesionesAcompanamiento\" WHERE \"Id\" = {sesionId} FOR NO KEY UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (sesion is null || sesion.CiudadanoId != ciudadanoId)
            return new ResultadoPosicionAcompanamiento.NoEncontrada();

        var ahora = reloj.GetUtcNow();
        if (!sesion.AdmitePosiciones(ahora))
            return new ResultadoPosicionAcompanamiento.SesionCerrada(sesion);

        // DP20: cualquier envío a una sesión activa cuenta como contacto, aunque repita una posición ya registrada.
        await db.SesionesAcompanamiento.Where(s => s.Id == sesion.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoContactoEn, (DateTimeOffset?)ahora), ct);

        var posicion = PosicionAcompanamiento.Crear(sesion.Id, enviada.Latitud, enviada.Longitud, enviada.PrecisionMetros, enviada.ObtenidaEn, ahora);
        if (posicion is null)
        {
            await transaccion.CommitAsync(ct); // conserva el último contacto
            return new ResultadoPosicionAcompanamiento.Invalida(new()
            {
                ["posicion"] = ["La posición no es válida: revisa las coordenadas, la precisión y la hora de obtención."],
            });
        }

        // Un reenvío de la misma posición se detecta antes de escribir, para no abortar la transacción.
        if (await db.PosicionesAcompanamiento.AnyAsync(p => p.SesionId == sesion.Id && p.ObtenidaEn == posicion.ObtenidaEn, ct))
        {
            await transaccion.CommitAsync(ct); // conserva el último contacto
            return new ResultadoPosicionAcompanamiento.YaRegistrada();
        }

        db.PosicionesAcompanamiento.Add(posicion);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
            return new ResultadoPosicionAcompanamiento.YaRegistrada();
        }

        // Sin coordenadas en el registro (RNF07).
        registro.LogDebug("Posición del acompañamiento {SesionId} registrada.", sesion.Id);
        return new ResultadoPosicionAcompanamiento.Registrada();
    }

    // HU36-CA02: el servidor registra el cierre. HU36-CA07: un reenvío devuelve el mismo estado final sin duplicar nada.
    public async Task<ResultadoFinalizacion> FinalizarAsync(Guid ciudadanoId, Guid sesionId, CancellationToken ct)
    {
        for (var intento = 0; ; intento++)
        {
            // Bloqueo exclusivo antes de tomar la hora: espera a las posiciones que se están guardando (bloqueo compartido),
            // así que la hora de cierre es posterior a la de cualquier posición incorporada y ninguna llega después (CA04).
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            var sesion = await db.SesionesAcompanamiento
                .FromSql($"SELECT *, xmin FROM \"SesionesAcompanamiento\" WHERE \"Id\" = {sesionId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (sesion is null || sesion.CiudadanoId != ciudadanoId)
                return new ResultadoFinalizacion.NoEncontrada();

            var ahora = reloj.GetUtcNow();
            var estadoAnterior = sesion.Estado;
            if (!sesion.Finalizar(ahora))
                return new ResultadoFinalizacion.Cerrada(sesion);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (intento < 2)
            {
                // El temporizador u otra solicitud la cerró a la vez: se vuelve a leer y a evaluar.
                db.ChangeTracker.Clear();
                continue;
            }

            // HU36-CA06: un cierre voluntario no es una señal de peligro; solo se anota cómo terminó.
            registro.LogInformation("Acompañamiento {SesionId} {Cierre} (antes: {Estado}).", sesion.Id,
                sesion.Estado == EstadoSesion.Finalizada ? "finalizado por el ciudadano" : "vencido", estadoAnterior);
            return new ResultadoFinalizacion.Cerrada(sesion);
        }
    }

    // HU37-CA01: cierra las sesiones activas cuya hora de vencimiento ya pasó (todas, o solo las de un ciudadano).
    // Un UPDATE condicionado al estado: si dos procesos lo intentan a la vez, cada sesión se cierra una sola vez.
    public async Task<IReadOnlyList<Guid>> VencerAsync(DateTimeOffset ahora, Guid? ciudadanoId, CancellationToken ct)
    {
        var vencidas = db.SesionesAcompanamiento.Where(s => s.Estado == EstadoSesion.Activa && s.VenceEn <= ahora);
        if (ciudadanoId is { } id)
            vencidas = vencidas.Where(s => s.CiudadanoId == id);
        var ids = await vencidas.Select(s => s.Id).ToListAsync(ct);
        if (ids.Count == 0)
            return ids;

        await db.SesionesAcompanamiento
            .Where(s => ids.Contains(s.Id) && s.Estado == EstadoSesion.Activa)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Estado, EstadoSesion.Vencida)
                .SetProperty(x => x.CerradaEn, x => (DateTimeOffset?)x.VenceEn), ct);
        foreach (var vencida in ids)
            registro.LogInformation("Acompañamiento {SesionId} vencido a los 30 minutos.", vencida);
        return ids;
    }

    private Task<SesionAcompanamiento?> PorSolicitudAsync(Guid ciudadanoId, Guid idSolicitud, CancellationToken ct) =>
        db.SesionesAcompanamiento.AsNoTracking().SingleOrDefaultAsync(s => s.CiudadanoId == ciudadanoId && s.IdSolicitud == idSolicitud, ct);
}

// HU37-CA01 (S4-T07): temporizador del servidor. Cierra las sesiones vencidas aunque el dispositivo esté desconectado; no
// renueva ni crea ninguna (CA03). Las consultas del ciudadano también vencen al acceder, por si el temporizador se retrasa.
public sealed class VencimientoAcompanamientos(IServiceScopeFactory alcances, TimeProvider reloj, IOptions<OpcionesAcompanamiento> opciones,
    ILogger<VencimientoAcompanamientos> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, opciones.Value.IntervaloVencimientoSegundos)), reloj);
        do
        {
            try
            {
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<GestionarAcompanamiento>().VencerAsync(reloj.GetUtcNow(), null, detener);
            }
            catch (Exception ex) when (!detener.IsCancellationRequested)
            {
                // Un fallo (p. ej., la base de datos no responde) no detiene el temporizador: se reintenta en la siguiente vuelta.
                registro.LogError(ex, "No se pudo revisar el vencimiento de los acompañamientos; se reintentará.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(detener));
    }
}
