namespace Cad.Server.Panel;

// Panel del caso en la central (05/11/2026): el detalle de un SOS o de un reporte se muestra dentro de la central, en un marco
// sobre el mapa, sin la barra del panel y con sus secciones en pestañas (panel-caso.css y panel-caso.js). El navegador marca la
// navegación dentro de un marco con «Sec-Fetch-Dest: iframe», también tras las redirecciones de cada formulario (PRG), así que
// la página sigue en modo panel sin arrastrar ningún parámetro; «?vista=panel» lo fuerza (pruebas y navegadores sin la cabecera).
public static class ModoPanel
{
    public const string ClaveVista = "PermitePanel";

    public static bool Activo(HttpRequest peticion) =>
        string.Equals(peticion.Headers["Sec-Fetch-Dest"], "iframe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(peticion.Query["vista"], "panel", StringComparison.OrdinalIgnoreCase);
}
