namespace Cad.Server.Modulos.Casos.Dominio;

// DP08 ampliada (26/10/2026) — foto que el ciudadano adjuntó a su reporte (HU10). Se guarda en la base de datos (Neon Free,
// costo cero, ADR-06) porque el disco de Render es efímero. Solo la ve el operador; ya sin metadatos (EXIF, GPS).
public sealed class FotoReporte
{
    public Guid CasoId { get; private set; }
    // Orden en que el ciudadano las adjuntó: 1, 2 o 3.
    public int Numero { get; private set; }
    public byte[] Contenido { get; private set; } = [];
    public DateTimeOffset RegistradaEn { get; private set; }

    private FotoReporte() { } // EF Core

    public static FotoReporte Registrar(Caso caso, int numero, byte[] contenido, DateTimeOffset ahora) => new()
    {
        CasoId = caso.Id,
        Numero = numero,
        Contenido = contenido,
        RegistradaEn = ahora,
    };
}
