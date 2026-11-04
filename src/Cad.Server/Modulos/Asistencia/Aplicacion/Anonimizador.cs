using System.Text.RegularExpressions;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Plan de asistencia con IA, 10 — datos mínimos: antes de enviar un texto al asistente se reemplazan los correos, los teléfonos
// (celulares de 9 dígitos, con +51 o separados) y los DNI u otros números largos por «[dato]». Las horas, las fechas cortas y los
// números de una dirección («cuadra 3», «Av. Ejército 512») no se tocan: sirven para entender el hecho.
public static partial class Anonimizador
{
    public const string Reemplazo = "[dato]";

    public static string Limpiar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;
        texto = Correo().Replace(texto, Reemplazo);
        texto = Telefono().Replace(texto, Reemplazo);
        return NumeroLargo().Replace(texto, Reemplazo);
    }

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}")]
    private static partial Regex Correo();

    // Celular peruano: 9 dígitos que empiezan por 9, opcionalmente con +51 y separados por espacios, puntos o guiones (987 654 321).
    [GeneratedRegex(@"(?<!\d)(?:\+?51[\s.-]?)?9\d{2}[\s.-]?\d{3}[\s.-]?\d{3}(?!\d)")]
    private static partial Regex Telefono();

    // DNI (8 dígitos), teléfonos fijos y cualquier otra cifra de 7 o más dígitos seguidos (con separadores simples).
    [GeneratedRegex(@"(?<!\d)\d(?:[\s.-]?\d){6,}(?!\d)")]
    private static partial Regex NumeroLargo();
}
