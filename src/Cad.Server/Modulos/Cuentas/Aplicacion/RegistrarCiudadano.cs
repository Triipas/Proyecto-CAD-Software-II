using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

public abstract record ResultadoRegistro
{
    public sealed record Creado(Guid Id) : ResultadoRegistro;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoRegistro;
    public sealed record Duplicado : ResultadoRegistro;
}

// HU01 — Registrar mi cuenta de ciudadano (S1-T05, S1-T06).
public sealed class RegistrarCiudadano(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    TimeProvider reloj)
{
    public async Task<ResultadoRegistro> EjecutarAsync(SolicitudRegistroCiudadano solicitud, CancellationToken ct)
    {
        var errores = Validar(solicitud);
        if (errores.Count > 0)
            return new ResultadoRegistro.Invalido(errores);

        var correo = ReglasCuenta.NormalizarCorreo(solicitud.Correo);

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
                return ReglasCuenta.EsDuplicado(creacion)
                    ? new ResultadoRegistro.Duplicado()
                    : new ResultadoRegistro.Invalido(ReglasCuenta.ErroresDeIdentity(creacion, "contrasena"));
            }

            var asignacion = await usuarios.AddToRoleAsync(usuario, Roles.Ciudadano);
            if (!asignacion.Succeeded)
                throw new InvalidOperationException("No se pudo asignar el rol Ciudadano a la cuenta nueva.");

            await transaccion.CommitAsync(ct);
            return new ResultadoRegistro.Creado(usuario.Id);
        }
        catch (DbUpdateException ex) when (ReglasCuenta.EsViolacionDeUnicidad(ex))
        {
            return new ResultadoRegistro.Duplicado();
        }
    }

    // DP01 (validada): nombres, apellidos, correo, teléfono móvil y contraseña son obligatorios.
    private static Dictionary<string, string[]> Validar(SolicitudRegistroCiudadano s)
    {
        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", s.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", s.Apellidos);
        ReglasCuenta.ValidarCorreo(errores, s.Correo);
        ReglasCuenta.ValidarTelefono(errores, s.Telefono, obligatorio: true);
        ReglasCuenta.ValidarContrasena(errores, "contrasena", s.Contrasena, obligatoria: true);
        return errores;
    }
}
