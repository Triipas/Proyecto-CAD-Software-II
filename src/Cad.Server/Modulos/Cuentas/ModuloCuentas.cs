using Cad.Contratos.Cuentas;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;

namespace Cad.Server.Modulos.Cuentas;

public static class ModuloCuentas
{
    public const string PoliticaIngresoCelular = "ingreso-celular";

    public static IServiceCollection AddModuloCuentas(this IServiceCollection services, IConfiguration configuracion)
    {
        services
            .AddIdentityCore<Usuario>(opciones =>
            {
                // ADR-08: la cuenta del ciudadano no tiene correo. La unicidad del correo del personal la garantiza
                // el índice único EmailIndex (y UserName = correo), además de la comprobación previa de cada caso de uso.
                opciones.User.RequireUniqueEmail = false;
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
        services.AddScoped<CambiarContrasena>();
        services.AddScoped<SembrarAdministrador>();
        services.AddScoped<CrearCuentaInstitucional>();
        services.AddScoped<ModificarCuentaInstitucional>();
        services.AddScoped<DesactivarCuentaInstitucional>();
        services.AddScoped<ConsultarCuentasInstitucionales>();
        services.AddSingleton<EmisorTokens>();

        // ADR-08: ingreso del ciudadano con celular verificado y PIN.
        services.Configure<OpcionesCodigos>(configuracion.GetSection(OpcionesCodigos.Seccion));
        services.Configure<OpcionesCuentasCiudadano>(configuracion.GetSection(OpcionesCuentasCiudadano.Seccion));
        services.AddHttpClient<IVerificacionSms, VerificacionSmsTwilio>(http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<CodigosCelular>();
        services.AddScoped<IngresoCelular>();
        services.AddScoped<MisDatosCiudadano>();

        // Límite por IP para las rutas que envían códigos o comprueban PIN: frena la fuerza bruta y el gasto en SMS.
        var porMinuto = configuracion.GetSection(OpcionesCodigos.Seccion).Get<OpcionesCodigos>()?.SolicitudesPorMinutoIp ?? 30;
        services.AddRateLimiter(limitador =>
        {
            limitador.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limitador.AddPolicy(PoliticaIngresoCelular, contexto => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });
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
                return Results.Ok(new RespuestaToken(token, expiraEn, exito.Rol, exito.Usuario.DebeCambiarContrasena));
            }

            var estado = resultado is ResultadoInicioSesion.CredencialesIncorrectas
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;
            return Results.Problem(statusCode: estado, title: Aplicacion.IniciarSesion.Mensaje(resultado));
        }).AllowAnonymous();

        // Cambio de la contraseña propia del sereno, obligatorio si entró con la provisional del administrador. 400 con los
        // motivos por campo; si es correcto, un token nuevo (el sello cambió y el anterior ya no vale).
        app.MapPost("/api/cuentas/contrasena", async (SolicitudCambioContrasena solicitud, HttpContext contexto, CambiarContrasena cambiar,
                EmisorTokens emisor) =>
            await cambiar.EjecutarAsync(contexto.User.IdUsuario(), solicitud.Actual, solicitud.Nueva, solicitud.Confirmacion) switch
            {
                ResultadoCambioContrasena.Correcto c when emisor.Emitir(c.Usuario, c.Rol) is var (token, expiraEn) =>
                    Results.Ok(new RespuestaToken(token, expiraEn, c.Rol)),
                ResultadoCambioContrasena.Invalido i => Results.ValidationProblem(i.Errores, title: "No se cambió la contraseña."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            })
            .RequireAuthorization(Politicas.Personal);

        MapIngresoCelular(app);

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

        // ADR-08, fase D — «Mis datos» y cambio de PIN: solo la propia cuenta de ciudadano.
        cuentas.MapGet("/yo/perfil", async (HttpContext contexto, MisDatosCiudadano misDatos, CancellationToken ct) =>
            await misDatos.ConsultarAsync(contexto.User.IdUsuario(), ct) is { } perfil ? Results.Ok(perfil) : Results.Unauthorized())
            .RequireAuthorization(Politicas.Ciudadano);

        cuentas.MapPut("/yo/perfil", async (SolicitudPerfilCiudadano solicitud, HttpContext contexto, MisDatosCiudadano misDatos, CancellationToken ct) =>
            await misDatos.ActualizarAsync(contexto.User.IdUsuario(), solicitud, ct) switch
            {
                ResultadoMisDatos.Correcto c => Results.Ok(c.Perfil),
                ResultadoMisDatos.Invalido i => Results.ValidationProblem(i.Errores, title: "Revisa los datos marcados."),
                _ => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Ese documento ya está registrado en otra cuenta."),
            })
            .RequireAuthorization(Politicas.Ciudadano);

        cuentas.MapPost("/yo/pin", async (SolicitudCambioPin solicitud, HttpContext contexto, MisDatosCiudadano misDatos, EmisorTokens emisor) =>
            await misDatos.CambiarPinAsync(contexto.User.IdUsuario(), solicitud) switch
            {
                ResultadoCambioPin.Correcto c => EmitirSesion(new ResultadoIngresoCelular.Correcto(c.Usuario, null), emisor, StatusCodes.Status200OK),
                ResultadoCambioPin.Invalido i => Results.ValidationProblem(i.Errores, title: "Revisa el PIN nuevo."),
                ResultadoCambioPin.SinPin => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Tu cuenta entra con correo y contraseña; no tiene PIN."),
                ResultadoCambioPin.PinIncorrecto p => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: p.IntentosRestantes == 1 ? "PIN actual incorrecto. Te queda 1 intento." : $"PIN actual incorrecto. Te quedan {p.IntentosRestantes} intentos."),
                _ => Results.Problem(statusCode: StatusCodes.Status423Locked,
                    title: "Tu PIN se bloqueó tras varios intentos. Cierra sesión y pide un código para crear uno nuevo."),
            })
            .RequireAuthorization(Politicas.Ciudadano).RequireRateLimiting(PoliticaIngresoCelular);

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

    // ADR-08: registro e ingreso del ciudadano con celular verificado y PIN. Ninguna ruta registra el celular,
    // el código, el PIN ni los tokens.
    private static void MapIngresoCelular(IEndpointRouteBuilder app)
    {
        var celular = app.MapGroup("/api/auth/celular").AllowAnonymous().RequireRateLimiting(PoliticaIngresoCelular);

        celular.MapPost("/codigo", async (SolicitudCodigoCelular solicitud, CodigosCelular codigos, CancellationToken ct) =>
            await codigos.EnviarAsync(solicitud.Celular, ct) switch
            {
                ResultadoCodigo.Enviado e => Results.Ok(new RespuestaCodigoCelular(e.ReenviarEnSegundos, e.ExpiraEnSegundos, e.CodigoDePrueba)),
                ResultadoCodigo.Invalido i => Results.ValidationProblem(i.Errores, title: "El número de celular no es válido."),
                ResultadoCodigo.Espera e => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                    title: $"Espera {e.Segundos} s para pedir otro código."),
                ResultadoCodigo.LimiteAlcanzado => Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                    title: "Pediste demasiados códigos para este número. Inténtalo de nuevo en una hora."),
                _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "No pudimos enviar el código en este momento. Inténtalo de nuevo en unos minutos."),
            });

