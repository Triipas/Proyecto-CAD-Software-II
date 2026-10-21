using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class PinPagina : ContentPage
{
    private readonly PinVistaModelo vistaModelo;

    public PinPagina(PinVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        vistaModelo.Preparar();
    }
}
