using System.Text.RegularExpressions;

namespace Cad.Server.Modulos.Asistencia;

// S6-T01 (plan de asistencia con IA, 5.4 y 6.3) — los criterios de triaje: una sola fuente, incrustada en el servidor. El asistente la
// recibe literal en el prompt y los operadores la leen en el panel (/panel/criterios-triaje): cambiar un criterio es cambiar el
// documento. Su versión (la línea «Versión …») queda en cada sugerencia para saber con qué criterios se decidió.
public static partial class CriteriosTriaje
{
    private const string Recurso = "Cad.Server.Modulos.Asistencia.Recursos.criterios-triaje.md";

    public static string Texto { get; } = Leer();

    public static string Version { get; } = VersionRegex().Match(Texto) is { Success: true } m ? m.Groups[1].Value : "sin-version";

    private static string Leer()
    {
        using var flujo = typeof(CriteriosTriaje).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"Falta el recurso incrustado {Recurso}.");
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd().Replace("\r\n", "\n").Trim();
    }

    [GeneratedRegex(@"Versión\s+([0-9A-Za-z.\-]+?)\.?\s")]
    private static partial Regex VersionRegex();
}
