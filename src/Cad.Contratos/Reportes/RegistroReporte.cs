namespace Cad.Contratos.Reportes;

// HU10 — Registrar un reporte de incidencia. IdSolicitud: lo genera la app y lo reutiliza en cada reintento técnico,
// como en el SOS (CA08). CategoriaId: del catálogo compartido (DP07). OcurridoAproximadamenteEn: fecha y hora
// aproximadas del hecho, no futuras (DP08). Fotos (DP08 ampliada, 26/10/2026): opcionales, hasta tres JPEG de hasta 1 MB cada
// uno, en base64 dentro de la misma solicitud para que un reintento con el mismo IdSolicitud no las duplique.
public sealed record SolicitudRegistroReporte(
    Guid IdSolicitud,
    Guid? CategoriaId,
    string? Descripcion,
    DateTimeOffset? OcurridoAproximadamenteEn,
    UbicacionHecho? Ubicacion,
    IReadOnlyList<byte[]>? Fotos = null);

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

// DP08 ampliada (26/10/2026): límites de las fotos del reporte, los mismos en la app y en el servidor.
public static class LimitesFotosReporte
{
    public const int Maximo = 3;
    public const int MaximoBytes = 1024 * 1024;
    // Lado mayor de la foto que prepara la app y su calidad JPEG: unos 200–400 KB por foto.
    public const int LadoMayorPixeles = 1600;
    public const int CalidadJpeg = 75;
}
