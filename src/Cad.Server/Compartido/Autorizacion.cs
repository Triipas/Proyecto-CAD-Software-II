using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Authorization;

namespace Cad.Server.Compartido;

// HU03 — matriz de permisos (S1-T09). La autorización se decide siempre en el servidor:
// cada endpoint o página declara una política con nombre propio, y lo que no declara nada
// exige al menos una sesión (denegación por defecto). Matriz documentada en docs/matriz-permisos.md.
public static class Politicas
{
    public const string Ciudadano = "Ciudadano";
    public const string Sereno = "Sereno";
    public const string Administrador = "Administrador";

    // DP04 (delegada y registrada el 12/10/2026): «operador autorizado» es cualquier cuenta activa
    // con rol Operador. Tiene nombre propio para poder restringirla después sin tocar los endpoints
    // (HU33, HU38 y HU39 la mencionan). El administrador no recibe permisos de operador.
    public const string OperadorAutorizado = "OperadorAutorizado";
}

public static class Autorizacion
{
    public static IServiceCollection AddAutorizacionCad(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Ciudadano, p => p.RequireRole(Roles.Ciudadano))
            .AddPolicy(Politicas.Sereno, p => p.RequireRole(Roles.Sereno))
            .AddPolicy(Politicas.OperadorAutorizado, p => p.RequireRole(Roles.Operador))
            .AddPolicy(Politicas.Administrador, p => p.RequireRole(Roles.Administrador))
            // Una ruta nueva sin [Authorize] ni [AllowAnonymous] queda protegida, no abierta.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
