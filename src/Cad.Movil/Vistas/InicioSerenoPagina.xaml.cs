using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class InicioSerenoPagina : ContentPage
{
    public InicioSerenoPagina(InicioSerenoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = vistaModelo;
    }
}
