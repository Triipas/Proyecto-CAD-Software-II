using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// JWT firmado con HS256 y vida configurable, sin token de refresco (Plan de Arquitectura, 9.6).
public sealed class EmisorTokens(IOptions<OpcionesJwt> opciones, TimeProvider reloj)
{
    public (string Token, DateTimeOffset ExpiraEn) Emitir(Usuario usuario, string rol)
    {
        var jwt = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var minutos = rol == Roles.Ciudadano && jwt.DuracionMinutosCiudadano > 0 ? jwt.DuracionMinutosCiudadano : jwt.DuracionMinutos;
        var expira = ahora.AddMinutes(minutos);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emisor,
            Audience = jwt.Audiencia,
            Subject = Autenticacion.CrearIdentidad(usuario, rol, "jwt"),
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = expira.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.ObtenerClave(), SecurityAlgorithms.HmacSha256),
        });

        return (token, expira);
    }
}
