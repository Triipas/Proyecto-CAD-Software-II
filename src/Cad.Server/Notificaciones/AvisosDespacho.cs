using System.Threading.Channels;
using Cad.Server.Compartido;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Notificaciones;

// HU21-CA01 — avisos del despacho al sereno asignado. AsignarCaso los encola después del COMMIT y un servicio en
// segundo plano los envía: el operador no espera a FCM. La cola está en memoria (una sola instancia del servidor);
// si un aviso se pierde en un reinicio, el despacho sigue visible al abrir la app (ADR-07, punto 4).
public sealed class ColaAvisosDespacho
{
    private readonly Channel<Guid> canal = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Encolar(Guid serenoId) => canal.Writer.TryWrite(serenoId);

    public IAsyncEnumerable<Guid> LeerAsync(CancellationToken ct) => canal.Reader.ReadAllAsync(ct);
}

public sealed class ServicioAvisosDespacho(ColaAvisosDespacho cola, IServiceScopeFactory alcances, ILogger<ServicioAvisosDespacho> registro)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        await foreach (var serenoId in cola.LeerAsync(detener))
        {
            try
            {
                using var alcance = alcances.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<AvisarDespacho>().EjecutarAsync(serenoId, detener);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogError(ex, "No se pudo avisar al sereno {SerenoId} de su despacho.", serenoId);
            }
        }
    }
}

// Un aviso: por el canal en tiempo real (app abierta) y, si hay credencial y dispositivo, por push.
public sealed class AvisarDespacho(CadDbContext db, INotificador notificador, IEnviadorPush push, ILogger<AvisarDespacho> registro)
{
    // Límite para no retener el servicio si FCM no responde.
    private static readonly TimeSpan EsperaPush = TimeSpan.FromSeconds(10);

    public async Task EjecutarAsync(Guid serenoId, CancellationToken ct)
    {
        try
        {
            await notificador.DespachoAsignadoAsync(serenoId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogError(ex, "No se pudo enviar el aviso en tiempo real del despacho al sereno {SerenoId}.", serenoId);
        }

        if (!push.Configurado)
            return;

        var dispositivo = await db.DispositivosSereno.SingleOrDefaultAsync(d => d.SerenoId == serenoId, ct);
        if (dispositivo is null)
        {
            registro.LogInformation("El sereno {SerenoId} no tiene un dispositivo registrado para la notificación push.", serenoId);
            return;
        }

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(EsperaPush);
        var resultado = await push.EnviarDespachoAsync(dispositivo.Token, limite.Token);
        if (resultado == ResultadoPush.TokenInvalido)
        {
            // La app se desinstaló o renovó el token: se borra para no reintentar con él.
            db.DispositivosSereno.Remove(dispositivo);
            await db.SaveChangesAsync(ct);
        }
        registro.LogInformation("Notificación push del despacho al sereno {SerenoId}: {Resultado}.", serenoId, resultado);
    }
}
