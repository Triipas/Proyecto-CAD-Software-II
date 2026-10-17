namespace Cad.Server.Modulos.Acompanamiento.Dominio;

// HU42: reglas que generan un aviso técnico o de verificación (CA01, CA02).
public enum ReglaAviso { UbicacionDesactualizada, ConfirmacionSinRespuesta }

// HU43: estados del aviso (CA01, CA03).
public enum EstadoAviso { Generado, EnRevision, Resuelto }

// HU43-CA02: tipos de actuación registrada.
public enum TipoActuacionAviso { Contacto, Accion, Resultado }

// HU42 — aviso técnico o de verificación de una sesión activa, con su regla, su momento y los datos que lo originaron. No es
// un SOS ni genera despacho (CA04). Clave: la condición concreta (la última posición o la confirmación), así que una condición
// que persiste no genera otro aviso (CA03). HU43 — su revisión, sus actuaciones, su resolución y su vínculo con un caso.
public sealed class AvisoAcompanamiento
{
    public const int MaximoJustificacion = 1000;

    public Guid Id { get; private set; }
    public Guid SesionId { get; private set; }
    public Guid CiudadanoId { get; private set; }
    public ReglaAviso Regla { get; private set; }
    public string Clave { get; private set; } = "";
    public DateTimeOffset GeneradoEn { get; private set; }
    // Datos de origen: hora de la última posición (o del inicio, si no hubo ninguna) o de la presentación de la confirmación.
    public DateTimeOffset ReferenciaEn { get; private set; }
    public Guid? ConfirmacionId { get; private set; }

    public EstadoAviso Estado { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public DateTimeOffset? RevisionIniciadaEn { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }

    // HU43-CA04/CA05: caso vinculado (existente o creado desde el aviso), con quién y cuándo.
    public Guid? CasoId { get; private set; }
    public Guid? VinculadoPor { get; private set; }
    public DateTimeOffset? VinculadoEn { get; private set; }
    public bool CasoCreado { get; private set; }
    public string? JustificacionCaso { get; private set; }

    public uint Version { get; private set; }

    private AvisoAcompanamiento() { } // EF Core

    public static AvisoAcompanamiento UbicacionDesactualizada(SesionAcompanamiento sesion, DateTimeOffset? ultimaPosicion, DateTimeOffset ahora)
    {
        var referencia = ultimaPosicion ?? sesion.IniciadaEn;
        return Generar(sesion, ReglaAviso.UbicacionDesactualizada, ultimaPosicion is null ? "sin-posiciones" : $"posicion:{referencia.UtcTicks}",
            referencia, null, ahora);
    }

    public static AvisoAcompanamiento ConfirmacionSinRespuesta(SesionAcompanamiento sesion, ConfirmacionBienestar confirmacion, DateTimeOffset ahora) =>
        Generar(sesion, ReglaAviso.ConfirmacionSinRespuesta, $"confirmacion:{confirmacion.Id}",
            confirmacion.PresentadaEn ?? throw new InvalidOperationException("Solo una confirmación presentada genera un aviso."), confirmacion.Id, ahora);

    private static AvisoAcompanamiento Generar(SesionAcompanamiento sesion, ReglaAviso regla, string clave, DateTimeOffset referencia, Guid? confirmacion,
        DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        SesionId = sesion.Id,
        CiudadanoId = sesion.CiudadanoId,
        Regla = regla,
        Clave = clave,
        GeneradoEn = ahora,
        ReferenciaEn = referencia,
        ConfirmacionId = confirmacion,
        Estado = EstadoAviso.Generado,
    };

    // HU43-CA01: pasa a «En revisión» con su responsable.
    public bool IniciarRevision(Guid operadorId, DateTimeOffset ahora)
    {
        if (Estado != EstadoAviso.Generado)
            return false;
        Estado = EstadoAviso.EnRevision;
        ResponsableId = operadorId;
        RevisionIniciadaEn = ahora;
        return true;
    }

    // HU43-CA03: solo un aviso en revisión se resuelve; conserva su responsable y sus actuaciones.
    public bool Resolver(Guid operadorId, DateTimeOffset ahora)
    {
        if (Estado != EstadoAviso.EnRevision)
            return false;
        Estado = EstadoAviso.Resuelto;
        ResueltoPor = operadorId;
        ResueltoEn = ahora;
        return true;
    }

    // HU43-CA04/CA05: un aviso se asocia con un solo caso.
    public void Vincular(Guid casoId, Guid operadorId, DateTimeOffset ahora, string? justificacionCasoCreado = null)
    {
        if (CasoId is not null)
            throw new InvalidOperationException("El aviso ya está vinculado con un caso.");
        CasoId = casoId;
        VinculadoPor = operadorId;
        VinculadoEn = ahora;
        CasoCreado = justificacionCasoCreado is not null;
        JustificacionCaso = justificacionCasoCreado?.Trim();
    }
}

// HU43-CA02: contacto, acción o resultado registrado sobre un aviso en revisión, con su responsable y su momento.
public sealed class ActuacionAviso
{
    public const int MaximoDescripcion = 1000;

    public Guid Id { get; private set; }
    public Guid AvisoId { get; private set; }
    public TipoActuacionAviso Tipo { get; private set; }
    public string Descripcion { get; private set; } = "";
    public Guid OperadorId { get; private set; }
    public DateTimeOffset RegistradaEn { get; private set; }
    // Un reenvío del mismo formulario no duplica la actuación.
    public Guid IdSolicitud { get; private set; }

    private ActuacionAviso() { } // EF Core

    public static ActuacionAviso Registrar(AvisoAcompanamiento aviso, TipoActuacionAviso tipo, string descripcion, Guid operadorId, Guid idSolicitud,
        DateTimeOffset ahora)
    {
        if (aviso.Estado != EstadoAviso.EnRevision)
            throw new InvalidOperationException("Solo se registran actuaciones en un aviso en revisión.");
        return new()
        {
            Id = Guid.CreateVersion7(),
            AvisoId = aviso.Id,
            Tipo = tipo,
            Descripcion = descripcion.Trim(),
            OperadorId = operadorId,
            RegistradaEn = ahora,
            IdSolicitud = idSolicitud,
        };
    }
}
