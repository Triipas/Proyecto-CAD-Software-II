namespace Cad.Server.Modulos.Casos.Dominio;

// HU09-CA01 — cancelación de un SOS pendiente por su titular: motivo, fecha y hora. Una por caso (la clave es el caso).
public sealed class CancelacionSos
{
    public const int MaximoMotivo = 500;

    public Guid CasoId { get; private set; }
    public Guid CiudadanoId { get; private set; }
    public string Motivo { get; private set; } = "";
    public DateTimeOffset CanceladaEn { get; private set; }

    private CancelacionSos() { } // EF Core

    public static CancelacionSos Registrar(Caso caso, string motivo, DateTimeOffset ahora) => new()
    {
        CasoId = caso.Id,
        CiudadanoId = caso.CiudadanoId!.Value,
        Motivo = motivo.Trim(),
        CanceladaEn = ahora,
    };
}
