using Cad.Contratos.Serenos;

namespace Cad.Movil.Servicios;

// HU21-CA01 (ADR-07) — notificaciones push del despacho en el modo sereno. El permiso de notificaciones (Android 13
// o superior) se pide solo después de explicarlo, como el de ubicación (HU06). Con permiso, el token del dispositivo se
// registra en el servidor al abrir el modo sereno y se quita al salir de la sesión. El token nunca se muestra ni se
// registra en ningún log.
public sealed class ServicioNotificaciones
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly IServicioPush push;
    private readonly TimeProvider reloj;
    private readonly SemaphoreSlim registro = new(1, 1);

    public ServicioNotificaciones(IApiCad api, IAlmacenSesion sesiones, IServicioPush push, TimeProvider reloj)
    {
        (this.api, this.sesiones, this.push, this.reloj) = (api, sesiones, push, reloj);
        push.TokenRenovado += token => _ = RegistrarSinExcepcionesAsync(token);
    }

    public event Action? Cambio;

    // Hay Firebase configurado y Google Play Services en el dispositivo.
    public bool Disponible => push.Disponible;
    public EstadoPermisoNotificaciones Permiso { get; private set; } = EstadoPermisoNotificaciones.Desconocido;
    // El servidor tiene registrado este dispositivo para el sereno de la sesión.
    public bool Registrado { get; private set; }
    public string? Aviso { get; private set; }

    // Al abrir el modo sereno: consulta el permiso (sin pedirlo) y, si está concedido, registra el dispositivo.
    public async Task PrepararAsync(CancellationToken ct = default)
    {
        if (!push.Disponible)
            return;
        Permiso = await push.ConsultarPermisoAsync();
        if (Permiso == EstadoPermisoNotificaciones.Concedido)
            await RegistrarAsync(null, ct);
        Cambio?.Invoke();
    }

    // Después de la explicación previa: pide el permiso y, si se concede, registra el dispositivo.
    public async Task<bool> PermitirAsync(CancellationToken ct = default)
    {
        if (!push.Disponible)
            return false;
        Permiso = await push.SolicitarPermisoAsync();
        if (Permiso == EstadoPermisoNotificaciones.Concedido)
            await RegistrarAsync(null, ct);
        Cambio?.Invoke();
        return Registrado;
    }

    // Al salir de la sesión: el teléfono deja de recibir los avisos de este sereno. Si no hay conexión, el servidor
    // sigue teniendo el token hasta que otro sereno ingrese en este teléfono o FCM lo dé por inválido.
    public async Task QuitarAsync()
    {
        if (Registrado && await sesiones.LeerAsync() is { } sesion && sesion.Vigente(reloj.GetUtcNow()))
        {
            try { await api.QuitarDispositivoAsync(sesion.Token, CancellationToken.None); }
            catch (Exception) { /* sin conexión: ver arriba */ }
        }
        (Registrado, Aviso, Permiso) = (false, null, EstadoPermisoNotificaciones.Desconocido);
        Cambio?.Invoke();
    }

    private async Task RegistrarAsync(string? tokenConocido, CancellationToken ct)
    {
        await registro.WaitAsync(ct);
        try
        {
            if (await sesiones.LeerAsync() is not { } sesion || !sesion.Vigente(reloj.GetUtcNow()))
                return;
            var token = tokenConocido ?? await push.ObtenerTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token))
            {
                Aviso = "No se pudo activar el aviso de despachos en este teléfono. Los verás al abrir la app.";
                Registrado = false;
                return;
            }
            switch (await api.RegistrarDispositivoAsync(sesion.Token, new SolicitudRegistroDispositivo(token), ct))
            {
                case ResultadoApi<SinContenido>.Correcto:
                    (Registrado, Aviso) = (true, null);
                    break;
                case ResultadoApi<SinContenido>.Rechazado { Estado: 401 }:
                    Registrado = false; // el fin de la sesión lo informa el turno del sereno
                    break;
                default:
                    (Registrado, Aviso) = (false, "No se pudo activar el aviso de despachos: sin conexión con el servidor. Se reintentará al volver a abrir la app.");
                    break;
            }
        }
        finally
        {
            registro.Release();
        }
    }

    private async Task RegistrarSinExcepcionesAsync(string token)
    {
        try
        {
            if (Permiso == EstadoPermisoNotificaciones.Concedido)
                await RegistrarAsync(token, CancellationToken.None);
        }
        catch (Exception)
        {
            Aviso = "No se pudo actualizar el aviso de despachos en este teléfono.";
        }
        Cambio?.Invoke();
    }
}

// Sin Firebase (pruebas en net10.0, otras plataformas o un equipo sin la configuración): no hay push.
public sealed class ServicioPushNoDisponible : IServicioPush
{
    public bool Disponible => false;
    public Task<EstadoPermisoNotificaciones> ConsultarPermisoAsync() => Task.FromResult(EstadoPermisoNotificaciones.Denegado);
    public Task<EstadoPermisoNotificaciones> SolicitarPermisoAsync() => Task.FromResult(EstadoPermisoNotificaciones.Denegado);
    public Task<string?> ObtenerTokenAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    public event Action<string>? TokenRenovado { add { } remove { } }
}
