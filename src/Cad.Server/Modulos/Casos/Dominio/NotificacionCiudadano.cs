namespace Cad.Server.Modulos.Casos.Dominio;

// HU31 — notificación dentro de la app por un cambio del estado público de un caso propio (E4: bandeja persistente; sin
// push al ciudadano). Vinculada con el caso (CA01) y solo para su titular (CA04). Persiste aunque el ciudadano esté
// desconectado (CA02). El texto es la vista previa: sin detalles del hecho (CA03).
public sealed class NotificacionCiudadano
{
    public Guid Id { get; private set; }
    public Guid CiudadanoId { get; private set; }
    public Guid CasoId { get; private set; }
    public string EstadoPublico { get; private set; } = "";
    public string Texto { get; private set; } = "";
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset? LeidaEn { get; private set; }

    private NotificacionCiudadano() { } // EF Core

    // CA03: solo el origen y el nuevo estado público; nunca la descripción, el motivo, el destino ni el resultado.
    public static NotificacionCiudadano PorCambioDeEstado(Caso caso, string estadoPublico, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CiudadanoId = caso.CiudadanoId!.Value,
        CasoId = caso.Id,
        EstadoPublico = estadoPublico,
        Texto = $"Tu {caso.Origen switch { OrigenCaso.Sos => "SOS", OrigenCaso.Reporte => "reporte", _ => "caso" }} pasó a «{estadoPublico}».",
        CreadaEn = ahora,
    };

    public void MarcarLeida(DateTimeOffset ahora) => LeidaEn ??= ahora;
}
