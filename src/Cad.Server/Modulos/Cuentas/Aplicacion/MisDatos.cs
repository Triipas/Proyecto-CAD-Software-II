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

// ADR-08, fase D — «Mis datos» del ciudadano (las reglas del registro único: solo la dirección es opcional) y cambio de PIN.
public sealed class MisDatosCiudadano(
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
        if (ValidacionDatosCiudadano.Validar(errores, s.Datos, Hoy(reloj, configuracion), usuario.PhoneNumber) is not { } d)
            return new ResultadoMisDatos.Invalido(errores);

        if (await DocumentoUsadoAsync(db, d, usuarioId, ct))
            return new ResultadoMisDatos.DocumentoDuplicado();

        var perfil = await db.PerfilesCiudadano.SingleOrDefaultAsync(p => p.UsuarioId == usuarioId, ct);
        if (perfil is null)
        {
            perfil = PerfilCiudadano.Nuevo(usuarioId);
            db.PerfilesCiudadano.Add(perfil);
        }
        Aplicar(perfil, d, reloj.GetUtcNow());
        usuario.Nombres = d.Nombres;
        usuario.Apellidos = d.Apellidos;

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

    public const string MensajeDocumentoDuplicado = "Ese documento ya está registrado en otra cuenta.";

    // La fecha de hoy en la zona horaria del panel (Lima), para validar la fecha de nacimiento.
    internal static DateOnly Hoy(TimeProvider reloj, IConfiguration configuracion)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime);
    }

    internal static Task<bool> DocumentoUsadoAsync(CadDbContext db, DatosCiudadanoValidos d, Guid? usuarioId, CancellationToken ct) =>
        db.PerfilesCiudadano.AnyAsync(p => p.UsuarioId != usuarioId && p.TipoDocumento == d.TipoDocumento
            && p.NumeroDocumento == d.NumeroDocumento, ct);

    internal static void Aplicar(PerfilCiudadano perfil, DatosCiudadanoValidos d, DateTimeOffset ahora) =>
        perfil.Actualizar(d.TipoDocumento, d.NumeroDocumento, d.FechaNacimiento, d.Direccion, d.ContactoNombre, d.ContactoParentesco,
            d.ContactoTelefono, ahora);

    public async Task<ResultadoCambioPin> CambiarPinAsync(Guid usuarioId, SolicitudCambioPin s)
    {
        var usuario = (await usuarios.FindByIdAsync(usuarioId.ToString()))!;
        if (usuario.PinHash is null)
            return new ResultadoCambioPin.SinPin();
        // Como al ingresar: el intento se cuenta de forma atómica antes de comprobar el PIN actual.
        if (!await IntentosPin.ReservarAsync(db, usuarioId, CancellationToken.None))
            return new ResultadoCambioPin.PinBloqueado();

        if (string.IsNullOrEmpty(s.PinActual)
            || hasher.VerifyHashedPassword(usuario, usuario.PinHash, s.PinActual) == PasswordVerificationResult.Failed)
        {
            var usados = await IntentosPin.UsadosAsync(db, usuarioId, CancellationToken.None);
            return usados >= Usuario.MaximoIntentosPin
                ? new ResultadoCambioPin.PinBloqueado()
                : new ResultadoCambioPin.PinIncorrecto(Usuario.MaximoIntentosPin - usados);
        }
        await IntentosPin.ReiniciarAsync(db, usuario, CancellationToken.None);

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

}
