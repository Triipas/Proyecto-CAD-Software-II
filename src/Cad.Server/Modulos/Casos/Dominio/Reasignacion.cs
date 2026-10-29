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
    // 27/10/2026 — el motivo del catálogo MotivosReasignacion (Cad.Contratos.Casos). Motivo guarda el detalle escrito o, sin
    // él, el texto del catálogo. Nulo antes.
    public string? MotivoCodigo { get; private set; }
    public DateTimeOffset ReasignadoEn { get; private set; }

    private Reasignacion() { } // EF Core

    public static Reasignacion Registrar(Caso caso, Guid serenoAnteriorId, Guid serenoNuevoId, Guid operadorId, string motivoCodigo, string motivo,
        DateTimeOffset ahora) => new()
    {
        MotivoCodigo = motivoCodigo,
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        SerenoAnteriorId = serenoAnteriorId,
        SerenoNuevoId = serenoNuevoId,
        OperadorId = operadorId,
        Motivo = motivo.Trim(),
        ReasignadoEn = ahora,
    };
}
