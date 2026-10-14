using Cad.Contratos.Serenos;

namespace Cad.Movil.Servicios;

public enum ResultadoActivacion { Activado, NecesitaExplicarPermiso, PermisoDenegado, SesionTerminada, Fallo }

// HU18 — Activar mi estado operativo y compartir mi ubicación (lado de la app, S2-T02/S2-T03).
// El estado operativo lo decide el servidor; la app lo consulta al abrirse y, mientras sea operativo,
// envía su posición actual cada «intervalo» (DP15). Según SPIKE01, solo es fiable con la app abierta y con
// conexión: si una lectura o un envío falla, se omite y se avisa (no se acumulan posiciones antiguas).
public sealed class ServicioTurnoSereno(IApiCad api, IServicioUbicacion ubicacion, IAlmacenSesion sesiones, TimeProvider reloj)
{
    // Igual que en el SOS: una lectura de ubicación nunca espera más de 10 s (DP11, hallazgo de SPIKE01).
    public static readonly TimeSpan EsperaUbicacionMaxima = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim cambio = new(1, 1);
    private readonly Lock bloqueo = new();
    private CancellationTokenSource? envio;
    private Task? tareaEnvio;

    public event Action? Cambio;

    // Estado confirmado por el servidor.
    public bool Operativo { get; private set; }
    // El bucle de envío está en marcha en este dispositivo.
    public bool Transmitiendo => envio is not null;
    public int IntervaloSegundos { get; private set; } = 30;
    public DateTimeOffset? UltimoEnvio { get; private set; }
    // Aviso del último intento de envío (sin posición, sin conexión…); null si fue bien.
    public string? AvisoEnvio { get; private set; }
    // Error de la última acción del usuario (activar, desactivar, cargar).
    public string? Error { get; private set; }
    public bool SesionTerminada { get; private set; }

    public Task<EstadoPermisoUbicacion> ConsultarPermisoAsync() => ubicacion.ConsultarPermisoAsync();
    public Task<EstadoPermisoUbicacion> SolicitarPermisoAsync() => ubicacion.SolicitarPermisoAsync();

    // Al abrir el modo sereno: el estado vive en el servidor. Si ya estaba operativo (p. ej., la app se
    // reinició), se reanuda el envío cuando hay permiso; si no lo hay, no se envía nada (HU18-CA03).
    public async Task CargarAsync(CancellationToken ct = default)
    {
        if (await TokenAsync() is not { } token)
            return;
        switch (await api.ConsultarEstadoOperativoAsync(token, ct))
        {
            case ResultadoApi<RespuestaEstadoOperativo>.Correcto c:
                Aplicar(c.Valor);
                Error = null;
                if (Operativo)
                {
                    if (await ubicacion.ConsultarPermisoAsync() == EstadoPermisoUbicacion.Concedido)
                        IniciarEnvio();
                    else
                        AvisoEnvio = "Sin permiso de ubicación: no se envían posiciones. Concede el permiso o desactiva tu estado.";
                }
                break;
            case ResultadoApi<RespuestaEstadoOperativo>.Rechazado { Estado: 401 }:
                MarcarSesionTerminada();
                break;
            case ResultadoApi<RespuestaEstadoOperativo>.Rechazado r:
                Error = r.Mensaje;
                break;
            case ResultadoApi<RespuestaEstadoOperativo>.SinRespuesta s:
                Error = $"No se pudo consultar tu estado: {s.Motivo}";
                break;
        }
        Notificar();
    }

