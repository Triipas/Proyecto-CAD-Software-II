namespace Cad.Server.Modulos.Casos.Dominio;

// Descarte de un caso falso o erróneo por la central (23/10/2026): motivo, operador, estado en que estaba y fecha y hora.
// Uno por caso (la clave es el caso). El motivo es interno; el ciudadano solo ve que su caso quedó «Descartado».
public sealed class DescarteCaso
{
    public const int MaximoMotivo = 500;

    public Guid CasoId { get; private set; }
    public Guid OperadorId { get; private set; }
    public string Motivo { get; private set; } = "";
    public EstadoCaso EstadoPrevio { get; private set; }
    public DateTimeOffset DescartadoEn { get; private set; }

    private DescarteCaso() { } // EF Core

    public static DescarteCaso Registrar(Caso caso, EstadoCaso estadoPrevio, Guid operadorId, string motivo, DateTimeOffset ahora) => new()
    {
        CasoId = caso.Id,
        OperadorId = operadorId,
        Motivo = motivo.Trim(),
        EstadoPrevio = estadoPrevio,
        DescartadoEn = ahora,
    };
}
