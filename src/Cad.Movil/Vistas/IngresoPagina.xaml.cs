using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class IngresoPagina : ContentPage
{
    public IngresoPagina(IngresoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = vistaModelo;
    }
}
