namespace Cad.Contratos.Cuentas;

// HU01: datos del formulario de registro del ciudadano (DP01, propuesta validada el 12/10/2026).
// El correo electrónico es el identificador de acceso.
public sealed record SolicitudRegistroCiudadano(
    string Nombres,
    string Apellidos,
    string Correo,
    string Telefono,
    string Contrasena);

public sealed record RespuestaRegistroCiudadano(Guid Id);
