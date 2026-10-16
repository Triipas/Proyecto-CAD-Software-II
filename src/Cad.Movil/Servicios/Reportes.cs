using Cad.Contratos.Reportes;

namespace Cad.Movil.Servicios;

// Datos del formulario de reporte tal como los dejó el ciudadano (HU10).
public sealed record DatosReporte(Guid? CategoriaId, string? Descripcion, DateTimeOffset? OcurridoEn, UbicacionHecho? Ubicacion);

public abstract record EstadoReporte
{
    public sealed record Editando : EstadoReporte;
    public sealed record Enviando(int Intento) : EstadoReporte;
    // Registrado SOLO tras la respuesta del servidor.
    public sealed record Registrado(RespuestaReporte Respuesta) : EstadoReporte;
    // Sin respuesta tras los reintentos: el reporte NO se da por registrado. Reintentar reenvía la misma solicitud.
    public sealed record NoConfirmado(string Mensaje) : EstadoReporte;
    // El servidor (o la comprobación previa) lo rechazó; Errores trae el motivo por campo cuando lo hay.
    public sealed record Rechazado(int Estado, string Mensaje, IReadOnlyDictionary<string, string[]> Errores) : EstadoReporte;
}

// HU10 — Registrar un reporte de incidencia (lado de la app). Mismo mecanismo que el SOS (S1-T14): un IdSolicitud por
// envío del ciudadano, reutilizado en cada reintento técnico, de modo que el servidor nunca crea dos reportes (CA08).
public sealed class RegistradorReporte(IApiCad api, IAlmacenSesion sesiones, TimeProvider reloj)
{
    // Los mismos límites que el servidor (DP08, DP10); el servidor vuelve a comprobarlo todo.
    public const int MaximoDescripcion = 2000;
    public const int MaximoReferencia = 500;

    public IReadOnlyList<TimeSpan> EsperasEntreIntentos { get; init; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private readonly SemaphoreSlim enCurso = new(1, 1);
    private SolicitudRegistroReporte? pendiente;

    public event Action<EstadoReporte>? Cambio;
    public EstadoReporte Estado { get; private set; } = new EstadoReporte.Editando();

    public bool PuedeReintentar => Estado is EstadoReporte.NoConfirmado && pendiente is not null;

    // Tras un envío sin confirmar no se admite otro con datos nuevos: podría estar ya registrado en el servidor.
    public bool PuedeEnviar => Estado is EstadoReporte.Editando or EstadoReporte.Rechazado;

    // CA02 y DP08: comprobación previa con los mismos criterios del servidor, para avisar sin esperar la red.
    public static Dictionary<string, string> Validar(DatosReporte datos, DateTimeOffset ahora)
    {
        var errores = new Dictionary<string, string>();
        if (datos.CategoriaId is null || datos.CategoriaId == Guid.Empty)
            errores["categoriaId"] = "Elige el tipo de incidencia.";
        var descripcion = datos.Descripcion?.Trim();
        if (string.IsNullOrEmpty(descripcion))
            errores["descripcion"] = "Describe lo ocurrido.";
        else if (descripcion.Length > MaximoDescripcion)
            errores["descripcion"] = $"La descripción admite hasta {MaximoDescripcion} caracteres.";
        if (datos.OcurridoEn is not { } ocurrido)
            errores["ocurridoAproximadamenteEn"] = "Indica la fecha y hora aproximadas del hecho.";
        else if (ocurrido > ahora)
            errores["ocurridoAproximadamenteEn"] = "La fecha y hora del hecho no pueden ser futuras.";

        switch (datos.Ubicacion)
        {
            case null:
                errores["ubicacion"] = "Indica dónde ocurrió: un punto en el mapa, tu posición actual o una referencia.";
                break;
            case { Fuente: FuentesUbicacionHecho.Texto } u when string.IsNullOrWhiteSpace(u.Referencia):
                errores["ubicacion"] = "Escribe una referencia del lugar del hecho.";
                break;
            case { Fuente: FuentesUbicacionHecho.Mapa or FuentesUbicacionHecho.PosicionActual } u when u.Latitud is null || u.Longitud is null:
                errores["ubicacion"] = "Elige el punto del hecho en el mapa.";
                break;
            case { Referencia: { } referencia } when referencia.Trim().Length > MaximoReferencia:
                errores["ubicacion"] = $"La referencia admite hasta {MaximoReferencia} caracteres.";
                break;
        }
        return errores;
    }

    public async Task EnviarAsync(DatosReporte datos, CancellationToken ct = default)
    {
        if (!PuedeEnviar || !await enCurso.WaitAsync(0, ct))
            return;
        try
        {
            var errores = Validar(datos, reloj.GetUtcNow());
            if (errores.Count > 0)
            {
                Publicar(new EstadoReporte.Rechazado(400, "Revisa los datos marcados.", errores.ToDictionary(e => e.Key, e => new[] { e.Value })));
                return;
            }

            var ubicacion = datos.Ubicacion!;
            pendiente = new SolicitudRegistroReporte(Guid.NewGuid(), datos.CategoriaId, datos.Descripcion!.Trim(), datos.OcurridoEn,
                ubicacion with { Referencia = string.IsNullOrWhiteSpace(ubicacion.Referencia) ? null : ubicacion.Referencia.Trim() });
            await EnviarPendienteAsync(ct);
        }
        finally
        {
            enCurso.Release();
        }
    }

    // Tras «NoConfirmado» se envía EXACTAMENTE la misma solicitud (mismo IdSolicitud).
    public async Task ReintentarAsync(CancellationToken ct = default)
    {
        if (!PuedeReintentar || !await enCurso.WaitAsync(0, ct))
            return;
        try
        {
            await EnviarPendienteAsync(ct);
        }
        finally
        {
            enCurso.Release();
        }
    }

    private async Task EnviarPendienteAsync(CancellationToken ct)
    {
        var solicitud = pendiente!;
        var sesion = await sesiones.LeerAsync();
        if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
        {
            Publicar(new EstadoReporte.Rechazado(401, "Tu sesión terminó. Vuelve a ingresar para enviar el reporte.", new Dictionary<string, string[]>()));
            return;
        }

        for (var intento = 1; ; intento++)
        {
            Publicar(new EstadoReporte.Enviando(intento));
            ResultadoApi<RespuestaReporte> resultado;
            try
            {
                resultado = await api.RegistrarReporteAsync(sesion.Token, solicitud, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                resultado = new ResultadoApi<RespuestaReporte>.SinRespuesta(e.GetType().Name);
            }
            switch (resultado)
            {
                case ResultadoApi<RespuestaReporte>.Correcto correcto:
                    pendiente = null;
                    Publicar(new EstadoReporte.Registrado(correcto.Valor));
                    return;
                case ResultadoApi<RespuestaReporte>.Rechazado rechazo:
                    pendiente = null;
                    Publicar(new EstadoReporte.Rechazado(rechazo.Estado, rechazo.Mensaje, rechazo.Errores));
                    return;
                case ResultadoApi<RespuestaReporte>.SinRespuesta when intento <= EsperasEntreIntentos.Count:
                    await Task.Delay(EsperasEntreIntentos[intento - 1], ct);
                    break;
                default:
                    Publicar(new EstadoReporte.NoConfirmado(
                        "No se pudo confirmar el reporte: no hay respuesta del servidor. Reinténtalo; no se duplicará."));
                    return;
            }
        }
    }

    private void Publicar(EstadoReporte estado)
    {
        Estado = estado;
        Cambio?.Invoke(estado);
    }
}
