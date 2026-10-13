using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// Cada rol entra por su interfaz (Plan de Arquitectura, Tabla 3):
// la app móvil para ciudadano y sereno; el panel web para operador y administrador.
public enum Canal { App, Panel }

public abstract record ResultadoInicioSesion
{
    public sealed record Exito(Usuario Usuario, string Rol) : ResultadoInicioSesion;
    public sealed record CredencialesIncorrectas : ResultadoInicioSesion;
    public sealed record CuentaDesactivada : ResultadoInicioSesion;
    public sealed record CanalNoPermitido(string Rol) : ResultadoInicioSesion;
}

// HU02 — Iniciar sesión (S1-T07). Comparte el hash de HU01 a través de UserManager.
public sealed class IniciarSesion(UserManager<Usuario> usuarios)
{
    private static readonly Dictionary<Canal, string[]> RolesPorCanal = new()
    {
        [Canal.App] = [Roles.Ciudadano, Roles.Sereno],
        [Canal.Panel] = [Roles.Operador, Roles.Administrador],
    };

    public async Task<ResultadoInicioSesion> EjecutarAsync(string? correo, string? contrasena, Canal canal)
    {
        if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrEmpty(contrasena))
            return new ResultadoInicioSesion.CredencialesIncorrectas();

        var usuario = await usuarios.FindByEmailAsync(ReglasCuenta.NormalizarCorreo(correo));
        if (usuario is null || !await usuarios.CheckPasswordAsync(usuario, contrasena))
            return new ResultadoInicioSesion.CredencialesIncorrectas();

        // Solo se informa de la desactivación a quien ya demostró conocer la contraseña (HU02-CA03).
        if (!usuario.Activo)
            return new ResultadoInicioSesion.CuentaDesactivada();

        var rol = (await usuarios.GetRolesAsync(usuario)).SingleOrDefault();
        if (rol is null)
            return new ResultadoInicioSesion.CredencialesIncorrectas();

        return RolesPorCanal[canal].Contains(rol)
            ? new ResultadoInicioSesion.Exito(usuario, rol)
            : new ResultadoInicioSesion.CanalNoPermitido(rol);
    }

    // El mismo mensaje para correo inexistente y contraseña errónea: no revela qué cuentas existen.
    public static string Mensaje(ResultadoInicioSesion resultado) => resultado switch
    {
        ResultadoInicioSesion.CuentaDesactivada => "La cuenta está desactivada.",
        ResultadoInicioSesion.CanalNoPermitido { Rol: Roles.Ciudadano or Roles.Sereno } =>
            "Esta cuenta debe ingresar desde la aplicación móvil.",
        ResultadoInicioSesion.CanalNoPermitido => "Esta cuenta debe ingresar desde el panel de central.",
        _ => "Correo o contraseña incorrectos.",
    };
}
