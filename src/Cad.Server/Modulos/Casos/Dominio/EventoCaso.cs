namespace Cad.Server.Modulos.Casos.Dominio;

// Historial de actuaciones de un caso: solo inserción (Plan de Arquitectura, 9.1). Base de HU27 y HU28.
public enum TipoEventoCaso { SosEmitido, IntentoContactoRegistrado }

// Pública = visible para el ciudadano titular; Interna = solo roles autorizados (RNF12).
public enum VisibilidadEvento { Publica, Interna }

public sealed class EventoCaso
{
    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public TipoEventoCaso Tipo { get; private set; }
    public Guid ActorId { get; private set; }
    public VisibilidadEvento Visibilidad { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }

    private EventoCaso() { } // EF Core

    public static EventoCaso Registrar(Caso caso, TipoEventoCaso tipo, Guid actorId, VisibilidadEvento visibilidad, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        Tipo = tipo,
        ActorId = actorId,
        Visibilidad = visibilidad,
        OcurridoEn = ahora,
    };
}
