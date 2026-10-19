namespace Cad.Contratos.Acompanamiento;

// HU34 — Activar el acompañamiento temporal. IdSolicitud: lo genera la app por pulsación y lo reutiliza en cada reintento
// técnico (RNF08). FinalidadAceptada: el ciudadano aceptó compartir su ubicación con la central durante el trayecto (CA02).
// UbicacionDisponible: la app tiene el permiso concedido y una posición actual (CA03), como el permiso declarado del
// sereno (DP16-a); sin ellos el servidor no crea la sesión.
public sealed record SolicitudInicioAcompanamiento(Guid IdSolicitud, bool FinalidadAceptada, bool UbicacionDisponible);

// Estado de la sesión que ve su titular. Ahora: hora del servidor, para calcular el tiempo restante sin depender del reloj
// del teléfono (CA01, CA08). IntervaloSegundos: cada cuánto intenta la app transmitir la última posición (HU35-CA01).
public sealed record SesionAcompanamientoVista(
    Guid Id,
    string Estado,
    DateTimeOffset IniciadaEn,
    DateTimeOffset VenceEn,
    DateTimeOffset? CerradaEn,
    DateTimeOffset Ahora,
    int IntervaloSegundos);

// HU35 — una posición con su hora de obtención y su precisión reportada (CA01). Una posición antigua conserva su hora (CA03).
public sealed record PosicionAcompanamientoEnviada(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn);

public static class EstadosAcompanamiento
{
    public const string Activa = "Activa";
    // HU36: el ciudadano la finalizó.
    public const string Finalizada = "Finalizada";
    // HU37: el servidor la cerró a los 30 minutos.
    public const string Vencida = "Vencida";
}

// HU40 y HU41 — confirmación de bienestar que ve el titular. Respuesta: null mientras está pendiente; CasoId: el SOS creado o
// vinculado con «Necesito ayuda».
public sealed record ConfirmacionBienestarVista(Guid Id, DateTimeOffset SolicitadaEn, DateTimeOffset? PresentadaEn, string? Respuesta,
    DateTimeOffset? RespondidaEn, Guid? CasoId);

// HU41 — respuesta del ciudadano. Con «Necesito ayuda», el SOS que se emite si no tiene uno vigente (HU07, HU08): la app lo
// prepara como cualquier SOS y reutiliza su IdSolicitud en cada reintento, así que una respuesta repetida no duplica el SOS (CA07).
public sealed record SolicitudRespuestaBienestar(string? Respuesta, Cad.Contratos.Sos.SolicitudEmisionSos? Sos);

public static class RespuestasBienestar
{
    public const string EstoyBien = "EstoyBien";
    public const string NecesitoAyuda = "NecesitoAyuda";
}
