using Cad.Server.Modulos.Analitica;
using Cad.Server.Panel.Pages.Analitica;

namespace Cad.Server.Tests.Analitica;

// «Reasignaciones»: la parte del motivo principal dicha en voz alta y los tramos del periodo (hasta ahora, en hora local).
public class ArreglarTests
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    [Theory]
    [InlineData(22, 58, "Casi ", "4 de cada 10", " reasignaciones")]
    [InlineData(30, 100, "", "3 de cada 10", " reasignaciones")]
    [InlineData(44, 100, "Más de ", "4 de cada 10", " reasignaciones")]
    [InlineData(10, 20, "", "La mitad", " de las reasignaciones")]
    [InlineData(7, 7, "", "Todas", " las reasignaciones")]
    [InlineData(2, 40, "", "2 de las 40", " reasignaciones")]
    public void Parte_DiceLaProporcionComoEnVozAlta(int veces, int total, string antes, string cifra, string despues) =>
        Assert.Equal((antes, cifra, despues), Arreglar.Parte(veces, total));

    [Fact]
    public void Tramos_DeUnMesSonSemanasYNoPasanDeAhora()
    {
        var desde = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var periodo = new PeriodoAnalitica(desde, desde.AddDays(30), Lima);

        Assert.Equal(5, ConsultarReasignaciones.Tramos(periodo, desde.AddDays(40)).Count);
        var enCurso = ConsultarReasignaciones.Tramos(periodo, desde.AddDays(10));
        Assert.Equal(2, enCurso.Count); // a los 10 días van dos semanas empezadas
        Assert.Equal(desde.AddDays(10), enCurso[^1].Hasta); // la que va en curso termina ahora, no al final de la semana
        Assert.Equal("1 oct", ConsultarReasignaciones.Tramos(periodo, desde.AddDays(40))[0].Etiqueta);
    }

    [Fact]
    public void Tramos_DeTreintaDiasSeNombranPorSuPrimerDiaYNoPorUnMes()
    {
        var desde = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var tramos = ConsultarReasignaciones.Tramos(new(desde, desde.AddDays(180), Lima), desde.AddDays(200));

        Assert.Equal(6, tramos.Count);
        Assert.Equal(["1 ene 26", "31 ene 26"], tramos.Take(2).Select(t => t.Etiqueta));
    }

    [Fact]
    public void Tramos_DeSieteDiasSonDiasYDeUnDiaSonCuatroHoras()
    {
        var desde = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(7, ConsultarReasignaciones.Tramos(new(desde, desde.AddDays(7), Lima), desde.AddDays(8)).Count);
        var horas = ConsultarReasignaciones.Tramos(new(desde, desde.AddDays(1), Lima), desde.AddDays(2));
        Assert.Equal(6, horas.Count);
        Assert.Equal("0–4 h", horas[0].Etiqueta);
        Assert.Equal("20–24 h", horas[^1].Etiqueta);
    }
}
