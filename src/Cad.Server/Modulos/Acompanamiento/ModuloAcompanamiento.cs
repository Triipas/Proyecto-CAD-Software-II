using Cad.Contratos.Acompanamiento;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Dominio;

namespace Cad.Server.Modulos.Acompanamiento;

public static class ModuloAcompanamiento
{
    public static IServiceCollection AddModuloAcompanamiento(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesAcompanamiento>(configuracion.GetSection(OpcionesAcompanamiento.Seccion));
        services.AddScoped<GestionarAcompanamiento>();
        services.AddScoped<Consultas.ConsultarAcompanamientos>();
        services.AddHostedService<VencimientoAcompanamientos>();
        services.AddScoped<ConservarRecorridos>();
        services.AddScoped<ConfirmarBienestar>();
        services.AddHostedService<EliminacionRecorridos>();
        return services;
    }

    // HU34–HU37 — canal de la app del ciudadano (JWT). Solo ciudadanos y solo sus propias sesiones.
    public static IEndpointRouteBuilder MapModuloAcompanamiento(this IEndpointRouteBuilder app)
    {
        var acompanamiento = app.MapGroup("/api/acompanamiento").RequireAuthorization(Politicas.Ciudadano);

        // HU34: 201 con la sesión activa (inicio y vencimiento registrados por el servidor, CA01); 200 con la misma sesión
        // ante un reintento técnico; 400 sin finalidad aceptada o sin ubicación (CA02, CA03); 409 si ya hay otra activa (CA06).
        app.MapPost("/api/acompanamiento", async (SolicitudInicioAcompanamiento solicitud, HttpContext contexto, GestionarAcompanamiento gestionar,
                CancellationToken ct) =>
            await gestionar.IniciarAsync(contexto.User.IdUsuario(), solicitud, ct) switch
            {
                ResultadoInicioAcompanamiento.Iniciada i => Results.Json(Vista(i.Sesion, gestionar), statusCode: StatusCodes.Status201Created),
                ResultadoInicioAcompanamiento.YaIniciada y => Results.Ok(Vista(y.Sesion, gestionar)),
                ResultadoInicioAcompanamiento.OtraSesionActiva => Results.Problem(
                    title: "Ya tienes un acompañamiento activo: finalízalo antes de iniciar otro.", statusCode: StatusCodes.Status409Conflict),
                ResultadoInicioAcompanamiento.Invalido i => Results.ValidationProblem(i.Errores, title: "No se inició el acompañamiento."),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            })
            .RequireAuthorization(Politicas.Ciudadano);

        // HU34-CA08: la sesión activa (200) o ninguna (204).
        acompanamiento.MapGet("/actual", async (HttpContext contexto, GestionarAcompanamiento gestionar, CancellationToken ct) =>
            await gestionar.ActivaAsync(contexto.User.IdUsuario(), ct) is { } sesion ? Results.Ok(Vista(sesion, gestionar)) : Results.NoContent());

        // HU37-CA06: una sesión concreta, también cerrada (al reconectar, la app sabe cómo terminó). Ajena = inexistente (404).
        acompanamiento.MapGet("/{id:guid}", async (Guid id, HttpContext contexto, GestionarAcompanamiento gestionar, CancellationToken ct) =>
            await gestionar.DelTitularAsync(contexto.User.IdUsuario(), id, ct) is { } sesion ? Results.Ok(Vista(sesion, gestionar)) : Results.NotFound());

        // HU35: 201 registrada; 200 reenvío de la misma posición; 409 con la sesión cerrada o vencida (la posición no se
        // incorpora: HU35-CA02, HU36-CA04, HU37-CA04); 404 sesión ajena o inexistente; 400 posición inválida.
        acompanamiento.MapPost("/{id:guid}/posiciones", async (Guid id, PosicionAcompanamientoEnviada posicion, HttpContext contexto,
                GestionarAcompanamiento gestionar, CancellationToken ct) =>
            await gestionar.RegistrarPosicionAsync(contexto.User.IdUsuario(), id, posicion, ct) switch
            {
                ResultadoPosicionAcompanamiento.Registrada => Results.StatusCode(StatusCodes.Status201Created),
                ResultadoPosicionAcompanamiento.YaRegistrada => Results.Ok(),
                ResultadoPosicionAcompanamiento.SesionCerrada c => Results.Problem(
                    title: "El acompañamiento ya terminó: la posición no se registró.", statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["estado"] = Texto(c.Sesion.Estado) }),
                ResultadoPosicionAcompanamiento.Invalida i => Results.ValidationProblem(i.Errores, title: "La posición no es válida."),
                _ => Results.NotFound(),
            });

