namespace Cad.Server.Modulos.Acompanamiento.Dominio;

// HU41: las dos respuestas posibles del ciudadano.
public enum RespuestaBienestar { EstoyBien, NecesitoAyuda }

// HU40 — solicitud de confirmación de bienestar durante un acompañamiento activo, con el operador y el momento (CA01), y
// HU41 — su presentación y su respuesta. No crea un SOS ni un despacho por sí misma (HU40-CA03, HU41-CA05): solo la
// respuesta «Necesito ayuda» del ciudadano inicia el flujo SOS (HU41-CA03) o se vincula con su SOS vigente (HU41-CA04).
public sealed class ConfirmacionBienestar
{
    public Guid Id { get; private set; }
    public Guid SesionId { get; private set; }
    public Guid CiudadanoId { get; private set; }
    public Guid OperadorId { get; private set; }
    // Idempotencia del formulario del operador: un reenvío no crea otra solicitud.
    public Guid IdSolicitud { get; private set; }
    public DateTimeOffset SolicitadaEn { get; private set; }
    // HU41-CA01: la app la mostró al ciudadano.
    public DateTimeOffset? PresentadaEn { get; private set; }
    public RespuestaBienestar? Respuesta { get; private set; }
    public DateTimeOffset? RespondidaEn { get; private set; }
    // HU41-CA03/CA04: el SOS creado o vinculado con «Necesito ayuda».
    public Guid? CasoId { get; private set; }

    // Token de concurrencia (xmin): dos respuestas simultáneas no se registran las dos.
    public uint Version { get; private set; }

    public bool Pendiente => Respuesta is null;

    private ConfirmacionBienestar() { } // EF Core

    public static ConfirmacionBienestar Solicitar(SesionAcompanamiento sesion, Guid operadorId, Guid idSolicitud, DateTimeOffset ahora)
    {
        if (!sesion.AdmitePosiciones(ahora))
            throw new InvalidOperationException("Solo se pide una confirmación durante una sesión activa.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            SesionId = sesion.Id,
            CiudadanoId = sesion.CiudadanoId,
            OperadorId = operadorId,
            IdSolicitud = idSolicitud,
            SolicitadaEn = ahora,
        };
    }

    // Se registra la primera presentación; las siguientes no la cambian.
    public bool RegistrarPresentacion(DateTimeOffset ahora)
    {
        if (PresentadaEn is not null)
            return false;
        PresentadaEn = ahora;
        return true;
    }

    public void Responder(RespuestaBienestar respuesta, Guid? casoId, DateTimeOffset ahora)
    {
        if (!Pendiente)
            throw new InvalidOperationException("La confirmación ya tiene respuesta.");
        if ((respuesta == RespuestaBienestar.NecesitoAyuda) != (casoId is not null))
            throw new ArgumentException("«Necesito ayuda» se vincula siempre con un SOS; «Estoy bien», nunca.", nameof(casoId));
        // Responder implica que se presentó.
        RegistrarPresentacion(ahora);
        Respuesta = respuesta;
        RespondidaEn = ahora;
        CasoId = casoId;
    }
}
