using System.Text.RegularExpressions;

namespace Cad.Contratos.Cuentas;

// Datos del ciudadano tal como se escriben en el formulario de registro o en «Mis datos».
public sealed record DatosCiudadanoEscritos(
    string? Nombres,
    string? Apellidos,
    string? TipoDocumento,
    string? NumeroDocumento,
    DateOnly? FechaNacimiento,
    string? Direccion,
    string? ContactoNombre,
    string? ContactoParentesco,
    string? ContactoTelefono);

// Los mismos datos, limpios y validados.
public sealed record DatosCiudadanoValidos(
    string Nombres,
    string Apellidos,
    string TipoDocumento,
    string NumeroDocumento,
    DateOnly FechaNacimiento,
    string? Direccion,
    string ContactoNombre,
    string ContactoParentesco,
    string ContactoTelefono);

// Registro único (revisión del 06/11/2026): la central necesita todos los datos para actuar, así que se piden una sola vez, al
// crear la cuenta. Son obligatorios nombres, apellidos, documento, fecha de nacimiento y contacto de emergencia; la dirección
// es opcional. La app valida con esta misma regla antes de enviar y el servidor la vuelve a aplicar.
public static partial class ValidacionDatosCiudadano
{
    public const int MaximoNombre = 100;
    public const string MensajeCelular = "Escribe un celular peruano de 9 dígitos que empiece por 9.";

    // Devuelve los datos limpios, o null si hay errores (quedan en «errores», por campo).
    public static DatosCiudadanoValidos? Validar(Dictionary<string, string[]> errores, DatosCiudadanoEscritos d, DateOnly hoy,
        string? celularPropio)
    {
        var nombres = Limpiar(d.Nombres);
        var apellidos = Limpiar(d.Apellidos);
        ValidarNombre(errores, "nombres", "tus nombres", nombres);
        ValidarNombre(errores, "apellidos", "tus apellidos", apellidos);

        var (tipo, numero) = ValidarDocumento(errores, d.TipoDocumento, d.NumeroDocumento);

        if (d.FechaNacimiento is not { } nacimiento)
            errores["fechaNacimiento"] = ["Escribe tu fecha de nacimiento."];
        else if (nacimiento >= hoy || nacimiento < hoy.AddYears(-120))
            errores["fechaNacimiento"] = ["Escribe una fecha de nacimiento válida."];

        var direccion = Limpiar(d.Direccion);
        if (direccion?.Length > DatosCiudadano.MaximoDireccion)
            errores["direccion"] = [$"La dirección no puede superar {DatosCiudadano.MaximoDireccion} caracteres."];

        var (contactoNombre, parentesco, contactoTelefono) = ValidarContacto(errores, d, celularPropio);

        return errores.Count > 0
            ? null
            : new DatosCiudadanoValidos(nombres!, apellidos!, tipo!, numero!, d.FechaNacimiento!.Value, direccion,
                contactoNombre!, parentesco!, contactoTelefono!);
    }

    // Quita espacios, guiones y el prefijo +51.
    public static string NormalizarCelular(string? valor)
    {
        var digitos = new string((valor ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 11 && digitos.StartsWith("51", StringComparison.Ordinal) ? digitos[2..] : digitos;
    }

    public static bool EsCelularValido(string celularNormalizado) => Celular().IsMatch(celularNormalizado);

    private static void ValidarNombre(Dictionary<string, string[]> errores, string campo, string que, string? valor)
    {
        if (valor is null)
            errores[campo] = [$"Escribe {que}."];
        else if (valor.Length > MaximoNombre)
            errores[campo] = [$"Máximo {MaximoNombre} caracteres."];
    }

    // DNI: 8 dígitos. Carné de extranjería: 9 a 12 caracteres alfanuméricos. Pasaporte: 6 a 12.
    private static (string? Tipo, string? Numero) ValidarDocumento(Dictionary<string, string[]> errores, string? tipoEscrito, string? numeroEscrito)
    {
        var tipo = Limpiar(tipoEscrito);
        var numero = Limpiar(numeroEscrito)?.Replace(" ", "").ToUpperInvariant();
        if (tipo is null || !DatosCiudadano.TiposDocumento.Contains(tipo))
        {
            errores["tipoDocumento"] = ["Elige el tipo de documento."];
            return (null, null);
        }
        if (numero is null)
        {
            errores["numeroDocumento"] = ["Escribe el número de tu documento."];
            return (null, null);
        }
        var valido = tipo switch
        {
            DatosCiudadano.Dni => Dni().IsMatch(numero),
            DatosCiudadano.CarneExtranjeria => Alfanumerico().IsMatch(numero) && numero.Length is >= 9 and <= 12,
            _ => Alfanumerico().IsMatch(numero) && numero.Length is >= 6 and <= 12,
        };
        if (!valido)
            errores["numeroDocumento"] = [tipo switch
            {
                DatosCiudadano.Dni => "El DNI tiene 8 dígitos.",
                DatosCiudadano.CarneExtranjeria => "El carné de extranjería tiene de 9 a 12 letras o números.",
                _ => "El pasaporte tiene de 6 a 12 letras o números.",
            }];
        return (tipo, numero);
    }

    private static (string? Nombre, string? Parentesco, string? Telefono) ValidarContacto(
        Dictionary<string, string[]> errores, DatosCiudadanoEscritos d, string? celularPropio)
    {
        var nombre = Limpiar(d.ContactoNombre);
        var parentesco = Limpiar(d.ContactoParentesco);
        var telefono = Limpiar(d.ContactoTelefono) is { } t ? NormalizarCelular(t) : null;

        if (nombre is null)
            errores["contactoNombre"] = ["Escribe el nombre de tu contacto de emergencia."];
        else if (nombre.Length > DatosCiudadano.MaximoNombreContacto)
            errores["contactoNombre"] = [$"El nombre no puede superar {DatosCiudadano.MaximoNombreContacto} caracteres."];
        if (parentesco is null || !DatosCiudadano.Parentescos.Contains(parentesco))
            errores["contactoParentesco"] = ["Elige tu relación con el contacto."];
        if (telefono is null || !EsCelularValido(telefono))
            errores["contactoTelefono"] = [MensajeCelular];
        else if (celularPropio is not null && telefono == NormalizarCelular(celularPropio))
            errores["contactoTelefono"] = ["El contacto de emergencia debe tener un número distinto del tuyo."];
        return (nombre, parentesco, telefono);
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex Dni();

    [GeneratedRegex(@"^[A-Z0-9]+$")]
    private static partial Regex Alfanumerico();

    [GeneratedRegex(@"^9\d{8}$")]
    private static partial Regex Celular();
}
