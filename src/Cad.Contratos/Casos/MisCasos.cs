namespace Cad.Contratos.Casos;

// HU30 — Consultar mis casos. Solo datos públicos para el titular (DP05): estado público, avances públicos y resultado.
// Nunca notas internas, intentos de contacto, operador ni sereno (CA05).
public sealed record MiCasoResumen(Guid Id, string Origen, DateTimeOffset RegistradoEn, string Estado, string? Resultado);

public sealed record MiCasoDetalle(Guid Id, string Origen, DateTimeOffset RegistradoEn, string Estado, string? Resultado,
    IReadOnlyList<AvancePublico> Avances);

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
