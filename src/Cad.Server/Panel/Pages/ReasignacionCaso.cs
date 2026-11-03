namespace Cad.Server.Panel.Pages;

// Bloque «Reasignar…» del detalle de un SOS o de un reporte (_ReasignarCaso, 24/10/2026).
public sealed record VistaReasignacion(string UrlPagina, string SerenoActual, IReadOnlyList<SerenoParaReasignar> Disponibles,
    FormularioReasignacion Formulario, Dictionary<string, string[]> Errores, string? Error)
{
    public bool Abierto => Errores.Count > 0 || Error is not null;
    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;
}

public sealed record SerenoParaReasignar(Guid Id, string Nombre, string Detalle);

public sealed class FormularioReasignacion
{
    public Guid IdSolicitud { get; set; }
    public Guid SerenoId { get; set; }
    // 27/10/2026: el motivo del catálogo MotivosReasignacion; Motivo es el detalle (obligatorio solo con «Otro motivo»).
    public string? MotivoCodigo { get; set; }
    public string? Motivo { get; set; }
}
