namespace Cad.Server.Compartido;

// Avisos en tiempo real a los clientes (Plan de Arquitectura, 3.8 y ADR-04).
// Se llaman siempre DESPUÉS de confirmar la transacción: recibir un aviso no equivale a persistir,
// y si un aviso se pierde, los clientes recuperan el estado desde la base de datos.
public interface INotificador
{
    // HU11: un SOS nuevo quedó registrado y debe aparecer en la sección urgente de la central.
    Task SosRegistradoAsync(Guid casoId, CancellationToken ct);

    // HU19-CA03 (y HU18-CA04): cambió el estado operativo, la posición o la disponibilidad de un sereno.
    Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct);
}
