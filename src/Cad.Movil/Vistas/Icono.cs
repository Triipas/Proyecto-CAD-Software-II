using Microsoft.Maui.Graphics;

namespace Cad.Movil.Vistas;

// Icono dibujado (rediseño CAD v2): trazo de 2 px en una caja de 24, el mismo juego que _Icono.cshtml del panel web.
// Sustituye a los símbolos Unicode; es decorativo, así que queda fuera del árbol de accesibilidad.
public sealed class Icono : GraphicsView, IDrawable
{
    // Círculo de centro (cx, cy) y radio r: se dibuja con DrawCircle; el resto son trazos SVG sin arcos.
    private static string Circulo(double cx, double cy, double r) => FormattableString.Invariant($"circulo {cx} {cy} {r}");

    private static readonly Dictionary<string, string[]> Trazos = new()
    {
        ["escudo"] = ["M12 2.5 L19.5 5.5 V11.5 C19.5 16.3 16.3 20.3 12 21.5 C7.7 20.3 4.5 16.3 4.5 11.5 V5.5 Z", "M8.75 12 L11 14.25 L15.25 9.75"],
        ["exito"] = [Circulo(12, 12, 9), "M8 12.25 L10.75 15 L16 9.5"],
        ["error"] = [Circulo(12, 12, 9), "M15 9 L9 15 M9 9 L15 15"],
        ["advertencia"] = ["M10.3 4.2 L2.6 17.5 C2.2 18.9 3 20.5 4.3 20.5 H19.7 C21 20.5 21.8 18.9 21.4 17.5 L13.7 4.2 C12.9 2.9 11.1 2.9 10.3 4.2 Z", "M12 9.5 V13.5 M12 17 L12.01 17"],
        ["informacion"] = [Circulo(12, 12, 9), "M12 11 V16.5 M12 7.75 L12.01 7.75"],
        ["urgente"] = ["M6 18 V13 C6 9.7 8.7 7 12 7 C15.3 7 18 9.7 18 13 V18", "M4 18 H20 V21 H4 Z M12 2 V4 M4.2 5.2 L5.6 6.6 M19.8 5.2 L18.4 6.6"],
        ["ubicacion"] = ["M12 21 C12 21 5 14.8 5 9.5 C5 5.6 8.1 2.5 12 2.5 C15.9 2.5 19 5.6 19 9.5 C19 14.8 12 21 12 21 Z", Circulo(12, 9.5, 2.5)],
        ["reloj"] = [Circulo(12, 12, 9), "M12 7 V12 L15 14"],
        ["bandeja"] = ["M22 12 H16 L14 15 H10 L8 12 H2", "M5.5 5.1 L2 12 V18 C2 19.1 2.9 20 4 20 H20 C21.1 20 22 19.1 22 18 V12 L18.5 5.1 C18.2 4.4 17.5 4 16.8 4 H7.2 C6.5 4 5.8 4.4 5.5 5.1 Z"],
        ["lupa"] = [Circulo(11, 11, 7), "M20 20 L16.5 16.5"],
        ["asignado"] = ["M16 21 V19 C16 16.8 14.2 15 12 15 H6 C3.8 15 2 16.8 2 19 V21", Circulo(9, 7, 4), "M16 11 L18 13 L22 9"],
        ["camino"] = ["M3 11 L22 2 L13 21 L11 13 Z"],
        ["derivar"] = ["M7 17 L17 7 M7 7 H17 V17"],
        ["cancelar"] = [Circulo(12, 12, 9), "M4.9 4.9 L19.1 19.1"],
        ["documento"] = ["M14 2 H6 C4.9 2 4 2.9 4 4 V20 C4 21.1 4.9 22 6 22 H18 C19.1 22 20 21.1 20 20 V8 Z", "M14 2 V8 H20"],
        ["ruta"] = [Circulo(6, 19, 3), "M9 19 H17.5 C19.4 19 21 17.4 21 15.5 C21 13.6 19.4 12 17.5 12 H6.5 C4.6 12 3 10.4 3 8.5 C3 6.6 4.6 5 6.5 5 H15", Circulo(18, 5, 3)],
        ["campana"] = ["M6 8 C6 4.7 8.7 2 12 2 C15.3 2 18 4.7 18 8 C18 15 21 17 21 17 H3 C3 17 6 15 6 8 Z", "M10.3 21 C10.6 21.6 11.3 22 12 22 C12.7 22 13.4 21.6 13.7 21"],
        ["derecha"] = ["M9 18 L15 12 L9 6"],
        ["izquierda"] = ["M15 18 L9 12 L15 6"],
        ["salir"] = ["M9 21 H5 C3.9 21 3 20.1 3 19 V5 C3 3.9 3.9 3 5 3 H9", "M16 17 L21 12 L16 7 M21 12 H9"],
        ["hecho"] = ["M5 12 L10 17 L19 7"],
        // Inicio (diseño A): centrar el mapa en la posición propia.
        ["centrar"] = [Circulo(12, 12, 7), Circulo(12, 12, 2.5), "M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22"],
        // Reporte: dictar la descripción.
        ["microfono"] = ["M12 2 C10.3 2 9 3.3 9 5 V12 C9 13.7 10.3 15 12 15 C13.7 15 15 13.7 15 12 V5 C15 3.3 13.7 2 12 2 Z", "M19 10 V12 C19 15.9 15.9 19 12 19 C8.1 19 5 15.9 5 12 V10 M12 19 V22"],
        // Catálogo de tipos de incidencia (PO, 22/10/2026): la clave llega del servidor en CategoriaCatalogo.Icono.
        ["robo"] = ["M19 7 V5 C19 3.9 18.1 3 17 3 H5 C3.9 3 3 3.9 3 5 V19 C3 20.1 3.9 21 5 21 H19 C20.1 21 21 20.1 21 19 V9 C21 7.9 20.1 7 19 7 H3", "M16.5 14 L16.51 14"],
        ["violencia"] = ["M3 10.5 L12 3 L21 10.5 V19 C21 20.1 20.1 21 19 21 H5 C3.9 21 3 20.1 3 19 Z", "M12 9.5 V13.5 M12 17 L12.01 17"],
        ["acoso"] = [Circulo(9, 7, 4), "M3 21 V19 C3 16.8 4.8 15 7 15 H11 C13.2 15 15 16.8 15 19 V21", "M19.5 7 V11.5 M19.5 15 L19.51 15"],
        ["extorsion"] = ["M21 15 C21 16.1 20.1 17 19 17 H7 L3 21 V5 C3 3.9 3.9 3 5 3 H19 C20.1 3 21 3.9 21 5 Z", "M12 6.5 V10.5 M12 13.5 L12.01 13.5"],
        ["drogas"] = ["M10.5 20.5 L20.5 10.5 C22.4 8.6 22.4 5.4 20.5 3.5 C18.6 1.6 15.4 1.6 13.5 3.5 L3.5 13.5 C1.6 15.4 1.6 18.6 3.5 20.5 C5.4 22.4 8.6 22.4 10.5 20.5 Z", "M8.5 8.5 L15.5 15.5"],
        ["alcohol"] = ["M10 2 H14 M10.5 2 V7 L8 10 V20 C8 21.1 8.9 22 10 22 H14 C15.1 22 16 21.1 16 20 V10 L13.5 7 V2", "M8 14 H16"],
        ["pelea"] = ["M13 2 L3 14 H12 L11 22 L21 10 H12 Z"],
        ["sospechoso"] = ["M2 12 C2 12 5.5 5 12 5 C18.5 5 22 12 22 12 C22 12 18.5 19 12 19 C5.5 19 2 12 2 12 Z", Circulo(12, 12, 3)],
        ["vandalismo"] = ["M5 3 H19 C20.1 3 21 3.9 21 5 V19 C21 20.1 20.1 21 19 21 H5 C3.9 21 3 20.1 3 19 V5 C3 3.9 3.9 3 5 3 Z", "M11 3 L9.5 9 L14 12 L11.5 21 M14 12 L21 10"],
        ["accidente"] = ["M5 17 H3 V12 L5.5 6.5 C5.8 5.6 6.6 5 7.5 5 H16.5 C17.4 5 18.2 5.6 18.5 6.5 L21 12 V17 H19", "M3 12 H21 M9 17 H15", Circulo(7, 17, 2), Circulo(17, 17, 2)],
        ["ruido"] = ["M11 5 L6 9 H2 V15 H6 L11 19 Z", "M15.5 8.5 C17.5 10.5 17.5 13.5 15.5 15.5 M19 5 C22.9 8.9 22.9 15.1 19 19"],
        ["riesgo"] = [Circulo(12, 12, 9), Circulo(12, 12, 4), "M5.6 5.6 L9.2 9.2 M14.8 9.2 L18.4 5.6 M14.8 14.8 L18.4 18.4 M9.2 14.8 L5.6 18.4"],
        ["otros"] =[Circulo(12, 12, 9), "M8 12 L8.01 12 M12 12 L12.01 12 M16 12 L16.01 12"],
    };

