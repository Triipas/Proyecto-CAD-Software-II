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
        services.AddScoped<Consultas.ConsultarSerenos>();
        services.AddScoped<Consultas.ConsultarDespacho>();
        services.AddScoped<GestionarDispositivo>();
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

        // HU21 — despacho vigente del sereno: 200 con los datos de DP06 o 204 si no tiene ninguno.
        sereno.MapGet("/despacho", async (HttpContext contexto, Consultas.ConsultarDespacho despachos, CancellationToken ct) =>
            await despachos.VigenteAsync(contexto.User.IdUsuario(), ct) is { } despacho ? Results.Ok(despacho) : Results.NoContent());

        // CA04: un caso que no está asignado a este sereno responde igual que uno que no existe (404).
        sereno.MapGet("/casos/{id:guid}", async (Guid id, HttpContext contexto, Consultas.ConsultarDespacho despachos, CancellationToken ct) =>
            await despachos.DelCasoAsync(contexto.User.IdUsuario(), id, ct) is { } despacho ? Results.Ok(despacho) : Results.NotFound());

        // HU22 — el sereno asignado confirma que va en camino; HU23 — registra el inicio de atención.
        // 200 con el despacho actualizado (también ante un reenvío); 409 si el estado lo impide; 404 si no es suyo.
        sereno.MapPost("/casos/{id:guid}/desplazamiento", (Guid id, HttpContext contexto, Casos.Aplicacion.AvanzarCaso avanzar,
                Consultas.ConsultarDespacho despachos, CancellationToken ct) =>
            AvanzarAsync(id, Casos.Aplicacion.Avance.ConfirmarDesplazamiento, contexto, avanzar, despachos, ct));
        // Rango de atención (24/10/2026): el cuerpo es opcional (posición y, fuera del rango, el motivo). 422 si el sereno está
        // lejos del punto del caso y no indicó por qué, con la distancia y el radio en los errores.
        sereno.MapPost("/casos/{id:guid}/atencion", (Guid id, SolicitudInicioAtencion? solicitud, HttpContext contexto,
                Casos.Aplicacion.AvanzarCaso avanzar, Consultas.ConsultarDespacho despachos, CancellationToken ct) =>
            AvanzarAsync(id, Casos.Aplicacion.Avance.IniciarAtencion, contexto, avanzar, despachos, ct, solicitud));

        // HU24 — cierre por el sereno asignado. 200 también ante un reenvío del mismo cierre; 400 si faltan la
        // tipificación o el resultado (CA02); 409 si el estado lo impide (CA03); 404 si el caso no es suyo (CA06).
        sereno.MapPost("/casos/{id:guid}/cierre", async (Guid id, SolicitudCierreCaso solicitud, HttpContext contexto,
                Casos.Aplicacion.CerrarCaso cerrar, CancellationToken ct) =>
            await cerrar.EjecutarAsync(id, new Casos.Aplicacion.DatosCierre(solicitud.CategoriaId, solicitud.ResultadoPublico, solicitud.NotasInternas),
                    contexto.User.IdUsuario(), Casos.Aplicacion.ActorAvance.Sereno, ct) switch
            {
                Casos.Aplicacion.ResultadoCierre.Cerrado c => Results.Ok(RespuestaCierre(c.Cierre)),
                Casos.Aplicacion.ResultadoCierre.YaCerrado c => Results.Ok(RespuestaCierre(c.Cierre)),
                Casos.Aplicacion.ResultadoCierre.Invalido i => Results.ValidationProblem(i.Errores, title: "No se pudo cerrar el caso."),
                Casos.Aplicacion.ResultadoCierre.Rechazado r => Results.Problem(title: r.Motivo, statusCode: StatusCodes.Status409Conflict),
                _ => Results.NotFound(),
            });

        // DP07 — catálogo para elegir la tipificación final.
        sereno.MapGet("/categorias", async (Consultas.ConsultarCategorias categorias, CancellationToken ct) =>
            Results.Ok((await categorias.EjecutarAsync(ct)).Select(c => new CategoriaCatalogo(c.Id, c.Nombre, c.Ayuda, c.Icono))));

        // ADR-07 — dispositivo para la notificación push del despacho. 204 en ambos casos.
        sereno.MapPut("/dispositivo", async (SolicitudRegistroDispositivo solicitud, HttpContext contexto,
                GestionarDispositivo dispositivos, CancellationToken ct) =>
        {
            if (GestionarDispositivo.Validar(solicitud.Token) is { } errores)
                return Results.ValidationProblem(errores, title: "No se pudo registrar el dispositivo.");
            await dispositivos.RegistrarAsync(contexto.User.IdUsuario(), solicitud.Token, ct);
            return Results.NoContent();
        });

        sereno.MapDelete("/dispositivo", async (HttpContext contexto, GestionarDispositivo dispositivos, CancellationToken ct) =>
        {
            await dispositivos.QuitarAsync(contexto.User.IdUsuario(), ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<IResult> AvanzarAsync(Guid casoId, Casos.Aplicacion.Avance avance, HttpContext contexto,
        Casos.Aplicacion.AvanzarCaso avanzar, Consultas.ConsultarDespacho despachos, CancellationToken ct, SolicitudInicioAtencion? inicio = null)
    {
        var serenoId = contexto.User.IdUsuario();
        return await avanzar.EjecutarAsync(casoId, avance, serenoId, Casos.Aplicacion.ActorAvance.Sereno, ct, inicio) switch
        {
            Casos.Aplicacion.ResultadoAvance.FueraDeRango f => Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [RangoAtencion.ErrorFueraDeRango] = [f.DistanciaMetros?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                        f.RadioMetros.ToString("0", System.Globalization.CultureInfo.InvariantCulture)],
                },
                title: f.Mensaje, statusCode: StatusCodes.Status422UnprocessableEntity),
            Casos.Aplicacion.ResultadoAvance.Invalido i => Results.ValidationProblem(i.Errores, title: "No se pudo registrar el inicio de atención."),
            Casos.Aplicacion.ResultadoAvance.Registrado or Casos.Aplicacion.ResultadoAvance.YaRegistrado =>
                Results.Ok(await despachos.DelCasoAsync(serenoId, casoId, ct)),
            Casos.Aplicacion.ResultadoAvance.Rechazado r => Results.Problem(title: r.Motivo, statusCode: StatusCodes.Status409Conflict),
            _ => Results.NotFound(),
        };
    }

    private static RespuestaCierreCaso RespuestaCierre(Casos.Dominio.CierreCaso cierre) =>
        new(cierre.CasoId, Casos.Dominio.EstadosCaso.Texto(Casos.Dominio.EstadoCaso.Cerrada), cierre.CerradoEn);

    private static RespuestaEstadoOperativo Respuesta(EstadoSereno? estado, GestionarEstadoOperativo estados) =>
        new(estado?.Operativo ?? false, estado?.CambiadoEn, estados.IntervaloPosicionesSegundos);
}
