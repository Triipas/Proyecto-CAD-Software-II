using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class AcompanamientoPagina : ContentPage
{
    private readonly AcompanamientoVistaModelo vistaModelo;

    public AcompanamientoPagina(AcompanamientoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // HU34-CA08 y HU37-CA06: al abrir la pantalla se consulta el estado real de la sesión en el servidor.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vistaModelo.AlDesaparecer();
    }

    // Tocar el texto también marca o desmarca la casilla.
    private void AlTocarFinalidad(object? sender, TappedEventArgs e) => vistaModelo.FinalidadAceptada = !vistaModelo.FinalidadAceptada;
}
