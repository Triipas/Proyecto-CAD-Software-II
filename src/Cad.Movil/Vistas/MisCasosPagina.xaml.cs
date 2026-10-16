using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class MisCasosPagina : ContentPage
{
    private readonly MisCasosVistaModelo vistaModelo;

    public MisCasosPagina(MisCasosVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // CA02: cada vez que se muestra (también al volver del detalle) se consulta el estado actual.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }
}