    public static readonly BindableProperty NombreProperty =
        BindableProperty.Create(nameof(Nombre), typeof(string), typeof(Icono), "informacion", propertyChanged: (b, _, _) => ((Icono)b).Invalidate());

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(Icono), Colors.Black, propertyChanged: (b, _, _) => ((Icono)b).Invalidate());

    public static readonly BindableProperty TamanoProperty =
        BindableProperty.Create(nameof(Tamano), typeof(double), typeof(Icono), 20d, propertyChanged: (b, _, nuevo) => ((Icono)b).WidthRequest = ((Icono)b).HeightRequest = (double)nuevo);

    public static readonly BindableProperty GrosorProperty =
        BindableProperty.Create(nameof(Grosor), typeof(float), typeof(Icono), 2f, propertyChanged: (b, _, _) => ((Icono)b).Invalidate());

    public Icono()
    {
        Drawable = this;
        WidthRequest = HeightRequest = 20;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);
    }

    public string Nombre { get => (string)GetValue(NombreProperty); set => SetValue(NombreProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Tamano { get => (double)GetValue(TamanoProperty); set => SetValue(TamanoProperty, value); }
    public float Grosor { get => (float)GetValue(GrosorProperty); set => SetValue(GrosorProperty, value); }

    public void Draw(ICanvas canvas, RectF caja)
    {
        if (!Trazos.TryGetValue(Nombre ?? "", out var trazos)) return;
        var escala = Math.Min(caja.Width, caja.Height) / 24f;
        canvas.SaveState();
        canvas.Translate(caja.X + (caja.Width - 24 * escala) / 2, caja.Y + (caja.Height - 24 * escala) / 2);
        canvas.Scale(escala, escala);
        canvas.StrokeColor = Color;
        canvas.StrokeSize = Grosor;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        foreach (var trazo in trazos)
        {
            if (trazo.StartsWith("circulo ", StringComparison.Ordinal))
            {
                var c = trazo[8..].Split(' ').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                canvas.DrawCircle(c[0], c[1], c[2]);
            }
            else
            {
                canvas.DrawPath(PathBuilder.Build(trazo));
            }
        }
        canvas.RestoreState();
    }
}
