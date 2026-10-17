namespace Cad.Server.Modulos.Acompanamiento.Dominio;

// HU39-B (CA05), DP22 (decisión del equipo de desarrollo delegada por el PO el 16/10/2026): conservación excepcional del
// recorrido de una sesión vinculado con un caso. Exige una finalidad explícita y queda registrada con su responsable y su
// momento; esas posiciones no se eliminan por plazo. Una por sesión; solo inserción.
public sealed class ConservacionRecorrido
{
    public const int MaximoFinalidad = 500;

    public Guid SesionId { get; private set; }
    public Guid CasoId { get; private set; }
    public string Finalidad { get; private set; } = "";
    public Guid OperadorId { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }

    private ConservacionRecorrido() { } // EF Core

    public static ConservacionRecorrido Registrar(Guid sesionId, Guid casoId, string finalidad, Guid operadorId, DateTimeOffset ahora) => new()
    {
        SesionId = sesionId,
        CasoId = casoId,
        Finalidad = finalidad.Trim(),
        OperadorId = operadorId,
        RegistradaEn = ahora,
    };
}
