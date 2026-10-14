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
    public const string EventoSerenoActualizado = "SerenoActualizado";
    public const string EventoCasoActualizado = "CasoActualizado";

    // RNF02: el panel confirma que el SOS ya se muestra. Sin efecto si la medición está desactivada.
    public Task ConfirmarPresentacion(Guid casoId, Modulos.Mediciones.RegistrarPresentacion registrar) =>
        registrar.EjecutarAsync(casoId, Context.User!.IdUsuario(), Context.ConnectionAborted);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GrupoOperadores);
        await base.OnConnectedAsync();
    }
}

public sealed class NotificadorSignalR(IHubContext<CentralHub> central, IHubContext<SerenoHub> serenos) : INotificador
{
    // HU21: sin datos del caso; la app los consulta con la sesión del sereno (DP06).
    public Task DespachoAsignadoAsync(Guid serenoId, CancellationToken ct) =>
        serenos.Clients.Group(SerenoHub.Grupo(serenoId)).SendAsync(SerenoHub.EventoDespachoAsignado, ct);

    // Solo el identificador: el panel lee el detalle de la base de datos (fuente de verdad).
    public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) =>
        central.Clients.Group(CentralHub.GrupoOperadores).SendAsync(CentralHub.EventoSosRecibido, new { id = casoId }, ct);

    public Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct) =>
        central.Clients.Group(CentralHub.GrupoOperadores).SendAsync(CentralHub.EventoSerenoActualizado, new { id = serenoId }, ct);

    public Task CasoActualizadoAsync(Guid casoId, CancellationToken ct) =>
        central.Clients.Group(CentralHub.GrupoOperadores).SendAsync(CentralHub.EventoCasoActualizado, new { id = casoId }, ct);
}
