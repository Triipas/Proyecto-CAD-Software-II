namespace Cad.Contratos.Casos;

// HU30 — Consultar mis casos. Solo datos públicos para el titular (DP05): estado público, avances públicos y resultado.
// Nunca notas internas, intentos de contacto, operador ni sereno (CA05).
public sealed record MiCasoResumen(Guid Id, string Origen, DateTimeOffset RegistradoEn, string Estado, string? Resultado);

public sealed record MiCasoDetalle(Guid Id, string Origen, DateTimeOffset RegistradoEn, string Estado, string? Resultado,
    IReadOnlyList<AvancePublico> Avances, MiEvaluacion? Evaluacion = null, MiReporte? Reporte = null);

// Decisión del PO del 26/10/2026: lo que el ciudadano dio en su reporte, para consultarlo en solo lectura. La categoría es la que
// él eligió (la corrección del operador es interna); Fotos es cuántas adjuntó: cada una se pide aparte (1 a Fotos).
public sealed record MiReporte(string Categoria, string Descripcion, DateTimeOffset OcurridoAproximadamenteEn, string FuenteUbicacion,
    double? Latitud, double? Longitud, string? Referencia, int Fotos);

// HU32: la evaluación que el titular registró de un caso cerrado (null si no lo evaluó).
public sealed record MiEvaluacion(int Puntuacion, string? Comentario, DateTimeOffset EvaluadaEn);

// Un cambio de estado público, con su momento y, cuando DP05 lo permite, un detalle (destino o motivo del descarte).
public sealed record AvancePublico(DateTimeOffset En, string Estado, string? Detalle);

// DP05 (decisión del PO del 14/10/2026): estados públicos.
public static class EstadosPublicos
{
    public const string Recibido = "Recibido";
    public const string EnRevision = "En revisión";
    public const string Admitido = "Admitido";
    public const string EnAtencion = "En atención";
    public const string Cerrado = "Cerrado";
    public const string Derivado = "Derivado";
    public const string Descartado = "Descartado";
    // HU09: SOS cancelado por su titular.
    public const string Cancelado = "Cancelado";
}

// HU09 — cancelación de un SOS propio pendiente: el motivo es obligatorio (CA01, CA04).
public sealed record SolicitudCancelacionSos(string? Motivo);

// HU32 — evaluación voluntaria de un caso propio cerrado: puntuación de 1 a 5 (CA02) y comentario opcional (CA01).
// IdSolicitud: lo genera la app por evaluación y lo reutiliza en cada reintento técnico.
public sealed record SolicitudEvaluacionCaso(Guid IdSolicitud, int? Puntuacion, string? Comentario);
