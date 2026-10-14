using Cad.Contratos.Serenos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Serenos.Aplicacion;
using Cad.Server.Modulos.Serenos.Dominio;

namespace Cad.Server.Modulos.Serenos;

public static class ModuloSerenos
{
    public static IServiceCollection AddModuloSerenos(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesSerenos>(configuracion.GetSection(OpcionesSerenos.Seccion));
        services.AddScoped<GestionarEstadoOperativo>();
        return services;
    }

    // HU18 — canal de la app del sereno (JWT). Solo serenos (CA05).
    public static IEndpointRouteBuilder MapModuloSerenos(this IEndpointRouteBuilder app)
    {
        var sereno = app.MapGroup("/api/sereno").RequireAuthorization(Politicas.Sereno);

        sereno.MapGet("/estado", async (HttpContext contexto, GestionarEstadoOperativo estados, CancellationToken ct) =>
            Results.Ok(Respuesta(await estados.ConsultarAsync(contexto.User.IdUsuario(), ct), estados)));

        sereno.MapPut("/estado", async (SolicitudCambioEstadoOperativo solicitud, HttpContext contexto,
                GestionarEstadoOperativo estados, CancellationToken ct) =>
            await estados.CambiarAsync(contexto.User.IdUsuario(), solicitud, ct) switch
            {
                ResultadoCambioEstado.Correcto c => Results.Ok(Respuesta(c.Estado, estados)),
                ResultadoCambioEstado.Invalido i => Results.ValidationProblem(i.Errores, title: "No se pudo cambiar el estado operativo."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            });

        // CA01: posición con marca de tiempo. 201 la primera vez; 200 si es un reenvío de la misma posición;
        // 409 si el sereno no está operativo (CA02, CA03): la posición no se registra.
        sereno.MapPost("/posiciones", async (PosicionSerenoEnviada posicion, HttpContext contexto,
                GestionarEstadoOperativo estados, CancellationToken ct) =>
            await estados.RegistrarPosicionAsync(contexto.User.IdUsuario(), posicion, ct) switch
            {
                ResultadoPosicion.Registrada => Results.StatusCode(StatusCodes.Status201Created),
                ResultadoPosicion.YaRegistrada => Results.Ok(),
                ResultadoPosicion.NoOperativo => Results.Problem(
                    title: "Tu estado no es operativo: la posición no se registró.", statusCode: StatusCodes.Status409Conflict),
                ResultadoPosicion.Invalida i => Results.ValidationProblem(i.Errores, title: "La posición no es válida."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            });

        return app;
    }

    private static RespuestaEstadoOperativo Respuesta(EstadoSereno? estado, GestionarEstadoOperativo estados) =>
        new(estado?.Operativo ?? false, estado?.CambiadoEn, estados.IntervaloPosicionesSegundos);
}
