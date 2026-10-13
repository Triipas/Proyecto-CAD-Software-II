using System.Globalization;
using System.Text;

namespace Cad.PruebaCarga;

public sealed record Opciones(
    Uri Url, string Entorno, int Ciudadanos, int Paneles, int Sos,
    int PausaMinMs, int PausaMaxMs, int EsperaAcusesSegundos, string Salida)
{
    public static Opciones Leer(string[] args)
    {
        var valores = new Dictionary<string, string>();
        for (var i = 0; i + 1 < args.Length; i += 2)
            valores[args[i].TrimStart('-')] = args[i + 1];

        string Texto(string clave, string? porDefecto = null) =>
            valores.TryGetValue(clave, out var v) ? v : porDefecto ?? throw new ArgumentException($"Falta --{clave}.");
        int Entero(string clave, int porDefecto) =>
            valores.TryGetValue(clave, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : porDefecto;

        return new Opciones(
            new Uri(Texto("url")), Texto("entorno"),
            Entero("ciudadanos", 19), Entero("paneles", 1), Entero("sos", 100),
            Entero("pausa-min-ms", 200), Entero("pausa-max-ms", 1500), Entero("espera-acuses-s", 20),
            Texto("salida", "docs/mediciones"));
    }
}

public static class Informe
{
    public static string Markdown(Opciones o, string ejecucion, ResultadoRnf02 r, IReadOnlyDictionary<int, int> rechazos)
    {
        static string Ms(double? valor) => valor is null ? "—" : $"{valor.Value.ToString("0", CultureInfo.InvariantCulture)} ms";
        var esEnsayo = o.Entorno.Contains("local", StringComparison.OrdinalIgnoreCase) || o.Entorno.Contains("ensayo", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine($"# Medición RNF02 / HU11-CA05 — {o.Entorno}");
        sb.AppendLine();
        if (esEnsayo)
            sb.AppendLine("> **Ensayo, no válido para aceptar RNF02**: el requisito exige el entorno de pruebas publicado.\n");
        sb.AppendLine($"- Ejecución: `{ejecucion}` (UTC) · Servidor: `{o.Url.Host}`");
        sb.AppendLine($"- Criterio (Plan Scrum, RNF02): con {Estadisticas.SesionesRequeridas} sesiones concurrentes y al menos {Estadisticas.SosMinimos} SOS, al menos el {Estadisticas.PorcentajeMinimo} % se presenta en la central en ≤ 3 s desde la aceptación del servidor.");
        sb.AppendLine("- Medida: `PresentadoEn − AceptadoEn`, ambos con el reloj del servidor. «Presentado» = acuse del panel real cuando el SOS ya se muestra y se ha avisado. Un SOS sin acuse cuenta como incumplimiento.");
        sb.AppendLine("- **No es el tiempo de respuesta del sereno.**");
        sb.AppendLine();
        sb.AppendLine("| Parámetro | Valor |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Sesiones concurrentes | {r.SesionesConcurrentes} ({o.Ciudadanos} de ciudadano generadas por la herramienta + {o.Paneles} panel(es) de operador abiertos) |");
        sb.AppendLine($"| SOS solicitados / aceptados (201) | {o.Sos} / {r.SosAceptados} |");
        sb.AppendLine($"| Rechazados por el servidor | {(rechazos.Count == 0 ? "ninguno" : string.Join(", ", rechazos.Select(x => $"{x.Value} × HTTP {x.Key}")))} |");
        sb.AppendLine($"| Pausa entre SOS de una sesión | {o.PausaMinMs}–{o.PausaMaxMs} ms |");
        sb.AppendLine();
        sb.AppendLine("| Resultado | Valor |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| SOS con acuse de presentación | {r.SosConAcuse} de {r.SosAceptados} |");
        sb.AppendLine($"| Presentados en ≤ 3 s | {r.DentroDelLimite} de {r.SosAceptados} (**{r.PorcentajeDentroDelLimite.ToString("0.##", CultureInfo.InvariantCulture)} %**) |");
        sb.AppendLine($"| p50 / p95 / máximo | {Ms(r.P50Ms)} / {Ms(r.P95Ms)} / {Ms(r.MaximoMs)} |");
        sb.AppendLine($"| Muestra ≥ {Estadisticas.SosMinimos} SOS | {(r.CumpleTamanoDeMuestra ? "sí" : "no")} |");
        sb.AppendLine($"| Sesiones ≥ {Estadisticas.SesionesRequeridas} | {(r.CumpleSesiones ? "sí" : "no")} |");
        sb.AppendLine($"| **Resultado** | **{(r.Cumple ? "CUMPLE" : "NO CUMPLE")}** |");
        return sb.ToString();
    }
}
