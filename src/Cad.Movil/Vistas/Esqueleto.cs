namespace Cad.Movil.Vistas;

// Esqueleto de una lista (Movimiento, fase 4, 26/10/2026): mientras llega la primera respuesta, tarjetas grises con la forma de
// las de la lista ocupan su lugar y laten suavemente, en vez de una pantalla vacía con un indicador girando. Solo late mientras
// se ve; con las animaciones del teléfono quitadas, queda quieto. Lo leen como «Cargando…».
public sealed class Esqueleto : VerticalStackLayout
{
    public static readonly BindableProperty FilasProperty =
        BindableProperty.Create(nameof(Filas), typeof(int), typeof(Esqueleto), 3, propertyChanged: (b, _, _) => ((Esqueleto)b).Armar());

    private bool latiendo;

    public Esqueleto()
    {
        Spacing = 12;
        SemanticProperties.SetDescription(this, "Cargando…");
        Armar();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible) && IsVisible) Latir();
        };
        Loaded += (_, _) => Latir();
    }

    public int Filas { get => (int)GetValue(FilasProperty); set => SetValue(FilasProperty, value); }

    private void Armar()
    {
        Children.Clear();
        for (var i = 0; i < Filas; i++)
            Children.Add(Tarjeta(i));
    }

    // Como una tarjeta de la lista: una etiqueta corta arriba y una línea de texto debajo.
    private static Border Tarjeta(int indice)
    {
        var recursos = Application.Current?.Resources;
        Color Recurso(string clave, Color otro) => recursos is not null && recursos.TryGetValue(clave, out var valor) ? (Color)valor : otro;
        var gris = Recurso("ColorBorde", Color.FromArgb("#D9E2EE"));
        return new Border
        {
            StrokeThickness = 1,
            Stroke = gris,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = Recurso("ColorSuperficie", Colors.White),
            Padding = new Thickness(16),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new BoxView { Color = gris, CornerRadius = 6, HeightRequest = 22, WidthRequest = 140, HorizontalOptions = LayoutOptions.Start },
                    new BoxView { Color = gris, CornerRadius = 6, HeightRequest = 14, WidthRequest = indice % 2 == 0 ? 220 : 180, HorizontalOptions = LayoutOptions.Start },
                },
            },
        };
    }

    private async void Latir()
    {
        if (latiendo || Movimiento.Reducido) return;
        latiendo = true;
        try
        {
            while (IsVisible && Handler is not null)
            {
                await this.FadeToAsync(0.55, 700, Easing.SinInOut);
                await this.FadeToAsync(1, 700, Easing.SinInOut);
            }
        }
        finally
        {
            Opacity = 1;
            latiendo = false;
        }
    }
}
