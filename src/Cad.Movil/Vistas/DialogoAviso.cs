using Microsoft.Maui.Controls.Shapes;
#if ANDROID
using Android.Graphics.Drawables;
using Microsoft.Maui.Platform;
#endif

namespace Cad.Movil.Vistas;

// Aviso emergente (25/10/2026, decisión del PO): diálogo centrado sobre la pantalla oscurecida, con icono, título según el tipo,
// el texto y «Entendido». Solo para los avisos de un momento (resultado de una acción o un suceso); los de estado siguen en la
// pantalla. Se abren de uno en uno y un aviso idéntico al que ya está abierto o en cola no se repite (la misma propiedad
// puede mostrarse en dos sitios de una pantalla).
// No es una página modal: abrir una página haría que la pantalla de debajo «desapareciera» y «reapareciera» (recargaría el
// formulario, cancelaría la vuelta automática del reporte…). En Android es una ventana de diálogo nativa con la tarjeta MAUI
// dentro, que no detiene la actividad, así que la pantalla no se entera.
public static class DialogoAviso
{
    private static readonly SemaphoreSlim turno = new(1, 1);
    private static readonly HashSet<(string, string)> pendientes = [];

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
            await MainThread.InvokeOnMainThreadAsync(() => PresentarAsync(tipo, texto));
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

    private static (string Titulo, string Color) Aspecto(string tipo) => tipo switch
    {
        "exito" => ("Listo", "ColorExito"),
        "advertencia" => ("Atención", "ColorAdvertencia"),
        "error" => ("No se pudo completar", "ColorError"),
        _ => ("Aviso", "ColorInformacion"),
    };

    private static Color Recurso(string clave) =>
        Application.Current?.Resources.TryGetValue(clave, out var valor) == true ? (Color)valor : Colors.Black;

    // La tarjeta: icono, título, texto y «Entendido» (cerrar lo hace quien la presenta).
    private static Border Tarjeta(string tipo, string texto, Action cerrar)
    {
        var (titulo, color) = Aspecto(tipo);
        var icono = new Icono { Nombre = tipo is "exito" or "advertencia" or "error" ? tipo : "informacion", Tamano = 32, Color = Recurso(color), HorizontalOptions = LayoutOptions.Center };
        var encabezado = new Label { Text = titulo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Recurso("ColorTexto"), HorizontalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetHeadingLevel(encabezado, SemanticHeadingLevel.Level1);
        var cuerpo = new Label { Text = texto, FontSize = 16, TextColor = Recurso("ColorTexto"), HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
        var entendido = new Button { Text = "Entendido", Margin = new Thickness(0, 8, 0, 0) };
        if (Application.Current?.Resources.TryGetValue("BotonPrincipal", out var estilo) == true)
            entendido.Style = (Style)estilo;
        entendido.Clicked += (_, _) => cerrar();

        var tarjeta = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            BackgroundColor = Recurso("ColorSuperficie"),
            Padding = new Thickness(24, 24, 24, 20),
            MaximumWidthRequest = 380,
            Content = new VerticalStackLayout { Spacing = 12, Children = { icono, encabezado, cuerpo, entendido } },
        };
        // El lector de pantalla lo anuncia completo al abrirse.
        SemanticProperties.SetDescription(tarjeta, $"{titulo}: {texto}");
        return tarjeta;
    }

#if ANDROID
    private static async Task PresentarAsync(string tipo, string texto)
    {
        if (Platform.CurrentActivity is not { } actividad || Application.Current?.Windows.FirstOrDefault()?.Handler?.MauiContext is not { } contexto)
            return;
        var cerrado = new TaskCompletionSource();
        Android.App.Dialog? dialogo = null;
        var tarjeta = Tarjeta(tipo, texto, () => dialogo?.Dismiss());
        var envoltura = new Grid { Padding = new Thickness(24), Children = { tarjeta } };

        dialogo = new Android.App.Dialog(actividad);
        dialogo.RequestWindowFeature((int)Android.Views.WindowFeatures.NoTitle);
        dialogo.SetContentView(envoltura.ToPlatform(contexto));
        dialogo.SetCancelable(true); // «Atrás» también lo cierra
        dialogo.SetCanceledOnTouchOutside(false);
        if (dialogo.Window is { } ventana)
        {
            ventana.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.Transparent));
            ventana.SetLayout(Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.WrapContent);
            ventana.SetDimAmount(0.55f);
        }
        dialogo.DismissEvent += (_, _) => cerrado.TrySetResult();
        tarjeta.Opacity = 0;
        tarjeta.Scale = 0.94;
        dialogo.Show();
        await Task.WhenAll(tarjeta.FadeToAsync(1, Movimiento.Rapida, Movimiento.Salida), tarjeta.ScaleToAsync(1, Movimiento.Rapida, Movimiento.Salida));
        tarjeta.SetSemanticFocus();
        await cerrado.Task;
        envoltura.Handler?.DisconnectHandler();
    }
#else
    // Fuera de Android (y en las pruebas) basta la alerta del sistema.
    private static async Task PresentarAsync(string tipo, string texto)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is { } pagina)
            await pagina.DisplayAlertAsync(Aspecto(tipo).Titulo, texto, "Entendido");
    }
#endif
}
