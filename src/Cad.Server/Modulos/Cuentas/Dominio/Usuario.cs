using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas.Dominio;

// Cuenta de cualquiera de los cuatro roles. El correo es el identificador de acceso
// (UserName = Email) y el teléfono se guarda en PhoneNumber de Identity.
public class Usuario : IdentityUser<Guid>
{
    public required string Nombres { get; set; }
    public required string Apellidos { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}
