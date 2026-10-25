using Microsoft.Maui.Controls.Shapes;

namespace Cad.Movil.Vistas;

// Aviso emergente (25/10/2026, decisión del PO): diálogo centrado sobre la pantalla oscurecida, con icono, título según el tipo,
// el texto y «Entendido». Solo para los avisos de un momento (resultado de una acción o un suceso); los de estado siguen en la
// pantalla. Se abren de uno en uno y un aviso idéntico al que ya está abierto o en cola no se repite (la misma propiedad
// puede mostrarse en dos sitios de una pantalla).
public sealed class DialogoAviso : ContentPage
{
    private static readonly SemaphoreSlim turno = new(1, 1);
    private static readonly HashSet<(string, string)> pendientes = [];
    private readonly TaskCompletionSource cerrado = new();
    private readonly Border tarjeta;

    private DialogoAviso(string tipo, string texto)
    {
        var (color, titulo) = tipo switch
        {
            "exito" => ("ColorExito", "Listo"),
            "advertencia" => ("ColorAdvertencia", "Atención"),
            "error" => ("ColorError", "No se pudo completar"),
            _ => ("ColorInformacion", "Aviso"),
        };
        BackgroundColor = Color.FromRgba(0, 0, 0, 0.55);
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);

        var icono = new Icono { Nombre = tipo is "exito" or "advertencia" or "error" ? tipo : "informacion", Tamano = 32, Color = Recurso(color), HorizontalOptions = LayoutOptions.Center };
        var encabezado = new Label { Text = titulo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Recurso("ColorTexto"), HorizontalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetHeadingLevel(encabezado, SemanticHeadingLevel.Level1);
        var cuerpo = new Label { Text = texto, FontSize = 16, TextColor = Recurso("ColorTexto"), HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
        var entendido = new Button { Text = "Entendido", Margin = new Thickness(0, 8, 0, 0) };
        if (Application.Current?.Resources.TryGetValue("BotonPrincipal", out var estilo) == true)
            entendido.Style = (Style)estilo;
        entendido.Clicked += async (_, _) => await CerrarAsync();

        tarjeta = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            BackgroundColor = Recurso("ColorSuperficie"),
            Padding = new Thickness(24, 24, 24, 20),
            Margin = new Thickness(24),
            MaximumWidthRequest = 380,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.25f, Radius = 24, Offset = new Point(0, 8) },
            Content = new VerticalStackLayout { Spacing = 12, Children = { icono, encabezado, cuerpo, entendido } },
        };
        Content = new Grid { Children = { tarjeta } };
        // El lector de pantalla lo anuncia completo al abrirse.
        SemanticProperties.SetDescription(tarjeta, $"{titulo}: {texto}");
    }

    private static Color Recurso(string clave) =>
        Application.Current?.Resources.TryGetValue(clave, out var valor) == true ? (Color)valor : Colors.Black;

    // Abre el aviso cuando le toca (uno a la vez) y espera a que se cierre.
    public static async Task MostrarAsync(string tipo, string texto)
    {
        var clave = (tipo, texto);
        lock (pendientes)
            if (!pendientes.Add(clave))
                return; // ya está abierto o en cola
        await turno.WaitAsync();
        try
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } raiz)
                return;
            var dialogo = new DialogoAviso(tipo, texto);
            await MainThread.InvokeOnMainThreadAsync(() => raiz.Navigation.PushModalAsync(dialogo, false));
            await dialogo.cerrado.Task;
        }
        catch (Exception)
        {
            // Sin ventana o en plena navegación: el aviso se pierde, pero nunca cierra la app.
        }
        finally
        {
            lock (pendientes)
                pendientes.Remove(clave);
            turno.Release();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        tarjeta.Opacity = 0;
        tarjeta.Scale = 0.94;
        await Task.WhenAll(tarjeta.FadeToAsync(1, 160, Easing.CubicOut), tarjeta.ScaleToAsync(1, 160, Easing.CubicOut));
        tarjeta.SetSemanticFocus();
    }

    // «Atrás» del sistema también lo cierra.
    protected override bool OnBackButtonPressed()
    {
        _ = CerrarAsync();
        return true;
    }

    private async Task CerrarAsync()
    {
        if (cerrado.Task.IsCompleted)
            return;
        try
        {
            await Navigation.PopModalAsync(false);
        }
        finally
        {
            cerrado.TrySetResult();
        }
    }
}