        celular.MapPost("/verificar", async (SolicitudVerificacionCelular solicitud, CodigosCelular codigos, CancellationToken ct) =>
            await codigos.VerificarAsync(solicitud.Celular, solicitud.Codigo, ct) switch
            {
                ResultadoVerificacion.Verificado v => Results.Ok(new RespuestaVerificacionCelular(v.Token, v.CuentaExiste, v.PuedeRegistrarDeNuevo)),
                ResultadoVerificacion.Incorrecto i => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: i.IntentosRestantes == 1 ? "Código incorrecto. Te queda 1 intento." : $"Código incorrecto. Te quedan {i.IntentosRestantes} intentos."),
                _ => Results.Problem(statusCode: StatusCodes.Status410Gone,
                    title: "El código venció o ya no es válido. Pide uno nuevo."),
            });

        celular.MapPost("/registro", async (SolicitudRegistroCelular solicitud, IngresoCelular ingreso, EmisorTokens emisor, CancellationToken ct) =>
            Responder(await ingreso.RegistrarAsync(solicitud, ct), emisor, StatusCodes.Status201Created));

        celular.MapPost("/ingresar", async (SolicitudIngresoCelular solicitud, IngresoCelular ingreso, EmisorTokens emisor, CancellationToken ct) =>
            Responder(await ingreso.IngresarAsync(solicitud, ct), emisor, StatusCodes.Status200OK));

        celular.MapPost("/pin", async (SolicitudRestablecerPin solicitud, IngresoCelular ingreso, EmisorTokens emisor, CancellationToken ct) =>
            Responder(await ingreso.RestablecerPinAsync(solicitud, ct), emisor, StatusCodes.Status200OK));
    }

    private static IResult Responder(ResultadoIngresoCelular resultado, EmisorTokens emisor, int estadoCorrecto) => resultado switch
    {
        ResultadoIngresoCelular.Correcto c => EmitirSesion(c, emisor, estadoCorrecto),
        ResultadoIngresoCelular.Invalido i => Results.ValidationProblem(i.Errores, title: "Revisa los datos."),
        ResultadoIngresoCelular.TokenInvalido => Results.Problem(statusCode: StatusCodes.Status410Gone,
            title: "La verificación del número venció. Pide un código nuevo."),
        ResultadoIngresoCelular.Duplicado => Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Este número ya tiene una cuenta. Ingresa con tu PIN."),
        ResultadoIngresoCelular.SinCuenta => Results.Problem(statusCode: StatusCodes.Status404NotFound,
            title: "No hay una cuenta con este número. Crea una."),
        ResultadoIngresoCelular.PinIncorrecto p => Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: p.IntentosRestantes == 1 ? "PIN incorrecto. Te queda 1 intento." : $"PIN incorrecto. Te quedan {p.IntentosRestantes} intentos."),
        ResultadoIngresoCelular.PinBloqueado => Results.Problem(statusCode: StatusCodes.Status423Locked,
            title: "Tu PIN se bloqueó tras varios intentos. Pide un código para crear uno nuevo."),
        ResultadoIngresoCelular.CuentaDesactivada => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: Aplicacion.IniciarSesion.Mensaje(new ResultadoInicioSesion.CuentaDesactivada())),
        _ => Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired,
            title: "Este teléfono no está vinculado a tu cuenta. Te enviaremos un código para verificarlo."),
    };

    private static IResult EmitirSesion(ResultadoIngresoCelular.Correcto correcto, EmisorTokens emisor, int estado)
    {
        var (token, expiraEn) = emisor.Emitir(correcto.Usuario, Roles.Ciudadano);
        return Results.Json(new RespuestaIngresoCelular(token, expiraEn, Roles.Ciudadano, correcto.LlaveDispositivo), statusCode: estado);
    }
}
