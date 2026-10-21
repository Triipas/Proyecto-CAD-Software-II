using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class CrearCuentaPagina : ContentPage
{
    private readonly CrearCuentaVistaModelo vistaModelo;

    public CrearCuentaPagina(CrearCuentaVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // Al pasar al PIN, el teclado del sistema no se queda abierto sobre el teclado propio.
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await CampoNombres.HideSoftInputAsync(CancellationToken.None);
        await CampoApellidos.HideSoftInputAsync(CancellationToken.None);
    }

    // Tocar el texto también marca la casilla: el objetivo táctil es la fila entera.
    private void AlTocarAceptacion(object? emisor, TappedEventArgs e) => vistaModelo.AceptaTerminos = !vistaModelo.AceptaTerminos;
}
