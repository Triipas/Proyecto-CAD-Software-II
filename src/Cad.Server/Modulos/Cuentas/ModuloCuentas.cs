using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas;

public static class ModuloCuentas
{
    public static IServiceCollection AddModuloCuentas(this IServiceCollection services)
    {
        services
            .AddIdentityCore<Usuario>(opciones =>
            {
                opciones.User.RequireUniqueEmail = true;
                // UserName = correo, ya validado en RegistrarCiudadano; la lista por defecto de Identity
                // rechazaría caracteres válidos en un correo.
                opciones.User.AllowedUserNameCharacters = string.Empty;
                // Coherente con la validación de RegistrarCiudadano: el servidor siempre revalida.
                opciones.Password.RequiredLength = RegistrarCiudadano.LongitudMinimaContrasena;
                opciones.Password.RequireDigit = true;
                opciones.Password.RequireLowercase = false;
                opciones.Password.RequireUppercase = false;
                opciones.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CadDbContext>();

        services.AddScoped<RegistrarCiudadano>();
        return services;
    }

    public static IEndpointRouteBuilder MapModuloCuentas(this IEndpointRouteBuilder app)
    {
        var cuentas = app.MapGroup("/api/cuentas");

        cuentas.MapPost("/registro", async (SolicitudRegistroCiudadano solicitud, RegistrarCiudadano registrar, CancellationToken ct) =>
            await registrar.EjecutarAsync(solicitud, ct) switch
            {
                ResultadoRegistro.Creado creado =>
                    Results.Json(new RespuestaRegistroCiudadano(creado.Id), statusCode: StatusCodes.Status201Created),
                ResultadoRegistro.Invalido invalido =>
                    Results.ValidationProblem(invalido.Errores, title: "Los datos de registro no son válidos."),
                _ =>
                    Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "Ya existe una cuenta registrada con ese correo electrónico."),
            });

        return app;
    }
}
