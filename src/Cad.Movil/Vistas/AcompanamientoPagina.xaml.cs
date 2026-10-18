using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class AcompanamientoPagina : ContentPage
{
    private readonly AcompanamientoVistaModelo vistaModelo;
    private Window? ventana;

    public AcompanamientoPagina(AcompanamientoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    // HU34-CA08 y HU37-CA06: al abrir la pantalla se consulta el estado real de la sesión en el servidor.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // HU41-CA01: con la app en segundo plano la pantalla no se ve, aunque siga siendo la página actual (MAUI no llama a
        // OnDisappearing): la presentación de una confirmación solo se registra mientras la pantalla está a la vista.
        if ((Window ?? Application.Current?.Windows.FirstOrDefault()) is { } actual && ventana is null)
        {
            ventana = actual;
            ventana.Stopped += AlDetenerVentana;
            ventana.Resumed += AlReanudarVentana;
        }
        await vistaModelo.AlAparecerAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (ventana is not null)
        {
            ventana.Stopped -= AlDetenerVentana;
            ventana.Resumed -= AlReanudarVentana;
            ventana = null;
        }
        vistaModelo.AlDesaparecer();
    }

    private void AlDetenerVentana(object? sender, EventArgs e) => vistaModelo.AlDesaparecer();

    private async void AlReanudarVentana(object? sender, EventArgs e) => await vistaModelo.AlAparecerAsync();

    // Tocar el texto también marca o desmarca la casilla.
    private void AlTocarFinalidad(object? sender, TappedEventArgs e) => vistaModelo.FinalidadAceptada = !vistaModelo.FinalidadAceptada;
}
