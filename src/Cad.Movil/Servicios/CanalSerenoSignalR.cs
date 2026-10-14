using Microsoft.AspNetCore.SignalR.Client;

namespace Cad.Movil.Servicios;

// HU21-CA01 — conexión al canal /hubs/sereno. El token viaja en la cabecera Authorization (el cliente .NET lo hace
// así también con WebSockets), nunca en la URL. Tras perder la conexión se reconecta solo y avisa para que la app
// vuelva a consultar el despacho.
public sealed class CanalSerenoSignalR(IConfiguracionServidor configuracion, IAlmacenSesion sesiones) : ICanalSereno, IAsyncDisposable
{
    private readonly SemaphoreSlim cambio = new(1, 1);
    private HubConnection? conexion;

    public event Action? DespachoAsignado;
    public event Action? Reconectado;

    public async Task<bool> ConectarAsync(CancellationToken ct)
    {
        await cambio.WaitAsync(ct);
        try
        {
            if (conexion is { State: not HubConnectionState.Disconnected })
                return true;
            if (configuracion.Direccion is not { } direccion)
                return false;

            if (conexion is null)
            {
                conexion = new HubConnectionBuilder()
                    .WithUrl(new Uri(direccion, "hubs/sereno"), opciones =>
                        opciones.AccessTokenProvider = async () => (await sesiones.LeerAsync())?.Token)
                    .WithAutomaticReconnect()
                    .Build();
                conexion.On("DespachoAsignado", () => DespachoAsignado?.Invoke());
                conexion.Reconnected += _ =>
                {
                    Reconectado?.Invoke();
                    return Task.CompletedTask;
                };
            }

            await conexion.StartAsync(ct);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or System.Net.WebException or IOException or TimeoutException
                                      or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            cambio.Release();
        }
    }

    public async Task DesconectarAsync()
    {
        await cambio.WaitAsync();
        try
        {
            if (conexion is null)
                return;
            await conexion.DisposeAsync();
            conexion = null;
        }
        finally
        {
            cambio.Release();
        }
    }

    public async ValueTask DisposeAsync() => await DesconectarAsync();
}
