using System.Net.Mail;
using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

public abstract record ResultadoRegistro
{
    public sealed record Creado(Guid Id) : ResultadoRegistro;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoRegistro;
    public sealed record Duplicado : ResultadoRegistro;
}

// HU01 — Registrar mi cuenta de ciudadano (S1-T05, S1-T06).
public sealed partial class RegistrarCiudadano(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    TimeProvider reloj)
{
    public const int LongitudMaximaNombre = 100;
    public const int LongitudMaximaCorreo = 256;
    public const int LongitudMinimaContrasena = 8;
    public const int LongitudMaximaContrasena = 128;

    public async Task<ResultadoRegistro> EjecutarAsync(SolicitudRegistroCiudadano solicitud, CancellationToken ct)
    {
        var errores = Validar(solicitud);
        if (errores.Count > 0)
            return new ResultadoRegistro.Invalido(errores);

        var correo = NormalizarCorreo(solicitud.Correo);

        // Comprobación previa para el caso habitual (CA04). La garantía bajo concurrencia (CA05)
        // la da el índice único de la base de datos, que se traduce más abajo a Duplicado.
        if (await usuarios.FindByEmailAsync(correo) is not null)
            return new ResultadoRegistro.Duplicado();

        var usuario = new Usuario
        {
            Id = Guid.CreateVersion7(),
            UserName = correo,
            Email = correo,
            Nombres = solicitud.Nombres.Trim(),
            Apellidos = solicitud.Apellidos.Trim(),
            PhoneNumber = solicitud.Telefono.Trim(),
            Activo = true,
            CreadoEn = reloj.GetUtcNow(),
        };

        // Usuario y rol en una sola transacción: si algo falla no queda una cuenta sin rol.
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var creacion = await usuarios.CreateAsync(usuario, solicitud.Contrasena);
            if (!creacion.Succeeded)
            {
                if (creacion.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                    return new ResultadoRegistro.Duplicado();
                // La validación propia ya cubre estas reglas; esto solo evita un 500 si Identity discrepa.
                return new ResultadoRegistro.Invalido(creacion.Errors.Any(e => e.Code.StartsWith("Password"))
                    ? new() { ["contrasena"] = [MensajeContrasena] }
                    : new() { ["correo"] = ["El correo electrónico no tiene un formato válido."] });
            }

            var asignacion = await usuarios.AddToRoleAsync(usuario, Roles.Ciudadano);
            if (!asignacion.Succeeded)
                throw new InvalidOperationException("No se pudo asignar el rol Ciudadano a la cuenta nueva.");

            await transaccion.CommitAsync(ct);
            return new ResultadoRegistro.Creado(usuario.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new ResultadoRegistro.Duplicado();
        }
    }

    public static string NormalizarCorreo(string correo) => correo.Trim().ToLowerInvariant();

    private const string MensajeContrasena =
        "La contraseña debe tener entre 8 y 128 caracteres e incluir al menos una letra y un número.";

    // DP01 (propuesta validada): nombres, apellidos, correo, teléfono móvil y contraseña son obligatorios.
    private static Dictionary<string, string[]> Validar(SolicitudRegistroCiudadano s)
    {
        var errores = new Dictionary<string, string[]>();

        ValidarTexto(errores, "nombres", "Los nombres", s.Nombres);
        ValidarTexto(errores, "apellidos", "Los apellidos", s.Apellidos);

        if (string.IsNullOrWhiteSpace(s.Correo))
            errores["correo"] = ["El correo electrónico es obligatorio."];
        else if (!EsCorreoValido(NormalizarCorreo(s.Correo)))
            errores["correo"] = ["El correo electrónico no tiene un formato válido."];

        if (string.IsNullOrWhiteSpace(s.Telefono))
            errores["telefono"] = ["El teléfono móvil es obligatorio."];
        else if (!TelefonoMovilPeru().IsMatch(s.Telefono.Trim()))
            errores["telefono"] = ["El teléfono móvil debe tener 9 dígitos y empezar por 9."];

        if (string.IsNullOrEmpty(s.Contrasena))
            errores["contrasena"] = ["La contraseña es obligatoria."];
        else if (s.Contrasena.Length is < LongitudMinimaContrasena or > LongitudMaximaContrasena
                 || !s.Contrasena.Any(char.IsLetter) || !s.Contrasena.Any(char.IsDigit))
            errores["contrasena"] = [MensajeContrasena];

        return errores;
    }

    private static void ValidarTexto(Dictionary<string, string[]> errores, string campo, string etiqueta, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            errores[campo] = [$"{etiqueta} son obligatorios."];
        else if (valor.Trim().Length > LongitudMaximaNombre)
            errores[campo] = [$"{etiqueta} no pueden superar {LongitudMaximaNombre} caracteres."];
    }

    private static bool EsCorreoValido(string correo) =>
        correo.Length <= LongitudMaximaCorreo
        && MailAddress.TryCreate(correo, out var direccion)
        && direccion.Address == correo
        && direccion.Host.Contains('.');

    [GeneratedRegex(@"^9\d{8}$")]
    private static partial Regex TelefonoMovilPeru();
}
