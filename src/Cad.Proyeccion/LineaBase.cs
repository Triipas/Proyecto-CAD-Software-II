namespace Cad.Proyeccion;

// Parámetros de la línea base (plan, 8.4); van en Proyeccion:* y se ajustan en el mundo A.
public sealed record ParametrosLineaBase(int Semanas = 26, double VidaMedia = 6, double Encogimiento = 3, double Suavizado = 0.3);

// HU46 (plan, 8.4) — línea base estadística: casos esperados en una celda y un bloque.
//   1. Por semana atrás, los casos en la misma franja semanal (día y bloque), con memoria corta: peso 0,5^(k / vida media).
//   2. Reducción hacia lo general: con pocos casos, pesa más la tasa total de la celda repartida según la franja en toda la ciudad.
//   3. Suavizado espacial con las 8 celdas vecinas.
// Solo mira semanas anteriores al bloque.
public sealed class LineaBase(Historial historial, ParametrosLineaBase? parametros = null)
{
    private readonly ParametrosLineaBase p = parametros ?? new();
    private readonly Dictionary<(Celda, long), double> cache = [];
    private readonly Dictionary<long, double[]> pesoFranjaCache = [];

    public ParametrosLineaBase Parametros => p;

    public double Calcular(Celda celda, long bloque)
    {
        var propia = Reducida(celda, bloque);
        if (p.Suavizado <= 0)
            return propia;
        var vecinas = celda.Vecinas().Select(v => Reducida(v, bloque)).Average();
        return (1 - p.Suavizado) * propia + p.Suavizado * vecinas;
    }

    private double Reducida(Celda celda, long bloque)
    {
        if (cache.TryGetValue((celda, bloque), out var guardada))
            return guardada;
        double ponderado = 0, pesos = 0, efectivo = 0, totalSemanal = 0;
        for (var k = 1; k <= p.Semanas; k++)
        {
            var anterior = bloque - (long)k * Bloques.PorSemana;
            if (anterior < historial.PrimerBloque)
                break;
            var w = Math.Pow(0.5, k / p.VidaMedia);
            var n = historial.Casos(celda, anterior);
            ponderado += w * n;
            efectivo += w * n;
            pesos += w;
            // Toda la semana de la celda que termina en ese bloque (las 42 franjas).
            totalSemanal += w * historial.Casos(celda, anterior - Bloques.PorSemana + 1, anterior + 1);
        }
        var resultado = 0.0;
        if (pesos > 0)
        {
            var tasa = ponderado / pesos;
            var general = totalSemanal / pesos * PesoFranja(bloque)[Bloques.Franja(bloque)];
            var alfa = efectivo / (efectivo + p.Encogimiento);
            resultado = alfa * tasa + (1 - alfa) * general;
        }
        cache[(celda, bloque)] = resultado;
        return resultado;
    }

    // Qué parte de los casos de una semana cae en cada franja, en toda la ciudad (las mismas semanas que la línea base).
    private double[] PesoFranja(long bloque)
    {
        var semana = (bloque - historial.PrimerBloque) / Bloques.PorSemana;
        if (pesoFranjaCache.TryGetValue(semana, out var guardado))
            return guardado;
        var conteo = new double[Bloques.PorSemana];
        var desde = Math.Max(historial.PrimerBloque, bloque - (long)p.Semanas * Bloques.PorSemana);
        for (var b = desde; b < bloque; b++)
            conteo[Bloques.Franja(b)] += historial.TotalCiudad(b);
        var total = conteo.Sum();
        var pesos = total == 0 ? Enumerable.Repeat(1.0 / Bloques.PorSemana, Bloques.PorSemana).ToArray() : conteo.Select(c => c / total).ToArray();
        pesoFranjaCache[semana] = pesos;
        return pesos;
    }
}
