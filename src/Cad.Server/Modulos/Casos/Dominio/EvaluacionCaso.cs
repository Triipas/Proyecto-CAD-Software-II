namespace Cad.Server.Modulos.Casos.Dominio;

// HU32 — evaluación voluntaria de un caso cerrado por su titular. Una por caso: la clave es el propio caso, así que dos
// evaluaciones simultáneas no pueden coexistir (CA03). IdSolicitud distingue un reenvío técnico de un segundo intento.
public sealed class EvaluacionCaso
{
    public const int PuntuacionMinima = 1;
    public const int PuntuacionMaxima = 5;
    // Límite técnico de longitud (protección de la base de datos), no una regla de negocio.
    public const int MaximoComentario = 2000;

    public Guid CasoId { get; private set; }
    public Guid CiudadanoId { get; private set; }
    public Guid IdSolicitud { get; private set; }
    public int Puntuacion { get; private set; }
    public string? Comentario { get; private set; }
    public DateTimeOffset EvaluadaEn { get; private set; }

    private EvaluacionCaso() { } // EF Core

    public static bool PuntuacionValida(int? puntuacion) => puntuacion is >= PuntuacionMinima and <= PuntuacionMaxima;

    public static EvaluacionCaso Registrar(Caso caso, Guid idSolicitud, int puntuacion, string? comentario, DateTimeOffset ahora)
    {
        if (caso.Estado != EstadoCaso.Cerrada)
            throw new InvalidOperationException("Solo un caso cerrado puede evaluarse.");
        if (!PuntuacionValida(puntuacion))
            throw new ArgumentOutOfRangeException(nameof(puntuacion));
        return new()
        {
            CasoId = caso.Id,
            CiudadanoId = caso.CiudadanoId!.Value,
            IdSolicitud = idSolicitud,
            Puntuacion = puntuacion,
            Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(),
            EvaluadaEn = ahora,
        };
    }
}
