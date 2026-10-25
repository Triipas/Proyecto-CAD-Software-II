using Cad.Movil.Vistas;

namespace Cad.Movil.Tests;

// Avisos emergentes (25/10/2026): cuándo se abre el diálogo de un aviso de un momento.
public class AvisosEmergentesTests
{
    [Fact]
    public void SeAbreAlAparecer_YNoSeRepiteMientrasSigueIgual()
    {
        var disparador = new DisparadorAviso();

        Assert.False(disparador.Evaluar(false, "error", "No se pudo enviar.")); // aún no se muestra
        Assert.True(disparador.Evaluar(true, "error", "No se pudo enviar."));
        Assert.False(disparador.Evaluar(true, "error", "No se pudo enviar.")); // la misma notificación otra vez
    }

    [Fact]
    public void UnTextoOTipoNuevo_SeAbreOtraVez()
    {
        var disparador = new DisparadorAviso();
        Assert.True(disparador.Evaluar(true, "error", "Sin conexión."));

        Assert.True(disparador.Evaluar(true, "error", "El servidor rechazó la solicitud."));
        Assert.True(disparador.Evaluar(true, "exito", "El servidor rechazó la solicitud."));
    }

    [Fact]
    public void TrasOcultarse_ElMismoMensajeVuelveAAbrirse()
    {
        var disparador = new DisparadorAviso();
        Assert.True(disparador.Evaluar(true, "error", "Código incorrecto."));

        Assert.False(disparador.Evaluar(false, "error", "Código incorrecto.")); // se limpió (p. ej., al reintentar)
        Assert.True(disparador.Evaluar(true, "error", "Código incorrecto.")); // el mismo error en el siguiente intento
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinTexto_NoSeAbre(string? texto) => Assert.False(new DisparadorAviso().Evaluar(true, "error", texto));
}
