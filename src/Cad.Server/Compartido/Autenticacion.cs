using System.Security.Claims;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Cad.Server.Compartido;

public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    public string Emisor { get; set; } = "";
    public string Audiencia { get; set; } = "";
    // Base64 de al menos 32 bytes (256 bits). Nunca en el repositorio: user-secrets o Jwt__ClaveFirma.
    public string ClaveFirma { get; set; } = "";
    public int DuracionMinutos { get; set; }

    public SymmetricSecurityKey ObtenerClave()
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(ClaveFirma); }
        catch (FormatException) { bytes = []; }

        if (bytes.Length < 32)
            throw new InvalidOperationException(
                "Falta 'Jwt:ClaveFirma' o no es Base64 de al menos 32 bytes. Consulta la sección «Secretos locales» del README.");
        return new SymmetricSecurityKey(bytes);
    }
}

// Panel (navegador): cookie HttpOnly. App móvil: JWT Bearer. Las rutas /api aceptan ambos
// para que el JavaScript del panel pueda llamarlas con su cookie (Plan de Arquitectura, Tabla 15).
public static class Autenticacion
{
    public const string EsquemaMixto = "CadMixto";
    public const string ReclamoId = "sub";
    public const string ReclamoRol = "role";
    public const string ReclamoNombre = "name";
    // DP03 (16/10/2026): el sello de seguridad de la cuenta se comprueba en cada solicitud; al desactivar la cuenta (o
    // cambiar su contraseña) cambia el sello y la sesión abierta deja de valer.
    public const string ReclamoSello = "cad:sello";

    public static IServiceCollection AddAutenticacionCad(this IServiceCollection services, IConfiguration configuracion, bool desarrollo)
    {
        var jwt = configuracion.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>() ?? new OpcionesJwt();
        var clave = jwt.ObtenerClave(); // falla al arrancar si no está configurada
        services.Configure<OpcionesJwt>(configuracion.GetSection(OpcionesJwt.Seccion));

        services
            .AddAuthentication(EsquemaMixto)
            .AddPolicyScheme(EsquemaMixto, "Cookie o JWT", opciones =>
            {
                opciones.ForwardDefaultSelector = contexto =>
                    contexto.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(opciones =>
            {
                opciones.Cookie.Name = "cad.panel";
                opciones.Cookie.HttpOnly = true;
                opciones.Cookie.SameSite = SameSiteMode.Strict;
                // En desarrollo local se usa http; en el entorno publicado la cookie solo viaja por HTTPS (RNF09).
                opciones.Cookie.SecurePolicy = desarrollo ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                opciones.ExpireTimeSpan = TimeSpan.FromMinutes(configuracion.GetValue("Panel:DuracionSesionMinutos", 480));
                opciones.SlidingExpiration = false;
                opciones.LoginPath = "/panel/ingresar";
                opciones.AccessDeniedPath = "/panel/acceso-denegado";
                // Una solicitud a /api o a un hub nunca se redirige a una página: responde 401/403.
                opciones.Events.OnRedirectToLogin = contexto => ResponderORedirigir(contexto, StatusCodes.Status401Unauthorized);
                opciones.Events.OnRedirectToAccessDenied = contexto => ResponderORedirigir(contexto, StatusCodes.Status403Forbidden);
                opciones.Events.OnValidatePrincipal = async contexto =>
                {
                    if (!await SesionVigenteAsync(contexto.HttpContext, contexto.Principal))
                    {
                        contexto.RejectPrincipal();
                        await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            })
            .AddJwtBearer(opciones =>
            {
                opciones.MapInboundClaims = false;
                opciones.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Emisor,
                    ValidAudience = jwt.Audiencia,
                    IssuerSigningKey = clave,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ReclamoId,
                    RoleClaimType = ReclamoRol,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                opciones.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async contexto =>
                    {
                        if (!await SesionVigenteAsync(contexto.HttpContext, contexto.Principal))
                            contexto.Fail("La sesión ya no es válida.");
                    },
                };
            });

        // Las políticas y la denegación por defecto se registran en Autorizacion.AddAutorizacionCad.
        return services;
    }

    public static ClaimsIdentity CrearIdentidad(Usuario usuario, string rol, string esquema) => new(
        [
            new Claim(ReclamoId, usuario.Id.ToString()),
            new Claim(ReclamoRol, rol),
            new Claim(ReclamoNombre, $"{usuario.Nombres} {usuario.Apellidos}"),
            new Claim(ReclamoSello, usuario.SecurityStamp ?? ""),
        ],
        esquema, ReclamoId, ReclamoRol);

    public static Guid IdUsuario(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ReclamoId) ?? throw new InvalidOperationException("Sesión sin identificador."));

    // La cuenta existe, está activa y conserva el sello con el que se abrió la sesión (DP03).
    private static async Task<bool> SesionVigenteAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        if (principal?.FindFirstValue(ReclamoId) is not { } id || !Guid.TryParse(id, out var usuarioId))
            return false;
        var sello = principal.FindFirstValue(ReclamoSello) ?? "";
        var db = http.RequestServices.GetRequiredService<Persistencia.CadDbContext>();
        var cuenta = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId)
            .Select(u => new { u.Activo, u.SecurityStamp }).SingleOrDefaultAsync(http.RequestAborted);
        return cuenta is { Activo: true } && (cuenta.SecurityStamp ?? "") == sello;
    }

    private static Task ResponderORedirigir(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> contexto, int estado)
    {
        if (contexto.Request.Path.StartsWithSegments("/api") || contexto.Request.Path.StartsWithSegments("/hubs"))
            contexto.Response.StatusCode = estado;
        else
            contexto.Response.Redirect(contexto.RedirectUri);
        return Task.CompletedTask;
    }
}
