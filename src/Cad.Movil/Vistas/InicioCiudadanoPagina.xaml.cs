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

    // Rediseño CAD v2: al pulsar el SOS, el anillo se abre una vez y el teléfono vibra levemente (respuesta inmediata,
    // antes de que conteste la central). No es una animación persistente.
    private async void AlPulsarSos(object? sender, EventArgs e)
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
        catch (Exception) { /* sin vibración disponible: la respuesta visual basta */ }
        AnilloInterior.Opacity = 1;
        AnilloInterior.Scale = 1;
        await Task.WhenAll(
            AnilloInterior.ScaleTo(1.16, 380, Easing.CubicOut),
            AnilloInterior.FadeTo(0, 380, Easing.CubicOut),
            BotonSos.ScaleTo(0.96, 90, Easing.CubicOut));
        await BotonSos.ScaleTo(1, 160, Easing.CubicOut);
        AnilloInterior.Scale = 1;
        await AnilloInterior.FadeTo(1, 200, Easing.CubicOut);
    }
}
