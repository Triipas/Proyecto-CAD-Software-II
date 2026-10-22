using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Dominio;

// Cuenta de cualquiera de los cuatro roles. El personal entra con su correo (UserName = Email) y el teléfono
// se guarda en PhoneNumber de Identity. El ciudadano entra con su celular verificado y un PIN (ADR-08):
// UserName = celular, sin correo ni contraseña.
public class Usuario : IdentityUser<Guid>
{
    public required string Nombres { get; set; }
    public required string Apellidos { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }

    // ADR-08: PIN de 6 dígitos con el mismo hash que las contraseñas; nunca se guarda ni se registra en claro.
    public string? PinHash { get; set; }
    // Tras MaximoIntentosPin fallos seguidos, el PIN solo se puede restablecer con un código nuevo.
    public int IntentosPinFallidos { get; set; }

    // Versión de los términos que aceptó al registrarse y cuándo (prueba del consentimiento, Ley 29733).
    public string? TerminosVersion { get; set; }
    public DateTimeOffset? TerminosAceptadosEn { get; set; }

    // Personal (operador y sereno, 22/10/2026): DNI para identificarlo sin ambigüedad en la auditoría y código de personal
    // que genera el sistema (OP-001, S-001). Nulos en ciudadanos y en cuentas de personal anteriores sin completar.
    public string? Dni { get; set; }
    public string? CodigoPersonal { get; set; }

    // La contraseña la fijó el administrador (alta o restablecimiento): hasta cambiarla, la cuenta no puede operar.
    public bool DebeCambiarContrasena { get; set; }

    public const int MaximoIntentosPin = 5;
    public bool PinBloqueado => IntentosPinFallidos >= MaximoIntentosPin;
}
