using Cad.Contratos.Casos;

namespace Cad.Server.Panel.Pages;

// Datos para el parcial _CampoCatalogo (27/10/2026): una lista de un catálogo de registro (resultado del cierre, prioridad,
// motivos, destino, canal) con su etiqueta, ayuda y error accesibles. El valor enviado es el código; se muestra el texto.
public sealed record CampoCatalogo(
    string Id,
    string Nombre,
    string Etiqueta,
    IReadOnlyList<OpcionCatalogo> Opciones,
    string? Valor = null,
    string? Error = null,
    string? Ayuda = null,
    string Vacio = "Elige una opción",
    // Clase adicional del bloque (p. ej. la que lo muestra solo con una decisión elegida).
    string? Clase = null);
