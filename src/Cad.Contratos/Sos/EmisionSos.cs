namespace Cad.Contratos.Sos;

// HU07 — Emitir un SOS con una única activación. Sin formulario, categoría ni descripción (CA01).
// IdSolicitud: lo genera la app en cada activación y lo reutiliza en cada reintento técnico (CA07, DP14).
// ActivadoEn: momento de la activación según el dispositivo; sirve para descartar posiciones anteriores (HU08-CA03, DP11).
// Ubicacion: null cuando no se obtuvo una posición actual (HU08-CA02).
public sealed record SolicitudEmisionSos(Guid IdSolicitud, DateTimeOffset ActivadoEn, UbicacionSos? Ubicacion);

// HU08-CA01: coordenadas, precisión reportada y momento de obtención (reloj del dispositivo).
public sealed record UbicacionSos(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn);

// La app confirma el SOS solo al recibir esta respuesta (HU07-CA03).
public sealed record RespuestaSos(Guid Id, string Estado, DateTimeOffset RegistradoEn, bool UbicacionDisponible);
