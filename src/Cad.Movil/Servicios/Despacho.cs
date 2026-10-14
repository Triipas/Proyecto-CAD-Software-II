using Cad.Contratos.Serenos;

namespace Cad.Movil.Servicios;

// HU21-CA01 — canal en tiempo real del sereno (app abierta). Solo anuncia que hay un despacho: los datos se
// consultan siempre por la API, con la sesión del sereno (DP06).
public interface ICanalSereno
{
    event Action? DespachoAsignado;

    // Tras una reconexión pudo perderse un aviso: hay que volver a consultar.
    event Action? Reconectado;

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

    public async Task IniciarAsync(CancellationToken ct = default)
    {
        if (!suscrito)
        {
            canal.DespachoAsignado += AlRecibirAviso;
            canal.Reconectado += AlReconectar;
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

    // Al salir de la sesión: el siguiente sereno que ingrese en este teléfono no hereda nada.
    public async Task ReiniciarAsync()
    {
        if (suscrito)
        {
            canal.DespachoAsignado -= AlRecibirAviso;
            canal.Reconectado -= AlReconectar;
            suscrito = false;
        }
        await canal.DesconectarAsync();
        (Actual, Consultado, Error, AvisoNuevo, CanalConectado) = (null, false, null, false, false);
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
