using Cad.Contratos.Casos;
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
        services.AddScoped<DecidirReporte>();
        services.AddScoped<Modulos.Consultas.ConsultarReportesAdmitidos>();
        services.AddScoped<Modulos.Consultas.ConsultarCasos>();
        services.AddScoped<Modulos.Consultas.ConsultarMisCasos>();
        services.AddScoped<Modulos.Consultas.BandejaCiudadano>();
        services.AddScoped<Modulos.Consultas.ConsultarReportes>();
        services.AddScoped<Modulos.Consultas.ConsultarDetalleReporte>();
        services.AddScoped<RegistrarIntentoContacto>();
        services.AddScoped<AsignarCaso>();
        services.AddScoped<ClasificarCaso>();
        services.AddScoped<AvanzarCaso>();
        services.AddScoped<CerrarCaso>();
        services.AddScoped<CancelarSos>();
        services.AddScoped<DescartarCaso>();
        services.AddScoped<ReasignarCaso>();
        services.AddScoped<EvaluarCaso>();
        services.AddScoped<Modulos.Consultas.ConsultarSosPendientes>();
        services.AddScoped<Modulos.Consultas.ConsultarCasosEnMapa>();
        services.AddSingleton<Modulos.Consultas.RedCalles>();
        services.AddScoped<Modulos.Consultas.ConsultarRutasEnCamino>();
        services.AddScoped<Modulos.Consultas.ConsultarDetalleSos>();
        services.AddScoped<Modulos.Consultas.ConsultarFichaCiudadano>(); // ADR-08, fase D
        services.AddScoped<Modulos.Consultas.ConsultarCategorias>();
        services.AddScoped<Modulos.Consultas.ConsultarHistorialCaso>();
        services.AddScoped<Modulos.Consultas.ConsultarDescarte>();
        services.AddScoped<Modulos.Consultas.ConsultarTiemposCaso>();
        services.AddScoped<Modulos.Consultas.ConsultarConcentracion>();
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
                Results.Ok((await categorias.EjecutarAsync(ct)).Select(c => new CategoriaCatalogo(c.Id, c.Nombre, c.Ayuda, c.Icono))))
            .RequireAuthorization(Politicas.Ciudadano);

        // HU30 — mis casos: solo los del ciudadano que consulta; el de otro responde 404, también con una solicitud directa (CA04).
        app.MapGet("/api/casos/mios", async (HttpContext contexto, Modulos.Consultas.ConsultarMisCasos consultar, CancellationToken ct) =>
                Results.Ok(await consultar.ListarAsync(contexto.User.IdUsuario(), ct)))
            .RequireAuthorization(Politicas.Ciudadano);
        app.MapGet("/api/casos/mios/{id:guid}", async (Guid id, HttpContext contexto, Modulos.Consultas.ConsultarMisCasos consultar, CancellationToken ct) =>
                await consultar.DetalleAsync(contexto.User.IdUsuario(), id, ct) is { } detalle ? Results.Ok(detalle) : Results.NotFound())
            .RequireAuthorization(Politicas.Ciudadano);

        // HU09 — cancelar un SOS propio pendiente, con motivo: 200 con el caso actualizado (también ante un reenvío, sin cambiar
        // el motivo ni el momento ya registrados: CA08); 400 sin motivo (CA04); 409 si ya no está pendiente (CA03); 404 si no es
        // suyo o no existe, sin distinguirlos (CA05).
        app.MapPost("/api/casos/mios/{id:guid}/cancelacion", async (Guid id, SolicitudCancelacionSos solicitud, HttpContext contexto, CancelarSos cancelar,
                Modulos.Consultas.ConsultarMisCasos consultar, CancellationToken ct) =>
            {
                var ciudadano = contexto.User.IdUsuario();
                return await cancelar.EjecutarAsync(ciudadano, id, solicitud.Motivo, ct) switch
                {
                    ResultadoCancelacion.Cancelado or ResultadoCancelacion.YaCancelado => Results.Ok(await consultar.DetalleAsync(ciudadano, id, ct)),
                    ResultadoCancelacion.Rechazado r => Results.Problem(title: r.Motivo, statusCode: StatusCodes.Status409Conflict),
                    ResultadoCancelacion.Invalido i => Results.ValidationProblem(i.Errores, title: "No se canceló el SOS."),
                    _ => Results.NotFound(),
                };
            })
            .RequireAuthorization(Politicas.Ciudadano);

        // HU32 — evaluar un caso propio cerrado: 201 la primera vez; 200 con la misma evaluación ante un reenvío técnico; 400 sin
        // puntuación válida (CA02); 409 si ya está evaluado (CA03) o no está cerrado (CA04); 404 si no es suyo o no existe (CA05).
        app.MapPost("/api/casos/mios/{id:guid}/evaluacion", async (Guid id, SolicitudEvaluacionCaso solicitud, HttpContext contexto, EvaluarCaso evaluar,
                CancellationToken ct) =>
            await evaluar.EjecutarAsync(contexto.User.IdUsuario(), id, solicitud, ct) switch
            {
                ResultadoEvaluacion.Registrada r => Results.Json(Vista(r.Evaluacion), statusCode: StatusCodes.Status201Created),
                ResultadoEvaluacion.YaRegistrada r => Results.Ok(Vista(r.Evaluacion)),
                ResultadoEvaluacion.Rechazada r => Results.Problem(title: r.Motivo, statusCode: StatusCodes.Status409Conflict),
                ResultadoEvaluacion.Invalida i => Results.ValidationProblem(i.Errores, title: "No se registró la evaluación."),
                _ => Results.NotFound(),
            })
            .RequireAuthorization(Politicas.Ciudadano);

        // HU31 — bandeja de notificaciones: solo las del ciudadano que consulta (CA04).
        app.MapGet("/api/notificaciones", async (HttpContext contexto, Modulos.Consultas.BandejaCiudadano bandeja, CancellationToken ct) =>
                Results.Ok(await bandeja.ConsultarAsync(contexto.User.IdUsuario(), ct)))
            .RequireAuthorization(Politicas.Ciudadano);
        app.MapPost("/api/notificaciones/{id:guid}/leida", async (Guid id, HttpContext contexto, Modulos.Consultas.BandejaCiudadano bandeja, CancellationToken ct) =>
                await bandeja.MarcarLeidaAsync(contexto.User.IdUsuario(), id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(Politicas.Ciudadano);

        return app;
    }

    private static RespuestaSos Respuesta(Caso caso) =>
        new(caso.Id, caso.Estado.ToString(), caso.RegistradoEn, caso.UbicacionDisponible);

    private static MiEvaluacion Vista(EvaluacionCaso e) => new(e.Puntuacion, e.Comentario, e.EvaluadaEn);

    private static RespuestaReporte RespuestaDeReporte(Caso caso) =>
        new(caso.Id, EstadosCaso.Texto(caso.Estado), caso.RegistradoEn);
}
