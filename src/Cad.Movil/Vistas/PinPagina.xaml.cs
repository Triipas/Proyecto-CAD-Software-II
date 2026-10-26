using System.ComponentModel;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class PinPagina : ContentPage
{
    private readonly PinVistaModelo vistaModelo;
    private int llenos;

    public PinPagina(PinVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
        vistaModelo.PropertyChanged += AlCambiarVistaModelo;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        vistaModelo.Preparar();
        llenos = vistaModelo.Puntos.Count(p => p);
    }

    // Movimiento (26/10/2026): el punto del dígito que se escribe «cae» en su lugar (crece un poco más de su tamaño y se asienta);
    // el que se borra se encoge un instante. Así cada toque se ve también en los puntos, no solo en la tecla. Si se vacían todos
    // de golpe (un PIN rechazado), no: de eso se encarga la sacudida.
    private void AlCambiarVistaModelo(object? emisor, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PinVistaModelo.Puntos)) return;
        var ahora = vistaModelo.Puntos.Count(p => p);
        var antes = llenos;
        llenos = ahora;
        if (Movimiento.Reducido || Math.Abs(ahora - antes) != 1) return;
        // Después de que la lista de puntos se rehaga con el valor nuevo.
        Dispatcher.Dispatch(() =>
        {
            var indice = ahora > antes ? ahora - 1 : ahora;
            if (indice < 0 || indice >= PuntosPin.Children.Count || PuntosPin.Children[indice] is not VisualElement punto) return;
            if (ahora > antes) Caer(punto);
            else Encoger(punto);
        });
    }

    private static void Caer(VisualElement punto)
    {
        punto.AbortAnimation("punto");
        new Animation
        {
            { 0, 0.55, new Animation(v => punto.Scale = v, 0.4, 1.2, Movimiento.Salida) },
            { 0.55, 1, new Animation(v => punto.Scale = v, 1.2, 1, Movimiento.Estandar) },
        }.Commit(punto, "punto", length: Movimiento.Base, finished: (_, _) => punto.Scale = 1);
    }

    private static void Encoger(VisualElement punto)
    {
        punto.AbortAnimation("punto");
        new Animation
        {
            { 0, 0.4, new Animation(v => punto.Scale = v, 1, 0.75, Movimiento.Salida) },
            { 0.4, 1, new Animation(v => punto.Scale = v, 0.75, 1, Movimiento.Estandar) },
        }.Commit(punto, "punto", length: Movimiento.Rapida, finished: (_, _) => punto.Scale = 1);
    }
}