    // DP16-a: sin permiso de ubicación concedido no se activa (ni se llama al servidor).
    public async Task<ResultadoActivacion> ActivarAsync(CancellationToken ct = default)
    {
        if (!await cambio.WaitAsync(0, ct))
            return ResultadoActivacion.Fallo;
        try
        {
            switch (await ubicacion.ConsultarPermisoAsync())
            {
                case EstadoPermisoUbicacion.Desconocido:
                    return ResultadoActivacion.NecesitaExplicarPermiso;
                case EstadoPermisoUbicacion.Denegado or EstadoPermisoUbicacion.Restringido:
                    return ResultadoActivacion.PermisoDenegado;
            }

            if (await TokenAsync() is not { } token)
                return ResultadoActivacion.SesionTerminada;

            var resultado = await api.CambiarEstadoOperativoAsync(token, new SolicitudCambioEstadoOperativo(true, true), ct);
            switch (resultado)
            {
                case ResultadoApi<RespuestaEstadoOperativo>.Correcto c:
                    Aplicar(c.Valor);
                    Error = null;
                    IniciarEnvio();
                    return ResultadoActivacion.Activado;
                case ResultadoApi<RespuestaEstadoOperativo>.Rechazado { Estado: 401 }:
                    MarcarSesionTerminada();
                    return ResultadoActivacion.SesionTerminada;
                case ResultadoApi<RespuestaEstadoOperativo>.Rechazado r:
                    Error = r.Mensaje;
                    return ResultadoActivacion.Fallo;
                case ResultadoApi<RespuestaEstadoOperativo>.SinRespuesta s:
                    Error = $"No se pudo activar tu estado: {s.Motivo}";
                    return ResultadoActivacion.Fallo;
            }
            return ResultadoActivacion.Fallo;
        }
        finally
        {
            cambio.Release();
            Notificar();
        }
    }

    // CA02: primero se detiene el envío en el dispositivo; después se informa al servidor. Si el servidor
    // no responde, el envío sigue detenido y el usuario puede reintentar la desactivación.
    // DP16-b: desactivarse siempre está permitido; una asignación vigente sigue vigente.
    public async Task DesactivarAsync(CancellationToken ct = default)
    {
        if (!await cambio.WaitAsync(0, ct))
            return;
        try
        {
            await DetenerEnvioAsync();
            if (await TokenAsync() is not { } token)
                return;

            var permiso = await ubicacion.ConsultarPermisoAsync() == EstadoPermisoUbicacion.Concedido;
            switch (await api.CambiarEstadoOperativoAsync(token, new SolicitudCambioEstadoOperativo(false, permiso), ct))
            {
                case ResultadoApi<RespuestaEstadoOperativo>.Correcto c:
                    Aplicar(c.Valor);
                    Error = null;
                    AvisoEnvio = null;
                    break;
                case ResultadoApi<RespuestaEstadoOperativo>.Rechazado { Estado: 401 }:
                    MarcarSesionTerminada();
                    break;
                case ResultadoApi<RespuestaEstadoOperativo>.Rechazado r:
                    Error = r.Mensaje;
                    break;
                case ResultadoApi<RespuestaEstadoOperativo>.SinRespuesta s:
                    Error = $"Se detuvo el envío de tu ubicación, pero no se pudo confirmar la desactivación: {s.Motivo} Reinténtalo.";
                    break;
            }
        }
        finally
        {
            cambio.Release();
            Notificar();
        }
    }

    // Al salir de la sesión o cerrar el modo sereno: deja de enviar sin cambiar el estado en el servidor.
    public Task DetenerAsync() => DetenerEnvioAsync();

    private void IniciarEnvio()
    {
        lock (bloqueo)
        {
            if (envio is not null)
                return;
            var propio = new CancellationTokenSource();
            envio = propio;
            var intervalo = TimeSpan.FromSeconds(Math.Max(1, IntervaloSegundos));
            tareaEnvio = Task.Run(() => BucleEnvioAsync(intervalo, propio), propio.Token);
        }
    }

    private async Task DetenerEnvioAsync()
    {
        CancellationTokenSource? actual;
        Task? tarea;
        lock (bloqueo)
        {
            (actual, tarea) = (envio, tareaEnvio);
            (envio, tareaEnvio) = (null, null);
        }
        if (actual is null)
            return;
        await actual.CancelAsync();
        try { if (tarea is not null) await tarea; } catch (OperationCanceledException) { }
        actual.Dispose();
    }

