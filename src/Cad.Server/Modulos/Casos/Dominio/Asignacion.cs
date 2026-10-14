namespace Cad.Server.Modulos.Casos.Dominio;

// HU20 — Asignar un caso pendiente a un sereno disponible (S2-T05, S2-T06).
// «Vigente» deja de serlo al cerrar el caso (HU24). La base de datos garantiza una sola asignación vigente
// por caso y otra por sereno con índices únicos parciales (HU20-CA04, CA05; RNF08).
public sealed class Asignacion
{
    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid SerenoId { get; private set; }
    public Guid OperadorId { get; private set; }

    // Idempotencia: la misma solicitud reenviada no crea una segunda asignación (CA08).
    public Guid IdSolicitud { get; private set; }

    public DateTimeOffset AsignadaEn { get; private set; }
    public bool Vigente { get; private set; }

    // HU24-CA07: al cerrar el caso la asignación deja de ser vigente y el sereno queda libre.
    public DateTimeOffset? FinalizadaEn { get; private set; }

    public void Finalizar(DateTimeOffset ahora)
    {
        if (!Vigente)
            throw new InvalidOperationException("La asignación ya no está vigente.");
        Vigente = false;
        FinalizadaEn = ahora;
    }

    private Asignacion() { } // EF Core

    public static Asignacion Registrar(Caso caso, Guid serenoId, Guid operadorId, Guid idSolicitud, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        SerenoId = serenoId,
        OperadorId = operadorId,
        IdSolicitud = idSolicitud,
        AsignadaEn = ahora,
        Vigente = true,
    };
}
