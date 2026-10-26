namespace Cad.Server.Modulos.Acompanamiento.Dominio;

// 26/10/2026 — lo que el ciudadano informa por su cuenta durante el acompañamiento, sin que la central lo pida (las
// confirmaciones de HU40/HU41 son las que pide el operador). Decisión del equipo con el usuario del 26/10/2026:
//  - EstoyBien: solo informa.
//  - AlgoNoEstaBien: no emite un SOS, pero la sesión pasa a requerir atención hasta que vuelva a decir que está bien.
//  - NecesitoAyuda: como en HU41, se vincula con su SOS vigente o emite uno; se acepta aunque la sesión ya haya terminado.
//  - Llegue: llegó a su destino; se registra y la sesión se finaliza (HU36).
public enum EstadoCiudadano { EstoyBien, AlgoNoEstaBien, NecesitoAyuda, Llegue }

public sealed class EstadoInformado
{
    public Guid Id { get; private set; }
    public Guid SesionId { get; private set; }
    public Guid CiudadanoId { get; private set; }
    // Idempotencia: la app lo genera por pulsación y lo reutiliza en cada reintento (RNF08).
    public Guid IdSolicitud { get; private set; }
    public EstadoCiudadano Estado { get; private set; }
    public DateTimeOffset InformadoEn { get; private set; }
    // Solo con «Necesito ayuda»: el SOS creado o vinculado.
    public Guid? CasoId { get; private set; }

    private EstadoInformado() { } // EF Core

    public static EstadoInformado Registrar(SesionAcompanamiento sesion, Guid idSolicitud, EstadoCiudadano estado, Guid? casoId, DateTimeOffset ahora)
    {
        if ((estado == EstadoCiudadano.NecesitoAyuda) != (casoId is not null))
            throw new ArgumentException("«Necesito ayuda» se vincula siempre con un SOS; los demás estados, nunca.", nameof(casoId));
        if (estado != EstadoCiudadano.NecesitoAyuda && !sesion.AdmitePosiciones(ahora))
            throw new InvalidOperationException("Solo se informa un estado durante una sesión activa (salvo «Necesito ayuda»).");
        return new()
        {
            Id = Guid.CreateVersion7(),
            SesionId = sesion.Id,
            CiudadanoId = sesion.CiudadanoId,
            IdSolicitud = idSolicitud,
            Estado = estado,
            InformadoEn = ahora,
            CasoId = casoId,
        };
    }
}
