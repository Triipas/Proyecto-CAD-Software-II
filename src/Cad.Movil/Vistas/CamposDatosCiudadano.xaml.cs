namespace Cad.Movil.Vistas;

public partial class CamposDatosCiudadano : ContentView
{
    public CamposDatosCiudadano() => InitializeComponent();

    // Al pasar al PIN, el teclado del sistema no se queda abierto sobre el teclado propio.
    public Task OcultarTecladoAsync() => CampoNombres.HideSoftInputAsync(CancellationToken.None);
}
