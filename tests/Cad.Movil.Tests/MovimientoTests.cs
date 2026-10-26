using Cad.Movil.Vistas;
using Microsoft.Maui;

namespace Cad.Movil.Tests;

// Movimiento de la app (26/10/2026): las curvas son las mismas cubic-bezier del panel web (tokens.css).
public class MovimientoTests
{
    public static TheoryData<string> Curvas => ["Salida", "Entrada", "Estandar", "Desplazar"];

    private static Easing Curva(string nombre) => nombre switch
    {
        "Salida" => Movimiento.Salida,
        "Entrada" => Movimiento.Entrada,
        "Estandar" => Movimiento.Estandar,
        _ => Movimiento.Desplazar,
    };

    [Theory]
    [MemberData(nameof(Curvas))]
    public void Empieza_en_cero_termina_en_uno_y_nunca_retrocede(string nombre)
    {
        var curva = Curva(nombre);
        Assert.Equal(0, curva.Ease(0), 6);
        Assert.Equal(1, curva.Ease(1), 6);
        var anterior = 0.0;
        for (var x = 0.01; x <= 1; x += 0.01)
        {
            var valor = curva.Ease(x);
            Assert.True(valor >= anterior - 1e-6, $"{nombre} retrocede en x={x:0.00}");
            anterior = valor;
        }
    }

    [Fact]
    public void La_salida_avanza_rapido_al_principio_y_la_entrada_tarda_en_arrancar()
    {
        // Valores de referencia de cubic-bezier(0.16, 1, 0.3, 1) y (0.7, 0, 0.84, 0) en la mitad del tiempo.
        Assert.Equal(0.97178, Movimiento.Salida.Ease(0.5), 4);
        Assert.Equal(0.02822, Movimiento.Entrada.Ease(0.5), 4);
    }

    [Fact]
    public void Una_curva_lineal_devuelve_el_mismo_avance()
    {
        var lineal = Movimiento.Curva(0, 0, 1, 1);
        foreach (var x in new[] { 0.1, 0.25, 0.5, 0.75, 0.9 })
            Assert.Equal(x, lineal.Ease(x), 4);
    }
}
