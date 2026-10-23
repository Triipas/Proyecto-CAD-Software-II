using System.ComponentModel;
using System.Text.Json;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class InicioSerenoPagina : ContentPage
{
    // Centro y teselas como el mapa del panel (Panel:Mapa en appsettings del servidor).
    private const double CentroLatitud = -12.0464, CentroLongitud = -77.0428;
    private const int Zoom = 13;
    private const string Teselas = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    private readonly InicioSerenoVistaModelo vistaModelo;
    private readonly IConfiguracionServidor servidor;
    private bool mapaCargado, mapaListo;

    public InicioSerenoPagina(InicioSerenoVistaModelo vistaModelo, IConfiguracionServidor servidor)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        this.servidor = servidor;
        vistaModelo.PropertyChanged += AlCambiarVistaModelo;
        Mapa.Navigated += async (_, _) =>
        {
            mapaListo = true;
            await DibujarAsync();
        };
        SizeChanged += (_, _) => AjustarPanel();
    }

    // Al abrir el modo sereno se consulta el estado en el servidor (y se reanuda el envío si ya estaba operativo).
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

    private async void AlCambiarVistaModelo(object? emisor, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(InicioSerenoVistaModelo.Destino) or nameof(InicioSerenoVistaModelo.PosicionPropia) or nameof(InicioSerenoVistaModelo.RutaMapa):
                await DibujarAsync();
                break;
            case nameof(InicioSerenoVistaModelo.HayDespacho) when vistaModelo.HayDespacho && mapaListo:
                // El mapa estaba oculto: Leaflet recalcula su tamaño al mostrarse.
                await EjecutarAsync("window.cad && window.cad.ajustar()");
                break;
            case nameof(InicioSerenoVistaModelo.PanelExpandido):
                AjustarPanel();
                break;
        }
    }

    // Recogido, el panel ocupa como máximo algo menos de la mitad de la pantalla; desplegado (detalle o cierre) crece y el mapa se
    // reduce (sin desaparecer: queda como referencia).
    private void AjustarPanel()
    {
        if (Height <= 0) return;
        var proporcion = vistaModelo.PanelExpandido ? 0.6 : 0.42;
        PanelDespacho.MaximumHeightRequest = Height * proporcion;
    }

    // Leaflet va dentro de la página (sin CDN), como en el inicio del ciudadano. Las posiciones solo se dibujan aquí.
    private async Task CargarMapaAsync()
    {
        var config = JsonSerializer.Serialize(new { lat = CentroLatitud, lon = CentroLongitud, zoom = Zoom, teselas = Teselas });
        var html = (await LeerAsync("mapa/despacho.html"))
            .Replace("/*LEAFLET_CSS*/", await LeerAsync("mapa/leaflet.css"))
            .Replace("/*CONFIG*/", config)
            .Replace("/*LEAFLET_JS*/", await LeerAsync("mapa/leaflet.js"));
        Mapa.Source = new HtmlWebViewSource { Html = html, BaseUrl = servidor.Direccion?.ToString() ?? "https://localhost/" };
    }

    private async Task DibujarAsync()
    {
        if (!mapaListo) return; // el mapa se dibuja en cuanto termina de cargar
        var destino = vistaModelo.Destino;
        var propia = vistaModelo.PosicionPropia;
        var datos = JsonSerializer.Serialize(new
        {
            destino = destino is null ? null : new { lat = destino.Latitud, lon = destino.Longitud, precision = destino.PrecisionMetros ?? 0, tipo = destino.Tipo },
            yo = propia is null ? null : new { lat = propia.Latitud, lon = propia.Longitud },
            ruta = vistaModelo.RutaMapa?.Select(p => new[] { p.Latitud, p.Longitud }),
        });
        await EjecutarAsync($"window.cad && window.cad.mostrar({datos})");
    }

    private async void AlVerAmbos(object? emisor, TappedEventArgs e)
    {
        if (mapaListo) await EjecutarAsync("window.cad && window.cad.verAmbos()");
    }

    private Task EjecutarAsync(string script) => MainThread.InvokeOnMainThreadAsync(() => Mapa.EvaluateJavaScriptAsync(script));

    private static async Task<string> LeerAsync(string archivo)
    {
        await using var flujo = await FileSystem.OpenAppPackageFileAsync(archivo);
        using var lector = new StreamReader(flujo);
        return await lector.ReadToEndAsync();
    }
}
