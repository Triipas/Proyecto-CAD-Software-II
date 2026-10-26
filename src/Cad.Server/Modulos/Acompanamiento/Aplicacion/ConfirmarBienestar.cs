using Cad.Contratos.Acompanamiento;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

public abstract record ResultadoSolicitudBienestar
{
    public sealed record Solicitada(ConfirmacionBienestar Confirmacion) : ResultadoSolicitudBienestar;
    // Reenvío del mismo formulario del operador: se devuelve la solicitud ya registrada.
    public sealed record YaSolicitada(ConfirmacionBienestar Confirmacion) : ResultadoSolicitudBienestar;
    // HU40-CA02: la sesión está cerrada o vencida.
    public sealed record SesionNoActiva : ResultadoSolicitudBienestar;
    // HU40-CA04 (DP20, decisión del PO del 16/10/2026): una sola confirmación pendiente por sesión.
    public sealed record OtraPendiente(ConfirmacionBienestar Pendiente) : ResultadoSolicitudBienestar;
    public sealed record NoEncontrada : ResultadoSolicitudBienestar;
}

public abstract record ResultadoRespuestaBienestar
{
    public sealed record Registrada(ConfirmacionBienestar Confirmacion) : ResultadoRespuestaBienestar;
    // HU41-CA07: la misma respuesta reenviada; no se registra otra vez ni se crea otro SOS.
    public sealed record YaRegistrada(ConfirmacionBienestar Confirmacion) : ResultadoRespuestaBienestar;
    // Ya respondida con la otra opción: la primera respuesta no cambia.
    public sealed record OtraRespuesta(ConfirmacionBienestar Confirmacion) : ResultadoRespuestaBienestar;
    public sealed record Invalida(Dictionary<string, string[]> Errores) : ResultadoRespuestaBienestar;
    public sealed record NoEncontrada : ResultadoRespuestaBienestar;
}

// HU40 — Solicitar una confirmación de bienestar (S5-T04) y HU41 — Responderla (S5-T05, S5-T06).
public sealed class ConfirmarBienestar(CadDbContext db, EmitirSos emitirSos, TimeProvider reloj, INotificador notificador, ILogger<ConfirmarBienestar> registro)
{
    // Decisión del PO del 16/10/2026: SOS vigente para «Necesito ayuda» = Pendiente, Asignada, En camino o En atención
    // (lo que el ciudadano ve como «Recibido» o «En atención»).
    public static readonly EstadoCaso[] EstadosSosVigente = [EstadoCaso.Pendiente, EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion];
    private const int Intentos = 3;

    // HU40: la autorización (operador, DP04) la aplica la página.
    public async Task<ResultadoSolicitudBienestar> SolicitarAsync(Guid sesionId, Guid operadorId, Guid idSolicitud, CancellationToken ct)
    {
        if (await db.ConfirmacionesBienestar.AsNoTracking().SingleOrDefaultAsync(c => c.OperadorId == operadorId && c.IdSolicitud == idSolicitud, ct)
            is { } repetida)
            return new ResultadoSolicitudBienestar.YaSolicitada(repetida);

        // Bloqueo compartido de la sesión: una finalización o un vencimiento simultáneos esperan, así que la solicitud solo se
        // registra si la sesión estaba activa al recibirla (CA02).
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var sesion = await db.SesionesAcompanamiento
            .FromSql($"SELECT *, xmin FROM \"SesionesAcompanamiento\" WHERE \"Id\" = {sesionId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (sesion is null)
            return new ResultadoSolicitudBienestar.NoEncontrada();
        var ahora = reloj.GetUtcNow();
        if (!sesion.AdmitePosiciones(ahora))
            return new ResultadoSolicitudBienestar.SesionNoActiva();
        if (await db.ConfirmacionesBienestar.AsNoTracking().FirstOrDefaultAsync(c => c.SesionId == sesionId && c.Respuesta == null, ct) is { } pendiente)
            return new ResultadoSolicitudBienestar.OtraPendiente(pendiente);

        var confirmacion = ConfirmacionBienestar.Solicitar(sesion, operadorId, idSolicitud, ahora);
        db.ConfirmacionesBienestar.Add(confirmacion);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Dos solicitudes a la vez: el índice único de pendientes (o el del formulario) deja pasar solo una.
            await transaccion.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await db.ConfirmacionesBienestar.AsNoTracking().SingleOrDefaultAsync(c => c.OperadorId == operadorId && c.IdSolicitud == idSolicitud, ct)
                is { } misma
                ? new ResultadoSolicitudBienestar.YaSolicitada(misma)
                : new ResultadoSolicitudBienestar.OtraPendiente(
                    await db.ConfirmacionesBienestar.AsNoTracking().FirstAsync(c => c.SesionId == sesionId && c.Respuesta == null, ct));
        }

        registro.LogInformation("Confirmación de bienestar {ConfirmacionId} solicitada en el acompañamiento {SesionId}.", confirmacion.Id, sesionId);
        await AvisarAsync(sesionId);
        return new ResultadoSolicitudBienestar.Solicitada(confirmacion);
    }

