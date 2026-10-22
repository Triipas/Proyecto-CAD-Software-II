using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class ReportePagina : ContentPage
{
    // Centro y teselas como el mapa del panel (Panel:Mapa en appsettings del servidor).
    private const double CentroLatitud = -12.0464, CentroLongitud = -77.0428;
    private const int Zoom = 13;
    private const string Teselas = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    private readonly ReporteVistaModelo vistaModelo;
    private readonly IConfiguracionServidor servidor;
    private bool mapaCargado;

    public ReportePagina(ReporteVistaModelo vistaModelo, IConfiguracionServidor servidor)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        this.servidor = servidor;
        vistaModelo.PropertyChanged += AlCambiarVistaModelo;
        vistaModelo.PuntoCambiado += (lat, lon) => _ = PonerPuntoAsync(lat, lon);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vistaModelo.AlAparecerAsync();
    }

    // Botón «atrás» del sistema: desde el formulario, vuelve al mosaico de tipos.
    protected override bool OnBackButtonPressed() => vistaModelo.VolverAlMosaico() || base.OnBackButtonPressed();

    private async void AlCambiarVistaModelo(object? emisor, PropertyChangedEventArgs e)
    {
        // Cada paso empieza arriba.
        if (e.PropertyName == nameof(ReporteVistaModelo.EligiendoTipo))
            await Desplazamiento.ScrollToAsync(0, 0, false);
        else if (e.PropertyName == nameof(ReporteVistaModelo.MostrarMapa) && vistaModelo.MostrarMapa && !mapaCargado)
        {
            mapaCargado = true;
            await CargarMapaAsync();
        }
        else if (e.PropertyName == nameof(ReporteVistaModelo.Latitud) && vistaModelo.Latitud is null && mapaCargado)
            await EjecutarAsync("window.cad && window.cad.quitarPunto()");
    }

    // Leaflet va dentro de la página (sin CDN). La dirección base es la del servidor CAD, de modo que las peticiones de
    // teselas identifiquen su origen ante OpenStreetMap.
    private async Task CargarMapaAsync()
    {
        var plantilla = await LeerAsync("mapa/reporte.html");
        var punto = vistaModelo.Latitud is { } lat && vistaModelo.Longitud is { } lon ? new[] { lat, lon } : null;
        var config = JsonSerializer.Serialize(new { lat = punto?[0] ?? CentroLatitud, lon = punto?[1] ?? CentroLongitud, zoom = Zoom, teselas = Teselas, punto });
        var html = plantilla
            .Replace("/*LEAFLET_CSS*/", await LeerAsync("mapa/leaflet.css"))
            .Replace("/*CONFIG*/", config)
            .Replace("/*LEAFLET_JS*/", await LeerAsync("mapa/leaflet.js"));
        Mapa.Source = new HtmlWebViewSource { Html = html, BaseUrl = servidor.Direccion?.ToString() ?? "https://localhost/" };
    }

    private static async Task<string> LeerAsync(string archivo)
    {
        await using var flujo = await FileSystem.OpenAppPackageFileAsync(archivo);
        using var lector = new StreamReader(flujo);
        return await lector.ReadToEndAsync();
    }

    // El mapa comunica el punto tocado con una navegación «cadmapa://punto?lat=…&lon=…», que se cancela aquí.
    private void AlNavegarMapa(object? emisor, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("cadmapa://", StringComparison.OrdinalIgnoreCase))
            return;
        e.Cancel = true;
        var consulta = new Uri(e.Url).Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (consulta.TryGetValue("lat", out var lat) && consulta.TryGetValue("lon", out var lon)
            && double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitud)
            && double.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitud))
            vistaModelo.ElegirPuntoEnMapa(latitud, longitud);
    }

    private async Task PonerPuntoAsync(double lat, double lon)
    {
        if (!mapaCargado)
            return; // el mapa se carga después con el punto ya incluido
        await EjecutarAsync(string.Create(CultureInfo.InvariantCulture, $"window.cad && window.cad.ponerPunto({lat}, {lon})"));
    }

    private Task EjecutarAsync(string script) => MainThread.InvokeOnMainThreadAsync(() => Mapa.EvaluateJavaScriptAsync(script));
}
