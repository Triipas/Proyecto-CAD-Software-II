using System.Globalization;
using System.Text.Json;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class AcompanamientoPagina : ContentPage
{
    // Centro y teselas como el mapa del inicio y del panel (Panel:Mapa en appsettings del servidor).
    private const double CentroLatitud = -16.3988, CentroLongitud = -71.5369;
    private const int Zoom = 14;
    private const string Teselas = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    // Lo que tapan las tarjetas flotantes (aprox.): el recorrido se encuadra en la franja libre entre ellas.
    private const int AltoArriba = 90, AltoDock = 300; // acomodo del 26/10/2026

    private readonly AcompanamientoVistaModelo vistaModelo;
    private readonly IConfiguracionServidor servidor;
    private Window? ventana;
    private bool mapaCargado, mapaListo;

    public AcompanamientoPagina(AcompanamientoVistaModelo vistaModelo, IConfiguracionServidor servidor)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        this.servidor = servidor;
        vistaModelo.RecorridoCambio += () => _ = DibujarRecorridoAsync();
        Mapa.Navigated += async (_, _) =>
        {
            mapaListo = true;
            await DibujarRecorridoAsync();
        };
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
        if (!mapaCargado)
        {
            mapaCargado = true;
            await CargarMapaAsync();
        }
        await vistaModelo.AlAparecerAsync();
    }

    // Leaflet va dentro de la página (sin CDN), como en el inicio. El recorrido lo pone la app: el mapa no lee ni envía nada.
    private async Task CargarMapaAsync()
    {
        var config = JsonSerializer.Serialize(new { lat = CentroLatitud, lon = CentroLongitud, zoom = Zoom, teselas = Teselas, arriba = AltoArriba, dock = AltoDock, reducido = Movimiento.Reducido });
        var html = (await LeerAsync("mapa/acompanamiento.html"))
            .Replace("/*LEAFLET_CSS*/", await LeerAsync("mapa/leaflet.css"))
            .Replace("/*CONFIG*/", config)
            .Replace("/*LEAFLET_JS*/", await LeerAsync("mapa/leaflet.js"));
        Mapa.Source = new HtmlWebViewSource { Html = html, BaseUrl = servidor.Direccion?.ToString() ?? "https://localhost/" };
    }

    private async Task DibujarRecorridoAsync()
    {
        if (!mapaListo)
            return; // al terminar de cargar se dibuja lo que haya
        var puntos = string.Join(",", vistaModelo.Recorrido.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.Latitud},{p.Longitud}]")));
        var script = $"window.cad && window.cad.recorrido([{puntos}], {(vistaModelo.LatidoAlDia ? "true" : "false")})";
        await MainThread.InvokeOnMainThreadAsync(() => Mapa.EvaluateJavaScriptAsync(script));
    }

    private static async Task<string> LeerAsync(string archivo)
    {
        await using var flujo = await FileSystem.OpenAppPackageFileAsync(archivo);
        using var lector = new StreamReader(flujo);
        return await lector.ReadToEndAsync();
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
