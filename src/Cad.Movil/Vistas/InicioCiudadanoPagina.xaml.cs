using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class InicioCiudadanoPagina : ContentPage
{
    private readonly InicioCiudadanoVistaModelo vistaModelo;

    public InicioCiudadanoPagina(InicioCiudadanoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // HU06: la comprobación del permiso se hace al abrir la aplicación (una vez por apertura).
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }
}
