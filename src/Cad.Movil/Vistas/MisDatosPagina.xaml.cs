using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class MisDatosPagina : ContentPage
{
    private readonly MisDatosVistaModelo vistaModelo;

    public MisDatosPagina(MisDatosVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.CargarAsync();
    }
}
