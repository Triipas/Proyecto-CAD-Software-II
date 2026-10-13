using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// Reglas de datos de cuenta compartidas por el registro de ciudadano (HU01) y las cuentas
// institucionales (HU04). DP01 (validada): correo como identificador de acceso; teléfono móvil
// peruano de 9 dígitos; contraseña de 8 a 128 caracteres con al menos una letra y un número.
public static partial class ReglasCuenta
{
    public const int LongitudMaximaNombre = 100;
    public const int LongitudMaximaCorreo = 256;
    public const int LongitudMinimaContrasena = 8;
    public const int LongitudMaximaContrasena = 128;

    public const string MensajeContrasena =
        "La contraseña debe tener entre 8 y 128 caracteres e incluir al menos una letra y un número.";
    public const string MensajeCorreoInvalido = "El correo electrónico no tiene un formato válido.";

    public static string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    public static void ValidarNombres(Dictionary<string, string[]> errores, string campo, string etiqueta, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            errores[campo] = [$"{etiqueta} son obligatorios."];
        else if (valor.Trim().Length > LongitudMaximaNombre)
            errores[campo] = [$"{etiqueta} no pueden superar {LongitudMaximaNombre} caracteres."];
    }

    public static void ValidarCorreo(Dictionary<string, string[]> errores, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            errores["correo"] = ["El correo electrónico es obligatorio."];
        else if (!EsCorreoValido(NormalizarCorreo(valor)))
            errores["correo"] = [MensajeCorreoInvalido];
    }

    public static void ValidarTelefono(Dictionary<string, string[]> errores, string? valor, bool obligatorio)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            if (obligatorio)
                errores["telefono"] = ["El teléfono móvil es obligatorio."];
        }
        else if (!TelefonoMovilPeru().IsMatch(valor.Trim()))
        {
            errores["telefono"] = ["El teléfono móvil debe tener 9 dígitos y empezar por 9."];
        }
    }

    public static void ValidarContrasena(Dictionary<string, string[]> errores, string campo, string? valor, bool obligatoria)
    {
        if (string.IsNullOrEmpty(valor))
        {
            if (obligatoria)
                errores[campo] = ["La contraseña es obligatoria."];
        }
        else if (valor.Length is < LongitudMinimaContrasena or > LongitudMaximaContrasena
                 || !valor.Any(char.IsLetter) || !valor.Any(char.IsDigit))
        {
            errores[campo] = [MensajeContrasena];
        }
    }

    public static string? NormalizarTelefono(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // Errores de Identity que significan «ese correo ya pertenece a otra cuenta».
    public static bool EsDuplicado(IdentityResult resultado) =>
        resultado.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");

    // La validación propia ya cubre estas reglas; esto solo evita un 500 si Identity discrepa.
    public static Dictionary<string, string[]> ErroresDeIdentity(IdentityResult resultado, string campoContrasena) =>
        resultado.Errors.Any(e => e.Code.StartsWith("Password"))
            ? new() { [campoContrasena] = [MensajeContrasena] }
            : new() { ["correo"] = [MensajeCorreoInvalido] };

    private static bool EsCorreoValido(string correo) =>
        correo.Length <= LongitudMaximaCorreo
        && MailAddress.TryCreate(correo, out var direccion)
        && direccion.Address == correo
        && direccion.Host.Contains('.');

    [GeneratedRegex(@"^9\d{8}$")]
    private static partial Regex TelefonoMovilPeru();
}
