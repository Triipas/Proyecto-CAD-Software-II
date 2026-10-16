using System.Globalization;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;

namespace Cad.Movil.Servicios;

public enum EstadoAcompanamiento
{
    // Sin sesión en este teléfono.
    SinSesion,
    // Esperando la respuesta del servidor al inicio.
    Iniciando,
    // El servidor no respondió al inicio: no se muestra activa ni se transmite (HU34-CA07).
    NoConfirmado,
    Activo,
    // El ciudadano la finalizó: se dejó de transmitir y se está comunicando el cierre.
    Finalizando,
    // Finalizada en el teléfono, pero el cierre aún no llegó al servidor (HU36-CA03; efecto en la central: DP20).
    CierrePendiente,
    Finalizado,
    Vencido,
}

public enum ResultadoInicioAcompanamiento
{
    Iniciado, FaltaFinalidad, NecesitaExplicarPermiso, PermisoDenegado, SinUbicacion, OtraSesionActiva, NoConfirmado, Rechazado, SesionTerminada,
}

// Almacén local de la app (Preferences en Android). Guarda el cierre pendiente aunque se cierre la app (HU36-CA03).
public interface IAlmacenLocal
{
    string? Leer(string clave);
    void Guardar(string clave, string valor);
    void Borrar(string clave);
}

