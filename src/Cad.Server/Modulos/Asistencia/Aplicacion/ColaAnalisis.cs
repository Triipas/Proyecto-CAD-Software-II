using System.Threading.Channels;
using Cad.Server.Compartido;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// S6-T11 — la cola de análisis, con el mismo patrón que los avisos del despacho (ColaAvisosDespacho): en memoria y con un solo
// lector, porque hay una sola instancia del servidor. Si un pedido se pierde en un reinicio, ese reporte queda sin sugerencia y
// el operador lo trabaja como siempre.
public sealed class ColaAnalisis : IColaAnalisisReportes
{
    private readonly Channel<Guid> canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Encolar(Guid casoId) => canal.Writer.TryWrite(casoId);

    public IAsyncEnumerable<Guid> LeerAsync(CancellationToken ct) => canal.Reader.ReadAllAsync(ct);
}

public sealed class ServicioAnalisis(ColaAnalisis cola, IServiceScopeFactory alcances, ILogger<ServicioAnalisis> registro) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        await foreach (var casoId in cola.LeerAsync(detener))
        {
            try
            {
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<AnalizarReporte>().EjecutarAsync(casoId, detener);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !detener.IsCancellationRequested)
            {
                // Solo el tipo de error: el mensaje podría arrastrar parte de una respuesta (RNF07).
                registro.LogError("No se pudo analizar el reporte {CasoId} ({Error}); queda sin sugerencia.", casoId, ex.GetType().Name);
            }
        }
    }
}
