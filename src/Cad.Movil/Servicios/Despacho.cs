using Cad.Contratos.Serenos;

namespace Cad.Movil.Servicios;

// HU21-CA01 — canal en tiempo real del sereno (app abierta). Solo anuncia que hay un despacho: los datos se
// consultan siempre por la API, con la sesión del sereno (DP06).
public interface ICanalSereno
{
    event Action? DespachoAsignado;

    // Tras una reconexión pudo perderse un aviso: hay que volver a consultar.
    event Action? Reconectado;

    // HU23-CA02: otro responsable (un operador) cambió el estado del despacho.
    event Action? DespachoActualizado;

    // Devuelve false si no se pudo conectar (sin red o sin servidor); el despacho sigue siendo consultable.
    Task<bool> ConectarAsync(CancellationToken ct);
    Task DesconectarAsync();
}

// HU21 — despacho del sereno en la app: se consulta al abrir la pantalla, al recibir el aviso y al reconectar.
// Si el aviso no llega (sin conexión, app cerrada), el despacho aparece igualmente al abrir la app (ADR-07, punto 4).
public sealed class ServicioDespacho(IApiCad api, IAlmacenSesion sesiones, ICanalSereno canal, TimeProvider reloj)
{
    private readonly SemaphoreSlim consulta = new(1, 1);
    private bool suscrito;

    public event Action? Cambio;

    public DespachoSereno? Actual { get; private set; }
    // Al menos una consulta respondió: solo entonces se puede afirmar «no tienes un despacho».
    public bool Consultado { get; private set; }
    public string? Error { get; private set; }
    // Llegó un aviso de despacho mientras la pantalla estaba abierta.
    public bool AvisoNuevo { get; private set; }
    public bool CanalConectado { get; private set; }

    // HU24: catálogo para la tipificación final (DP07), errores del último intento de cierre y aviso del cierre.
    public IReadOnlyList<CategoriaCatalogo> Categorias { get; private set; } = [];
    public IReadOnlyDictionary<string, string[]> ErroresCierre { get; private set; } = new Dictionary<string, string[]>();
    public string? CierreReciente { get; private set; }

    public async Task IniciarAsync(CancellationToken ct = default)
    {
        if (!suscrito)
        {
            canal.DespachoAsignado += AlRecibirAviso;
            canal.Reconectado += AlReconectar;
            canal.DespachoActualizado += AlReconectar;
            suscrito = true;
        }
        AvisoNuevo = false;
        CanalConectado = await canal.ConectarAsync(ct);
        await ActualizarAsync(ct);
    }

    public async Task ActualizarAsync(CancellationToken ct = default)
    {
        await consulta.WaitAsync(ct);
        try
        {
            var sesion = await sesiones.LeerAsync();
            if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
                return; // el fin de la sesión lo informa el turno del sereno

            switch (await api.ConsultarDespachoAsync(sesion.Token, ct))
            {
                case ResultadoApi<DespachoSereno?>.Correcto c:
                    Actual = c.Valor;
                    Consultado = true;
                    Error = null;
                    if (c.Valor is not null)
                        CierreReciente = null;
                    break;
                case ResultadoApi<DespachoSereno?>.Rechazado { Estado: 401 }:
                    break; // ídem
                case ResultadoApi<DespachoSereno?>.Rechazado r:
                    Error = r.Mensaje;
                    break;
                case ResultadoApi<DespachoSereno?>.SinRespuesta s:
                    // Se conserva el último despacho conocido: es mejor que dejar la pantalla vacía.
                    Error = $"No se pudo consultar tu despacho: {s.Motivo}";
                    break;
            }
        }
        finally
        {
            consulta.Release();
            Cambio?.Invoke();
        }
    }

    // HU22 — «Voy en camino» y HU23 — «Iniciar atención». Un reenvío no duplica nada (el servidor lo reconoce).
    // Si el estado ya no lo permite, se informa y se vuelve a consultar el despacho.
    public Task<bool> ConfirmarDesplazamientoAsync(CancellationToken ct = default) =>
        AvanzarAsync((api, token, id) => api.ConfirmarDesplazamientoAsync(token, id, ct), "confirmar el desplazamiento", ct);

    public Task<bool> IniciarAtencionAsync(CancellationToken ct = default) =>
        AvanzarAsync((api, token, id) => api.IniciarAtencionAsync(token, id, ct), "registrar el inicio de atención", ct);

