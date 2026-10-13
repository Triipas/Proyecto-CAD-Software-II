using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// DP02 (supuesto aprobado el 12/10/2026): el administrador inicial se crea al arrancar a partir de
// Admin:Email y Admin:ClaveInicial (variables de entorno o user-secrets), solo para la validación.
// Si ya existe una cuenta con ese correo no se modifica.
public sealed class SembrarAdministrador(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    TimeProvider reloj,
    IConfiguration configuracion,
    ILogger<SembrarAdministrador> registro)
{
    public async Task EjecutarAsync()
    {
        var correo = configuracion["Admin:Email"];
        var clave = configuracion["Admin:ClaveInicial"];
        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrEmpty(clave))
        {
            registro.LogWarning("Admin:Email o Admin:ClaveInicial no están configurados; no se crea el administrador inicial.");
            return;
        }

        correo = RegistrarCiudadano.NormalizarCorreo(correo);
        if (await usuarios.FindByEmailAsync(correo) is not null)
            return;

        var administrador = new Usuario
        {
            Id = Guid.CreateVersion7(),
            UserName = correo,
            Email = correo,
            Nombres = "Administrador",
            Apellidos = "Inicial",
            Activo = true,
            CreadoEn = reloj.GetUtcNow(),
        };

        await using var transaccion = await db.Database.BeginTransactionAsync();
        var creacion = await usuarios.CreateAsync(administrador, clave);
        if (!creacion.Succeeded)
            throw new InvalidOperationException(
                "No se pudo crear el administrador inicial: " + string.Join(" ", creacion.Errors.Select(e => e.Code)));
        var asignacion = await usuarios.AddToRoleAsync(administrador, Roles.Administrador);
        if (!asignacion.Succeeded)
            throw new InvalidOperationException("No se pudo asignar el rol Administrador.");
        await transaccion.CommitAsync();

        registro.LogInformation("Administrador inicial creado con el identificador {UsuarioId}.", administrador.Id);
    }
}
