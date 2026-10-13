using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class RegistroPagina : ContentPage
{
    public RegistroPagina(RegistroVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = vistaModelo;
    }
}
