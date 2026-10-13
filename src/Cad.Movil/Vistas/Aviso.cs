namespace Cad.Movil.Vistas;

// Aviso reutilizable: símbolo + texto + color (nunca solo color), como el componente .aviso del panel web.
// Tipos: exito, informacion, advertencia, error.
public sealed class Aviso : Border
{
    public static readonly BindableProperty TipoProperty =
        BindableProperty.Create(nameof(Tipo), typeof(string), typeof(Aviso), "informacion", propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    public static readonly BindableProperty TextoProperty =
        BindableProperty.Create(nameof(Texto), typeof(string), typeof(Aviso), "", propertyChanged: (b, _, _) => ((Aviso)b).Actualizar());

    private readonly Label simbolo = new() { FontSize = 18, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Start };
    private readonly Label texto = new() { FontSize = 15, LineBreakMode = LineBreakMode.WordWrap };

    public Aviso()
    {
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 };
        Padding = new Thickness(12, 10);
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 };
        grid.Add(simbolo, 0);
        grid.Add(texto, 1);
        Content = grid;
        SemanticProperties.SetHeadingLevel(this, SemanticHeadingLevel.None);
        Actualizar();
    }

    public string Tipo { get => (string)GetValue(TipoProperty); set => SetValue(TipoProperty, value); }
    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    private void Actualizar()
    {
        var (color, fondo, marca, prefijo) = Tipo switch
        {
            "exito" => ("ColorExito", "ColorExitoFondo", "✓", "Correcto"),
            "advertencia" => ("ColorAdvertencia", "ColorAdvertenciaFondo", "!", "Atención"),
            "error" => ("ColorError", "ColorErrorFondo", "✕", "Error"),
            _ => ("ColorInformacion", "ColorInformacionFondo", "i", "Información"),
        };
        var recursos = Application.Current?.Resources;
        Color Recurso(string clave) => recursos is not null && recursos.TryGetValue(clave, out var valor) ? (Color)valor : Colors.Black;
        simbolo.Text = marca;
        simbolo.TextColor = texto.TextColor = Recurso(color);
        BackgroundColor = Recurso(fondo);
        Stroke = Recurso(color);
        texto.Text = Texto;
        // El lector de pantalla anuncia el tipo además del texto.
        SemanticProperties.SetDescription(this, $"{prefijo}: {Texto}");
    }
}
