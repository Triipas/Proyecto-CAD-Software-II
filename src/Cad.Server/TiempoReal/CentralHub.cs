using Cad.Server.Compartido;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cad.Server.TiempoReal;

// Canal de la central: solo operadores autorizados (DP04). Solo envía eventos; las escrituras van
// siempre por HTTP (ADR-04). Un cliente no elige grupos: el servidor lo asigna al conectarse (HU03).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public sealed class CentralHub : Hub
{
    public const string Ruta = "/hubs/central";
    public const string GrupoOperadores = "operadores";
    public const string EventoSosRecibido = "SosRecibido";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoOperadores);
        await base.OnConnectedAsync();
    }
}

public sealed class NotificadorSignalR(IHubContext<CentralHub> central) : INotificador
{
    // Solo el identificador: el panel lee el detalle de la base de datos (fuente de verdad).
    public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) =>
        central.Clients.Group(CentralHub.GrupoOperadores).SendAsync(CentralHub.EventoSosRecibido, new { id = casoId }, ct);
}
