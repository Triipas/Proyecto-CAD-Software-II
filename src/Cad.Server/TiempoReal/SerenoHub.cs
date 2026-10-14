using Cad.Server.Compartido;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Cad.Server.TiempoReal;

// HU21-CA01 — canal de la app del sereno, con la app abierta (ADR-07, punto 2). Solo serenos, autenticados con
// su JWT en la cabecera Authorization: el servidor no acepta el token en la URL, que acabaría en los registros.
// Cada conexión entra solo en el grupo de su propio sereno: nadie recibe avisos de otro.
[Authorize(Policy = Politicas.Sereno)]
public sealed class SerenoHub : Hub
{
    public const string Ruta = "/hubs/sereno";
    public const string EventoDespachoAsignado = "DespachoAsignado";

    public static string Grupo(Guid serenoId) => $"sereno:{serenoId}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(Context.User!.IdUsuario()));
        await base.OnConnectedAsync();
    }
}
