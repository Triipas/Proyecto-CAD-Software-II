using Cad.Contratos.Sos;

namespace Cad.Movil.Servicios;

public abstract record EstadoSos
{
    public sealed record Listo : EstadoSos;
    public sealed record ObteniendoUbicacion : EstadoSos;
    public sealed record Enviando(int Intento) : EstadoSos;
    // Confirmado SOLO tras la respuesta del servidor (HU07-CA03).
    public sealed record Confirmado(RespuestaSos Respuesta) : EstadoSos;
    // Sin respuesta tras los reintentos: el SOS NO se da por registrado (HU07-CA04).
    // Reintentar reutiliza la misma solicitud (mismo IdSolicitud: HU07-CA07).
    public sealed record NoConfirmado(string Mensaje) : EstadoSos;
    // El servidor rechazó la solicitud (p. ej. sesión vencida): no se reintenta automáticamente.
    public sealed record Rechazado(int Estado, string Mensaje) : EstadoSos;
}

// HU07 — Emitir un SOS con una única activación — y HU08 — ubicación actual del SOS (lado de la app).
public sealed class EmisorSos(IApiCad api, IServicioUbicacion ubicacion, IAlmacenSesion sesiones, TimeProvider reloj,
    IReproductorSonidos? sonidos = null)
{
    // DP11 (decidida por delegación): espera máxima para obtener la ubicación actual al emitir.
    public static readonly TimeSpan EsperaUbicacion = TimeSpan.FromSeconds(10);
    // Reintentos técnicos automáticos con el mismo IdSolicitud, y su espera (configurable para las pruebas).
    public IReadOnlyList<TimeSpan> EsperasEntreIntentos { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private readonly SemaphoreSlim enCurso = new(1, 1);
    private SolicitudEmisionSos? pendiente;

    public event Action<EstadoSos>? Cambio;
    public EstadoSos Estado { get; private set; } = new EstadoSos.Listo();

    // CA01: una sola activación inicia el envío, sin formulario ni confirmación adicional.
    // Un segundo toque mientras se envía no crea otro SOS (se ignora).
    public async Task ActivarAsync(CancellationToken ct = default)
    {
        if (!await enCurso.WaitAsync(0, ct))
            return;
        try
        {
            pendiente = await PrepararSolicitudAsync(ct);
            await EnviarAsync(pendiente, ct);
        }
        finally
        {
            enCurso.Release();
        }
    }

    // Tras «NoConfirmado», el usuario puede reintentar: se envía EXACTAMENTE la misma solicitud.
    public async Task ReintentarAsync(CancellationToken ct = default)
    {
        if (pendiente is null || !await enCurso.WaitAsync(0, ct))
            return;
        try
        {
            await EnviarAsync(pendiente, ct);
        }
        finally
        {
            enCurso.Release();
        }
    }

    public bool PuedeReintentar => Estado is EstadoSos.NoConfirmado && pendiente is not null;

    // Al cerrar sesión o cambiar de cuenta: un SOS pendiente de reintento pertenece a quien lo emitió y no puede
    // reenviarse con la sesión de otra persona. Espera a que termine un envío en curso.
    public async Task ReiniciarAsync()
    {
        await enCurso.WaitAsync();
        try
        {
            pendiente = null;
            Publicar(new EstadoSos.Listo());
        }
        finally
        {
            enCurso.Release();
        }
    }

    private async Task<SolicitudEmisionSos> PrepararSolicitudAsync(CancellationToken ct)
    {
        var activadoEn = reloj.GetUtcNow();
        UbicacionSos? actual = null;

        // Solo con el permiso ya concedido: no se vuelve a pedir autorización al emitir (HU08-CA04).
        if (await ubicacion.ConsultarPermisoAsync() == EstadoPermisoUbicacion.Concedido)
        {
            Publicar(new EstadoSos.ObteniendoUbicacion());
            try
            {
                // Límite propio: aunque el servicio de ubicación no responda, el SOS sale a los 10 s (DP11).
                actual = await ubicacion.ObtenerActualAsync(EsperaUbicacion, ct).WaitAsync(EsperaUbicacion, reloj, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                actual = null; // sin ubicación, el SOS se emite igual (HU07-CA06, HU08-CA02)
            }
        }

        // DP11: una posición anterior a la activación no se presenta como actual (HU08-CA03).
        if (actual is not null && actual.ObtenidaEn < activadoEn)
            actual = null;

        return new SolicitudEmisionSos(Guid.NewGuid(), activadoEn, actual);
    }

    private async Task EnviarAsync(SolicitudEmisionSos solicitud, CancellationToken ct)
    {
        var sesion = await sesiones.LeerAsync();
        if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
        {
            Publicar(new EstadoSos.Rechazado(401, "Tu sesión terminó. Vuelve a ingresar para emitir el SOS."));
            return;
        }

        for (var intento = 1; ; intento++)
        {
            Publicar(new EstadoSos.Enviando(intento));
            ResultadoApi<RespuestaSos> resultado;
            try
            {
                resultado = await api.EmitirSosAsync(sesion.Token, solicitud, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Un fallo inesperado de la red nunca debe cerrar la app ni dar el SOS por enviado:
                // se trata como «sin respuesta» y se reintenta con el mismo IdSolicitud.
                resultado = new ResultadoApi<RespuestaSos>.SinRespuesta(e.GetType().Name);
            }
            switch (resultado)
            {
                case ResultadoApi<RespuestaSos>.Correcto correcto:
                    pendiente = null;
                    Publicar(new EstadoSos.Confirmado(correcto.Valor));
                    return;
                case ResultadoApi<RespuestaSos>.Rechazado rechazo:
                    Publicar(new EstadoSos.Rechazado(rechazo.Estado, rechazo.Mensaje));
                    return;
                case ResultadoApi<RespuestaSos>.SinRespuesta when intento <= EsperasEntreIntentos.Count:
                    await Task.Delay(EsperasEntreIntentos[intento - 1], ct);
                    break;
                default:
                    Publicar(new EstadoSos.NoConfirmado(
                        "No se pudo confirmar el SOS: no hay respuesta del servidor. Reinténtalo; no se duplicará."));
                    return;
            }
        }
    }

    private void Publicar(EstadoSos estado)
    {
        Estado = estado;
        Cambio?.Invoke(estado);
        // 24/10/2026: «te escuchamos» cuando el servidor confirma el SOS; si no se pudo, el sonido de error.
        if (estado is EstadoSos.Confirmado)
            sonidos?.Reproducir(Sonido.SosRegistrado);
        else if (estado is EstadoSos.Rechazado or EstadoSos.NoConfirmado)
            sonidos?.Reproducir(Sonido.Error);
    }
}
