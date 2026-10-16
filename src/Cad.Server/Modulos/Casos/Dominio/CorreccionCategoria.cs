namespace Cad.Server.Modulos.Casos.Dominio;

// HU14-CA03 — corrección de la categoría de un reporte en revisión: categoría anterior, nueva, responsable y momento.
// Solo inserción: varias correcciones quedan todas registradas; la original del ciudadano está en DetalleReporte.
public sealed class CorreccionCategoria
{
    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid CategoriaAnteriorId { get; private set; }
    public Guid CategoriaNuevaId { get; private set; }
    public Guid OperadorId { get; private set; }
    public DateTimeOffset CorregidaEn { get; private set; }

    private CorreccionCategoria() { } // EF Core

    public static CorreccionCategoria Registrar(Guid casoId, Guid anterior, Guid nueva, Guid operadorId, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = casoId,
        CategoriaAnteriorId = anterior,
        CategoriaNuevaId = nueva,
        OperadorId = operadorId,
        CorregidaEn = ahora,
    };
}
