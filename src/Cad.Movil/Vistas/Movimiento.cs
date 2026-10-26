namespace Cad.Movil.Vistas;

// Movimiento de la app (26/10/2026): las mismas duraciones y curvas que el panel web (tokens.css), para que la app y la central
// se sientan del mismo sistema. Solo se animan la opacidad, el desplazamiento y la escala. Toda animación empieza desde donde
// está la vista (si llega otro cambio a mitad de camino, corrige el destino sin saltar) y, con las animaciones del sistema
// quitadas, el cambio es inmediato.
public static class Movimiento
{
    public const uint Instantanea = 100; // presionado, cambio de color
    public const uint Rapida = 160;      // chips, avisos que se van
    public const uint Base = 220;        // entrada de tarjetas y avisos
    public const uint Lenta = 360;       // hojas, diálogos, cambios de pantalla

    public const double Desplazamiento = 8; // lo que sube una vista al entrar

    public static readonly Easing Salida = Curva(0.16, 1, 0.3, 1);       // entradas
    public static readonly Easing Entrada = Curva(0.7, 0, 0.84, 0);      // salidas
    public static readonly Easing Estandar = Curva(0.2, 0, 0, 1);        // cambios de estado
    public static readonly Easing Desplazar = Curva(0.77, 0, 0.175, 1); // reordenar, plegar

