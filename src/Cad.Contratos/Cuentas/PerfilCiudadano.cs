namespace Cad.Contratos.Cuentas;

// ADR-08, fase D — «Mis datos» del ciudadano. Desde el registro único (06/11/2026) el documento, la fecha de nacimiento y el
// contacto de emergencia se piden al crear la cuenta; la dirección sigue siendo opcional. Las cuentas anteriores los completan
// al entrar (DatosCompletos = false).

public sealed record PerfilCiudadanoVista(
    string Nombres,
    string Apellidos,
    string Celular,
    bool CelularVerificado,
    string? TipoDocumento,
    string? NumeroDocumento,
    DateOnly? FechaNacimiento,
    string? Direccion,
    string? ContactoNombre,
    string? ContactoParentesco,
    string? ContactoTelefono,
    string? TerminosVersion,
    DateTimeOffset? TerminosAceptadosEn,
    // Las cuentas creadas antes de ADR-08 entran con correo y contraseña y no tienen PIN.
    bool TienePin)
{
    // Tiene todos los datos obligatorios del registro único (la dirección es opcional).
    public bool DatosCompletos => NumeroDocumento is not null && FechaNacimiento is not null && ContactoTelefono is not null;
}

// Mismas reglas que el registro: solo la dirección puede ir vacía.
public sealed record SolicitudPerfilCiudadano(
    string Nombres,
    string Apellidos,
    string? TipoDocumento,
    string? NumeroDocumento,
    DateOnly? FechaNacimiento,
    string? Direccion,
    string? ContactoNombre,
    string? ContactoParentesco,
    string? ContactoTelefono)
{
    public DatosCiudadanoEscritos Datos => new(Nombres, Apellidos, TipoDocumento, NumeroDocumento, FechaNacimiento, Direccion,
        ContactoNombre, ContactoParentesco, ContactoTelefono);
}

public sealed record SolicitudCambioPin(string PinActual, string PinNuevo);

// Listas cerradas compartidas por la app y el servidor.
public static class DatosCiudadano
{
    public const string Dni = "DNI";
    public const string CarneExtranjeria = "CE";
    public const string Pasaporte = "Pasaporte";

    public static readonly string[] TiposDocumento = [Dni, CarneExtranjeria, Pasaporte];

    public static string NombreTipo(string tipo) => tipo switch
    {
        Dni => "DNI",
        CarneExtranjeria => "Carné de extranjería",
        Pasaporte => "Pasaporte",
        _ => tipo,
    };

    public static readonly string[] Parentescos =
        ["Madre", "Padre", "Pareja", "Hijo o hija", "Hermano o hermana", "Otro familiar", "Amistad", "Vecino o vecina", "Otro"];

    public const int MaximoDireccion = 200;
    public const int MaximoNombreContacto = 100;
}
