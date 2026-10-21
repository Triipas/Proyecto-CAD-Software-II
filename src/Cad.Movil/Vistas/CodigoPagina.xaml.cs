using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class CodigoPagina : ContentPage
{
    private readonly CodigoVistaModelo vistaModelo;
    private IDispatcherTimer? temporizador;

    public CodigoPagina(CodigoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Cuenta atrás de «Reenviar en 0:25».
        temporizador = Dispatcher.CreateTimer();
        temporizador.Interval = TimeSpan.FromSeconds(1);
        temporizador.Tick += (_, _) => vistaModelo.ActualizarCuentaAtras();
        temporizador.Start();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () => Campo.Focus());
    }

    protected override async void OnDisappearing()
    {
        temporizador?.Stop();
        base.OnDisappearing();
        await Campo.HideSoftInputAsync(CancellationToken.None);
    }
}
