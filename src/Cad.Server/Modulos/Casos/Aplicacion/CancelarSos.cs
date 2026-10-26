using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoCancelacion
{
    public sealed record Cancelado(CancelacionSos Cancelacion) : ResultadoCancelacion;
    // Reenvío de la misma cancelación: el SOS ya estaba cancelado; se devuelve la cancelación registrada.
    public sealed record YaCancelado(CancelacionSos Cancelacion) : ResultadoCancelacion;
    // CA03: el SOS ya no está pendiente (fue asignado o avanzó): no cambia.
    public sealed record Rechazado(string Motivo) : ResultadoCancelacion;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCancelacion;
    public sealed record NoEncontrado : ResultadoCancelacion;
}

// HU09 — Cancelar un SOS pendiente (S5-T01, S5-T02). Solo el titular (un SOS ajeno responde igual que uno inexistente) y solo
// mientras está «Pendiente». Si un operador lo asigna a la vez, el token xmin del caso hace que prevalezca uno: o queda
// cancelado y no se asigna (HU20), o queda asignado y la cancelación se rechaza (CA03). No finaliza un acompañamiento (alcance).
public sealed class CancelarSos(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<CancelarSos> registro)
{
    public const string MotivoNoPendiente = "El SOS ya no está pendiente: la central lo asignó a un sereno y no se puede cancelar.";
    public const string MotivoReporteDecidido = "La central ya decidió sobre el reporte: no se puede retirar.";
    public const string MotivoCambioConcurrente = "El SOS cambió mientras se cancelaba. Revisa su estado e inténtalo de nuevo.";
    private const int Intentos = 3;

    public async Task<ResultadoCancelacion> EjecutarAsync(Guid ciudadanoId, Guid casoId, string? motivo, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId && c.CiudadanoId == ciudadanoId
                && (c.Origen == OrigenCaso.Sos || c.Origen == OrigenCaso.Reporte), ct);
            if (caso is null)
                return new ResultadoCancelacion.NoEncontrado();
            if (caso.Estado == EstadoCaso.Cancelada)
                return new ResultadoCancelacion.YaCancelado(await db.CancelacionesSos.AsNoTracking().SingleAsync(c => c.CasoId == casoId, ct));
            var esReporte = caso.Origen == OrigenCaso.Reporte;
            if (!caso.PuedeCancelarse)
                return new ResultadoCancelacion.Rechazado(esReporte ? MotivoReporteDecidido : MotivoNoPendiente);

            // CA01: el motivo es obligatorio.
            if (string.IsNullOrWhiteSpace(motivo))
                return new ResultadoCancelacion.Invalido(new() { ["motivo"] = ["Indica el motivo de la cancelación."] });
            if (motivo.Trim().Length > CancelacionSos.MaximoMotivo)
                return new ResultadoCancelacion.Invalido(new() { ["motivo"] = [$"El motivo admite hasta {CancelacionSos.MaximoMotivo} caracteres."] });

            var ahora = reloj.GetUtcNow();
            caso.Cancelar();
            var cancelacion = CancelacionSos.Registrar(caso, motivo, ahora);
            db.CancelacionesSos.Add(cancelacion);
            db.EventosCaso.Add(EventoCaso.Registrar(caso, esReporte ? TipoEventoCaso.ReporteRetirado : TipoEventoCaso.SosCancelado, ciudadanoId, VisibilidadEvento.Publica, ahora));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException d && d.EsViolacionDeUnicidad())
            {
                // S5-T02: otra transacción cambió el caso (una asignación o una cancelación simultánea): se vuelve a evaluar
                // con los datos nuevos. No queda nada parcial: caso, cancelación y evento van en la misma transacción.
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoCancelacion.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation(esReporte ? "Reporte {CasoId} retirado por su titular." : "SOS {CasoId} cancelado por su titular.", casoId);
            // CA02: aviso a la central después de confirmar (la sección urgente lo retira y lo anuncia; un reporte sale de «Reportes»).
            try
            {
                if (esReporte)
                    await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
                else
                    await notificador.SosCanceladoAsync(casoId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                registro.LogError(ex, "No se pudo avisar a la central de la cancelación del SOS {CasoId}; se mostrará al resincronizar.", casoId);
            }
            return new ResultadoCancelacion.Cancelado(cancelacion);
        }
    }
}
