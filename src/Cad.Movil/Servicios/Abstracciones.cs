using Cad.Contratos.Cuentas;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;

namespace Cad.Movil.Servicios;

// Abstracciones de la plataforma: la lógica de la app (SOS, preparación de ubicación) se prueba
// con dobles en xUnit, sin emulador ni red.

public enum EstadoPermisoUbicacion { Desconocido, Concedido, Denegado, Restringido }

public interface IServicioUbicacion
{
    Task<EstadoPermisoUbicacion> ConsultarPermisoAsync();
    Task<EstadoPermisoUbicacion> SolicitarPermisoAsync();

    // Posición ACTUAL: se pide al sistema en este momento; nunca la última conocida (HU08-CA03).
    // null si no se obtiene dentro del tiempo máximo o el sistema no la proporciona.
    Task<UbicacionSos?> ObtenerActualAsync(TimeSpan tiempoMaximo, CancellationToken ct);
}

public interface IApiCad
{
    Task<ResultadoApi<RespuestaRegistroCiudadano>> RegistrarAsync(SolicitudRegistroCiudadano solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct);

    // HU10 — reporte de incidencia y catálogo de tipos (DP07).
    Task<ResultadoApi<RespuestaReporte>> RegistrarReporteAsync(string token, SolicitudRegistroReporte solicitud, CancellationToken ct);
    Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasReporteAsync(string token, CancellationToken ct);

    // HU18 — modo sereno.
    Task<ResultadoApi<RespuestaEstadoOperativo>> ConsultarEstadoOperativoAsync(string token, CancellationToken ct);
    Task<ResultadoApi<RespuestaEstadoOperativo>> CambiarEstadoOperativoAsync(string token, SolicitudCambioEstadoOperativo solicitud, CancellationToken ct);
    Task<ResultadoApi<SinContenido>> EnviarPosicionSerenoAsync(string token, PosicionSerenoEnviada posicion, CancellationToken ct);

    // HU21 — despacho vigente del sereno; Correcto(null) si no tiene ninguno (204).
    Task<ResultadoApi<DespachoSereno?>> ConsultarDespachoAsync(string token, CancellationToken ct);

    // HU22 y HU23 — avances del despacho; devuelven el despacho actualizado.
    Task<ResultadoApi<DespachoSereno?>> ConfirmarDesplazamientoAsync(string token, Guid casoId, CancellationToken ct);
    Task<ResultadoApi<DespachoSereno?>> IniciarAtencionAsync(string token, Guid casoId, CancellationToken ct);

    // HU24 — cierre del caso y catálogo para la tipificación final (DP07).
    Task<ResultadoApi<RespuestaCierreCaso>> CerrarCasoAsync(string token, Guid casoId, SolicitudCierreCaso solicitud, CancellationToken ct);
    Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasAsync(string token, CancellationToken ct);

    // HU21 (ADR-07) — dispositivo que recibe la notificación push del despacho.
    Task<ResultadoApi<SinContenido>> RegistrarDispositivoAsync(string token, SolicitudRegistroDispositivo solicitud, CancellationToken ct);
    Task<ResultadoApi<SinContenido>> QuitarDispositivoAsync(string token, CancellationToken ct);
}

public enum EstadoPermisoNotificaciones { Desconocido, Concedido, Denegado }

// HU21 (ADR-07) — notificaciones push en el dispositivo (Firebase Cloud Messaging en Android). Sin la configuración
// de Firebase o sin Google Play Services, Disponible es false y la app funciona igual: el despacho llega por el canal
// en tiempo real con la app abierta y se consulta al abrirla.
public interface IServicioPush
{
    bool Disponible { get; }
    Task<EstadoPermisoNotificaciones> ConsultarPermisoAsync();
    Task<EstadoPermisoNotificaciones> SolicitarPermisoAsync();
    // Token de registro de FCM; null si no se obtiene. Dato sensible: no se registra en ningún log.
    Task<string?> ObtenerTokenAsync(CancellationToken ct);
    // FCM renovó el token: hay que volver a registrarlo en el servidor.
    event Action<string>? TokenRenovado;
}

// Respuesta correcta sin cuerpo que leer (p. ej., 201 al registrar una posición).
public sealed record SinContenido;

public interface IAlmacenSesion
{
    Task GuardarAsync(SesionApp sesion);
    Task<SesionApp?> LeerAsync();
    void Cerrar();
}

public sealed record SesionApp(string Token, string Rol, DateTimeOffset ExpiraEn)
{
    public bool Vigente(DateTimeOffset ahora) => ExpiraEn > ahora;
}

// Resultado de una llamada a la API: la app distingue «el servidor respondió» de «no hubo respuesta».
public abstract record ResultadoApi<T>
{
    public sealed record Correcto(T Valor) : ResultadoApi<T>;
    // El servidor respondió con un rechazo: no tiene sentido reintentar la misma solicitud.
    public sealed record Rechazado(int Estado, string Mensaje, IReadOnlyDictionary<string, string[]> Errores) : ResultadoApi<T>;
    // Sin respuesta (red, tiempo agotado, servidor caído): se puede reintentar.
    public sealed record SinRespuesta(string Motivo) : ResultadoApi<T>;
}
