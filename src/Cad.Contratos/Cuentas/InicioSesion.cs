namespace Cad.Contratos.Cuentas;

// HU02: inicio de sesión de la app móvil (modos ciudadano y sereno).
public sealed record SolicitudInicioSesion(string Correo, string Contrasena);

// DebeCambiarContrasena: el sereno entró con la contraseña provisional del administrador; la app le pide cambiarla antes
// de operar (el servidor rechaza sus demás solicitudes hasta entonces).
public sealed record RespuestaToken(string Token, DateTimeOffset ExpiraEn, string Rol, bool DebeCambiarContrasena = false);

// Cambio de la contraseña propia del sereno (22/10/2026). Responde con un token nuevo: el anterior deja de valer.
public sealed record SolicitudCambioContrasena(string Actual, string Nueva, string Confirmacion);

// Datos mínimos de la cuenta autenticada; la app elige su modo según el rol.
public sealed record PerfilUsuario(Guid Id, string Nombres, string Apellidos, string Rol);
