using System.Text.RegularExpressions;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Plan de asistencia con IA, 10 — datos mínimos (ADR-10, «Anonimización: ley y estándares»). Antes de enviar un texto al
// asistente se quitan los identificadores directos, con las técnicas de enmascaramiento y supresión de ISO/IEC 20889:
// - correos, teléfonos (celulares de 9 dígitos, con +51 o separados) y DNI u otros números largos → «[dato]»;
// - placas de vehículos (ABC-123, A1B-234) → «[placa]»: identifican al propietario y el asistente no las necesita para
//   clasificar;
// - nombres de personas → «[nombre]»: los que el servidor conoce (quien reporta, su contacto) y los que el texto presenta como
//   nombre («me llamo…», «el señor…», «mi vecino…»).
// Las horas, las fechas cortas y los números de una dirección («cuadra 3», «Av. Ejército 512») no se tocan: sirven para entender
// el hecho y, sin nombre ni teléfono, no identifican a nadie por sí solos.
public static partial class Anonimizador
{
    public const string Reemplazo = "[dato]";
    public const string ReemplazoNombre = "[nombre]";
    public const string ReemplazoPlaca = "[placa]";

    public static string Limpiar(string texto) => Limpiar(texto, []);

    public static string Limpiar(string texto, IEnumerable<string?> nombresConocidos)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;
        texto = Correo().Replace(texto, Reemplazo);
        texto = Telefono().Replace(texto, Reemplazo);
        texto = NumeroLargo().Replace(texto, Reemplazo);
        texto = Placa().Replace(texto, ReemplazoPlaca);
        // Primero el nombre presentado completo: si antes se tapara una parte que coincide con un nombre conocido («mi vecina Rosa
        // García» con «Rosa» conocida), el resto («García») ya no se reconocería como nombre.
        texto = NombrePresentado().Replace(texto, m => m.Groups["antes"].Value + ReemplazoNombre);
        texto = QuitarNombresConocidos(texto, nombresConocidos);
        return NombresSeguidos().Replace(texto, ReemplazoNombre);
    }

    // Cada palabra de un nombre conocido (de 3 o más letras: «de», «la» o «del» no identifican), como palabra completa. Los
    // nombres compuestos con guion o apóstrofo («Pérez-García», «D'Angelo») se parten en sus palabras: ninguna se pierde.
    private static string QuitarNombresConocidos(string texto, IEnumerable<string?> nombres)
    {
        var palabras = nombres
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => PalabrasDeNombre().Split(n!))
            .Where(p => p.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ToList();
        if (palabras.Count == 0)
            return texto;
        var patron = $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', palabras.Select(Regex.Escape))})(?![\p{{L}}\p{{N}}])";
        return Regex.Replace(texto, patron, ReemplazoNombre, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    // Separa un nombre en sus palabras: todo lo que no es letra (espacios, guiones, apóstrofos, puntos).
    [GeneratedRegex(@"[^\p{L}]+")]
    private static partial Regex PalabrasDeNombre();

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}")]
    private static partial Regex Correo();

    // Celular peruano: 9 dígitos que empiezan por 9, opcionalmente con +51 y separados por espacios, puntos o guiones (987 654 321).
    [GeneratedRegex(@"(?<!\d)(?:\+?51[\s.-]?)?9\d{2}[\s.-]?\d{3}[\s.-]?\d{3}(?!\d)")]
    private static partial Regex Telefono();

    // DNI (8 dígitos), teléfonos fijos y cualquier otra cifra de 7 o más dígitos seguidos (con separadores simples).
    [GeneratedRegex(@"(?<!\d)\d(?:[\s.-]?\d){6,}(?!\d)")]
    private static partial Regex NumeroLargo();

    // Placa peruana: tres letras o cifras (al menos una letra), guion opcional y tres cifras.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?=[A-Za-z0-9]{0,2}[A-Za-z])[A-Za-z0-9]{3}-?\d{3}(?![\p{L}\p{N}])")]
    private static partial Regex Placa();

    // Un nombre propio (una a tres palabras con mayúscula inicial) justo después de algo que lo presenta como nombre de persona.
    // Solo la presentación ignora mayúsculas; el nombre tiene que empezar con mayúscula («mi vecino del 3» no se toca).
    [GeneratedRegex(@"(?<antes>(?<![\p{L}])(?i:me llamo|mi nombre es|se llama|se llamaba|soy|señor|señora|señorita|sr\.|sra\.|srta\.|don|doña|vecino|vecina|hijo|hija|hermano|hermana|esposo|esposa|sobrino|sobrina|nieto|nieta|primo|prima)\s+)\p{Lu}\p{Ll}+(?:[-'’]\p{L}+)?(?:\s+\p{Lu}\p{Ll}+(?:[-'’]\p{L}+)?){0,2}")]
    private static partial Regex NombrePresentado();

    // Dos o más «[nombre]» seguidos (nombre y apellido, o las dos partes de «Pérez-García») quedan en uno solo.
    [GeneratedRegex(@"\[nombre\](?:[\s'’-]+\[nombre\])+")]
    private static partial Regex NombresSeguidos();
}
