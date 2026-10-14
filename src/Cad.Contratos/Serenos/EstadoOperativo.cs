namespace Cad.Contratos.Serenos;

// HU18 — Activar mi estado operativo y compartir mi ubicación.
// UbicacionAutorizada: la app declara si el sistema le concedió el permiso de ubicación. El servidor no puede
// consultar ese permiso; rechaza activar sin él (DP16-a, decidida el 13/10/2026) y la app lo comprueba antes.
public sealed record SolicitudCambioEstadoOperativo(bool Operativo, bool UbicacionAutorizada);

// IntervaloPosicionesSegundos: cada cuánto envía la app la posición mientras está operativo (DP15: 30 s,
// configurable en el servidor).
public sealed record RespuestaEstadoOperativo(bool Operativo, DateTimeOffset? CambiadoEn, int IntervaloPosicionesSegundos);

// HU18-CA01: posición con su marca de tiempo (reloj del dispositivo).
public sealed record PosicionSerenoEnviada(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn);
