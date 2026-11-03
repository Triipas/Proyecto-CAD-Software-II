namespace Cad.Server.Panel.Pages;

// Bloque «Descartar el caso…» del detalle de un SOS o de un reporte (_DescartarCaso, 23/10/2026). Sereno: el que tiene el
// despacho, para explicar qué le pasa. Abierto: se vuelve a mostrar desplegado si el envío tuvo errores.
public sealed record VistaDescarte(string UrlPagina, string? Sereno, string? Motivo, string? Error, string? ErrorMotivo,
    string? MotivoCodigo = null, string? ErrorMotivoCodigo = null)
{
    public bool Abierto => Error is not null || ErrorMotivo is not null || ErrorMotivoCodigo is not null;
}

public sealed class FormularioDescarte
{
    // 27/10/2026: el motivo del catálogo MotivosDescarte; Motivo es su detalle por escrito.
    public string? MotivoCodigo { get; set; }
    public string? Motivo { get; set; }
}