        // HU36: 200 con la sesión cerrada, también ante un reenvío o si ya había vencido (CA07); 404 ajena o inexistente.
        acompanamiento.MapPost("/{id:guid}/finalizacion", async (Guid id, HttpContext contexto, GestionarAcompanamiento gestionar,
                CancellationToken ct) =>
            await gestionar.FinalizarAsync(contexto.User.IdUsuario(), id, ct) is ResultadoFinalizacion.Cerrada c
                ? Results.Ok(Vista(c.Sesion, gestionar))
                : Results.NotFound());

        // HU41-CA01: la confirmación pendiente de una sesión propia (200) o ninguna (204); 404 si la sesión es ajena o no existe.
        acompanamiento.MapGet("/{id:guid}/confirmacion", async (Guid id, HttpContext contexto, GestionarAcompanamiento gestionar,
                ConfirmarBienestar confirmar, CancellationToken ct) =>
            {
                var ciudadano = contexto.User.IdUsuario();
                if (await gestionar.DelTitularAsync(ciudadano, id, ct) is null)
                    return Results.NotFound();
                return await confirmar.PendienteAsync(ciudadano, id, ct) is { } pendiente ? Results.Ok(Vista(pendiente)) : Results.NoContent();
            });

        // HU41-CA01: la app la presentó al ciudadano (solo cuenta la primera vez).
        acompanamiento.MapPost("/confirmaciones/{id:guid}/presentacion", async (Guid id, HttpContext contexto, ConfirmarBienestar confirmar,
                CancellationToken ct) =>
            await confirmar.RegistrarPresentacionAsync(contexto.User.IdUsuario(), id, ct) is { } confirmacion
                ? Results.Ok(Vista(confirmacion))
                : Results.NotFound());

        // HU41: 200 con la respuesta registrada (también ante un reenvío, CA07); 400 sin respuesta válida o, con «Necesito ayuda»,
        // sin la solicitud del SOS; 409 si ya se respondió con la otra opción; 404 si la confirmación es ajena o no existe.
        acompanamiento.MapPost("/confirmaciones/{id:guid}/respuesta", async (Guid id, SolicitudRespuestaBienestar solicitud, HttpContext contexto,
                ConfirmarBienestar confirmar, CancellationToken ct) =>
            await confirmar.ResponderAsync(contexto.User.IdUsuario(), id, solicitud, ct) switch
            {
                ResultadoRespuestaBienestar.Registrada r => Results.Ok(Vista(r.Confirmacion)),
                ResultadoRespuestaBienestar.YaRegistrada r => Results.Ok(Vista(r.Confirmacion)),
                ResultadoRespuestaBienestar.OtraRespuesta => Results.Problem(
                    title: "Ya respondiste esta confirmación con la otra opción.", statusCode: StatusCodes.Status409Conflict),
                ResultadoRespuestaBienestar.Invalida i => Results.ValidationProblem(i.Errores, title: "No se registró la respuesta."),
                _ => Results.NotFound(),
            });

        return app;
    }

    public static string Texto(RespuestaBienestar respuesta) =>
        respuesta == RespuestaBienestar.EstoyBien ? RespuestasBienestar.EstoyBien : RespuestasBienestar.NecesitoAyuda;

    private static ConfirmacionBienestarVista Vista(ConfirmacionBienestar c) =>
        new(c.Id, c.SolicitadaEn, c.PresentadaEn, c.Respuesta is { } r ? Texto(r) : null, c.RespondidaEn, c.CasoId);

    public static string Texto(EstadoSesion estado) => estado switch
    {
        EstadoSesion.Activa => EstadosAcompanamiento.Activa,
        EstadoSesion.Finalizada => EstadosAcompanamiento.Finalizada,
        EstadoSesion.Vencida => EstadosAcompanamiento.Vencida,
        _ => estado.ToString(),
    };

    private static SesionAcompanamientoVista Vista(SesionAcompanamiento sesion, GestionarAcompanamiento gestionar) =>
        new(sesion.Id, Texto(sesion.Estado), sesion.IniciadaEn, sesion.VenceEn, sesion.CerradaEn, gestionar.Ahora, gestionar.IntervaloPosicionesSegundos);
}
