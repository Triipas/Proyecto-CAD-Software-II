using Cad.Server.Modulos.Analitica;
using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// «Cobertura por zona» como tablero: cada zona de los bloques en su fila y columna del plano, el borde de su bloque solo donde la
// vecina es de otro bloque, la base de cada bloque y el resumen por bloque.
public class TableroZonasTests
{
    private static ZonaCobertura Medida(Zona z, double porcentaje) => new(z.Sur, z.Oeste, z.Norte, z.Este, 0, porcentaje, 0);

    private static TableroZonas Armar(Func<string, double> porcentaje) =>
        TableroZonas.Armar(BloquesDistrito.Todos.SelectMany(b => b.Zonas).Select(z => Medida(z, porcentaje(BloquesDistrito.Referencia(z)))).ToList());

    [Fact]
    public void Armar_PoneLas53ZonasEnLaCuadriculaDeIaRYDe13a21()
    {
        var tablero = Armar(_ => 80);

        Assert.Equal(53, tablero.Celdas.Count);
        Assert.Equal((10, 9), (tablero.Columnas, tablero.Filas));
        var m13 = tablero.Celdas.Single(c => c.Nombre == "M13");
        Assert.Equal((4, 0), (m13.Columna, m13.Fila)); // M es la quinta columna desde I; 13, la primera fila
        Assert.Equal("bustamante", m13.Bloque.Clave);
        Assert.True(tablero.Proporcion is > 0.9 and < 1.3); // celdas casi cuadradas: 10 de ancho por 9 de alto
    }

    [Fact]
    public void Armar_MarcaElBordeSoloFrenteAOtroBloqueYLaBase()
    {
        var tablero = Armar(_ => 80);

        var k15 = tablero.Celdas.Single(c => c.Nombre == "K15"); // Avelino: arriba K14 es Bustamante, abajo K16 es Avelino
        Assert.True(k15.Norte);
        Assert.False(k15.Sur);
        Assert.True(tablero.Celdas.Single(c => c.Nombre == "K16").Base);
        Assert.Equal(0, tablero.Celdas.Single(c => c.Nombre == "L17").Distancia); // la base de Bustamante
    }

    [Fact]
    public void Armar_ResumePorBloqueConElPromedioYLasZonasBajas()
    {
        var tablero = Armar(nombre => nombre is "R21" or "Q21" ? 30 : 90);

        var simon = tablero.Bloques.Single(b => b.Bloque.Clave == "simon");
        Assert.Equal(20, simon.Zonas);
        Assert.Equal(2, simon.Bajas);
        Assert.Equal((18 * 90 + 2 * 30) / 20.0, simon.Porcentaje, 6);
        Assert.Equal(0, tablero.Bloques.Single(b => b.Bloque.Clave == "avelino").Bajas);
    }

    [Fact]
    public void Armar_SinZonasNoHayTablero() => Assert.Empty(TableroZonas.Armar([]).Celdas);
}
