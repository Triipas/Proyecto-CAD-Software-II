using Microsoft.Maui.Layouts;

namespace Cad.Movil.Vistas;

// Mosaico de columnas iguales (tipos del reporte). A diferencia de FlexLayout, mide cada casilla con el ancho de su columna,
// así el texto se parte en varias líneas y cada fila crece hasta la casilla más alta (también con el texto del sistema
// agrandado). Las casillas de una misma fila quedan del mismo alto.
public sealed class Mosaico : Layout
{
    public static readonly BindableProperty ColumnasProperty =
        BindableProperty.Create(nameof(Columnas), typeof(int), typeof(Mosaico), 3, propertyChanged: (b, _, _) => ((Mosaico)b).InvalidateMeasure());

    public static readonly BindableProperty EspacioProperty =
        BindableProperty.Create(nameof(Espacio), typeof(double), typeof(Mosaico), 8d, propertyChanged: (b, _, _) => ((Mosaico)b).InvalidateMeasure());

    public int Columnas { get => (int)GetValue(ColumnasProperty); set => SetValue(ColumnasProperty, value); }
    public double Espacio { get => (double)GetValue(EspacioProperty); set => SetValue(EspacioProperty, value); }

    protected override ILayoutManager CreateLayoutManager() => new Administrador(this);

    private sealed class Administrador(Mosaico mosaico) : LayoutManager(mosaico)
    {
        private readonly List<double> altosFila = [];

        public override Size Measure(double anchoDisponible, double altoDisponible)
        {
            var relleno = mosaico.Padding;
            var anchoCelda = AnchoCelda(anchoDisponible - relleno.HorizontalThickness);
            var visibles = Visibles();
            altosFila.Clear();
            for (var inicio = 0; inicio < visibles.Count; inicio += Columnas)
                altosFila.Add(visibles.Skip(inicio).Take(Columnas).Max(v => v.Measure(anchoCelda, double.PositiveInfinity).Height));
            var alto = altosFila.Sum() + mosaico.Espacio * Math.Max(0, altosFila.Count - 1) + relleno.VerticalThickness;
            return new Size(double.IsFinite(anchoDisponible) ? anchoDisponible : anchoCelda * Columnas, alto);
        }

        public override Size ArrangeChildren(Rect limites)
        {
            var relleno = mosaico.Padding;
            var anchoCelda = AnchoCelda(limites.Width - relleno.HorizontalThickness);
            var visibles = Visibles();
            var y = limites.Top + relleno.Top;
            for (var i = 0; i < visibles.Count; i++)
            {
                var fila = i / Columnas;
                if (fila >= altosFila.Count) break; // sin medir todavía
                if (i > 0 && i % Columnas == 0) y += altosFila[fila - 1] + mosaico.Espacio;
                var x = limites.Left + relleno.Left + (i % Columnas) * (anchoCelda + mosaico.Espacio);
                visibles[i].Arrange(new Rect(x, y, anchoCelda, altosFila[fila]));
            }
            return limites.Size;
        }

        // Con el texto del sistema agrandado (desde 130 %), una columna menos para que las palabras largas no se partan.
        private int Columnas => Math.Max(1, EscalaTexto() >= 1.3 ? mosaico.Columnas - 1 : mosaico.Columnas);

        private static double EscalaTexto() =>
#if ANDROID
            global::Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1;
#else
            1;
#endif

        private double AnchoCelda(double ancho) =>
            Math.Max(0, ((double.IsFinite(ancho) ? ancho : 360) - mosaico.Espacio * (Columnas - 1)) / Columnas);

        private List<IView> Visibles() => mosaico.Where(v => v.Visibility != Visibility.Collapsed).ToList();
    }
}
