namespace Cad.Movil.Servicios;

// Sonidos de la app (24/10/2026). Cada sonido acompaña a un aviso visible en pantalla: nunca es el único aviso.
// Archivos en Resources/Raw/sonidos; en Android suenan con el volumen de notificaciones.
public enum Sonido { NuevoDespacho, Llegaste, SosRegistrado, Error }

public interface IReproductorSonidos
{
    void Reproducir(Sonido sonido);
}

// Fuera de Android (y en las pruebas): sin sonido.
public sealed class SinSonidos : IReproductorSonidos
{
    public void Reproducir(Sonido sonido) { }
}
