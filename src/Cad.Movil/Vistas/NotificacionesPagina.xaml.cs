using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class NotificacionesPagina : ContentPage
{
    private readonly NotificacionesVistaModelo vistaModelo;

    public NotificacionesPagina(NotificacionesVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // CA02: cada vez que se muestra (también al volver de un caso) se consulta la bandeja guardada en el servidor.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }
}
