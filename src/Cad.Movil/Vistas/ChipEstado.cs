namespace Cad.Movil.Vistas;

// Chip de estado del rediseño CAD v2: tono + icono + texto literal del estado, como _ChipEstado.cshtml del panel web.
// El tono dice en qué punto del flujo está el caso; «Pendiente» es urgente solo en un SOS.
public sealed class ChipEstado : Border
{
    public static readonly BindableProperty EstadoProperty =
        BindableProperty.Create(nameof(Estado), typeof(string), typeof(ChipEstado), "", propertyChanged: (b, _, _) => ((ChipEstado)b).Actualizar());

    public static readonly BindableProperty UrgenteProperty =
        BindableProperty.Create(nameof(Urgente), typeof(bool), typeof(ChipEstado), false, propertyChanged: (b, _, _) => ((ChipEstado)b).Actualizar());

    private readonly Icono icono = new() { Tamano = 14, VerticalOptions = LayoutOptions.Center };
    private readonly Label texto = new() { FontSize = 13, FontFamily = "PublicSansSemiBold", VerticalOptions = LayoutOptions.Center };

    public ChipEstado()
    {
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 };
        Padding = new Thickness(8, 3, 10, 3);
        HorizontalOptions = LayoutOptions.Start;
        Content = new HorizontalStackLayout { Spacing = 4, Children = { icono, texto } };
        Actualizar();
    }

    public string Estado { get => (string)GetValue(EstadoProperty); set => SetValue(EstadoProperty, value); }
    public bool Urgente { get => (bool)GetValue(UrgenteProperty); set => SetValue(UrgenteProperty, value); }

    private void Actualizar()
    {
        var (tono, nombreIcono) = Estado switch
        {
            "Pendiente" => (Urgente ? "Urgente" : "Advertencia", "reloj"),
            "Recibido" => ("Informacion", "bandeja"),
            "En revisión" => ("Informacion", "lupa"),
            "Admitido" => ("Informacion", "exito"),
            "Asignada" or "Asignado" => ("Progreso", "asignado"),
            "En camino" => ("Progreso", "camino"),
            "En atención" => ("Progreso", "escudo"),
            "Cerrada" or "Cerrado" => ("Exito", "exito"),
            "Derivada" or "Derivado" => ("Neutro", "derivar"),
            "Descartada" or "Descartado" => ("Neutro", "error"),
            "Cancelada" or "Cancelado" => ("Neutro", "cancelar"),
            _ => ("Neutro", "informacion"),
        };
        var recursos = Application.Current?.Resources;
        Color Recurso(string clave) => recursos is not null && recursos.TryGetValue(clave, out var valor) ? (Color)valor : Colors.Black;
        icono.Nombre = nombreIcono;
        icono.Color = texto.TextColor = Recurso($"Color{tono}");
        BackgroundColor = Recurso($"Color{tono}Fondo");
        Stroke = Recurso($"Color{tono}Borde");
        texto.Text = Estado;
        SemanticProperties.SetDescription(this, $"Estado: {Estado}");
    }
}
