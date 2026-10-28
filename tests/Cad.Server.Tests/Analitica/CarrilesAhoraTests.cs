using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// Las pistas de «Ahora»: todas miden en veces lo esperado (la franja verde termina en el mismo lugar en cada fila) y los puntos
// casi juntos se agrupan en uno con su cantidad, para que ningún rótulo ni punto se pisen.
public class CarrilesAhoraTests
{
    [Theory]
    [InlineData(5, 5, 100.0 / 3)]
    [InlineData(10, 5, 200.0 / 3)]
    [InlineData(15, 5, 100)]
    [InlineData(5000, 5, 100)] // un caso muy lento se queda en el borde
    [InlineData(0, 60, 0)]
    public void Posicion_EsEnVecesLoEsperado(double minutos, int esperado, double enPista) =>
        Assert.Equal(enPista, Carriles.Posicion(minutos, esperado), 3);

    [Fact]
    public void Pista_AgrupaLosPuntosCasiJuntosYMarcaLosAtascados()
    {
        // Esperado: 30 min. 1, 2 y 3 min caen a menos de 3,5 % unos de otros; 20 min está aparte; 40 min ya pasó lo esperado.
        var html = Carriles.Pista([1, 2, 3, 20, 40], 30, "Prueba").ToString()!;

        Assert.Equal(3, Cuenta(html, "class=\"pista-a__caso"));
        Assert.Contains("pista-a__caso--grupo", html);
        Assert.Contains(">3<span class=\"pista-a__globo\"", html);
        Assert.Equal(1, Cuenta(html, "pista-a__caso--atascado"));
        Assert.Contains("3 casos en la etapa", html);
        Assert.Contains("width:33.33%", html); // la franja de lo esperado
    }

    [Fact]
    public void Pista_UnCasoMuyLento_QuedaEnElBordeYSeMarcaComoLejano()
    {
        var html = Carriles.Pista([840], 5, "Prueba").ToString()!;

        Assert.Contains("pista-a__caso--lejos", html);
        Assert.Contains("left:100%", html);
        Assert.Contains("14 h 00 min en la etapa</b>", html);
    }

    [Fact]
    public void Pista_SinCasos_SoloDibujaLaFranja()
    {
        var html = Carriles.Pista([], 15, "Prueba").ToString()!;

        Assert.Contains("pista-a__esperado", html);
        Assert.DoesNotContain("pista-a__caso", html);
    }

    private static int Cuenta(string texto, string parte) => texto.Split(parte).Length - 1;
}
