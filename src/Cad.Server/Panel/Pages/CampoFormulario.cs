namespace Cad.Server.Panel.Pages;

// Datos para el parcial _Campo: etiqueta, control, ayuda y error accesibles.
public sealed record CampoFormulario(
    string Id,
    string Nombre,
    string Etiqueta,
    string Tipo = "text",
    string? Valor = null,
    string? Error = null,
    string? Ayuda = null,
    string? Autocompletar = null,
    bool Requerido = true);
