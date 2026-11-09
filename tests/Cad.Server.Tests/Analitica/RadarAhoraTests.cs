using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// El radar de «Ahora»: todas las porciones miden en veces lo esperado (el anillo verde cae en el mismo radio en cada etapa), los SOS
// ocupan la parte de arriba y cada caso queda dentro de la porción de su etapa.
public class RadarAhoraTests
{
    [Theory]
    [InlineData(0, Radar.Centro)]
    [InlineData(1, Radar.Centro + Radar.Paso)]
    [InlineData(2, Radar.Centro + 2 * Radar.Paso)]
    [InlineData(50, Radar.Borde)] // un caso muy lento se queda en el borde
    public void Radio_EsEnVecesLoEsperado(double veces, double radio) =>
        Assert.Equal(radio, Radar.Radio(veces), 3);

    [Fact]
    public void Etapas_CompletanElCirculoSinSolaparseYLosSosVanArriba()
    {
        Assert.Equal(10, Radar.Etapas.Count);
        for (var i = 1; i < Radar.Etapas.Count; i++)
            Assert.Equal(Radar.Etapas[i - 1].Hasta, Radar.Etapas[i].Desde, 6); // sin huecos ni solapes
        Assert.Equal(360, Radar.Etapas[^1].Hasta - Radar.Etapas[0].Desde, 6);
        Assert.All(Radar.Etapas, e => Assert.Equal(36, e.Hasta - e.Desde, 6));

        var sos = Radar.Etapas.Where(e => !e.Reporte).ToList();
        Assert.Equal(4, sos.Count);
        Assert.Equal(-90, (sos[0].Desde + sos[^1].Hasta) / 2, 3); // centrados arriba
        Assert.Equal(EstadoCaso.Pendiente, Radar.De(EstadoCaso.Pendiente, reporte: true)!.Estado);
        Assert.Equal("Admitido, sin asignar", Radar.De(EstadoCaso.Pendiente, reporte: true)!.Nombre);
    }

    [Fact]
    public void Casos_SeRepartenDentroDeSuPorcionDelMasAntiguoAlMasNuevo()
    {
        var etapa = Radar.De(EstadoCaso.Recibido, reporte: true)!;
        var ahora = DateTimeOffset.UtcNow;
        var abiertos = new[]
        {
            new CasoAtascado(Guid.NewGuid(), OrigenCaso.Reporte, EstadoCaso.Recibido, ahora.AddMinutes(-5), 5, 15),
            new CasoAtascado(Guid.NewGuid(), OrigenCaso.Reporte, EstadoCaso.Recibido, ahora.AddMinutes(-48), 48, 15),
            new CasoAtascado(Guid.NewGuid(), OrigenCaso.Sos, EstadoCaso.Pendiente, ahora.AddMinutes(-2), 2, 3),
        };

        var casos = Radar.Casos(etapa, abiertos);

        Assert.Equal(2, casos.Count);
        Assert.Equal(48, casos[0].Minutos); // el más antiguo primero
        Assert.All(casos, c => Assert.InRange(c.Grados, etapa.Desde, etapa.Hasta));
        Assert.True(casos[0].Tarde && casos[0].Lejos);
        Assert.False(casos[1].Tarde);
        Assert.Equal(Radar.Borde, Math.Sqrt(Math.Pow(casos[0].Posicion.X, 2) + Math.Pow(casos[0].Posicion.Y, 2)), 3);
    }

    [Fact]
    public void Marcas_DiezSosAtascadosEnElBorde_SeJuntanSinPisarse()
    {
        // El caso que se reportó en la revisión: diez SOS pendientes con más de 3× lo esperado quedan todos en el borde y se juntan en
        // pocas marcas que no se pisan.
        var etapa = Radar.De(EstadoCaso.Pendiente, reporte: false)!;
        var ahora = DateTimeOffset.UtcNow;
        var abiertos = Enumerable.Range(0, 10)
            .Select(i => new CasoAtascado(Guid.NewGuid(), OrigenCaso.Sos, EstadoCaso.Pendiente, ahora.AddMinutes(-(20 + i)), 20 + i, 3))
            .ToList();

        var marcas = Radar.Marcas(Radar.Casos(etapa, abiertos));

        Assert.Equal(10, marcas.Sum(m => m.Casos.Count));
        Assert.True(marcas.Count <= 4);
        Assert.All(marcas, m => Assert.True(m.Lejos && m.Tarde));
        for (var i = 0; i < marcas.Count; i++)
            for (var j = i + 1; j < marcas.Count; j++)
                Assert.True(Math.Sqrt(Math.Pow(marcas[i].X - marcas[j].X, 2) + Math.Pow(marcas[i].Y - marcas[j].Y, 2)) >= Radar.Separacion);
    }

    [Fact]
    public void Marcas_CasosSeparados_QuedanCadaUnoEnSuMarca()
    {
        var etapa = Radar.De(EstadoCaso.Recibido, reporte: true)!;
        var ahora = DateTimeOffset.UtcNow;
        var abiertos = new[]
        {
            new CasoAtascado(Guid.NewGuid(), OrigenCaso.Reporte, EstadoCaso.Recibido, ahora.AddMinutes(-2), 2, 15),
            new CasoAtascado(Guid.NewGuid(), OrigenCaso.Reporte, EstadoCaso.Recibido, ahora.AddMinutes(-30), 30, 15),
        };

        var marcas = Radar.Marcas(Radar.Casos(etapa, abiertos));

        Assert.Equal(2, marcas.Count);
        Assert.All(marcas, m => Assert.False(m.Grupo));
        Assert.Equal($"caso-{abiertos[1].CasoId}", marcas[0].Clave); // el más antiguo primero
    }

    [Fact]
    public void Llegada_EsCuandoPasaElHaz()
    {
        Assert.Equal(0, Radar.Llegada(Radar.Inicio));
        Assert.Equal(Radar.DuracionBarrido / 2, Radar.Llegada(Radar.Inicio + 180));
    }

    [Fact]
    public void Cuna_EsUnaPorcionCerrada()
    {
        var d = Radar.Cuna(Radar.Centro, Radar.Borde, -90, -60);

        Assert.StartsWith("M", d);
        Assert.EndsWith("Z", d);
        Assert.Equal(2, d.Split('A').Length - 1);
    }
}
