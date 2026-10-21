using System.Globalization;
using System.Text.Json;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class InicioCiudadanoPagina : ContentPage
{
    // Centro y teselas como el mapa del panel (Panel:Mapa en appsettings del servidor).
    private const double CentroLatitud = -12.0464, CentroLongitud = -77.0428;
    private const int Zoom = 13;
    private const string Teselas = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    private readonly InicioCiudadanoVistaModelo vistaModelo;
    private readonly IConfiguracionServidor servidor;
    private bool mapaCargado, mapaListo;
    private UbicacionSos? pendiente;

    public InicioCiudadanoPagina(InicioCiudadanoVistaModelo vistaModelo, IConfiguracionServidor servidor)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        this.servidor = servidor;
        vistaModelo.PosicionParaMapa += posicion => _ = MostrarPosicionAsync(posicion);
        Mapa.Navigated += async (_, _) =>
        {
            mapaListo = true;
            if (pendiente is { } p) await MostrarPosicionAsync(p);
        };
    }

    // HU06: la comprobación del permiso se hace al abrir la aplicación (una vez por apertura).
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!mapaCargado)
        {
            mapaCargado = true;
            await CargarMapaAsync();
        }
        await vistaModelo.AlAparecerAsync();
    }

    // Leaflet va dentro de la página (sin CDN), como en el reporte. La posición solo se dibuja aquí; no se envía.
    private async Task CargarMapaAsync()
    {
        var config = JsonSerializer.Serialize(new { lat = CentroLatitud, lon = CentroLongitud, zoom = Zoom, teselas = Teselas });
        var html = (await LeerAsync("mapa/inicio.html"))
            .Replace("/*LEAFLET_CSS*/", await LeerAsync("mapa/leaflet.css"))
            .Replace("/*CONFIG*/", config)
            .Replace("/*LEAFLET_JS*/", await LeerAsync("mapa/leaflet.js"));
        Mapa.Source = new HtmlWebViewSource { Html = html, BaseUrl = servidor.Direccion?.ToString() ?? "https://localhost/" };
    }

    private async Task MostrarPosicionAsync(UbicacionSos posicion)
    {
        if (!mapaListo)
        {
            pendiente = posicion; // el mapa la pinta en cuanto termine de cargar
            return;
        }
        pendiente = null;
        var script = string.Create(CultureInfo.InvariantCulture,
            $"window.cad && window.cad.posicion({posicion.Latitud}, {posicion.Longitud}, {Math.Max(posicion.PrecisionMetros, 5)})");
        await MainThread.InvokeOnMainThreadAsync(() => Mapa.EvaluateJavaScriptAsync(script));
    }

    private static async Task<string> LeerAsync(string archivo)
    {
        await using var flujo = await FileSystem.OpenAppPackageFileAsync(archivo);
        using var lector = new StreamReader(flujo);
        return await lector.ReadToEndAsync();
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
            AnilloInterior.ScaleToAsync(1.16, 380, Easing.CubicOut),
            AnilloInterior.FadeToAsync(0, 380, Easing.CubicOut),
            BotonSos.ScaleToAsync(0.96, 90, Easing.CubicOut));
        await BotonSos.ScaleToAsync(1, 160, Easing.CubicOut);
        AnilloInterior.Scale = 1;
        await AnilloInterior.FadeToAsync(1, 200, Easing.CubicOut);
    }
}
