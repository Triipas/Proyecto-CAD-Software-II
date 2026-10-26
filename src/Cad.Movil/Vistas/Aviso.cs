namespace Cad.Movil.Vistas;

// Aviso reutilizable: icono dibujado + texto + color (nunca solo color), como el componente .aviso del panel web.
// Tipos: exito, informacion, advertencia, error.
// Emergente (25/10/2026): un aviso de un momento (resultado de una acción o un suceso) no ocupa sitio en la pantalla: se abre
// como diálogo centrado (DialogoAviso) cada vez que aparece o cambia. Con Emergente, la visibilidad va en Mostrar, no en IsVisible.
// Los avisos de estado (sin conexión, sin permiso…) siguen en la pantalla, porque describen algo que sigue vigente.
public sealed class Aviso : Border
{
    public static readonly BindableProperty TipoProperty =
        BindableProperty.Create(nameof(Tipo), typeof(string), typeof(Aviso), "informacion", propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    public static readonly BindableProperty TextoProperty =
        BindableProperty.Create(nameof(Texto), typeof(string), typeof(Aviso), "", propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    public static readonly BindableProperty EmergenteProperty =
        BindableProperty.Create(nameof(Emergente), typeof(bool), typeof(Aviso), false, propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    public static readonly BindableProperty MostrarProperty =
        BindableProperty.Create(nameof(Mostrar), typeof(bool), typeof(Aviso), false, propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    private readonly DisparadorAviso disparador = new();
    private Page? paginaEnEspera;

    private readonly Icono simbolo = new() { Tamano = 20, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 1, 0, 0) };
    private readonly Label texto = new() { FontSize = 15, LineBreakMode = LineBreakMode.WordWrap };

    public Aviso()
    {
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 };
        Padding = new Thickness(12, 10);
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        grid.Add(simbolo, 0);
        grid.Add(texto, 1);
        Content = grid;
        SemanticProperties.SetHeadingLevel(this, SemanticHeadingLevel.None);
        // Movimiento (fase 4): un aviso de estado que se muestra con la pantalla abierta entra subiendo.
        Movimiento.SetAparecer(this, true);
        Actualizar();
    }

    public string Tipo { get => (string)GetValue(TipoProperty); set => SetValue(TipoProperty, value); }
    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }
    public bool Emergente { get => (bool)GetValue(EmergenteProperty); set => SetValue(EmergenteProperty, value); }
    public bool Mostrar { get => (bool)GetValue(MostrarProperty); set => SetValue(MostrarProperty, value); }

    private void Actualizar()
    {
        var (color, fondo, borde, prefijo) = Tipo switch
        {
            "exito" => ("ColorExito", "ColorExitoFondo", "ColorExitoBorde", "Correcto"),
            "advertencia" => ("ColorAdvertencia", "ColorAdvertenciaFondo", "ColorAdvertenciaBorde", "Atención"),
            "error" => ("ColorError", "ColorErrorFondo", "ColorErrorBorde", "Error"),
            _ => ("ColorInformacion", "ColorInformacionFondo", "ColorInformacionBorde", "Información"),
        };
        var recursos = Application.Current?.Resources;
        Color Recurso(string clave) => recursos is not null && recursos.TryGetValue(clave, out var valor) ? (Color)valor : Colors.Black;
        simbolo.Nombre = Tipo is "exito" or "advertencia" or "error" ? Tipo : "informacion";
        simbolo.Color = texto.TextColor = Recurso(color);
        BackgroundColor = Recurso(fondo);
        Stroke = Recurso(borde);
        texto.Text = Texto;
        // El lector de pantalla anuncia el tipo además del texto.
        SemanticProperties.SetDescription(this, $"{prefijo}: {Texto}");
        if (Emergente)
        {
            IsVisible = false;
            if (disparador.Evaluar(Mostrar, Tipo, Texto))
                Abrir();
        }
    }

    // Solo sobre su propia pantalla: si el aviso cambia mientras se ve otra, se abre al volver a ella.
    private void Abrir()
    {
        if (PaginaPropia() is not { } pagina)
        {
            // Aún no está en su pantalla (se está construyendo): se abre al cargarse.
            Loaded -= AlCargar;
            Loaded += AlCargar;
            return;
        }
        if (Shell.Current?.CurrentPage is { } actual && actual != pagina)
        {
            if (paginaEnEspera is null)
            {
                paginaEnEspera = pagina;
                pagina.Appearing += AlVolverALaPagina;
            }
            return;
        }
        _ = DialogoAviso.MostrarAsync(Tipo, Texto);
    }

    private void AlCargar(object? emisor, EventArgs e)
    {
        Loaded -= AlCargar;
        if (Emergente && Mostrar && !string.IsNullOrWhiteSpace(Texto))
            Abrir();
    }

    private void AlVolverALaPagina(object? emisor, EventArgs e)
    {
        if (paginaEnEspera is { } pagina)
            pagina.Appearing -= AlVolverALaPagina;
        paginaEnEspera = null;
        if (Emergente && Mostrar && !string.IsNullOrWhiteSpace(Texto))
            _ = DialogoAviso.MostrarAsync(Tipo, Texto);
    }

    private Page? PaginaPropia()
    {
        Element? elemento = this;
        while (elemento is not null and not Page)
            elemento = elemento.Parent;
        return elemento as Page;
    }
}