    // CA01: una posición actual al activarse y después una cada intervalo, mientras siga operativo.
    private async Task BucleEnvioAsync(TimeSpan intervalo, CancellationTokenSource propio)
    {
        var ct = propio.Token;
        using var temporizador = new PeriodicTimer(intervalo, reloj);
        do
        {
            if (!await EnviarUnaPosicionAsync(intervalo, ct))
            {
                // El servidor ya no lo considera operativo, o la sesión terminó: el envío se detiene.
                // Solo se retira este bucle (si entre tanto se activó otro, no se toca).
                var retirado = false;
                lock (bloqueo)
                {
                    if (envio == propio)
                    {
                        (envio, tareaEnvio) = (null, null);
                        retirado = true;
                    }
                }
                if (retirado)
                    propio.Dispose();
                Notificar();
                return;
            }
            Notificar();
        }
        while (await temporizador.WaitForNextTickAsync(ct));
    }

    // Devuelve false si hay que detener el envío.
    private async Task<bool> EnviarUnaPosicionAsync(TimeSpan intervalo, CancellationToken ct)
    {
        if (await TokenAsync() is not { } token)
            return false;

        if (await ubicacion.ConsultarPermisoAsync() != EstadoPermisoUbicacion.Concedido)
        {
            AvisoEnvio = "Sin permiso de ubicación: no se envió la posición.";
            return true;
        }

        var espera = intervalo < EsperaUbicacionMaxima ? intervalo : EsperaUbicacionMaxima;
        Contratos.Sos.UbicacionSos? lectura;
        try
        {
            lectura = await ubicacion.ObtenerActualAsync(espera, ct).WaitAsync(espera, reloj, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            lectura = null;
        }
        if (lectura is null)
        {
            AvisoEnvio = "No se pudo obtener tu posición en el último intento.";
            return true;
        }

        ResultadoApi<SinContenido> resultado;
        try
        {
            resultado = await api.EnviarPosicionSerenoAsync(token,
                new PosicionSerenoEnviada(lectura.Latitud, lectura.Longitud, lectura.PrecisionMetros, lectura.ObtenidaEn), ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            resultado = new ResultadoApi<SinContenido>.SinRespuesta(e.GetType().Name);
        }

        switch (resultado)
        {
            case ResultadoApi<SinContenido>.Correcto:
                UltimoEnvio = reloj.GetUtcNow();
                AvisoEnvio = null;
                return true;
            case ResultadoApi<SinContenido>.Rechazado { Estado: 409 }:
                Operativo = false;
                AvisoEnvio = null;
                Error = "El servidor indica que tu estado no es operativo: se detuvo el envío de tu ubicación.";
                return false;
            case ResultadoApi<SinContenido>.Rechazado { Estado: 401 }:
                MarcarSesionTerminada();
                return false;
            case ResultadoApi<SinContenido>.Rechazado r:
                AvisoEnvio = $"La posición no se aceptó: {r.Mensaje}";
                return true;
            default:
                AvisoEnvio = "Sin conexión con el servidor: la última posición no se envió. Se volverá a intentar.";
                return true;
        }
    }

    private void Aplicar(RespuestaEstadoOperativo estado)
    {
        Operativo = estado.Operativo;
        IntervaloSegundos = estado.IntervaloPosicionesSegundos;
    }

    private void MarcarSesionTerminada()
    {
        SesionTerminada = true;
        Error = "Tu sesión terminó. Vuelve a ingresar.";
    }

    private async Task<string?> TokenAsync()
    {
        var sesion = await sesiones.LeerAsync();
        if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
        {
            MarcarSesionTerminada();
            return null;
        }
        return sesion.Token;
    }

    private void Notificar() => Cambio?.Invoke();
}
