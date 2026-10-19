namespace Cad.Movil.Vistas;

// Encabezado marino de borde a borde (rediseño CAD v2, fase 4): «Volver» opcional, título y una línea de ayuda.
// Sustituye a la etiqueta sobre el título; el contexto lo dan la navegación y el propio título.
public sealed class EncabezadoPagina : ContentView
{
    public static readonly BindableProperty TituloProperty =
        BindableProperty.Create(nameof(Titulo), typeof(string), typeof(EncabezadoPagina), "", propertyChanged: (b, _, n) => ((EncabezadoPagina)b).titulo.Text = (string)n);

    public static readonly BindableProperty AyudaProperty =
        BindableProperty.Create(nameof(Ayuda), typeof(string), typeof(EncabezadoPagina), "", propertyChanged: (b, _, n) =>
        {
            var e = (EncabezadoPagina)b;
            e.ayuda.Text = (string)n;
            e.ayuda.IsVisible = !string.IsNullOrEmpty((string)n);
        });

    public static readonly BindableProperty MostrarVolverProperty =
        BindableProperty.Create(nameof(MostrarVolver), typeof(bool), typeof(EncabezadoPagina), true, propertyChanged: (b, _, n) => ((EncabezadoPagina)b).volver.IsVisible = (bool)n);

    public static readonly BindableProperty TextoVolverProperty =
        BindableProperty.Create(nameof(TextoVolver), typeof(string), typeof(EncabezadoPagina), "Volver", propertyChanged: (b, _, n) => ((EncabezadoPagina)b).textoVolver.Text = (string)n);

    private readonly Label titulo;
    private readonly Label ayuda;
    private readonly Grid volver;
    private readonly Label textoVolver;

    public EncabezadoPagina()
    {
        var recursos = Application.Current?.Resources;
        T Recurso<T>(string clave) => recursos is not null && recursos.TryGetValue(clave, out var valor) ? (T)valor : default!;

        titulo = new Label { Style = Recurso<Style>("TituloSobreMarino") };
        ayuda = new Label { Style = Recurso<Style>("TextoSobreMarino"), IsVisible = false };
        textoVolver = new Label { Text = "Volver", TextColor = Colors.White, FontFamily = "PublicSansSemiBold", FontSize = 15, VerticalOptions = LayoutOptions.Center };

        // «Volver»: un botón transparente sobre el icono y el texto, para que TalkBack lo anuncie como botón.
        var boton = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0 };
        boton.Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
        SemanticProperties.SetDescription(boton, "Volver a la pantalla anterior");
        volver = new Grid
        {
            HorizontalOptions = LayoutOptions.Start,
            MinimumHeightRequest = 44,
            Margin = new Thickness(-6, 0, 0, 2),
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 2, Padding = new Thickness(0, 0, 8, 0), VerticalOptions = LayoutOptions.Center,
                    Children = { new Icono { Nombre = "izquierda", Color = Colors.White, Tamano = 22 }, textoVolver },
                },
                boton,
            },
        };

        Content = new Grid
        {
            Style = Recurso<Style>("EncabezadoMarino"),
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            Children = { volver },
        };
        var grid = (Grid)Content;
        grid.Add(titulo, 0, 1);
        grid.Add(ayuda, 0, 2);
    }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    public string Ayuda { get => (string)GetValue(AyudaProperty); set => SetValue(AyudaProperty, value); }
    public bool MostrarVolver { get => (bool)GetValue(MostrarVolverProperty); set => SetValue(MostrarVolverProperty, value); }
    public string TextoVolver { get => (string)GetValue(TextoVolverProperty); set => SetValue(TextoVolverProperty, value); }
}
