namespace Cad.Movil.Vistas;

// Aviso que se quita deslizándolo a un lado (23/10/2026), para que al abrir la app no se acumulen avisos ya vistos. Queda oculto
// mientras su Clave (el texto o el caso del aviso) no cambie, también al volver a abrir la app; un aviso nuevo vuelve a verse.
// Solo envuelve avisos informativos: los que piden algo (permisos, reintentar un SOS, volver a ingresar) no se pueden quitar.
// Es un SwipeView porque distingue el gesto horizontal del desplazamiento vertical de la página.
public sealed class Descartable : SwipeView
{
    public static readonly BindableProperty NombreProperty =
        BindableProperty.Create(nameof(Nombre), typeof(string), typeof(Descartable), "", propertyChanged: (b, _, _) => ((Descartable)b).Revisar());

    public static readonly BindableProperty ClaveProperty =
        BindableProperty.Create(nameof(Clave), typeof(string), typeof(Descartable), "", propertyChanged: (b, _, _) => ((Descartable)b).Revisar());

    // Falso: el aviso se muestra pero no se puede quitar (p. ej. un SOS que hay que reintentar).
    public static readonly BindableProperty PermitidoProperty =
        BindableProperty.Create(nameof(Permitido), typeof(bool), typeof(Descartable), true, propertyChanged: (b, _, _) => ((Descartable)b).Revisar());

    private View? observado;
    private string? quitadoEnMemoria; // por si el teléfono no deja guardar

    private readonly SwipeItems izquierda = Lado(), derecha = Lado();

    public Descartable()
    {
        foreach (var lado in new[] { izquierda, derecha })
            ((SwipeItem)lado[0]).Invoked += async (_, _) => await QuitarAsync();
        // Las colecciones se asignan una sola vez: cambiarlas mientras la página se construye (al poner el contenido) hacía
        // que MAUI las recorriera a la vez y la página del ciudadano se cerraba al abrirse.
        LeftItems = izquierda;
        RightItems = derecha;
        BackgroundColor = Colors.Transparent;
        Revisar();
    }

    private static SwipeItems Lado() => new([new SwipeItem { Text = "Quitar", BackgroundColor = Colors.Transparent }])
    {
        Mode = SwipeMode.Execute,
        SwipeBehaviorOnInvoked = SwipeBehaviorOnInvoked.Close,
    };

    // Qué tarjeta es (p. ej. «notificaciones»): cada una recuerda su último aviso quitado.
    public string? Nombre { get => (string?)GetValue(NombreProperty); set => SetValue(NombreProperty, value); }
    // Texto o identificador del aviso; puede llegar vacío o nulo mientras no hay aviso.
    public string? Clave { get => (string?)GetValue(ClaveProperty); set => SetValue(ClaveProperty, value); }
    public bool Permitido { get => (bool)GetValue(PermitidoProperty); set => SetValue(PermitidoProperty, value); }

    // El aviso envuelto decide cuándo hay algo que mostrar (su IsVisible); la tarjeta lo sigue para no dejar un hueco vacío.
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName != nameof(Content))
            return;
        if (observado is not null)
            observado.PropertyChanged -= AlCambiarContenido;
        observado = Content;
        if (observado is not null)
            observado.PropertyChanged += AlCambiarContenido;
        Revisar();
    }

    private void AlCambiarContenido(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsVisible))
            Revisar();
    }

    private string LlaveGuardada => $"aviso-quitado.{Nombre}";

    private void Revisar()
    {
        var descartado = Permitido && !string.IsNullOrEmpty(Nombre) && !string.IsNullOrEmpty(Clave) && Leer() == Clave;
        // Sin elementos visibles a los lados no hay gesto; los botones del aviso siguen funcionando.
        if (izquierda is not null)
        {
            izquierda[0].IsVisible = derecha[0].IsVisible = Permitido;
            SemanticProperties.SetHint(this, Permitido ? "Desliza a un lado para quitar este aviso" : "");
        }
        IsVisible = (Content?.IsVisible ?? false) && !descartado;
        if (IsVisible)
            Opacity = 1;
    }

    private async Task QuitarAsync()
    {
        if (!Permitido || string.IsNullOrEmpty(Clave))
            return;
        quitadoEnMemoria = Clave;
        Guardar(Clave);
        await this.FadeToAsync(0, Movimiento.Rapida, Movimiento.Entrada);
        Revisar();
    }

    // Preferences guarda solo el último aviso quitado de cada tarjeta (texto o identificador del caso, sin datos personales).
    private string? Leer()
    {
        try { return Preferences.Default.Get<string?>(LlaveGuardada, null) ?? quitadoEnMemoria; }
        catch (Exception) { return quitadoEnMemoria; }
    }

    private void Guardar(string clave)
    {
        try { Preferences.Default.Set(LlaveGuardada, clave); }
        catch (Exception) { /* sin almacenamiento: queda oculto mientras la app siga abierta */ }
    }
}
