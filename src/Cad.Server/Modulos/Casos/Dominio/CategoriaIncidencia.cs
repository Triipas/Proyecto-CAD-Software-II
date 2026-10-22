namespace Cad.Server.Modulos.Casos.Dominio;

// DP07: catálogo compartido de categorías. Lo usan la clasificación del SOS (HU13), los reportes (HU10, HU14), la tipificación
// final (HU24), la búsqueda (HU26) y el mapa de concentración (HU29).
// Ampliado el 22/10/2026 (decisión del PO): 12 categorías de seguridad ciudadana más «Otros», en lenguaje del ciudadano.
// Fuera del alcance: incendios (116) y limpieza pública. La descripción del reporte sigue siendo obligatoria (DP08).
public sealed class CategoriaIncidencia
{
    public const int MaximoAyuda = 120;

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public int Orden { get; private set; }
    // Ejemplos breves que orientan al ciudadano al elegir («Me robaron, intento de robo»).
    public string Ayuda { get; private set; } = "";
    // Clave del icono dibujado con que la app muestra la categoría (nunca sin su nombre).
    public string Icono { get; private set; } = "";

    private CategoriaIncidencia() { } // EF Core

    private static CategoriaIncidencia Nueva(string id, string nombre, int orden, string ayuda, string icono) =>
        new() { Id = new Guid(id), Nombre = nombre, Orden = orden, Ayuda = ayuda, Icono = icono };

    // Identificadores fijos: la semilla es estable entre entornos. Las cuatro primeras son las de RF16.2 y conservan su
    // identificador y su posición en esta lista (los casos existentes no cambian); Orden es el orden en que se presentan.
    public static readonly IReadOnlyList<CategoriaIncidencia> Iniciales =
    [
        Nueva("0199e2b0-0000-7000-8000-000000000001", "Violencia familiar", 2, "Agresión, gritos o maltrato en una vivienda", "violencia"),
        Nueva("0199e2b0-0000-7000-8000-000000000002", "Robo o hurto", 1, "Me robaron, intento de robo", "robo"),
        Nueva("0199e2b0-0000-7000-8000-000000000003", "Consumo o venta de drogas", 5, "Venta o consumo en un punto fijo", "drogas"),
        Nueva("0199e2b0-0000-7000-8000-000000000004", "Otros", 13, "Solo si nada de lo anterior encaja", "otros"),
        Nueva("0199e2b0-0000-7000-8000-000000000005", "Acoso en la vía pública", 3, "Acoso, tocamientos o persecución", "acoso"),
        Nueva("0199e2b0-0000-7000-8000-000000000006", "Extorsión o amenazas", 4, "Cobro de cupos, amenazas", "extorsion"),
        Nueva("0199e2b0-0000-7000-8000-000000000007", "Consumo de alcohol en la vía pública", 6, "Grupos bebiendo en la calle o el parque", "alcohol"),
        Nueva("0199e2b0-0000-7000-8000-000000000008", "Peleas o riñas", 7, "Pelea entre personas o grupos", "pelea"),
        Nueva("0199e2b0-0000-7000-8000-000000000009", "Persona o vehículo sospechoso", 8, "Merodeo, vehículo abandonado", "sospechoso"),
        Nueva("0199e2b0-0000-7000-8000-000000000010", "Daños o vandalismo", 9, "Pintas, destrozos, robo de mobiliario público", "vandalismo"),
        Nueva("0199e2b0-0000-7000-8000-000000000011", "Accidente de tránsito", 10, "Choque sin heridos graves", "accidente"),
        Nueva("0199e2b0-0000-7000-8000-000000000012", "Ruidos molestos o escándalo", 11, "Fiestas o escándalo en la vía pública", "ruido"),
        Nueva("0199e2b0-0000-7000-8000-000000000013", "Persona en riesgo", 12, "Menor o adulto mayor perdido, persona desorientada", "riesgo"),
    ];

    public static CategoriaIncidencia ViolenciaFamiliar => Iniciales[0];
    public static CategoriaIncidencia RoboOHurto => Iniciales[1];
    public static CategoriaIncidencia Drogas => Iniciales[2];
    public static CategoriaIncidencia Otros => Iniciales[3];
}
