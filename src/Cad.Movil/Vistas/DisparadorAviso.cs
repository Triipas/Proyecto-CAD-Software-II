namespace Cad.Movil.Vistas;

// Cuándo un aviso emergente se abre (25/10/2026): al aparecer (pasa a mostrarse con texto) o al cambiar su texto o su tipo
// mientras se muestra. Repetir el mismo estado no lo vuelve a abrir; cuando deja de mostrarse, el mismo mensaje puede volver
// a abrirse la próxima vez (p. ej., el mismo error tras otro intento). Sin MAUI, para probarlo.
public sealed class DisparadorAviso
{
    private (string Tipo, string Texto)? ultimo;

    // Devuelve true si hay que abrir el diálogo con este estado.
    public bool Evaluar(bool mostrar, string? tipo, string? texto)
    {
        if (!mostrar || string.IsNullOrWhiteSpace(texto))
        {
            ultimo = null;
            return false;
        }
        var actual = (tipo ?? "informacion", texto);
        if (ultimo == actual)
            return false;
        ultimo = actual;
        return true;
    }
}
