using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class CambiarContrasenaPagina : ContentPage
{
    private readonly CambiarContrasenaVistaModelo vistaModelo;

    public CambiarContrasenaPagina(CambiarContrasenaVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // Sin contraseña nueva no hay sesión: «atrás» equivale a salir y volver a la bienvenida.
    protected override bool OnBackButtonPressed()
    {
        vistaModelo.Salir.Execute(null);
        return true;
    }
}
