using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class InicioSerenoPagina : ContentPage
{
    private readonly InicioSerenoVistaModelo vistaModelo;

    public InicioSerenoPagina(InicioSerenoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // Al abrir el modo sereno se consulta el estado en el servidor (y se reanuda el envío si ya estaba operativo).
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }
}
