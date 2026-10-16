using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
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
        services.AddScoped<RegistrarReporte>();
        services.AddScoped<RevisarReporte>();
        services.AddScoped<Modulos.Consultas.ConsultarReportes>();
        services.AddScoped<Modulos.Consultas.ConsultarDetalleReporte>();
        services.AddScoped<RegistrarIntentoContacto>();
        services.AddScoped<AsignarCaso>();
        services.AddScoped<ClasificarCaso>();
        services.AddScoped<AvanzarCaso>();
        services.AddScoped<CerrarCaso>();
        services.AddScoped<Modulos.Consultas.ConsultarSosPendientes>();
        services.AddScoped<Modulos.Consultas.ConsultarDetalleSos>();
        services.AddScoped<Modulos.Consultas.ConsultarCategorias>();
        services.AddScoped<Modulos.Consultas.ConsultarHistorialCaso>();
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

        // HU10 — registro de un reporte: 201 la primera vez; 200 con el mismo reporte ante una reiteración técnica (CA08);
        // 400 sin crear nada si falta o no es válido algún dato (CA02).
        app.MapPost("/api/reportes", async (SolicitudRegistroReporte solicitud, HttpContext contexto, RegistrarReporte registrar, CancellationToken ct) =>
            await registrar.EjecutarAsync(contexto.User.IdUsuario(), solicitud, ct) switch
            {
                ResultadoRegistroReporte.Registrado r => Results.Json(RespuestaDeReporte(r.Caso), statusCode: StatusCodes.Status201Created),
                ResultadoRegistroReporte.YaRegistrado r => Results.Ok(RespuestaDeReporte(r.Caso)),
                ResultadoRegistroReporte.Invalido i => Results.ValidationProblem(i.Errores, title: "El reporte no es válido."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            })
            .RequireAuthorization(Politicas.Ciudadano);

        // HU10-CA01 y DP07 — catálogo para elegir el tipo del reporte.
        app.MapGet("/api/reportes/categorias", async (Modulos.Consultas.ConsultarCategorias categorias, CancellationToken ct) =>
                Results.Ok((await categorias.EjecutarAsync(ct)).Select(c => new CategoriaCatalogo(c.Id, c.Nombre))))
            .RequireAuthorization(Politicas.Ciudadano);

        return app;
    }

    private static RespuestaSos Respuesta(Caso caso) =>
        new(caso.Id, caso.Estado.ToString(), caso.RegistradoEn, caso.UbicacionDisponible);

    private static RespuestaReporte RespuestaDeReporte(Caso caso) =>
        new(caso.Id, EstadosCaso.Texto(caso.Estado), caso.RegistradoEn);
}
