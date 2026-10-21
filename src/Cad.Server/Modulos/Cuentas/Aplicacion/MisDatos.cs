using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

public abstract record ResultadoMisDatos
{
    public sealed record Correcto(PerfilCiudadanoVista Perfil) : ResultadoMisDatos;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoMisDatos;
    public sealed record DocumentoDuplicado : ResultadoMisDatos;
}

public abstract record ResultadoCambioPin
{
    public sealed record Correcto(Usuario Usuario) : ResultadoCambioPin;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCambioPin;
    public sealed record SinPin : ResultadoCambioPin;
    public sealed record PinIncorrecto(int IntentosRestantes) : ResultadoCambioPin;
    public sealed record PinBloqueado : ResultadoCambioPin;
}

// ADR-08, fase D — «Mis datos» del ciudadano (todo opcional salvo nombres y apellidos) y cambio de PIN.
public sealed partial class MisDatosCiudadano(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    IPasswordHasher<Usuario> hasher,
    TimeProvider reloj,
    IConfiguration configuracion)
{
    public async Task<PerfilCiudadanoVista?> ConsultarAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is null)
            return null;
        var perfil = await db.PerfilesCiudadano.AsNoTracking().SingleOrDefaultAsync(p => p.UsuarioId == usuarioId, ct);
        return Vista(usuario, perfil);
    }

    public async Task<ResultadoMisDatos> ActualizarAsync(Guid usuarioId, SolicitudPerfilCiudadano s, CancellationToken ct)
    {
        var usuario = (await usuarios.FindByIdAsync(usuarioId.ToString()))!;
        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", s.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", s.Apellidos);

        var (tipo, numero) = ValidarDocumento(errores, s.TipoDocumento, s.NumeroDocumento);
        var nacimiento = ValidarNacimiento(errores, s.FechaNacimiento);
        var direccion = Limpiar(s.Direccion);
        if (direccion?.Length > DatosCiudadano.MaximoDireccion)
            errores["direccion"] = [$"La dirección no puede superar {DatosCiudadano.MaximoDireccion} caracteres."];
        var (contactoNombre, parentesco, contactoTelefono) = ValidarContacto(errores, s, usuario.PhoneNumber);
        if (errores.Count > 0)
            return new ResultadoMisDatos.Invalido(errores);

        if (numero is not null && await db.PerfilesCiudadano.AnyAsync(
                p => p.UsuarioId != usuarioId && p.TipoDocumento == tipo && p.NumeroDocumento == numero, ct))
            return new ResultadoMisDatos.DocumentoDuplicado();

        var perfil = await db.PerfilesCiudadano.SingleOrDefaultAsync(p => p.UsuarioId == usuarioId, ct);
        if (perfil is null)
        {
            perfil = PerfilCiudadano.Nuevo(usuarioId);
            db.PerfilesCiudadano.Add(perfil);
        }
        perfil.Actualizar(tipo, numero, nacimiento, direccion, contactoNombre, parentesco, contactoTelefono, reloj.GetUtcNow());
        usuario.Nombres = s.Nombres.Trim();
        usuario.Apellidos = s.Apellidos.Trim();

        try
        {
            // UpdateAsync guarda también el perfil (mismo contexto de datos): una sola escritura.
            var actualizacion = await usuarios.UpdateAsync(usuario);
            if (!actualizacion.Succeeded)
                throw new InvalidOperationException("Identity rechazó la actualización de los datos del ciudadano.");
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            return new ResultadoMisDatos.DocumentoDuplicado();
        }
        return new ResultadoMisDatos.Correcto(Vista(usuario, perfil));
    }

    public async Task<ResultadoCambioPin> CambiarPinAsync(Guid usuarioId, SolicitudCambioPin s)
    {
        var usuario = (await usuarios.FindByIdAsync(usuarioId.ToString()))!;
        if (usuario.PinHash is null)
            return new ResultadoCambioPin.SinPin();
        if (usuario.PinBloqueado)
            return new ResultadoCambioPin.PinBloqueado();

        if (string.IsNullOrEmpty(s.PinActual)
            || hasher.VerifyHashedPassword(usuario, usuario.PinHash, s.PinActual) == PasswordVerificationResult.Failed)
        {
            usuario.IntentosPinFallidos++;
            await usuarios.UpdateAsync(usuario);
            return usuario.PinBloqueado
                ? new ResultadoCambioPin.PinBloqueado()
                : new ResultadoCambioPin.PinIncorrecto(Usuario.MaximoIntentosPin - usuario.IntentosPinFallidos);
        }

        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarPin(errores, "pinNuevo", s.PinNuevo);
        if (errores.Count == 0 && s.PinNuevo == s.PinActual)
            errores["pinNuevo"] = ["El PIN nuevo debe ser distinto del actual."];
        if (errores.Count > 0)
            return new ResultadoCambioPin.Invalido(errores);

        usuario.PinHash = hasher.HashPassword(usuario, s.PinNuevo);
        usuario.IntentosPinFallidos = 0;
        // Sello nuevo: cualquier otra sesión abierta deja de valer; quien cambió el PIN recibe un token nuevo.
        await usuarios.UpdateSecurityStampAsync(usuario);
        return new ResultadoCambioPin.Correcto(usuario);
    }

    private static PerfilCiudadanoVista Vista(Usuario u, PerfilCiudadano? p) => new(
        u.Nombres, u.Apellidos, u.PhoneNumber ?? "", u.PhoneNumberConfirmed,
        p?.TipoDocumento, p?.NumeroDocumento, p?.FechaNacimiento, p?.Direccion,
        p?.ContactoNombre, p?.ContactoParentesco, p?.ContactoTelefono,
        u.TerminosVersion, u.TerminosAceptadosEn, u.PinHash is not null);

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // DNI: 8 dígitos. Carné de extranjería: 9 a 12 caracteres alfanuméricos. Pasaporte: 6 a 12.
    private static (string? Tipo, string? Numero) ValidarDocumento(Dictionary<string, string[]> errores, string? tipoEscrito, string? numeroEscrito)
    {
        var tipo = Limpiar(tipoEscrito);
        var numero = Limpiar(numeroEscrito)?.Replace(" ", "").ToUpperInvariant();
        if (tipo is null && numero is null)
            return (null, null);
        if (tipo is null || !DatosCiudadano.TiposDocumento.Contains(tipo))
        {
            errores["tipoDocumento"] = ["Elige el tipo de documento."];
            return (null, null);
        }
        if (numero is null)
        {
            errores["numeroDocumento"] = ["Escribe el número de documento o quita el tipo."];
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

    // Solo se comprueba que sea una fecha posible; la edad mínima para registrarse sigue pendiente del PO (DP-C2).
    private DateOnly? ValidarNacimiento(Dictionary<string, string[]> errores, DateOnly? fecha)
    {
        if (fecha is not { } f)
            return null;
        var zona = TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime);
        if (f >= hoy || f < hoy.AddYears(-120))
            errores["fechaNacimiento"] = ["Escribe una fecha de nacimiento válida."];
        return f;
    }

    private static (string? Nombre, string? Parentesco, string? Telefono) ValidarContacto(
        Dictionary<string, string[]> errores, SolicitudPerfilCiudadano s, string? celularPropio)
    {
        var nombre = Limpiar(s.ContactoNombre);
        var parentesco = Limpiar(s.ContactoParentesco);
        var telefono = Limpiar(s.ContactoTelefono) is { } t ? ReglasCuenta.NormalizarCelular(t) : null;
        if (nombre is null && parentesco is null && telefono is null)
            return (null, null, null);

        if (nombre is null)
            errores["contactoNombre"] = ["Escribe el nombre de tu contacto o deja vacíos todos sus datos."];
        else if (nombre.Length > DatosCiudadano.MaximoNombreContacto)
            errores["contactoNombre"] = [$"El nombre no puede superar {DatosCiudadano.MaximoNombreContacto} caracteres."];
        if (parentesco is null || !DatosCiudadano.Parentescos.Contains(parentesco))
            errores["contactoParentesco"] = ["Elige tu relación con el contacto."];
        if (telefono is null || !ReglasCuenta.EsCelularValido(telefono))
            errores["contactoTelefono"] = [ReglasCuenta.MensajeCelular];
        else if (telefono == celularPropio)
            errores["contactoTelefono"] = ["El contacto de emergencia debe tener un número distinto del tuyo."];
        return (nombre, parentesco, telefono);
    }

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex Dni();

    [GeneratedRegex(@"^[A-Z0-9]+$")]
    private static partial Regex Alfanumerico();
}
