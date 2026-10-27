using System.Runtime.CompilerServices;

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
    // Pulsable (lo activan todos los estilos de botón): al tocar, el botón se hunde y vuelve. Desde el 26/10/2026 se siente como
    // las teclas del PIN en toda la app: un golpe de vibración corto al presionar y el tinte de «presionado» de cada estilo.
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
        if (emisor is not Button boton) return;
        Vibrar();
        // Un botón ancho se hunde un poco menos que una tecla: la misma sensación sin que el borde se mueva demasiado.
        if (!Reducido) _ = boton.ScaleToAsync(boton.Width > 200 ? 0.97 : 0.95, Instantanea, Salida);
    }

    private static void Vibrar()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch (Exception) { /* sin vibración disponible: queda la respuesta visual */ }
    }

    private static void AlSoltar(object? emisor, EventArgs e)
    {
        if (emisor is not Button boton) return;
        if (Reducido) boton.Scale = 1;
        else _ = boton.ScaleToAsync(1, Rapida, Salida);
    }

    // Tecla (el teclado del PIN): como una tecla física. Al tocarla el teléfono da un golpe de vibración corto y la tecla se
    // hunde más que un botón común; al soltarla vuelve. El color de «presionada» lo da el estado visual del estilo. Con las
    // animaciones del teléfono quitadas queda la vibración y el color, sin movimiento.
    public static readonly BindableProperty TeclaProperty =
        BindableProperty.CreateAttached("Tecla", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarTecla);

    public static bool GetTecla(BindableObject vista) => (bool)vista.GetValue(TeclaProperty);
    public static void SetTecla(BindableObject vista, bool valor) => vista.SetValue(TeclaProperty, valor);

    private static void AlCambiarTecla(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not Button boton) return;
        boton.Pressed -= AlPresionarTecla;
        boton.Released -= AlSoltar;
        if (!(bool)nuevo) return;
        boton.Pressed += AlPresionarTecla;
        boton.Released += AlSoltar;
    }

    private static void AlPresionarTecla(object? emisor, EventArgs e)
    {
        if (emisor is not Button boton) return;
        Vibrar();
        if (!Reducido) _ = boton.ScaleToAsync(0.94, Instantanea, Salida);
    }

    // Tarjeta (26/10/2026): un botón transparente sobre una tarjeta (Reportar, Acompañar, una fila de «Mis casos»…). Al presionarlo
    // la tarjeta entera responde como una tecla del PIN: vibra, se hunde y su marco se tiñe de azul hasta soltarla.
    public static readonly BindableProperty TarjetaProperty =
        BindableProperty.CreateAttached("Tarjeta", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarTarjeta);

    public static bool GetTarjeta(BindableObject vista) => (bool)vista.GetValue(TarjetaProperty);
    public static void SetTarjeta(BindableObject vista, bool valor) => vista.SetValue(TarjetaProperty, valor);

    // Colores del marco antes de presionar, para devolverlos al soltar.
    private static readonly ConditionalWeakTable<Border, Tuple<Brush?, Color?, double>> marcos = new();

    private static void AlCambiarTarjeta(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not Button boton) return;
        boton.Pressed -= AlPresionarTarjeta;
        boton.Released -= AlSoltarTarjeta;
        if (!(bool)nuevo) return;
        boton.Pressed += AlPresionarTarjeta;
        boton.Released += AlSoltarTarjeta;
    }

    // La tarjeta es el contenedor del botón; su marco, el primer Border dentro de ella (o ella misma).
    private static (VisualElement? Tarjeta, Border? Marco) TarjetaDe(Button boton)
    {
        var tarjeta = boton.Parent as VisualElement;
        var marco = tarjeta as Border ?? (tarjeta as Layout)?.Children.OfType<Border>().FirstOrDefault();
        return (tarjeta, marco);
    }

    private static void AlPresionarTarjeta(object? emisor, EventArgs e)
    {
        if (emisor is not Button boton) return;
        Vibrar();
        var (tarjeta, marco) = TarjetaDe(boton);
        if (marco is not null && Application.Current?.Resources is { } recursos)
        {
            marcos.AddOrUpdate(marco, new Tuple<Brush?, Color?, double>(marco.Stroke, marco.BackgroundColor, marco.StrokeThickness));
            if (recursos.TryGetValue("ColorPrimario", out var primario) && primario is Color azul)
                marco.Stroke = new SolidColorBrush(azul);
            marco.StrokeThickness = Math.Max(1.5, marco.StrokeThickness);
            if (recursos.TryGetValue("ColorTenue", out var tenue) && tenue is Color fondo)
                marco.BackgroundColor = fondo;
        }
        if (tarjeta is not null && !Reducido) _ = tarjeta.ScaleToAsync(tarjeta.Width > 200 ? 0.97 : 0.95, Instantanea, Salida);
    }

    private static void AlSoltarTarjeta(object? emisor, EventArgs e)
    {
        if (emisor is not Button boton) return;
        var (tarjeta, marco) = TarjetaDe(boton);
        if (marco is not null && marcos.TryGetValue(marco, out var antes))
        {
            marco.Stroke = antes.Item1;
            marco.BackgroundColor = antes.Item2;
            marco.StrokeThickness = antes.Item3;
            marcos.Remove(marco);
        }
        if (tarjeta is null) return;
        if (Reducido) tarjeta.Scale = 1;
        else _ = tarjeta.ScaleToAsync(1, Rapida, Salida);
    }

    // Tocable (26/10/2026): una tarjeta con TapGestureRecognizer (los tipos del reporte). El toque no tiene «presionado», así que
    // al tocarla vibra y se hunde un instante, como una tecla.
    public static readonly BindableProperty TocableProperty =
        BindableProperty.CreateAttached("Tocable", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarTocable);

    public static bool GetTocable(BindableObject vista) => (bool)vista.GetValue(TocableProperty);
    public static void SetTocable(BindableObject vista, bool valor) => vista.SetValue(TocableProperty, valor);

    private static void AlCambiarTocable(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not View tarjeta || !(bool)nuevo || anterior is true) return;
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) =>
        {
            Vibrar();
            if (Reducido) return;
            await tarjeta.ScaleToAsync(0.95, Instantanea, Salida);
            await tarjeta.ScaleToAsync(1, Rapida, Salida);
        };
        tarjeta.GestureRecognizers.Add(toque);
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

    // ---------- Transversal (fase 4, 26/10/2026) ----------
    // Sacudida corta de lado a lado: «esto no fue aceptado» (un PIN o un código incorrecto), con una vibración.
    public static async Task SacudirAsync(this VisualElement vista)
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
        catch (Exception) { /* sin vibración disponible */ }
        if (Reducido) return;
        vista.AbortAnimation("sacudir");
        var tramos = new (double hasta, double fin)[] { (-10, 0.15), (10, 0.35), (-7, 0.55), (7, 0.75), (-3, 0.9), (0, 1) };
        var animacion = new Animation();
        double desde = 0, inicio = 0;
        foreach (var (hasta, fin) in tramos)
        {
            animacion.Add(inicio, fin, new Animation(v => vista.TranslationX = v, desde, hasta, Estandar));
            (desde, inicio) = (hasta, fin);
        }
        var listo = new TaskCompletionSource();
        animacion.Commit(vista, "sacudir", length: Lenta, finished: (_, _) => { vista.TranslationX = 0; listo.TrySetResult(); });
        await listo.Task;
    }

    // SacudirAl="{Binding Mensaje}": la vista se sacude cada vez que llega un mensaje de error nuevo (no al vaciarse).
    public static readonly BindableProperty SacudirAlProperty =
        BindableProperty.CreateAttached("SacudirAl", typeof(string), typeof(Movimiento), null, propertyChanged: (vista, _, nuevo) =>
        {
            if (vista is VisualElement elemento && nuevo is string { Length: > 0 } && elemento.Handler is not null)
                _ = elemento.SacudirAsync();
        });

    public static string? GetSacudirAl(BindableObject vista) => (string?)vista.GetValue(SacudirAlProperty);
    public static void SetSacudirAl(BindableObject vista, string? valor) => vista.SetValue(SacudirAlProperty, valor);

    // Aparecer: lo que se muestra con la pantalla ya abierta (un aviso, un error de un campo) entra subiendo en vez de saltar.
    // Al ocultarse desaparece de inmediato: la visibilidad la decide el enlace de datos, que no espera a una animación.
    public static readonly BindableProperty AparecerProperty =
        BindableProperty.CreateAttached("Aparecer", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarAparecer);

    public static bool GetAparecer(BindableObject vista) => (bool)vista.GetValue(AparecerProperty);
    public static void SetAparecer(BindableObject vista, bool valor) => vista.SetValue(AparecerProperty, valor);

    private static void AlCambiarAparecer(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not VisualElement elemento || !(bool)nuevo || (bool)anterior) return;
        elemento.PropertyChanged += (_, e) =>
        {
            // Solo si la pantalla ya está armada: lo que viene visible al abrirla entra con la pantalla.
            if (e.PropertyName != nameof(VisualElement.IsVisible) || !elemento.IsVisible || elemento.Handler is null) return;
            elemento.Dispatcher.Dispatch(() => _ = elemento.ResaltarAsync());
        };
    }

    // EntradaPagina (todas las pantallas, desde el estilo de ContentPage): al abrirse, el contenido termina de aparecer subiendo
    // 8 px. Empieza a media opacidad (nunca en blanco: un cuadro vacío al cambiar de pestaña se ve como un parpadeo) y se anima un
    // momento después, cuando la pantalla ya terminó de armarse (antes, la animación no llegaría a verse).
    public static readonly BindableProperty EntradaPaginaProperty =
        BindableProperty.CreateAttached("EntradaPagina", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarEntradaPagina);

    public static bool GetEntradaPagina(BindableObject vista) => (bool)vista.GetValue(EntradaPaginaProperty);
    public static void SetEntradaPagina(BindableObject vista, bool valor) => vista.SetValue(EntradaPaginaProperty, valor);

    private const int EsperaPagina = 60;

    private static void AlCambiarEntradaPagina(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not ContentPage pagina || !(bool)nuevo || (bool)anterior) return;
        pagina.Appearing += (_, _) =>
        {
            if (Reducido || pagina.Content is not { } contenido) return;
            contenido.CancelAnimations();
            contenido.Opacity = 0.5;
            contenido.TranslationY = Desplazamiento;
            pagina.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(EsperaPagina), () => _ = Task.WhenAll(
                contenido.FadeToAsync(1, Base, Salida),
                contenido.TranslateToAsync(contenido.TranslationX, 0, Base, Salida)));
        };
    }

    // ---------- Listas (fase 3, 26/10/2026) ----------
    // EntradaEscalonada (en un layout con BindableLayout): la primera vez que la lista se llena, sus elementos entran uno tras
    // otro; después, lo que llega solo (un aviso nuevo) entra por su cuenta. Un refresco que rehace toda la lista no se anima.
    // La entrada espera un momento: al llenarse la lista la pantalla está ocupada y una animación inmediata no se vería.
    public static readonly BindableProperty EntradaEscalonadaProperty =
        BindableProperty.CreateAttached("EntradaEscalonada", typeof(bool), typeof(Movimiento), false, propertyChanged: AlCambiarEntradaEscalonada);

    public static bool GetEntradaEscalonada(BindableObject vista) => (bool)vista.GetValue(EntradaEscalonadaProperty);
    public static void SetEntradaEscalonada(BindableObject vista, bool valor) => vista.SetValue(EntradaEscalonadaProperty, valor);

    private const int EsperaLista = 80;

    private static void AlCambiarEntradaEscalonada(BindableObject vista, object anterior, object nuevo)
    {
        if (vista is not Layout lista || !(bool)nuevo || (bool)anterior) return;
        var llenada = false;
        var rehaciendo = false;
        var tanda = new List<VisualElement>();
        lista.ChildAdded += (_, e) =>
        {
            if (e.Element is not VisualElement elemento || Reducido) return;
            if (llenada)
            {
                // Una lista rehecha empieza vacía: su primer elemento llega solo. Esa tanda entera se muestra sin animar.
                if (lista.Children.Count == 1)
                {
                    rehaciendo = true;
                    lista.Dispatcher.Dispatch(() => rehaciendo = false);
                }
                if (rehaciendo) return;
            }
            elemento.Opacity = 0;
            tanda.Add(elemento);
            if (tanda.Count > 1) return;
            lista.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(EsperaLista), async () =>
            {
                var elementos = tanda.ToList();
                tanda.Clear();
                llenada = true;
                await EntradaEscalonadaAsync(elementos);
            });
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
