using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// El histograma de «Tiempos de respuesta»: una columna por minuto y la última junta todo lo que llega al borde de la escala, para que
// un SOS de horas no aplaste a los demás. El periodo anterior se dibuja como línea escalonada sobre las mismas columnas.
public class DistribucionTiemposTests
{
    [Fact]
    public void Contar_UnaColumnaPorMinutoYLaUltimaJuntaElBorde()
    {
        // La columna i va de más de i a i + 1 inclusive: 3 cae en la 2; 3,99 y 4, en la 3.
        var conteos = Distribucion.Contar([0, 0.2, 0.9, 3, 3.99, 4, 9.5, 240], 10);

        Assert.Equal(10, conteos.Length);
        Assert.Equal(3, conteos[0]);
        Assert.Equal(1, conteos[2]);
        Assert.Equal(2, conteos[3]);
        Assert.Equal(2, conteos[9]); // 9,5 y el SOS de 4 horas
        Assert.Equal(8, conteos.Sum());
    }

    [Fact]
    public void Contar_ElObjetivoJustoCaeDentro()
    {
        // Un SOS atendido en 15 min justos cuenta como dentro del objetivo («≤ 15 min»), igual que en el titular: va en la
        // columna 14, la última verde; uno de 15 min y un segundo ya va en la 15, la primera ocre.
        var conteos = Distribucion.Contar([15, 15 + 1 / 60.0], 35);

        Assert.Equal(1, conteos[14]);
        Assert.Equal(1, conteos[15]);
    }

    [Fact]
    public void Escalonada_RecorreTodasLasColumnasEnLaMismaEscala()
    {
        var d = Distribucion.Escalonada([0, 5, 10, 5], 10);

        Assert.StartsWith("M0 100H250", d);
        Assert.Contains("V0H750", d);   // la columna más alta toca el techo
        Assert.EndsWith("H1000", d);
    }

    [Theory]
    [InlineData(3, 35, "3 a 4 min")]
    [InlineData(34, 35, "más de 34 min")]
    public void Rango_NombraElMinutoYElBorde(int columna, int columnas, string texto) =>
        Assert.Equal(texto, Distribucion.Rango(columna, columnas));
}
