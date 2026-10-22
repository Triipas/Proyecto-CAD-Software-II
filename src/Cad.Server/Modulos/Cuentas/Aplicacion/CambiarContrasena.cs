using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

public abstract record ResultadoCambioContrasena
{
    // La cuenta con su sello nuevo: quien llama abre una sesión nueva (la anterior deja de valer).
    public sealed record Correcto(Usuario Usuario, string Rol) : ResultadoCambioContrasena;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCambioContrasena;
}

// Cambio de la contraseña propia (22/10/2026), obligatorio para el personal cuya contraseña fijó el administrador. Pide la
// actual, valida la nueva con las reglas de DP01 y exige que sea distinta. Cambiar la contraseña renueva el sello de
// seguridad, así que las sesiones abiertas con la provisional dejan de valer.
public sealed class CambiarContrasena(UserManager<Usuario> usuarios)
{
    public async Task<ResultadoCambioContrasena> EjecutarAsync(Guid usuarioId, string? actual, string? nueva, string? confirmacion)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(actual))
            errores["actual"] = ["Escribe tu contraseña actual."];
        ReglasCuenta.ValidarContrasena(errores, "nueva", nueva, obligatoria: true);
        if (!errores.ContainsKey("nueva") && nueva == actual)
            errores["nueva"] = ["La contraseña nueva debe ser distinta de la actual."];
        if (!errores.ContainsKey("nueva") && nueva != confirmacion)
            errores["confirmacion"] = ["Las contraseñas no coinciden."];
        if (errores.Count > 0)
            return new ResultadoCambioContrasena.Invalido(errores);

        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is null || !usuario.Activo || !await usuarios.CheckPasswordAsync(usuario, actual!))
            return new ResultadoCambioContrasena.Invalido(new() { ["actual"] = ["La contraseña actual no es correcta."] });

        // El indicador se guarda en la misma actualización que el hash nuevo.
        usuario.DebeCambiarContrasena = false;
        var cambio = await usuarios.ChangePasswordAsync(usuario, actual!, nueva!);
        if (!cambio.Succeeded)
            return new ResultadoCambioContrasena.Invalido(new() { ["nueva"] = [ReglasCuenta.MensajeContrasena] });

        var rol = (await usuarios.GetRolesAsync(usuario)).Single();
        return new ResultadoCambioContrasena.Correcto(usuario, rol);
    }
}
