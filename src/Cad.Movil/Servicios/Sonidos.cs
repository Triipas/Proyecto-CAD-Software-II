namespace Cad.Movil.Servicios;

// Sonidos de la app (24/10/2026). Cada sonido acompaña a un aviso visible en pantalla: nunca es el único aviso.
// Archivos en Resources/Raw/sonidos; en Android suenan con el volumen de notificaciones.
// 07/11/2026: DespachoModificado (la central cambió o retiró el despacho), ConexionPerdida y ConexionRecuperada (canal en
// tiempo real del sereno) y OperacionConfirmada (un envío o avance que el servidor aceptó).
public enum Sonido { NuevoDespacho, Llegaste, SosRegistrado, Error, DespachoModificado, ConexionPerdida, ConexionRecuperada, OperacionConfirmada }

public interface IReproductorSonidos
{
    void Reproducir(Sonido sonido);
}

// Fuera de Android (y en las pruebas): sin sonido.
public sealed class SinSonidos : IReproductorSonidos
{
    public void Reproducir(Sonido sonido) { }
}
