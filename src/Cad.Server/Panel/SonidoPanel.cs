namespace Cad.Server.Panel;

// Sonidos del panel (24/10/2026): tras una acción (PRG), la página siguiente reproduce una vez el sonido que se dejó aquí.
// Lo lee _LayoutPanel y lo toca sonidos.js solo si el operador activó los avisos sonoros. Archivos en wwwroot/sonidos.
public static class SonidoPanel
{
    public const string Clave = "Sonido";
    public const string CasoAsignado = "caso-asignado";
    public const string ReporteAdmitido = "reporte-admitido";
}