    private async Task<bool> AvanzarAsync(Func<IApiCad, string, Guid, Task<ResultadoApi<DespachoSereno?>>> llamada, string accion, CancellationToken ct)
    {
        if (Actual is not { } despacho || !await consulta.WaitAsync(0, ct))
            return false; // sin despacho, o con otra operación en curso (doble toque)
        var aplicado = false;
        var recargar = false;
        try
        {
            var sesion = await sesiones.LeerAsync();
            if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
                return false;
            switch (await llamada(api, sesion.Token, despacho.CasoId))
            {
                case ResultadoApi<DespachoSereno?>.Correcto c:
                    Actual = c.Valor;
                    Error = null;
                    aplicado = true;
                    break;
                case ResultadoApi<DespachoSereno?>.Rechazado { Estado: 401 }:
                    break;
                case ResultadoApi<DespachoSereno?>.Rechazado r:
                    Error = r.Mensaje;
                    recargar = true; // el estado cambió (p. ej., un operador registró el inicio) o el caso ya no es suyo
                    break;
                case ResultadoApi<DespachoSereno?>.SinRespuesta s:
                    Error = $"No se pudo {accion}: {s.Motivo} Reinténtalo.";
                    break;
            }
        }
        finally
        {
            consulta.Release();
            Cambio?.Invoke();
        }
        if (recargar)
        {
            var motivo = Error;
            await ActualizarAsync(ct);
            Error = motivo;
            Cambio?.Invoke();
        }
        return aplicado;
    }

    public async Task CargarCategoriasAsync(CancellationToken ct = default)
    {
        if (Categorias.Count > 0)
            return;
        var sesion = await sesiones.LeerAsync();
        if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
            return;
        if (await api.ConsultarCategoriasAsync(sesion.Token, ct) is ResultadoApi<List<CategoriaCatalogo>>.Correcto c)
            Categorias = c.Valor;
        else
            Error = "No se pudo cargar el catálogo de tipificaciones. Reinténtalo.";
        Cambio?.Invoke();
    }

    // HU24 — cierre por el sereno asignado. Devuelve true si el caso quedó cerrado (también ante un reenvío).
    public async Task<bool> CerrarAsync(Guid? categoriaId, string? resultadoPublico, string? notasInternas, CancellationToken ct = default)
    {
        if (Actual is not { } despacho || !await consulta.WaitAsync(0, ct))
            return false;
        var cerrado = false;
        var recargar = false;
        try
        {
            var sesion = await sesiones.LeerAsync();
            if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
                return false;
            ErroresCierre = new Dictionary<string, string[]>();
            switch (await api.CerrarCasoAsync(sesion.Token, despacho.CasoId, new SolicitudCierreCaso(categoriaId, resultadoPublico, notasInternas), ct))
            {
                case ResultadoApi<RespuestaCierreCaso>.Correcto c:
                    Actual = null;
                    Consultado = true;
                    Error = null;
                    CierreReciente = $"Caso cerrado a las {c.Valor.CerradoEn.ToLocalTime():HH:mm:ss}.";
                    cerrado = true;
                    break;
                case ResultadoApi<RespuestaCierreCaso>.Rechazado { Estado: 400 } r:
                    ErroresCierre = r.Errores; // CA02: faltan la tipificación o el resultado
                    Error = null;
                    break;
                case ResultadoApi<RespuestaCierreCaso>.Rechazado { Estado: 401 }:
                    break;
                case ResultadoApi<RespuestaCierreCaso>.Rechazado r:
                    Error = r.Mensaje; // p. ej., el operador ya lo cerró
                    recargar = true;
                    break;
                case ResultadoApi<RespuestaCierreCaso>.SinRespuesta s:
                    Error = $"No se pudo cerrar el caso: {s.Motivo} Reinténtalo.";
                    break;
            }
        }
        finally
        {
            consulta.Release();
            Cambio?.Invoke();
        }
        if (recargar)
        {
            var motivo = Error;
            await ActualizarAsync(ct);
            Error = motivo;
            Cambio?.Invoke();
        }
        return cerrado;
    }

    // Al salir de la sesión: el siguiente sereno que ingrese en este teléfono no hereda nada.
    public async Task ReiniciarAsync()
    {
        if (suscrito)
        {
            canal.DespachoAsignado -= AlRecibirAviso;
            canal.Reconectado -= AlReconectar;
            canal.DespachoActualizado -= AlReconectar;
            suscrito = false;
        }
        await canal.DesconectarAsync();
        (Actual, Consultado, Error, AvisoNuevo, CanalConectado, CierreReciente) = (null, false, null, false, false, null);
        ErroresCierre = new Dictionary<string, string[]>();
        Cambio?.Invoke();
    }

    private async void AlRecibirAviso()
    {
        AvisoNuevo = true;
        await ActualizarSinExcepcionesAsync();
    }

    private async void AlReconectar()
    {
        CanalConectado = true;
        await ActualizarSinExcepcionesAsync();
    }

    // Los avisos llegan desde el canal (async void): un fallo no debe cerrar la app.
    private async Task ActualizarSinExcepcionesAsync()
    {
        try
        {
            await ActualizarAsync();
        }
        catch (Exception)
        {
            Error = "No se pudo consultar tu despacho.";
            Cambio?.Invoke();
        }
    }
}
