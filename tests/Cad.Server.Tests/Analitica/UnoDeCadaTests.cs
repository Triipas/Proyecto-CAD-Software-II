using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// «Reportes y SOS falsos»: la frase «uno de cada N» y los cuadros de 100 que reparten un total sin perder ni sobrar uno.
public class UnoDeCadaTests
{
    [Theory]
    [InlineData(42, 1050, 25)]
    [InlineData(9, 520, 58)]
    [InlineData(1, 4, 4)]
    [InlineData(3, 2, 1)]  // más falsos que la mitad: al menos «uno de cada 1»
    public void Cada_RedondeaElTotalPorFalso(int falsos, int total, int esperado) =>
        Assert.Equal(esperado, UnoDeCada.Cada(falsos, total));

    [Fact]
    public void Cada_SinFalsosNoHayFrase() => Assert.Null(UnoDeCada.Cada(0, 300));

    [Fact]
    public void Cuadros_SumanCienYSiguenLaProporcion()
    {
        var cuadros = UnoDeCada.Cuadros([476, 9, 21, 14]);

        Assert.Equal(100, cuadros.Sum());
        Assert.Equal([91, 2, 4, 3], cuadros); // 91,5 · 1,7 · 4,0 · 2,7: los dos restos mayores (falsos y alarmas) suben
    }

    [Fact]
    public void Cuadros_UnaParteConCasosNuncaQuedaEnCero()
    {
        var cuadros = UnoDeCada.Cuadros([997, 1, 2]);

        Assert.Equal(100, cuadros.Sum());
        Assert.All(cuadros, c => Assert.True(c >= 1));
    }

    [Fact]
    public void Cuadros_SinCasosTodoEnCero() => Assert.Equal([0, 0], UnoDeCada.Cuadros([0, 0]));

    [Fact]
    public void Escala_DejaAireSobreElMayorYEmpiezaEnDiez()
    {
        Assert.Equal(10, UnoDeCada.Escala([3.1, 8.2]));
        Assert.Equal(15, UnoDeCada.Escala([12]));
    }
}
