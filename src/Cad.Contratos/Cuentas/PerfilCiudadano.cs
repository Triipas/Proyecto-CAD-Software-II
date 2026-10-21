namespace Cad.Contratos.Cuentas;

// ADR-08, fase D — «Mis datos» del ciudadano. Al registrarse solo se piden nombres y apellidos (y el celular verificado);
// documento, fecha de nacimiento, dirección y contacto de emergencia son opcionales y se completan después.

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
    // Datos opcionales completados (documento, nacimiento, dirección, contacto de emergencia).
    public int DatosCompletados =>
        (NumeroDocumento is null ? 0 : 1) + (FechaNacimiento is null ? 0 : 1) + (Direccion is null ? 0 : 1) + (ContactoTelefono is null ? 0 : 1);

    public const int DatosOpcionales = 4;
}

// Todos los campos opcionales pueden ir vacíos; el documento y el contacto se guardan completos o no se guardan.
public sealed record SolicitudPerfilCiudadano(
    string Nombres,
    string Apellidos,
    string? TipoDocumento,
    string? NumeroDocumento,
    DateOnly? FechaNacimiento,
    string? Direccion,
    string? ContactoNombre,
    string? ContactoParentesco,
    string? ContactoTelefono);

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
