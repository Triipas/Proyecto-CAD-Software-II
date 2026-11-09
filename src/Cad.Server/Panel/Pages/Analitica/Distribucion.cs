using System.Globalization;
using System.Text;

namespace Cad.Server.Panel.Pages.Analitica;

// «Tiempos de respuesta» como distribución (14/11/2026): cada columna son los SOS que se atendieron en ese minuto, desde que el
// ciudadano emite el SOS hasta que el sereno empieza a atenderlo. Muestra todos los SOS y no solo dos cifras: se ve la cola de los
// que tardaron mucho. La última columna junta los que llegaron al borde de la escala («34 min o más»).
public static class Distribucion
{
    // Cuántos SOS cayeron en cada minuto, de 0 a columnas - 1; la última junta todo lo que llega a ella o la pasa.
    public static int[] Contar(IEnumerable<double> minutos, int columnas)
    {
        var conteos = new int[Math.Max(1, columnas)];
        foreach (var m in minutos)
            conteos[Math.Clamp((int)Math.Floor(Math.Max(0, m)), 0, conteos.Length - 1)]++;
        return conteos;
    }

    // El periodo anterior como línea escalonada sobre las mismas columnas, en un viewBox de 1000 × 100 (estirado al ancho del
    // gráfico, con el trazo sin deformar). Alto: la parte del máximo común, para que las dos formas se comparen en la misma escala.
    public static string Escalonada(IReadOnlyList<int> conteos, double maximo)
    {
        if (conteos.Count == 0 || maximo <= 0) return "";
        var s = new StringBuilder();
        for (var i = 0; i < conteos.Count; i++)
        {
            var y = N(100 - conteos[i] / maximo * 100);
            s.Append(i == 0 ? $"M0 {y}" : $"V{y}").Append($"H{N((i + 1) * 1000.0 / conteos.Count)}");
        }
        return s.ToString();
    }

    public static string Rango(int columna, int columnas) =>
        columna == columnas - 1 ? $"{columna} min o más" : $"{columna} a {columna + 1} min";

    public static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
