namespace Cad.Contratos.Cuentas;

// ADR-08: registro e ingreso del ciudadano con celular verificado por código y PIN de 6 dígitos.

public sealed record SolicitudCodigoCelular(string Celular);

// CodigoDePrueba solo llega con el proveedor simulado en desarrollo, para que la app Debug lo muestre.
public sealed record RespuestaCodigoCelular(int ReenviarEnSegundos, int ExpiraEnSegundos, string? CodigoDePrueba);

public sealed record SolicitudVerificacionCelular(string Celular, string Codigo);

// PuedeRegistrarDeNuevo: solo en desarrollo (DP25), un número con cuenta se puede volver a registrar.
public sealed record RespuestaVerificacionCelular(string TokenVerificacion, bool CuentaExiste, bool PuedeRegistrarDeNuevo);

public sealed record SolicitudRegistroCelular(string TokenVerificacion, string Nombres, string Apellidos, string Pin, bool AceptaTerminos);

// Desde el teléfono vinculado basta la llave del dispositivo; desde otro teléfono hace falta un token de verificación.
public sealed record SolicitudIngresoCelular(string Celular, string Pin, string? LlaveDispositivo, string? TokenVerificacion);

public sealed record SolicitudRestablecerPin(string TokenVerificacion, string PinNuevo);

// LlaveDispositivo llega cuando este teléfono se acaba de vincular; la app la guarda en su almacenamiento seguro.
public sealed record RespuestaIngresoCelular(string Token, DateTimeOffset ExpiraEn, string Rol, string? LlaveDispositivo);
