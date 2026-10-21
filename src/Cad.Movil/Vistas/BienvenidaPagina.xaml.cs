using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class BienvenidaPagina : ContentPage
{
    private readonly BienvenidaVistaModelo vistaModelo;

    public BienvenidaPagina(BienvenidaVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.PrepararAsync();
    }

    // Al pasar a otra pantalla, el teclado no se queda abierto encima.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await CampoCelular.HideSoftInputAsync(CancellationToken.None);
    }

    // Abre el marcador con el número escrito: la persona decide si llama.
    private void AlLlamar(object? emisor, EventArgs e)
    {
        if (emisor is Button { CommandParameter: string numero } && PhoneDialer.Default.IsSupported)
            PhoneDialer.Default.Open(numero);
    }
}