    // HU41-CA01: la confirmación pendiente de una sesión del titular (la más reciente), o null.
    public Task<ConfirmacionBienestar?> PendienteAsync(Guid ciudadanoId, Guid sesionId, CancellationToken ct) =>
        db.ConfirmacionesBienestar.AsNoTracking()
            .Where(c => c.SesionId == sesionId && c.CiudadanoId == ciudadanoId && c.Respuesta == null)
            .OrderByDescending(c => c.SolicitadaEn).FirstOrDefaultAsync(ct);

    public Task<ConfirmacionBienestar?> DelTitularAsync(Guid ciudadanoId, Guid confirmacionId, CancellationToken ct) =>
        db.ConfirmacionesBienestar.AsNoTracking().SingleOrDefaultAsync(c => c.Id == confirmacionId && c.CiudadanoId == ciudadanoId, ct);

    // HU41-CA01: la app la presentó. Solo cuenta la primera vez. null si no es del titular o no existe.
    public async Task<ConfirmacionBienestar?> RegistrarPresentacionAsync(Guid ciudadanoId, Guid confirmacionId, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var confirmacion = await db.ConfirmacionesBienestar.SingleOrDefaultAsync(c => c.Id == confirmacionId && c.CiudadanoId == ciudadanoId, ct);
            if (confirmacion is null || !confirmacion.RegistrarPresentacion(reloj.GetUtcNow()))
                return confirmacion;
            try
            {
                await db.SaveChangesAsync(ct);
                await AvisarAsync(confirmacion.SesionId);
                return confirmacion;
            }
            catch (DbUpdateConcurrencyException) when (intento < Intentos)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // HU41-CA02/CA03/CA04/CA07. La respuesta se acepta aunque la sesión haya terminado mientras tanto: pedir ayuda nunca se
    // rechaza por eso. «Necesito ayuda» se vincula con el SOS vigente del ciudadano o emite uno con el flujo de HU07/HU08.
    public async Task<ResultadoRespuestaBienestar> ResponderAsync(Guid ciudadanoId, Guid confirmacionId, SolicitudRespuestaBienestar solicitud,
        CancellationToken ct)
    {
        RespuestaBienestar respuesta;
        switch (solicitud.Respuesta)
        {
            case RespuestasBienestar.EstoyBien:
                respuesta = RespuestaBienestar.EstoyBien;
                break;
            case RespuestasBienestar.NecesitoAyuda when solicitud.Sos is { } sos && sos.IdSolicitud != Guid.Empty:
                respuesta = RespuestaBienestar.NecesitoAyuda;
                break;
            case RespuestasBienestar.NecesitoAyuda:
                return new ResultadoRespuestaBienestar.Invalida(new() { ["sos"] = ["Falta la solicitud del SOS (con su identificador)."] });
            default:
                return new ResultadoRespuestaBienestar.Invalida(new() { ["respuesta"] = ["Responde «Estoy bien» o «Necesito ayuda»."] });
        }

        for (var intento = 1; ; intento++)
        {
            var confirmacion = await db.ConfirmacionesBienestar.SingleOrDefaultAsync(c => c.Id == confirmacionId && c.CiudadanoId == ciudadanoId, ct);
            if (confirmacion is null)
                return new ResultadoRespuestaBienestar.NoEncontrada();
            if (!confirmacion.Pendiente)
                return confirmacion.Respuesta == respuesta
                    ? new ResultadoRespuestaBienestar.YaRegistrada(confirmacion)
                    : new ResultadoRespuestaBienestar.OtraRespuesta(confirmacion);

            Guid? casoId = null;
            if (respuesta == RespuestaBienestar.NecesitoAyuda)
            {
                var (vigente, errores) = await SosVigenteOEmitirAsync(ciudadanoId, solicitud.Sos!, ct);
                if (errores is not null)
                    return new ResultadoRespuestaBienestar.Invalida(errores);
                casoId = vigente;
                // EmitirSos usa este mismo contexto (y lo limpia ante un reintento simultáneo): se vuelve a leer la confirmación.
                db.ChangeTracker.Clear();
                confirmacion = await db.ConfirmacionesBienestar.SingleAsync(c => c.Id == confirmacionId, ct);
                if (!confirmacion.Pendiente)
                    continue;
            }

            confirmacion.Responder(respuesta, casoId, reloj.GetUtcNow());
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (intento < Intentos)
            {
                // Otra respuesta (o la presentación) se registró a la vez: se vuelve a evaluar.
                db.ChangeTracker.Clear();
                continue;
            }

            registro.LogInformation("Confirmación de bienestar {ConfirmacionId} respondida: {Respuesta}.", confirmacion.Id, respuesta);
            await AvisarAsync(confirmacion.SesionId);
            return new ResultadoRespuestaBienestar.Registrada(confirmacion);
        }
    }

    // HU41-CA04: un SOS propio vigente se vincula, sin crear otro. CA03: si no hay, se emite con el flujo de HU07/HU08; su
    // IdSolicitud hace que un reenvío devuelva el mismo SOS (CA07, DP14). También lo usa «Necesito ayuda» informado por el
    // ciudadano por su cuenta (InformarEstado). Devuelve el SOS, o los errores de una solicitud de SOS inválida.
    public async Task<(Guid? CasoId, Dictionary<string, string[]>? Errores)> SosVigenteOEmitirAsync(Guid ciudadanoId,
        Cad.Contratos.Sos.SolicitudEmisionSos sos, CancellationToken ct)
    {
        var vigente = await db.Casos.AsNoTracking()
            .Where(c => c.CiudadanoId == ciudadanoId && c.Origen == OrigenCaso.Sos && EstadosSosVigente.Contains(c.Estado))
            .OrderByDescending(c => c.RegistradoEn).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (vigente is not null)
            return (vigente, null);
        return await emitirSos.EjecutarAsync(ciudadanoId, sos, ct) switch
        {
            ResultadoEmisionSos.Registrado r => (r.Caso.Id, null),
            ResultadoEmisionSos.YaRegistrado r => (r.Caso.Id, null),
            ResultadoEmisionSos.Invalido i => (null, i.Errores),
            _ => throw new InvalidOperationException("Resultado de la emisión del SOS no contemplado."),
        };
    }

    // 24/10/2026: después del COMMIT, la central vuelve a leer la sesión. Un fallo del canal no afecta al registro.
    private async Task AvisarAsync(Guid sesionId)
    {
        try
        {
            await notificador.AcompanamientoActualizadoAsync(sesionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogWarning(ex, "No se pudo avisar a la central del acompañamiento {SesionId}; se verá al actualizar.", sesionId);
        }
    }
}
