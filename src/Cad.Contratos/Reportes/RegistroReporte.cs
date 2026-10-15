namespace Cad.Contratos.Reportes;

// HU10 — Registrar un reporte de incidencia. IdSolicitud: lo genera la app y lo reutiliza en cada reintento técnico,
// como en el SOS (CA08). CategoriaId: del catálogo compartido (DP07). OcurridoAproximadamenteEn: fecha y hora
// aproximadas del hecho, no futuras (DP08).
public sealed record SolicitudRegistroReporte(
    Guid IdSolicitud,
    Guid? CategoriaId,
    string? Descripcion,
    DateTimeOffset? OcurridoAproximadamenteEn,
    UbicacionHecho? Ubicacion);

// CA03: ubicación del hecho, que no es la ubicación actual del ciudadano. Fuente: «Mapa» (punto elegido en el mapa),
// «Texto» (solo referencia textual; sin coordenadas) o «PosicionActual» (el ciudadano eligió usar su posición, CA04).
// La referencia es opcional con un punto y obligatoria con «Texto» (DP10: se guarda como texto).
public sealed record UbicacionHecho(string? Fuente, double? Latitud, double? Longitud, double? PrecisionMetros, string? Referencia);

public sealed record RespuestaReporte(Guid Id, string Estado, DateTimeOffset RegistradoEn);

public static class FuentesUbicacionHecho
{
    public const string Mapa = "Mapa";
    public const string Texto = "Texto";
    public const string PosicionActual = "PosicionActual";
}