    // Android: «Quitar animaciones» (accesibilidad) y la escala de animación en 0 dejan la duración de animación en 0. Se lee
    // en cada animación: puede cambiar con la app abierta.
    public static bool Reducido
    {
        get
        {
#if ANDROID
            try
            {
                var resolvedor = Android.App.Application.Context.ContentResolver;
                return Android.Provider.Settings.Global.GetFloat(resolvedor, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
    }

    // Aparece subiendo 8 px. Si ya estaba entrando o saliendo, sigue desde donde quedó.
    public static async Task MostrarAsync(this VisualElement vista, double desde = Desplazamiento)
    {
        vista.CancelAnimations();
        if (!vista.IsVisible)
        {
            vista.Opacity = 0;
            vista.TranslationY = desde;
            vista.IsVisible = true;
        }
        if (Reducido)
        {
            vista.Opacity = 1;
            vista.TranslationY = 0;
            return;
        }
        await Task.WhenAll(vista.FadeToAsync(1, Base, Salida), vista.TranslateToAsync(vista.TranslationX, 0, Base, Salida));
    }

    // Se desvanece bajando un poco y queda oculta (sin dejar el hueco). Si otra animación la interrumpe, no se oculta.
    public static async Task OcultarAsync(this VisualElement vista, double hacia = Desplazamiento / 2)
    {
        vista.CancelAnimations();
        if (!vista.IsVisible)
            return;
        if (!Reducido)
        {
            var cancelado = await Task.WhenAll(vista.FadeToAsync(0, Rapida, Entrada), vista.TranslateToAsync(vista.TranslationX, hacia, Rapida, Entrada));
            if (cancelado.Any(c => c))
                return;
        }
        vista.IsVisible = false;
        vista.Opacity = 1;
        vista.TranslationY = 0;
    }

    // Mostrar u ocultar según un valor, para usarlo donde antes se cambiaba IsVisible.
    public static Task AlternarAsync(this VisualElement vista, bool visible) => visible ? vista.MostrarAsync() : vista.OcultarAsync();

    // Respuesta al toque: se hunde un poco y vuelve. Con vibración opcional (sin vibrador disponible, basta la respuesta visual).
    public static async Task PulsarAsync(this VisualElement vista, HapticFeedbackType? vibracion = null)
    {
        if (vibracion is { } tipo)
        {
            try { HapticFeedback.Default.Perform(tipo); }
            catch (Exception) { /* sin vibración disponible */ }
        }
        if (Reducido)
            return;
        vista.AbortAnimation("pulso");
        new Animation
        {
            { 0, 0.4, new Animation(v => vista.Scale = v, vista.Scale, 0.97, Salida) },
            { 0.4, 1, new Animation(v => vista.Scale = v, 0.97, 1, Salida) },
        }.Commit(vista, "pulso", length: Instantanea + Rapida, finished: (_, _) => vista.Scale = 1);
        await Task.Delay((int)(Instantanea + Rapida));
    }

    // Algo ya visible cambió (el estado del caso, la acción de la etapa): vuelve a aparecer subiendo 8 px, para que el cambio
    // se note sin tener que leerlo todo de nuevo.
    public static async Task ResaltarAsync(this VisualElement vista)
    {
        vista.CancelAnimations();
        if (Reducido || !vista.IsVisible)
        {
            vista.Opacity = 1;
            vista.TranslationY = 0;
            return;
        }
        vista.Opacity = 0;
        vista.TranslationY = Desplazamiento;
        await Task.WhenAll(vista.FadeToAsync(1, Base, Salida), vista.TranslateToAsync(vista.TranslationX, 0, Base, Salida));
    }

    // ---------- Botones (fase 2, 26/10/2026) ----------
    // Pulsable (los estilos BotonPrincipal y BotonSecundario lo activan): al tocar, el botón se hunde un poco y vuelve.
    public static readonly BindableProperty PulsableProperty =
        BindableProperty.CreateAttached("Pulsable", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarPulsable);

    public static bool GetPulsable(BindableObject vista) => (bool)vista.GetValue(PulsableProperty);
    public static void SetPulsable(BindableObject vista, bool valor) => vista.SetValue(PulsableProperty, valor);

    private static void AlCambiarPulsable(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not Button boton) return;
        boton.Pressed -= AlPresionar;
        boton.Released -= AlSoltar;
        if (!(bool)nuevo) return;
        boton.Pressed += AlPresionar;
        boton.Released += AlSoltar;
    }

    private static void AlPresionar(object? emisor, EventArgs e)
    {
        if (emisor is Button boton && !Reducido) _ = boton.ScaleToAsync(0.97, Instantanea, Salida);
    }

    private static void AlSoltar(object? emisor, EventArgs e)
    {
        if (emisor is not Button boton) return;
        if (Reducido) boton.Scale = 1;
        else _ = boton.ScaleToAsync(1, Rapida, Salida);
    }

    // TextoEnvio (las acciones que se registran en la central, como «Voy en camino»): al tocar, el teléfono vibra un instante y,
    // si el envío tarda, el mismo botón dice «Enviando…» hasta que vuelve a estar disponible. Un envío rápido no cambia el texto.
    public static readonly BindableProperty TextoEnvioProperty =
        BindableProperty.CreateAttached("TextoEnvio", typeof(string), typeof(Movimiento), null, propertyChanged: AlCambiarTextoEnvio);

    public static string? GetTextoEnvio(BindableObject vista) => (string?)vista.GetValue(TextoEnvioProperty);
    public static void SetTextoEnvio(BindableObject vista, string? valor) => vista.SetValue(TextoEnvioProperty, valor);

    private const int EsperaTextoEnvio = 150;

    private static void AlCambiarTextoEnvio(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not Button boton || anterior is not null) return; // se engancha una sola vez
        string? original = null;
        boton.Clicked += async (_, _) =>
        {
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
            catch (Exception) { /* sin vibración disponible */ }
            await Task.Delay(EsperaTextoEnvio);
            if (boton.IsEnabled || original is not null || GetTextoEnvio(boton) is not { Length: > 0 } texto) return;
            original = boton.Text;
            boton.Text = texto;
        };
        boton.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Button.IsEnabled) || !boton.IsEnabled || original is null) return;
            boton.Text = original;
            original = null;
        };
    }

    // Cambia el contenido de una vista sin salto: se apaga, se aplica el cambio y vuelve (p. ej. un chip que cambia de estado).
    public static async Task CruzarAsync(this VisualElement vista, Action cambio)
    {
        vista.CancelAnimations();
        if (Reducido || !vista.IsVisible)
        {
            cambio();
            vista.Opacity = 1;
            return;
        }
        await vista.FadeToAsync(0, Instantanea, Entrada);
        cambio();
        await vista.FadeToAsync(1, Rapida, Salida);
    }

    // Entrada escalonada de una lista (la primera vez que se muestra): cada elemento 40 ms después del anterior, y desde el
    // sexto todos a la vez, para que una lista larga no tarde en estar completa.
    public static Task EntradaEscalonadaAsync(IEnumerable<VisualElement> vistas, uint paso = 40, int escalonados = 6)
    {
        var tareas = vistas.Select((vista, i) => EntrarConRetrasoAsync(vista, (int)(Math.Min(i, escalonados) * paso)));
        return Task.WhenAll(tareas);
    }

    private static async Task EntrarConRetrasoAsync(VisualElement vista, int retraso)
    {
        if (Reducido)
        {
            vista.Opacity = 1;
            vista.TranslationY = 0;
            return;
        }
        vista.Opacity = 0;
        vista.TranslationY = Desplazamiento;
        if (retraso > 0)
            await Task.Delay(retraso);
        await Task.WhenAll(vista.FadeToAsync(1, Base, Salida), vista.TranslateToAsync(vista.TranslationX, 0, Base, Salida));
    }

    // Curva cúbica de Bézier como en CSS (cubic-bezier(x1, y1, x2, y2)): para un avance x busca el parámetro t con Newton y,
    // si no converge, por bisección; devuelve y(t).
    public static Easing Curva(double x1, double y1, double x2, double y2) => new(x => Resolver(x, x1, y1, x2, y2));

    private static double Resolver(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        static double Bezier(double t, double a, double b) => 3 * a * (1 - t) * (1 - t) * t + 3 * b * (1 - t) * t * t + t * t * t;
        static double Derivada(double t, double a, double b) => 3 * a * (1 - t) * (1 - t) + 6 * (b - a) * (1 - t) * t + 3 * (1 - b) * t * t;

        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(t, x1, x2) - x;
            if (Math.Abs(error) < 1e-6) return Bezier(t, y1, y2);
            var pendiente = Derivada(t, x1, x2);
            if (Math.Abs(pendiente) < 1e-6) break;
            t -= error / pendiente;
        }
        double bajo = 0, alto = 1;
        t = x;
        for (var i = 0; i < 40; i++)
        {
            var valor = Bezier(t, x1, x2);
            if (Math.Abs(valor - x) < 1e-6) break;
            if (valor < x) bajo = t; else alto = t;
            t = (bajo + alto) / 2;
        }
        return Bezier(t, y1, y2);
    }
}