// HU34–HU37 (lado de la app, S4-T01–S4-T07). La sesión la registra y la vence el servidor; la app la muestra, transmite la
// última posición cada intervalo (15 s) solo mientras está activa y, al finalizar, deja de transmitir al instante.
// Según SPIKE01 la transmisión solo es fiable con la app abierta y con conexión (DP13, pendiente): la interfaz lo dice.
public sealed class ServicioAcompanamiento(IApiCad api, IServicioUbicacion ubicacion, IAlmacenSesion sesiones, IAlmacenLocal almacen,
    TimeProvider reloj)
{
    public const string ClaveCierrePendiente = "cad.acompanamiento.cierre-pendiente";
    // Igual que en el SOS y en HU18: una lectura de ubicación nunca espera más de 10 s (DP11, SPIKE01).
    public static readonly TimeSpan EsperaUbicacionMaxima = TimeSpan.FromSeconds(10);
    // Cada cuánto se reintenta comunicar un cierre pendiente mientras la app está abierta.
    public static readonly TimeSpan ReintentoCierre = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim accion = new(1, 1);
    private readonly Lock bloqueo = new();
    private CancellationTokenSource? envio;
    private Task? tareaEnvio;
    private CancellationTokenSource? reintento;
    private Guid? idSolicitudPendiente;
    private UbicacionSos? ultimaPosicion;
    // Hora del servidor menos hora del teléfono: el tiempo restante no depende de un reloj del teléfono desajustado.
    private TimeSpan desfase;

    public event Action? Cambio;

    public EstadoAcompanamiento Estado { get; private set; } = EstadoAcompanamiento.SinSesion;
    public SesionAcompanamientoVista? Sesion { get; private set; }
    public DateTimeOffset? UltimoEnvio { get; private set; }
    // HU35-CA04: condición real de la transmisión (sin conexión, sin permiso, sin posiciones nuevas); null si el último envío fue bien.
    public string? Condicion { get; private set; }
    public string? Error { get; private set; }
    public bool SesionTerminada { get; private set; }
    public bool Transmitiendo => envio is not null;

    public bool Activo => Estado == EstadoAcompanamiento.Activo;

    // HU34-CA01/CA08: tiempo restante según la hora del servidor; null si no hay sesión activa.
    public TimeSpan? Restante => Activo && Sesion is { } s
        ? Max(TimeSpan.Zero, s.VenceEn - (reloj.GetUtcNow() + desfase))
        : null;

    // Al abrir la pantalla o el inicio: primero se comunica un cierre pendiente; después se consulta la sesión activa.
    public async Task CargarAsync(CancellationToken ct = default)
    {
        await accion.WaitAsync(ct);
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            if (Pendiente() is not null)
            {
                await EnviarCierreAsync(token, ct);
                if (Estado == EstadoAcompanamiento.CierrePendiente)
                    return;
            }

            switch (await api.ConsultarAcompanamientoActualAsync(token, ct))
            {
                case ResultadoApi<SesionAcompanamientoVista?>.Correcto { Valor: { } activa }:
                    Aplicar(activa);
                    Error = null;
                    if (await ubicacion.ConsultarPermisoAsync() == EstadoPermisoUbicacion.Concedido)
                        IniciarEnvio();
                    else
                        Condicion = "Sin permiso de ubicación: tu posición no se está compartiendo. Concede el permiso o finaliza el acompañamiento.";
                    break;
                case ResultadoApi<SesionAcompanamientoVista?>.Correcto:
                    // HU37-CA06: la sesión que se veía activa ya terminó (p. ej., venció sin conexión): se muestra cómo terminó.
                    await DetenerEnvioAsync();
                    if (Sesion is { } conocida && Estado == EstadoAcompanamiento.Activo)
                        await ConsultarFinAsync(token, conocida.Id, ct);
                    else if (Estado is EstadoAcompanamiento.Activo or EstadoAcompanamiento.Iniciando)
                        Estado = EstadoAcompanamiento.SinSesion;
                    Error = null;
                    break;
                case ResultadoApi<SesionAcompanamientoVista?>.Rechazado { Estado: 401 }:
                    MarcarSesionTerminada();
                    break;
                case ResultadoApi<SesionAcompanamientoVista?>.Rechazado r:
                    Error = r.Mensaje;
                    break;
                case ResultadoApi<SesionAcompanamientoVista?>.SinRespuesta s:
                    Error = $"No se pudo consultar tu acompañamiento: {s.Motivo}";
                    break;
            }
        }
        finally
        {
            accion.Release();
            Notificar();
        }
    }

    // HU34: el botón independiente. Antes de llamar al servidor se exige la finalidad aceptada (CA02), el permiso y una
    // posición actual (CA03). La sesión solo se muestra activa y solo se transmite tras la respuesta del servidor (CA07).
    public async Task<ResultadoInicioAcompanamiento> IniciarAsync(bool finalidadAceptada, CancellationToken ct = default)
    {
        if (!finalidadAceptada)
            return ResultadoInicioAcompanamiento.FaltaFinalidad;
        if (!await accion.WaitAsync(0, ct))
            return ResultadoInicioAcompanamiento.Rechazado;
        try
        {
            if (Estado is EstadoAcompanamiento.Activo or EstadoAcompanamiento.Finalizando or EstadoAcompanamiento.CierrePendiente)
                return ResultadoInicioAcompanamiento.OtraSesionActiva;

            switch (await ubicacion.ConsultarPermisoAsync())
            {
                case EstadoPermisoUbicacion.Desconocido:
                    return ResultadoInicioAcompanamiento.NecesitaExplicarPermiso;
                case EstadoPermisoUbicacion.Denegado or EstadoPermisoUbicacion.Restringido:
                    return ResultadoInicioAcompanamiento.PermisoDenegado;
            }

            var posicion = await LeerPosicionAsync(EsperaUbicacionMaxima, ct);
            if (posicion is null)
            {
                Error = "No se pudo obtener tu ubicación. Activa la ubicación del teléfono e inténtalo de nuevo. No se inició el acompañamiento.";
                return ResultadoInicioAcompanamiento.SinUbicacion;
            }

            if (await TokenAsync() is not { } token)
                return ResultadoInicioAcompanamiento.SesionTerminada;

            // Un reintento tras no recibir respuesta usa el mismo identificador: el servidor no crea dos sesiones.
            idSolicitudPendiente ??= Guid.NewGuid();
            Estado = EstadoAcompanamiento.Iniciando;
            Error = null;
            Notificar();

            switch (await api.IniciarAcompanamientoAsync(token, new SolicitudInicioAcompanamiento(idSolicitudPendiente.Value, true, true), ct))
            {
                case ResultadoApi<SesionAcompanamientoVista>.Correcto c:
                    idSolicitudPendiente = null;
                    ultimaPosicion = posicion;
                    UltimoEnvio = null;
                    Condicion = null;
                    Aplicar(c.Valor);
                    IniciarEnvio();
                    return ResultadoInicioAcompanamiento.Iniciado;
                case ResultadoApi<SesionAcompanamientoVista>.Rechazado { Estado: 409 } r:
                    idSolicitudPendiente = null;
                    Estado = EstadoAcompanamiento.SinSesion;
                    Error = r.Mensaje;
                    return ResultadoInicioAcompanamiento.OtraSesionActiva;
                case ResultadoApi<SesionAcompanamientoVista>.Rechazado { Estado: 401 }:
                    Estado = EstadoAcompanamiento.SinSesion;
                    MarcarSesionTerminada();
                    return ResultadoInicioAcompanamiento.SesionTerminada;
                case ResultadoApi<SesionAcompanamientoVista>.Rechazado r:
                    idSolicitudPendiente = null;
                    Estado = EstadoAcompanamiento.SinSesion;
                    Error = r.Errores.Values.SelectMany(m => m).FirstOrDefault() ?? r.Mensaje;
                    return ResultadoInicioAcompanamiento.Rechazado;
                default:
                    Estado = EstadoAcompanamiento.NoConfirmado;
                    Error = "No se pudo confirmar el inicio con la central. El acompañamiento no está activo y tu ubicación no se comparte. Puedes reintentarlo.";
                    return ResultadoInicioAcompanamiento.NoConfirmado;
            }
        }
        finally
        {
            accion.Release();
            Notificar();
        }
    }

    // HU36: la captura y la transmisión se detienen de inmediato (CA01), con o sin conexión; después se comunica el cierre
    // (CA02). Sin conexión queda guardado y se reenvía hasta que el servidor lo confirme (CA03).
    public async Task FinalizarAsync(CancellationToken ct = default)
    {
        await DetenerEnvioAsync();
        ultimaPosicion = null;
        await accion.WaitAsync(ct);
        try
        {
            if (Sesion is not { } sesion || Estado != EstadoAcompanamiento.Activo)
                return;
            almacen.Guardar(ClaveCierrePendiente, sesion.Id.ToString());
            Estado = EstadoAcompanamiento.Finalizando;
            Condicion = null;
            Notificar();
            if (await TokenAsync() is { } token)
                await EnviarCierreAsync(token, ct);
            else
                Estado = EstadoAcompanamiento.CierrePendiente;
        }
        finally
        {
            accion.Release();
            Notificar();
        }
    }

    // Al salir de la sesión de la app: se deja de transmitir y se olvida el estado. Un cierre pendiente se conserva y se
    // enviará en el próximo ingreso.
    public async Task ReiniciarAsync()
    {
        await DetenerEnvioAsync();
        DetenerReintento();
        Estado = EstadoAcompanamiento.SinSesion;
        Sesion = null;
        UltimoEnvio = null;
        Condicion = null;
        Error = null;
        SesionTerminada = false;
        idSolicitudPendiente = null;
        ultimaPosicion = null;
        Notificar();
    }

    public Task DetenerAsync() => DetenerEnvioAsync();

    private Guid? Pendiente() => Guid.TryParse(almacen.Leer(ClaveCierrePendiente), out var id) ? id : null;

    private async Task EnviarCierreAsync(string token, CancellationToken ct)
    {
        if (Pendiente() is not { } id)
            return;
        switch (await api.FinalizarAcompanamientoAsync(token, id, ct))
        {
            case ResultadoApi<SesionAcompanamientoVista>.Correcto c:
                almacen.Borrar(ClaveCierrePendiente);
                DetenerReintento();
                Aplicar(c.Valor);
                Error = null;
                break;
            case ResultadoApi<SesionAcompanamientoVista>.Rechazado { Estado: 401 }:
                Estado = EstadoAcompanamiento.CierrePendiente;
                MarcarSesionTerminada();
                break;
            case ResultadoApi<SesionAcompanamientoVista>.Rechazado:
                // La sesión no existe para este usuario (p. ej., otra cuenta ingresó en el teléfono): no hay nada que cerrar.
                almacen.Borrar(ClaveCierrePendiente);
                DetenerReintento();
                Estado = Sesion?.Id == id ? EstadoAcompanamiento.Finalizado : EstadoAcompanamiento.SinSesion;
                break;
            default:
                Estado = EstadoAcompanamiento.CierrePendiente;
                ProgramarReintento();
                break;
        }
    }

    private async Task ConsultarFinAsync(string token, Guid id, CancellationToken ct)
    {
        if (await api.ConsultarAcompanamientoAsync(token, id, ct) is ResultadoApi<SesionAcompanamientoVista>.Correcto c)
            Aplicar(c.Valor);
        else
            Estado = EstadoAcompanamiento.SinSesion;
    }

    private void Aplicar(SesionAcompanamientoVista vista)
    {
        Sesion = vista;
        desfase = vista.Ahora - reloj.GetUtcNow();
        Estado = vista.Estado switch
        {
            EstadosAcompanamiento.Activa => EstadoAcompanamiento.Activo,
            EstadosAcompanamiento.Vencida => EstadoAcompanamiento.Vencido,
            _ => EstadoAcompanamiento.Finalizado,
        };
    }

    private void IniciarEnvio()
    {
        lock (bloqueo)
        {
            if (envio is not null || Sesion is not { } sesion)
                return;
            var propio = new CancellationTokenSource();
            envio = propio;
            var intervalo = TimeSpan.FromSeconds(Math.Max(1, sesion.IntervaloSegundos));
            tareaEnvio = Task.Run(() => BucleEnvioAsync(sesion.Id, intervalo, propio), propio.Token);
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

    // HU35-CA01: un envío al activarse y después uno cada intervalo, mientras la sesión siga activa.
    private async Task BucleEnvioAsync(Guid sesionId, TimeSpan intervalo, CancellationTokenSource propio)
    {
        var ct = propio.Token;
        using var temporizador = new PeriodicTimer(intervalo, reloj);
        try
        {
            do
            {
                if (!await EnviarAsync(sesionId, intervalo, ct))
                {
                    Retirar(propio);
                    return;
                }
                Notificar();
            }
            while (await temporizador.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    // Devuelve false si hay que dejar de transmitir (la sesión terminó o la sesión de la app ya no es válida).
    private async Task<bool> EnviarAsync(Guid sesionId, TimeSpan intervalo, CancellationToken ct)
    {
        if (await TokenAsync() is not { } token)
            return false;

        // HU37-CA02: al cumplirse los 30 minutos se deja de transmitir y se consulta cómo terminó, sin esperar al servidor.
        if (Restante is { } restante && restante <= TimeSpan.Zero)
        {
            await ConsultarFinAsync(token, sesionId, ct);
            if (Estado == EstadoAcompanamiento.Activo)
                Estado = EstadoAcompanamiento.Vencido; // sin respuesta: el servidor la cierra igualmente (HU37-CA01)
            return false;
        }

        if (await ubicacion.ConsultarPermisoAsync() != EstadoPermisoUbicacion.Concedido)
        {
            Condicion = "Sin permiso de ubicación: tu posición no se está compartiendo. Concede el permiso o finaliza el acompañamiento.";
            return true;
        }

        var nueva = await LeerPosicionAsync(intervalo < EsperaUbicacionMaxima ? intervalo : EsperaUbicacionMaxima, ct);
        if (nueva is not null)
            ultimaPosicion = nueva;
        // HU35-CA01/CA03: se intenta transmitir la última posición disponible, con su hora de obtención original.
        if (ultimaPosicion is not { } posicion)
        {
            Condicion = "No se pudo obtener tu posición: la central no recibe tu ubicación. Revisa que la ubicación del teléfono esté activa.";
            return true;
        }

        ResultadoApi<SinContenido> resultado;
        try
        {
            resultado = await api.EnviarPosicionAcompanamientoAsync(token, sesionId,
                new PosicionAcompanamientoEnviada(posicion.Latitud, posicion.Longitud, posicion.PrecisionMetros, posicion.ObtenidaEn), ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            resultado = new ResultadoApi<SinContenido>.SinRespuesta(e.GetType().Name);
        }

        switch (resultado)
        {
            case ResultadoApi<SinContenido>.Correcto:
                UltimoEnvio = reloj.GetUtcNow();
                Condicion = nueva is null
                    ? $"Sin posiciones nuevas: se envió la última disponible, obtenida a las {posicion.ObtenidaEn.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}."
                    : null;
                return true;
            case ResultadoApi<SinContenido>.Rechazado { Estado: 409 }:
                // El servidor ya la cerró (finalizada o vencida): se deja de transmitir y se muestra cómo terminó.
                await ConsultarFinAsync(token, sesionId, ct);
                if (Estado == EstadoAcompanamiento.Activo)
                    Estado = EstadoAcompanamiento.Finalizado;
                Condicion = null;
                return false;
            case ResultadoApi<SinContenido>.Rechazado { Estado: 401 }:
                MarcarSesionTerminada();
                return false;
            case ResultadoApi<SinContenido>.Rechazado r:
                Condicion = $"La posición no se aceptó: {r.Mensaje}";
                return true;
            default:
                Condicion = "Sin conexión con el servidor: tu última posición no llegó a la central. Se volverá a intentar.";
                return true;
        }
    }

    private async Task<UbicacionSos?> LeerPosicionAsync(TimeSpan espera, CancellationToken ct)
    {
        try
        {
            return await ubicacion.ObtenerActualAsync(espera, ct).WaitAsync(espera, reloj, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private void Retirar(CancellationTokenSource propio)
    {
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
    }

    private void ProgramarReintento()
    {
        lock (bloqueo)
        {
            if (reintento is not null)
                return;
            var propio = new CancellationTokenSource();
            reintento = propio;
            _ = Task.Run(async () =>
            {
                using var temporizador = new PeriodicTimer(ReintentoCierre, reloj);
                try
                {
                    while (await temporizador.WaitForNextTickAsync(propio.Token))
                    {
                        await accion.WaitAsync(propio.Token);
                        try
                        {
                            if (await TokenAsync() is { } token)
                                await EnviarCierreAsync(token, propio.Token);
                        }
                        finally
                        {
                            accion.Release();
                            Notificar();
                        }
                        if (Pendiente() is null)
                            return;
                    }
                }
                catch (OperationCanceledException) { }
            }, propio.Token);
        }
    }

    private void DetenerReintento()
    {
        CancellationTokenSource? actual;
        lock (bloqueo)
        {
            actual = reintento;
            reintento = null;
        }
        actual?.Cancel();
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

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private void Notificar() => Cambio?.Invoke();
}
