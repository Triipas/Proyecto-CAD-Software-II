namespace Cad.Server.Modulos.Casos.Dominio;

// DP07 (supuesto aprobado por el PO el 13/10/2026): catálogo compartido de categorías con el mínimo de RF16.2.
// Lo usan la clasificación del SOS (HU13), los reportes (HU10, HU14) y la tipificación final (HU24).
public sealed class CategoriaIncidencia
{
    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public int Orden { get; private set; }

    private CategoriaIncidencia() { } // EF Core

    // Identificadores fijos: la semilla es estable entre entornos.
    public static readonly IReadOnlyList<CategoriaIncidencia> Iniciales =
    [
        new() { Id = new Guid("0199e2b0-0000-7000-8000-000000000001"), Nombre = "Violencia doméstica", Orden = 1 },
        new() { Id = new Guid("0199e2b0-0000-7000-8000-000000000002"), Nombre = "Robo", Orden = 2 },
        new() { Id = new Guid("0199e2b0-0000-7000-8000-000000000003"), Nombre = "Presunta actividad relacionada con drogas", Orden = 3 },
        new() { Id = new Guid("0199e2b0-0000-7000-8000-000000000004"), Nombre = "Otros", Orden = 4 },
    ];
}
