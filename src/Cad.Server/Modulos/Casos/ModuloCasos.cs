using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Modulos.Casos;

public static class ModuloCasos
{
    public static IServiceCollection AddModuloCasos(this IServiceCollection services)
    {
        services.AddScoped<EmitirSos>();
        services.AddScoped<RegistrarIntentoContacto>();
        services.AddScoped<AsignarCaso>();
        services.AddScoped<ClasificarCaso>();
        services.AddScoped<AvanzarCaso>();
        services.AddScoped<Modulos.Consultas.ConsultarSosPendientes>();
        services.AddScoped<Modulos.Consultas.ConsultarDetalleSos>();
        services.AddScoped<Modulos.Consultas.ConsultarCategorias>();
        return services;
    }

    public static IEndpointRouteBuilder MapModuloCasos(this IEndpointRouteBuilder app)
    {
        // HU07/HU08 — la app confirma el SOS solo cuando recibe esta respuesta (CA03):
        // 201 la primera vez; 200 con el mismo SOS ante una reiteración técnica (CA07).
        app.MapPost("/api/sos", async (SolicitudEmisionSos solicitud, HttpContext contexto, EmitirSos emitir, CancellationToken ct) =>
            await emitir.EjecutarAsync(contexto.User.IdUsuario(), solicitud, ct) switch
            {
                ResultadoEmisionSos.Registrado r => Results.Json(Respuesta(r.Caso), statusCode: StatusCodes.Status201Created),
                ResultadoEmisionSos.YaRegistrado r => Results.Ok(Respuesta(r.Caso)),
                ResultadoEmisionSos.Invalido i => Results.ValidationProblem(i.Errores, title: "La solicitud de SOS no es válida."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            })
            .RequireAuthorization(Politicas.Ciudadano);

        return app;
    }

    private static RespuestaSos Respuesta(Caso caso) =>
        new(caso.Id, caso.Estado.ToString(), caso.RegistradoEn, caso.UbicacionDisponible);
}
