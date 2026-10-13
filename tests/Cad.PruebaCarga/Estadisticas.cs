namespace Cad.PruebaCarga;

// RNF02: «Con 20 sesiones concurrentes en el entorno de pruebas, al menos el 95% de los SOS deberá
// mostrarse y notificarse en central dentro de 3 segundos desde su aceptación por el servidor.
// Una prueba de al menos 100 SOS registrará aceptación y presentación.» (Plan Scrum, RNF02)
public sealed record ResultadoRnf02(
    int SesionesConcurrentes,
    int SosAceptados,
    int SosConAcuse,
    int DentroDelLimite,
    double PorcentajeDentroDelLimite,
    double? P50Ms,
    double? P95Ms,
    double? MaximoMs,
    bool CumpleTamanoDeMuestra,
    bool CumpleSesiones,
    bool Cumple);

public static class Estadisticas
{
    public const int SesionesRequeridas = 20;
    public const int SosMinimos = 100;
    public const double PorcentajeMinimo = 95.0;
    public static readonly TimeSpan Limite = TimeSpan.FromSeconds(3);

    // latencias: una por SOS aceptado; null = sin acuse de presentación (cuenta como incumplimiento).
    public static ResultadoRnf02 Evaluar(IReadOnlyList<TimeSpan?> latencias, int sesionesConcurrentes)
    {
        var medidas = latencias.OfType<TimeSpan>().Select(l => l.TotalMilliseconds).Order().ToList();
        var dentro = latencias.Count(l => l is { } valor && valor <= Limite);
        var porcentaje = latencias.Count == 0 ? 0 : Math.Round(100.0 * dentro / latencias.Count, 2);
        var muestra = latencias.Count >= SosMinimos;
        var sesiones = sesionesConcurrentes >= SesionesRequeridas;

        return new ResultadoRnf02(
            sesionesConcurrentes, latencias.Count, medidas.Count, dentro, porcentaje,
            Percentil(medidas, 50), Percentil(medidas, 95), medidas.Count == 0 ? null : medidas[^1],
            muestra, sesiones, muestra && sesiones && porcentaje >= PorcentajeMinimo);
    }

    // Percentil por rango más cercano sobre valores ordenados.
    public static double? Percentil(IReadOnlyList<double> ordenados, int percentil)
    {
        if (ordenados.Count == 0) return null;
        var rango = (int)Math.Ceiling(percentil / 100.0 * ordenados.Count);
        return ordenados[Math.Clamp(rango, 1, ordenados.Count) - 1];
    }
}
