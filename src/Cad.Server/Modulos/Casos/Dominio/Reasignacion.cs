namespace Cad.Server.Modulos.Casos.Dominio;

// Reasignación de un caso a otro sereno por la central (24/10/2026): de quién a quién, quién la hizo, por qué y cuándo.
// Solo inserción. La asignación anterior queda cancelada y la nueva vigente.
public sealed class Reasignacion
{
    public const int MaximoMotivo = 500;

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid SerenoAnteriorId { get; private set; }
    public Guid SerenoNuevoId { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Motivo { get; private set; } = "";
    public DateTimeOffset ReasignadoEn { get; private set; }

    private Reasignacion() { } // EF Core

    public static Reasignacion Registrar(Caso caso, Guid serenoAnteriorId, Guid serenoNuevoId, Guid operadorId, string motivo, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        SerenoAnteriorId = serenoAnteriorId,
        SerenoNuevoId = serenoNuevoId,
        OperadorId = operadorId,
        Motivo = motivo.Trim(),
        ReasignadoEn = ahora,
    };
}
