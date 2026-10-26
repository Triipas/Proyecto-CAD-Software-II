using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
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
    // Personal (sereno) y cuentas antiguas de ciudadano: correo y contraseña (HU02).
    Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct);
    // Personal (22/10/2026): el sereno cambia la contraseña provisional del administrador; responde con un token nuevo.
    Task<ResultadoApi<RespuestaToken>> CambiarContrasenaAsync(string token, SolicitudCambioContrasena solicitud, CancellationToken ct);

    // ADR-08: ciudadano con celular verificado y PIN.
    Task<ResultadoApi<RespuestaCodigoCelular>> EnviarCodigoCelularAsync(SolicitudCodigoCelular solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaVerificacionCelular>> VerificarCelularAsync(SolicitudVerificacionCelular solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaIngresoCelular>> RegistrarConCelularAsync(SolicitudRegistroCelular solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaIngresoCelular>> IngresarConCelularAsync(SolicitudIngresoCelular solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaIngresoCelular>> RestablecerPinAsync(SolicitudRestablecerPin solicitud, CancellationToken ct);

    // ADR-08, fase D: «Mis datos» (opcionales) y cambio de PIN.
    Task<ResultadoApi<PerfilCiudadanoVista>> ConsultarPerfilAsync(string token, CancellationToken ct);
    Task<ResultadoApi<PerfilCiudadanoVista>> ActualizarPerfilAsync(string token, SolicitudPerfilCiudadano solicitud, CancellationToken ct);
    Task<ResultadoApi<RespuestaIngresoCelular>> CambiarPinAsync(string token, SolicitudCambioPin solicitud, CancellationToken ct);

    Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct);

    // HU10 — reporte de incidencia y catálogo de tipos (DP07).
    Task<ResultadoApi<RespuestaReporte>> RegistrarReporteAsync(string token, SolicitudRegistroReporte solicitud, CancellationToken ct);
    Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasReporteAsync(string token, CancellationToken ct);

    // HU30 — mis casos (solo datos públicos, DP05). El caso de otro ciudadano responde 404 (CA04).
    Task<ResultadoApi<List<MiCasoResumen>>> ConsultarMisCasosAsync(string token, CancellationToken ct);
    Task<ResultadoApi<MiCasoDetalle>> ConsultarMiCasoAsync(string token, Guid casoId, CancellationToken ct);
    // HU09: cancela un SOS propio pendiente; devuelve el caso actualizado.
    Task<ResultadoApi<MiCasoDetalle>> CancelarSosAsync(string token, Guid casoId, SolicitudCancelacionSos solicitud, CancellationToken ct);
    // HU32: evalúa un caso propio cerrado; devuelve la evaluación registrada.
    Task<ResultadoApi<MiEvaluacion>> EvaluarCasoAsync(string token, Guid casoId, SolicitudEvaluacionCaso solicitud, CancellationToken ct);

    // HU31 — bandeja de notificaciones del ciudadano.
    Task<ResultadoApi<BandejaNotificaciones>> ConsultarNotificacionesAsync(string token, CancellationToken ct);
    Task<ResultadoApi<SinContenido>> MarcarNotificacionLeidaAsync(string token, Guid notificacionId, CancellationToken ct);

    // HU34–HU37 — acompañamiento temporal. ConsultarActual: Correcto(null) si no hay sesión activa (204).
    Task<ResultadoApi<SesionAcompanamientoVista>> IniciarAcompanamientoAsync(string token, SolicitudInicioAcompanamiento solicitud, CancellationToken ct);
    Task<ResultadoApi<SesionAcompanamientoVista?>> ConsultarAcompanamientoActualAsync(string token, CancellationToken ct);
    Task<ResultadoApi<SesionAcompanamientoVista>> ConsultarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct);
    Task<ResultadoApi<SinContenido>> EnviarPosicionAcompanamientoAsync(string token, Guid sesionId, PosicionAcompanamientoEnviada posicion, CancellationToken ct);
    Task<ResultadoApi<SesionAcompanamientoVista>> FinalizarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct);
    // HU41: confirmación de bienestar pendiente (null si no hay), su presentación y la respuesta.
    Task<ResultadoApi<ConfirmacionBienestarVista?>> ConsultarConfirmacionAsync(string token, Guid sesionId, CancellationToken ct);
    Task<ResultadoApi<ConfirmacionBienestarVista>> RegistrarPresentacionConfirmacionAsync(string token, Guid confirmacionId, CancellationToken ct);
    Task<ResultadoApi<ConfirmacionBienestarVista>> ResponderConfirmacionAsync(string token, Guid confirmacionId, SolicitudRespuestaBienestar respuesta,
        CancellationToken ct);
    // 26/10/2026: estado que el ciudadano informa por su cuenta durante el acompañamiento.
    Task<ResultadoApi<EstadoInformadoVista>> InformarEstadoAcompanamientoAsync(string token, Guid sesionId, SolicitudEstadoAcompanamiento solicitud,
        CancellationToken ct);

    // HU18 — modo sereno.
    Task<ResultadoApi<RespuestaEstadoOperativo>> ConsultarEstadoOperativoAsync(string token, CancellationToken ct);
    Task<ResultadoApi<RespuestaEstadoOperativo>> CambiarEstadoOperativoAsync(string token, SolicitudCambioEstadoOperativo solicitud, CancellationToken ct);
    Task<ResultadoApi<SinContenido>> EnviarPosicionSerenoAsync(string token, PosicionSerenoEnviada posicion, CancellationToken ct);

    // HU21 — despacho vigente del sereno; Correcto(null) si no tiene ninguno (204).
    Task<ResultadoApi<DespachoSereno?>> ConsultarDespachoAsync(string token, CancellationToken ct);

    // HU22 y HU23 — avances del despacho; devuelven el despacho actualizado.
    Task<ResultadoApi<DespachoSereno?>> ConfirmarDesplazamientoAsync(string token, Guid casoId, CancellationToken ct);
    Task<ResultadoApi<DespachoSereno?>> IniciarAtencionAsync(string token, Guid casoId, SolicitudInicioAtencion solicitud, CancellationToken ct);

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

// DP13 (16/10/2026): mantiene la transmisión del acompañamiento con la app fuera de la pantalla (en Android, un servicio en
// primer plano con notificación). En las pruebas y fuera de Android no hace nada.
public interface IServicioSegundoPlano
{
    void Iniciar(DateTimeOffset venceEn);
    void Detener();
    // HU41: avisa en el teléfono de que la central pide una confirmación (útil con la app en segundo plano).
    void AvisarConfirmacion();
}

public sealed class SegundoPlanoNoDisponible : IServicioSegundoPlano
{
    public void Iniciar(DateTimeOffset venceEn) { }
    public void Detener() { }
    public void AvisarConfirmacion() { }
}

// Reporte (22/10/2026): dictado de la descripción con el reconocedor de voz del teléfono (en Android, el del sistema).
// La app no graba ni guarda audio: solo recibe el texto reconocido. Null si el ciudadano cancela o no se entendió nada.
public interface IDictado
{
    bool Disponible { get; }
    Task<string?> DictarAsync();
}

public sealed class DictadoNoDisponible : IDictado
{
    public bool Disponible => false;
    public Task<string?> DictarAsync() => Task.FromResult<string?>(null);
}

// Respuesta correcta sin cuerpo que leer (p. ej., 201 al registrar una posición).
public sealed record SinContenido;

public interface IAlmacenSesion
{
    Task GuardarAsync(SesionApp sesion);
    Task<SesionApp?> LeerAsync();
    void Cerrar();
}

// ADR-08 (DP24): el teléfono vinculado a la cuenta del ciudadano. La llave solo vive en el almacenamiento seguro;
// sobrevive al cierre de sesión para que volver a entrar solo pida el PIN.
public interface IAlmacenDispositivo
{
    Task GuardarAsync(DispositivoVinculado dispositivo);
    Task<DispositivoVinculado?> LeerAsync();
    void Olvidar();
}

public sealed record DispositivoVinculado(string Celular, string Llave);

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
