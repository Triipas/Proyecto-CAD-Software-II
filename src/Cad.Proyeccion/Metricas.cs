namespace Cad.Proyeccion;

// HU46 (plan, 9.3) — cómo se mide una proyección en un bloque: de los casos que ocurrieron, qué parte cayó en el 10 % de celdas con
// más casos esperados (acierto) y cuánto mejor que el azar es eso por unidad de área (PAI = acierto ÷ fracción de área; 1 es azar).
// También la desviación de Poisson y el error absoluto, que miden el número y no solo el orden.
public static class Metricas
{
    public const double FraccionArea = 0.10;

    public sealed record Resultado(double Acierto, double Pai, double DesviacionPoisson, double ErrorAbsoluto, int Casos);

    // esperados y reales: por celda, en el mismo orden. Con empates en lo esperado, se toma el orden dado (es determinista).
    public static Resultado Evaluar(IReadOnlyList<double> esperados, IReadOnlyList<int> reales)
    {
        var n = esperados.Count;
        var top = Math.Max(1, (int)Math.Ceiling(FraccionArea * n));
        var casos = reales.Sum();
        var elegidas = Enumerable.Range(0, n).OrderByDescending(i => esperados[i]).ThenBy(i => i).Take(top).ToList();
        var acierto = casos == 0 ? 0 : elegidas.Sum(i => reales[i]) / (double)casos;
        var pai = acierto / ((double)top / n);
        double desviacion = 0, error = 0;
        for (var i = 0; i < n; i++)
        {
            var mu = Math.Max(esperados[i], 1e-6);
            var y = reales[i];
            desviacion += 2 * ((y > 0 ? y * Math.Log(y / mu) : 0) - (y - mu));
            error += Math.Abs(y - esperados[i]);
        }
        return new Resultado(acierto, pai, desviacion / n, error / n, casos);
    }

    // Promedio de varios bloques, ponderando el acierto y el PAI por los casos de cada bloque (un bloque sin casos no dice nada).
    public static Resultado Promedio(IReadOnlyList<Resultado> resultados)
    {
        var casos = resultados.Sum(r => r.Casos);
        if (resultados.Count == 0)
            return new Resultado(0, 0, 0, 0, 0);
        double Ponderado(Func<Resultado, double> f) => casos == 0 ? 0 : resultados.Sum(r => f(r) * r.Casos) / casos;
        return new Resultado(Ponderado(r => r.Acierto), Ponderado(r => r.Pai), resultados.Average(r => r.DesviacionPoisson),
            resultados.Average(r => r.ErrorAbsoluto), casos);
    }
}
