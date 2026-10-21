using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class PerfilPagina : ContentPage
{
    private readonly PerfilVistaModelo vistaModelo;

    public PerfilPagina(PerfilVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        // Sin sesión, pedir ayuda vuelve a exigir el PIN: se avisa antes de cerrar.
        vistaModelo.ConfirmarCierre = () => DisplayAlertAsync("¿Cerrar sesión?",
            "Para volver a pedir ayuda tendrás que ingresar tu PIN.", "Cerrar sesión", "Cancelar");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.CargarAsync();
    }

    private void AlLlamar(object? emisor, EventArgs e)
    {
        if (emisor is Button { CommandParameter: string numero } && PhoneDialer.Default.IsSupported)
            PhoneDialer.Default.Open(numero);
    }
}
