namespace Cad.Contratos.Cuentas;

// HU02: inicio de sesión de la app móvil (modos ciudadano y sereno).
public sealed record SolicitudInicioSesion(string Correo, string Contrasena);

public sealed record RespuestaToken(string Token, DateTimeOffset ExpiraEn, string Rol);

// Datos mínimos de la cuenta autenticada; la app elige su modo según el rol.
public sealed record PerfilUsuario(Guid Id, string Nombres, string Apellidos, string Rol);
