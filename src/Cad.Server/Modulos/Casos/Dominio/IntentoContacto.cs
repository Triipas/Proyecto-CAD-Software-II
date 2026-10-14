namespace Cad.Server.Modulos.Casos.Dominio;

// HU12-CA02: los tres resultados válidos de un intento de contacto. Cualquier otro valor se rechaza (CA03).
public enum ResultadoContacto { Contactado, SinRespuesta, NoFuePosibleComunicar }

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01).
// Solo inserción: varios intentos sobre un mismo SOS se conservan sin sobrescribirse (CA07).
public sealed class IntentoContacto
{
    // Límites técnicos de longitud (protección de la base de datos), no reglas de negocio.
    public const int MaximoInformacion = 2000;
    public const int MaximoReferencia = 500;

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid OperadorId { get; private set; }

    // Idempotencia del formulario: un reenvío técnico no crea un segundo intento (mecanismo de S1-T14).
    public Guid IdSolicitud { get; private set; }

    public ResultadoContacto Resultado { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }

    // CA04: información del hecho obtenida en la comunicación (opcional).
    public string? InformacionHecho { get; private set; }

    // CA05, DP10 (supuesto aceptado el 13/10/2026): referencia de ubicación indicada por el ciudadano.
    // Es texto, separado de las coordenadas del dispositivo: no las sustituye ni genera marcador en el mapa.
    public string? ReferenciaUbicacion { get; private set; }

    private IntentoContacto() { } // EF Core

    public static IntentoContacto Registrar(Caso caso, Guid operadorId, Guid idSolicitud, ResultadoContacto resultado,
        string? informacionHecho, string? referenciaUbicacion, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        OperadorId = operadorId,
        IdSolicitud = idSolicitud,
        Resultado = resultado,
        RegistradoEn = ahora,
        InformacionHecho = Limpiar(informacionHecho),
        ReferenciaUbicacion = Limpiar(referenciaUbicacion),
    };

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
