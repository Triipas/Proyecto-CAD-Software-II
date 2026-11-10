namespace Cad.Server.Panel.Pages.Analitica;

// «Reportes y SOS falsos» como «uno de cada» (16/11/2026): las cuentas de los dibujos. Los íconos del titular son tantos como dice
// la frase («1 de cada 25»: 25 íconos y el último resaltado); los cuadros de 100 reparten un total en partes que suman 100 justas.
public static class UnoDeCada
{
    // Con más íconos que estos, la fila de íconos deja de leerse de un vistazo: se dice la frase y no se dibuja.
    public const int MaximoIconos = 100;

    // «1 de cada N»: N redondeado, al menos 1. Sin falsos no hay frase.
    public static int? Cada(int falsos, int total) =>
        falsos <= 0 || total <= 0 ? null : Math.Max(1, (int)Math.Round((double)total / falsos, MidpointRounding.AwayFromZero));

    // Reparte 100 cuadros entre las partes, en proporción, sin que se pierda ni sobre uno (el resto mayor se lleva el cuadro). Una
    // parte con casos nunca queda en 0 si hay cuadros para ella: un falso entre 500 SOS sigue viéndose.
    public static int[] Cuadros(IReadOnlyList<int> partes, int cuadros = 100)
    {
        var total = partes.Sum();
        var resultado = new int[partes.Count];
        if (total <= 0) return resultado;
        var restos = new double[partes.Count];
        for (var i = 0; i < partes.Count; i++)
        {
            var exacto = (double)partes[i] * cuadros / total;
            resultado[i] = (int)Math.Floor(exacto);
            restos[i] = exacto - resultado[i];
        }
        foreach (var i in Enumerable.Range(0, partes.Count).OrderByDescending(i => restos[i]).ThenByDescending(i => partes[i]).Take(cuadros - resultado.Sum()))
            resultado[i]++;
        // Las partes con casos que quedaron en 0 toman un cuadro de la parte más grande.
        for (var i = 0; i < partes.Count; i++)
        {
            if (partes[i] > 0 && resultado[i] == 0)
            {
                var mayor = Enumerable.Range(0, partes.Count).MaxBy(j => resultado[j]);
                if (resultado[mayor] <= 1) break;
                resultado[mayor]--;
                resultado[i]++;
            }
        }
        return resultado;
    }

    // El borde derecho de las reglas de porcentajes: múltiplo de 5 que deja aire sobre el mayor valor, desde 10 %.
    public static double Escala(IEnumerable<double> porcentajes) =>
        Math.Max(10, Math.Ceiling(porcentajes.DefaultIfEmpty(0).Max() * 1.15 / 5) * 5);
}
