using Cad.Contratos.Cuentas;
using Cad.Server.Compartido;
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
                opciones.Password.RequiredLength = ReglasCuenta.LongitudMinimaContrasena;
                opciones.Password.RequireDigit = true;
                opciones.Password.RequireLowercase = false;
                opciones.Password.RequireUppercase = false;
                opciones.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CadDbContext>();

        services.AddScoped<RegistrarCiudadano>();
        services.AddScoped<IniciarSesion>();
        services.AddScoped<SembrarAdministrador>();
        services.AddScoped<CrearCuentaInstitucional>();
        services.AddScoped<ModificarCuentaInstitucional>();
        services.AddScoped<ConsultarCuentasInstitucionales>();
        services.AddSingleton<EmisorTokens>();
        return services;
    }

    public static IEndpointRouteBuilder MapModuloCuentas(this IEndpointRouteBuilder app)
    {
        // HU02 — inicio de sesión de la app móvil (ciudadano y sereno).
        app.MapPost("/api/auth/token", async (SolicitudInicioSesion solicitud, IniciarSesion iniciar, EmisorTokens emisor) =>
        {
            var resultado = await iniciar.EjecutarAsync(solicitud.Correo, solicitud.Contrasena, Canal.App);
            if (resultado is ResultadoInicioSesion.Exito exito)
            {
                var (token, expiraEn) = emisor.Emitir(exito.Usuario, exito.Rol);
                return Results.Ok(new RespuestaToken(token, expiraEn, exito.Rol));
            }

            var estado = resultado is ResultadoInicioSesion.CredencialesIncorrectas
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;
            return Results.Problem(statusCode: estado, title: Aplicacion.IniciarSesion.Mensaje(resultado));
        }).AllowAnonymous();

        var cuentas = app.MapGroup("/api/cuentas");

        // Cuenta autenticada (cookie o JWT): permite a cada cliente mostrar la interfaz de su rol.
        cuentas.MapGet("/yo", async (HttpContext contexto, UserManager<Usuario> usuarios) =>
        {
            var usuario = await usuarios.FindByIdAsync(contexto.User.IdUsuario().ToString());
            var rol = contexto.User.FindFirst(Autenticacion.ReclamoRol)?.Value;
            return usuario is null || rol is null
                ? Results.Unauthorized()
                : Results.Ok(new PerfilUsuario(usuario.Id, usuario.Nombres, usuario.Apellidos, rol));
        }).RequireAuthorization();

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
            }).AllowAnonymous();

        return app;
    }
}
