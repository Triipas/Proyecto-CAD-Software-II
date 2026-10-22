using System.Collections.Concurrent;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.Tests;

// Dobles de prueba de los servicios de la plataforma (sin emulador ni red).

public sealed class UbicacionFalsa : IServicioUbicacion
{
    public EstadoPermisoUbicacion Permiso { get; set; } = EstadoPermisoUbicacion.Concedido;
    public EstadoPermisoUbicacion RespuestaAlSolicitar { get; set; } = EstadoPermisoUbicacion.Concedido;
    public Func<UbicacionSos?> Posicion { get; set; } = () => null;
    public bool Falla { get; set; }
    // Simula una plataforma que nunca entrega lectura ni respeta el límite ni la cancelación.
    public bool NoResponde { get; set; }
    public int Solicitudes { get; private set; }
    public List<TimeSpan> Lecturas { get; } = [];

    public Task<EstadoPermisoUbicacion> ConsultarPermisoAsync() => Task.FromResult(Permiso);

    public Task<EstadoPermisoUbicacion> SolicitarPermisoAsync()
    {
        Solicitudes++;
        Permiso = RespuestaAlSolicitar;
        return Task.FromResult(Permiso);
    }

    public Task<UbicacionSos?> ObtenerActualAsync(TimeSpan tiempoMaximo, CancellationToken ct)
    {
        Lecturas.Add(tiempoMaximo);
        if (NoResponde) return new TaskCompletionSource<UbicacionSos?>().Task;
        return Falla ? throw new InvalidOperationException("GPS no disponible") : Task.FromResult(Posicion());
    }
}

public sealed class ApiFalsa : IApiCad
{
    // Respuestas en orden para cada emisión de SOS; la última se repite.
    public Queue<Func<SolicitudEmisionSos, Task<ResultadoApi<RespuestaSos>>>> RespuestasSos { get; } = new();
    public ConcurrentQueue<SolicitudEmisionSos> SosEnviados { get; } = new();
    public ResultadoApi<RespuestaToken> RespuestaIngreso { get; set; } =
        new ResultadoApi<RespuestaToken>.Correcto(new RespuestaToken("token-ficticio", DateTimeOffset.UtcNow.AddHours(8), "Ciudadano"));
    public int Ingresos { get; private set; }

    // ----- ADR-08: ciudadano con celular y PIN -----
    public ResultadoApi<RespuestaCodigoCelular> RespuestaCodigo { get; set; } =
        new ResultadoApi<RespuestaCodigoCelular>.Correcto(new RespuestaCodigoCelular(30, 600, "135790"));
    public ResultadoApi<RespuestaVerificacionCelular> RespuestaVerificacion { get; set; } =
        new ResultadoApi<RespuestaVerificacionCelular>.Correcto(new RespuestaVerificacionCelular("token-verificacion", false, false));
    public Queue<ResultadoApi<RespuestaIngresoCelular>> RespuestasIngresoCelular { get; } = new();
    public List<SolicitudCodigoCelular> CodigosPedidos { get; } = [];
    public List<SolicitudVerificacionCelular> Verificaciones { get; } = [];
    public List<SolicitudRegistroCelular> RegistrosCelular { get; } = [];
    public List<SolicitudIngresoCelular> IngresosCelular { get; } = [];
    public List<SolicitudRestablecerPin> PinesRestablecidos { get; } = [];

    public static ResultadoApi<RespuestaIngresoCelular> SesionCiudadano(string? llave = "llave-ficticia") =>
        new ResultadoApi<RespuestaIngresoCelular>.Correcto(
            new RespuestaIngresoCelular("token-ciudadano", DateTimeOffset.UtcNow.AddDays(30), "Ciudadano", llave));

    private ResultadoApi<RespuestaIngresoCelular> SiguienteIngresoCelular() =>
        RespuestasIngresoCelular.Count > 1 ? RespuestasIngresoCelular.Dequeue()
        : RespuestasIngresoCelular.Count == 1 ? RespuestasIngresoCelular.Peek()
        : SesionCiudadano();

    public Task<ResultadoApi<RespuestaCodigoCelular>> EnviarCodigoCelularAsync(SolicitudCodigoCelular solicitud, CancellationToken ct)
    {
        CodigosPedidos.Add(solicitud);
        return Task.FromResult(RespuestaCodigo);
    }

    public Task<ResultadoApi<RespuestaVerificacionCelular>> VerificarCelularAsync(SolicitudVerificacionCelular solicitud, CancellationToken ct)
    {
        Verificaciones.Add(solicitud);
        return Task.FromResult(RespuestaVerificacion);
    }

    public Task<ResultadoApi<RespuestaIngresoCelular>> RegistrarConCelularAsync(SolicitudRegistroCelular solicitud, CancellationToken ct)
    {
        RegistrosCelular.Add(solicitud);
        return Task.FromResult(SiguienteIngresoCelular());
    }

    public Task<ResultadoApi<RespuestaIngresoCelular>> IngresarConCelularAsync(SolicitudIngresoCelular solicitud, CancellationToken ct)
    {
        IngresosCelular.Add(solicitud);
        return Task.FromResult(SiguienteIngresoCelular());
    }

    public Task<ResultadoApi<RespuestaIngresoCelular>> RestablecerPinAsync(SolicitudRestablecerPin solicitud, CancellationToken ct)
    {
        PinesRestablecidos.Add(solicitud);
        return Task.FromResult(SiguienteIngresoCelular());
    }

    // ----- ADR-08, fase D: «Mis datos» y cambio de PIN -----
    public PerfilCiudadanoVista Perfil { get; set; } = new("Rosa", "Ficticia Quispe", "987654321", true, null, null, null, null,
        null, null, null, "2026-10-20", DateTimeOffset.UtcNow, TienePin: true);
    public ResultadoApi<PerfilCiudadanoVista>? RespuestaActualizarPerfil { get; set; }
    public List<SolicitudPerfilCiudadano> PerfilesEnviados { get; } = [];
    public List<SolicitudCambioPin> CambiosPin { get; } = [];

    public Task<ResultadoApi<PerfilCiudadanoVista>> ConsultarPerfilAsync(string token, CancellationToken ct) =>
        Task.FromResult<ResultadoApi<PerfilCiudadanoVista>>(new ResultadoApi<PerfilCiudadanoVista>.Correcto(Perfil));

    public Task<ResultadoApi<PerfilCiudadanoVista>> ActualizarPerfilAsync(string token, SolicitudPerfilCiudadano solicitud, CancellationToken ct)
    {
        PerfilesEnviados.Add(solicitud);
        if (RespuestaActualizarPerfil is { } respuesta)
            return Task.FromResult(respuesta);
        Perfil = Perfil with
        {
            Nombres = solicitud.Nombres, Apellidos = solicitud.Apellidos, TipoDocumento = solicitud.TipoDocumento,
            NumeroDocumento = solicitud.NumeroDocumento, FechaNacimiento = solicitud.FechaNacimiento, Direccion = solicitud.Direccion,
            ContactoNombre = solicitud.ContactoNombre, ContactoParentesco = solicitud.ContactoParentesco, ContactoTelefono = solicitud.ContactoTelefono,
        };
        return Task.FromResult<ResultadoApi<PerfilCiudadanoVista>>(new ResultadoApi<PerfilCiudadanoVista>.Correcto(Perfil));
    }

    public Task<ResultadoApi<RespuestaIngresoCelular>> CambiarPinAsync(string token, SolicitudCambioPin solicitud, CancellationToken ct)
    {
        CambiosPin.Add(solicitud);
        return Task.FromResult(SiguienteIngresoCelular());
    }

    public static ResultadoApi<RespuestaSos> Registrado(SolicitudEmisionSos s) =>
        new ResultadoApi<RespuestaSos>.Correcto(new RespuestaSos(Guid.NewGuid(), "Pendiente", DateTimeOffset.UtcNow, s.Ubicacion is not null));

    public Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct)
    {
        Ingresos++;
        return Task.FromResult(RespuestaIngreso);
    }

    // Personal (22/10/2026): cambio de la contraseña provisional del sereno.
    public List<(string Token, SolicitudCambioContrasena Solicitud)> CambiosContrasena { get; } = [];
    public ResultadoApi<RespuestaToken> RespuestaCambioContrasena { get; set; } =
        new ResultadoApi<RespuestaToken>.Correcto(new RespuestaToken("token-nuevo", DateTimeOffset.UtcNow.AddHours(8), "Sereno"));

    public Task<ResultadoApi<RespuestaToken>> CambiarContrasenaAsync(string token, SolicitudCambioContrasena solicitud, CancellationToken ct)
    {
        CambiosContrasena.Add((token, solicitud));
        return Task.FromResult(RespuestaCambioContrasena);
    }

    // ----- HU18: modo sereno -----
    public RespuestaEstadoOperativo EstadoServidor { get; set; } = new(false, null, 30);
    public Func<SolicitudCambioEstadoOperativo, ResultadoApi<RespuestaEstadoOperativo>>? RespuestaCambio { get; set; }
    public Func<PosicionSerenoEnviada, ResultadoApi<SinContenido>> RespuestaPosicion { get; set; } =
        _ => new ResultadoApi<SinContenido>.Correcto(new SinContenido());
    public ConcurrentQueue<SolicitudCambioEstadoOperativo> CambiosEstado { get; } = new();
    public ConcurrentQueue<PosicionSerenoEnviada> PosicionesEnviadas { get; } = new();

    public Task<ResultadoApi<RespuestaEstadoOperativo>> ConsultarEstadoOperativoAsync(string token, CancellationToken ct) =>
        Task.FromResult<ResultadoApi<RespuestaEstadoOperativo>>(new ResultadoApi<RespuestaEstadoOperativo>.Correcto(EstadoServidor));

    public Task<ResultadoApi<RespuestaEstadoOperativo>> CambiarEstadoOperativoAsync(string token, SolicitudCambioEstadoOperativo solicitud, CancellationToken ct)
    {
        CambiosEstado.Enqueue(solicitud);
        if (RespuestaCambio is not null)
            return Task.FromResult(RespuestaCambio(solicitud));
        EstadoServidor = EstadoServidor with { Operativo = solicitud.Operativo, CambiadoEn = DateTimeOffset.UtcNow };
        return Task.FromResult<ResultadoApi<RespuestaEstadoOperativo>>(new ResultadoApi<RespuestaEstadoOperativo>.Correcto(EstadoServidor));
    }

    public Task<ResultadoApi<SinContenido>> EnviarPosicionSerenoAsync(string token, PosicionSerenoEnviada posicion, CancellationToken ct)
    {
        PosicionesEnviadas.Enqueue(posicion);
        return Task.FromResult(RespuestaPosicion(posicion));
    }

    // ----- HU21: despacho -----
    public Func<ResultadoApi<DespachoSereno?>> RespuestaDespacho { get; set; } = () => new ResultadoApi<DespachoSereno?>.Correcto(null);
    public int ConsultasDespacho;

    public Func<string, ResultadoApi<DespachoSereno?>>? RespuestaAvance { get; set; }
    public ConcurrentQueue<string> Avances { get; } = new();

    public Task<ResultadoApi<DespachoSereno?>> ConfirmarDesplazamientoAsync(string token, Guid casoId, CancellationToken ct) => Avanzar("desplazamiento");
    public Task<ResultadoApi<DespachoSereno?>> IniciarAtencionAsync(string token, Guid casoId, CancellationToken ct) => Avanzar("atencion");

    private Task<ResultadoApi<DespachoSereno?>> Avanzar(string avance)
    {
        Avances.Enqueue(avance);
        return Task.FromResult(RespuestaAvance?.Invoke(avance) ?? RespuestaDespacho());
    }

    // ----- HU24: cierre -----
    public Func<SolicitudCierreCaso, ResultadoApi<RespuestaCierreCaso>> RespuestaCierre { get; set; } =
        _ => new ResultadoApi<RespuestaCierreCaso>.Correcto(new RespuestaCierreCaso(Guid.NewGuid(), "Cerrada", DateTimeOffset.UtcNow));
    public ConcurrentQueue<SolicitudCierreCaso> Cierres { get; } = new();
    public List<CategoriaCatalogo> Categorias { get; set; } = [new(Guid.NewGuid(), "Robo"), new(Guid.NewGuid(), "Otros")];

    public Task<ResultadoApi<RespuestaCierreCaso>> CerrarCasoAsync(string token, Guid casoId, SolicitudCierreCaso solicitud, CancellationToken ct)
    {
        Cierres.Enqueue(solicitud);
        return Task.FromResult(RespuestaCierre(solicitud));
    }

    public Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasAsync(string token, CancellationToken ct) =>
        Task.FromResult<ResultadoApi<List<CategoriaCatalogo>>>(new ResultadoApi<List<CategoriaCatalogo>>.Correcto(Categorias));

    // ----- HU21 (ADR-07): dispositivo -----
    public ConcurrentQueue<string> DispositivosRegistrados { get; } = new();
    public int DispositivosQuitados;
    public Func<ResultadoApi<SinContenido>> RespuestaDispositivo { get; set; } = () => new ResultadoApi<SinContenido>.Correcto(new SinContenido());

    public Task<ResultadoApi<SinContenido>> RegistrarDispositivoAsync(string token, SolicitudRegistroDispositivo solicitud, CancellationToken ct)
    {
        DispositivosRegistrados.Enqueue(solicitud.Token);
        return Task.FromResult(RespuestaDispositivo());
    }

    public Task<ResultadoApi<SinContenido>> QuitarDispositivoAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref DispositivosQuitados);
        return Task.FromResult<ResultadoApi<SinContenido>>(new ResultadoApi<SinContenido>.Correcto(new SinContenido()));
    }

    public Task<ResultadoApi<DespachoSereno?>> ConsultarDespachoAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref ConsultasDespacho);
        return Task.FromResult(RespuestaDespacho());
    }

    // ----- HU10: reportes -----
    // Respuestas en orden para cada envío de reporte; la última se repite.
    public Queue<Func<SolicitudRegistroReporte, Task<ResultadoApi<RespuestaReporte>>>> RespuestasReporte { get; } = new();
    public ConcurrentQueue<SolicitudRegistroReporte> ReportesEnviados { get; } = new();
    public Func<ResultadoApi<List<CategoriaCatalogo>>>? RespuestaCategoriasReporte { get; set; }
    public int ConsultasCategoriasReporte;

    public static ResultadoApi<RespuestaReporte> ReporteRegistrado(SolicitudRegistroReporte s) =>
        new ResultadoApi<RespuestaReporte>.Correcto(new RespuestaReporte(Guid.NewGuid(), "Recibido", DateTimeOffset.UtcNow));

    public Task<ResultadoApi<RespuestaReporte>> RegistrarReporteAsync(string token, SolicitudRegistroReporte solicitud, CancellationToken ct)
    {
        ReportesEnviados.Enqueue(solicitud);
        var respuesta = RespuestasReporte.Count > 1 ? RespuestasReporte.Dequeue()
            : RespuestasReporte.TryPeek(out var ultima) ? ultima : s => Task.FromResult(ReporteRegistrado(s));
        return respuesta(solicitud);
    }

    public Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasReporteAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref ConsultasCategoriasReporte);
        return Task.FromResult(RespuestaCategoriasReporte?.Invoke() ?? new ResultadoApi<List<CategoriaCatalogo>>.Correcto(Categorias));
    }

    // ----- HU30: mis casos -----
    public Func<ResultadoApi<List<MiCasoResumen>>> RespuestaMisCasos { get; set; } = () => new ResultadoApi<List<MiCasoResumen>>.Correcto([]);
    public Func<Guid, ResultadoApi<MiCasoDetalle>> RespuestaMiCaso { get; set; } =
        _ => new ResultadoApi<MiCasoDetalle>.Rechazado(404, "No encontrado", new Dictionary<string, string[]>());
    public int ConsultasMisCasos;
    public ConcurrentQueue<Guid> CasosConsultados { get; } = new();

    public Task<ResultadoApi<List<MiCasoResumen>>> ConsultarMisCasosAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref ConsultasMisCasos);
        return Task.FromResult(RespuestaMisCasos());
    }

    public Task<ResultadoApi<MiCasoDetalle>> ConsultarMiCasoAsync(string token, Guid casoId, CancellationToken ct)
    {
        CasosConsultados.Enqueue(casoId);
        return Task.FromResult(RespuestaMiCaso(casoId));
    }

    // ----- HU09: cancelar un SOS pendiente -----
    public Func<Guid, SolicitudCancelacionSos, ResultadoApi<MiCasoDetalle>> RespuestaCancelacion { get; set; } =
        (_, _) => new ResultadoApi<MiCasoDetalle>.Rechazado(404, "No encontrado", new Dictionary<string, string[]>());
    public ConcurrentQueue<(Guid CasoId, string? Motivo)> Cancelaciones { get; } = new();

    public Task<ResultadoApi<MiCasoDetalle>> CancelarSosAsync(string token, Guid casoId, SolicitudCancelacionSos solicitud, CancellationToken ct)
    {
        Cancelaciones.Enqueue((casoId, solicitud.Motivo));
        return Task.FromResult(RespuestaCancelacion(casoId, solicitud));
    }

    // ----- HU32: evaluar un caso cerrado -----
    public Func<Guid, SolicitudEvaluacionCaso, ResultadoApi<MiEvaluacion>> RespuestaEvaluacion { get; set; } =
        (_, s) => new ResultadoApi<MiEvaluacion>.Correcto(new MiEvaluacion(s.Puntuacion ?? 0, s.Comentario, DateTimeOffset.UnixEpoch));
    public ConcurrentQueue<(Guid CasoId, SolicitudEvaluacionCaso Solicitud)> Evaluaciones { get; } = new();

    public Task<ResultadoApi<MiEvaluacion>> EvaluarCasoAsync(string token, Guid casoId, SolicitudEvaluacionCaso solicitud, CancellationToken ct)
    {
        Evaluaciones.Enqueue((casoId, solicitud));
        return Task.FromResult(RespuestaEvaluacion(casoId, solicitud));
    }

    // ----- HU31: bandeja -----
    public Func<ResultadoApi<BandejaNotificaciones>> RespuestaBandeja { get; set; } =
        () => new ResultadoApi<BandejaNotificaciones>.Correcto(new BandejaNotificaciones(0, []));
    public ConcurrentQueue<Guid> NotificacionesLeidas { get; } = new();
    public int ConsultasBandeja;

    public Task<ResultadoApi<BandejaNotificaciones>> ConsultarNotificacionesAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref ConsultasBandeja);
        return Task.FromResult(RespuestaBandeja());
    }

    public Task<ResultadoApi<SinContenido>> MarcarNotificacionLeidaAsync(string token, Guid notificacionId, CancellationToken ct)
    {
        NotificacionesLeidas.Enqueue(notificacionId);
        return Task.FromResult<ResultadoApi<SinContenido>>(new ResultadoApi<SinContenido>.Correcto(new SinContenido()));
    }

    // ----- HU34–HU37: acompañamiento -----
    // inicio: hora de inicio de la sesión; ahoraServidor: hora del servidor en la respuesta (por defecto, la del inicio).
    public static SesionAcompanamientoVista SesionActiva(DateTimeOffset inicio, int intervalo = 15, Guid? id = null, DateTimeOffset? ahoraServidor = null) =>
        new(id ?? Guid.NewGuid(), EstadosAcompanamiento.Activa, inicio, inicio.AddMinutes(30), null, ahoraServidor ?? inicio, intervalo);
    public Func<SolicitudInicioAcompanamiento, ResultadoApi<SesionAcompanamientoVista>> RespuestaInicioAcompanamiento { get; set; } =
        s => new ResultadoApi<SesionAcompanamientoVista>.Correcto(SesionActiva(DateTimeOffset.UtcNow));
    public Func<ResultadoApi<SesionAcompanamientoVista?>> RespuestaAcompanamientoActual { get; set; } =
        () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(null);
    public Func<Guid, ResultadoApi<SesionAcompanamientoVista>> RespuestaAcompanamiento { get; set; } =
        _ => new ResultadoApi<SesionAcompanamientoVista>.Rechazado(404, "No encontrado", new Dictionary<string, string[]>());
    public Func<PosicionAcompanamientoEnviada, ResultadoApi<SinContenido>> RespuestaPosicionAcompanamiento { get; set; } =
        _ => new ResultadoApi<SinContenido>.Correcto(new SinContenido());
    public Func<Guid, ResultadoApi<SesionAcompanamientoVista>>? RespuestaFinalizacion { get; set; }
    public ConcurrentQueue<SolicitudInicioAcompanamiento> IniciosAcompanamiento { get; } = new();
    public ConcurrentQueue<PosicionAcompanamientoEnviada> PosicionesAcompanamiento { get; } = new();
    public ConcurrentQueue<Guid> Finalizaciones { get; } = new();

    public Task<ResultadoApi<SesionAcompanamientoVista>> IniciarAcompanamientoAsync(string token, SolicitudInicioAcompanamiento solicitud, CancellationToken ct)
    {
        IniciosAcompanamiento.Enqueue(solicitud);
        return Task.FromResult(RespuestaInicioAcompanamiento(solicitud));
    }

    public Task<ResultadoApi<SesionAcompanamientoVista?>> ConsultarAcompanamientoActualAsync(string token, CancellationToken ct) =>
        Task.FromResult(RespuestaAcompanamientoActual());

    public Task<ResultadoApi<SesionAcompanamientoVista>> ConsultarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct) =>
        Task.FromResult(RespuestaAcompanamiento(sesionId));

    public Task<ResultadoApi<SinContenido>> EnviarPosicionAcompanamientoAsync(string token, Guid sesionId, PosicionAcompanamientoEnviada posicion, CancellationToken ct)
    {
        PosicionesAcompanamiento.Enqueue(posicion);
        return Task.FromResult(RespuestaPosicionAcompanamiento(posicion));
    }

    public Task<ResultadoApi<SesionAcompanamientoVista>> FinalizarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct)
    {
        Finalizaciones.Enqueue(sesionId);
        return Task.FromResult(RespuestaFinalizacion?.Invoke(sesionId) ?? new ResultadoApi<SesionAcompanamientoVista>.Correcto(
            new SesionAcompanamientoVista(sesionId, EstadosAcompanamiento.Finalizada, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 15)));
    }

    // ----- HU41: confirmación de bienestar -----
    public Func<Guid, ResultadoApi<ConfirmacionBienestarVista?>> RespuestaConsultaConfirmacion { get; set; } =
        _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(null);
    public Func<Guid, SolicitudRespuestaBienestar, ResultadoApi<ConfirmacionBienestarVista>>? RespuestaConfirmacion { get; set; }
    public ConcurrentQueue<Guid> ConsultasConfirmacion { get; } = new();
    public ConcurrentQueue<Guid> Presentaciones { get; } = new();
    public ConcurrentQueue<(Guid Id, SolicitudRespuestaBienestar Respuesta)> RespuestasBienestar { get; } = new();

    public Task<ResultadoApi<ConfirmacionBienestarVista?>> ConsultarConfirmacionAsync(string token, Guid sesionId, CancellationToken ct)
    {
        ConsultasConfirmacion.Enqueue(sesionId);
        return Task.FromResult(RespuestaConsultaConfirmacion(sesionId));
    }

    public Task<ResultadoApi<ConfirmacionBienestarVista>> RegistrarPresentacionConfirmacionAsync(string token, Guid confirmacionId, CancellationToken ct)
    {
        Presentaciones.Enqueue(confirmacionId);
        return Task.FromResult<ResultadoApi<ConfirmacionBienestarVista>>(new ResultadoApi<ConfirmacionBienestarVista>.Correcto(
            new ConfirmacionBienestarVista(confirmacionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null)));
    }

    public Task<ResultadoApi<ConfirmacionBienestarVista>> ResponderConfirmacionAsync(string token, Guid confirmacionId, SolicitudRespuestaBienestar respuesta,
        CancellationToken ct)
    {
        RespuestasBienestar.Enqueue((confirmacionId, respuesta));
        return Task.FromResult(RespuestaConfirmacion?.Invoke(confirmacionId, respuesta) ?? new ResultadoApi<ConfirmacionBienestarVista>.Correcto(
            new ConfirmacionBienestarVista(confirmacionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, respuesta.Respuesta, DateTimeOffset.UtcNow,
                respuesta.Respuesta == Cad.Contratos.Acompanamiento.RespuestasBienestar.NecesitoAyuda ? Guid.NewGuid() : null)));
    }

    public Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct)
    {
        SosEnviados.Enqueue(solicitud);
        var respuesta = RespuestasSos.Count > 1 ? RespuestasSos.Dequeue() : RespuestasSos.TryPeek(out var ultima) ? ultima : s => Task.FromResult(Registrado(s));
        return respuesta(solicitud);
    }
}

// DP13: registra cuándo se pide mantener la transmisión en segundo plano y cuándo se detiene.
public sealed class SegundoPlanoFalso : IServicioSegundoPlano
{
    public bool Activo { get; private set; }
    public List<DateTimeOffset> Inicios { get; } = [];
    public int Detenciones { get; private set; }
    public void Iniciar(DateTimeOffset venceEn) { Activo = true; Inicios.Add(venceEn); }
    public void Detener() { Activo = false; Detenciones++; }
    public int AvisosConfirmacion { get; private set; }
    public void AvisarConfirmacion() => AvisosConfirmacion++;
}

public sealed class AlmacenLocalFalso : IAlmacenLocal
{
    public Dictionary<string, string> Valores { get; } = [];
    public string? Leer(string clave) => Valores.GetValueOrDefault(clave);
    public void Guardar(string clave, string valor) => Valores[clave] = valor;
    public void Borrar(string clave) => Valores.Remove(clave);
}

public sealed class SesionesFalsas : IAlmacenSesion
{
    public SesionApp? Sesion { get; set; } = new("token-ficticio", "Ciudadano", DateTimeOffset.UtcNow.AddHours(8));
    public Task GuardarAsync(SesionApp sesion) { Sesion = sesion; return Task.CompletedTask; }
    public Task<SesionApp?> LeerAsync() => Task.FromResult(Sesion);
    public void Cerrar() => Sesion = null;
}

public sealed class DispositivosFalsos : IAlmacenDispositivo
{
    public DispositivoVinculado? Dispositivo { get; set; }
    public Task GuardarAsync(DispositivoVinculado dispositivo) { Dispositivo = dispositivo; return Task.CompletedTask; }
    public Task<DispositivoVinculado?> LeerAsync() => Task.FromResult(Dispositivo);
    public void Olvidar() => Dispositivo = null;
}

public sealed class NavegacionFalsa : INavegacion
{
    public List<string> Rutas { get; } = [];
    public Task IrAAsync(string ruta) { Rutas.Add(ruta); return Task.CompletedTask; }
}

public sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora;
}

// HU21: canal en tiempo real del sereno, controlado por la prueba.
public sealed class CanalFalso : ICanalSereno
{
    public event Action? DespachoAsignado;
    public event Action? Reconectado;
    public event Action? DespachoActualizado;
    public bool Conecta { get; set; } = true;
    public int Conexiones;

    public Task<bool> ConectarAsync(CancellationToken ct) { Interlocked.Increment(ref Conexiones); return Task.FromResult(Conecta); }
    public Task DesconectarAsync() => Task.CompletedTask;
    public void Avisar() => DespachoAsignado?.Invoke();
    public void Reconectar() => Reconectado?.Invoke();
    public void Actualizar() => DespachoActualizado?.Invoke();
}

// HU21 (ADR-07): notificaciones push del dispositivo, controladas por la prueba.
public sealed class PushFalso : IServicioPush
{
    public bool Disponible { get; set; } = true;
    public EstadoPermisoNotificaciones Permiso { get; set; } = EstadoPermisoNotificaciones.Desconocido;
    public EstadoPermisoNotificaciones RespuestaSolicitud { get; set; } = EstadoPermisoNotificaciones.Concedido;
    public string? Token { get; set; } = "token-fcm-ficticio";
    public int Solicitudes;
    public event Action<string>? TokenRenovado;

    public Task<EstadoPermisoNotificaciones> ConsultarPermisoAsync() => Task.FromResult(Permiso);

    public Task<EstadoPermisoNotificaciones> SolicitarPermisoAsync()
    {
        Solicitudes++;
        Permiso = RespuestaSolicitud;
        return Task.FromResult(Permiso);
    }

    public Task<string?> ObtenerTokenAsync(CancellationToken ct) => Task.FromResult(Token);
    public void Renovar(string token) { Token = token; TokenRenovado?.Invoke(token); }
}

public sealed class DictadoFalso : IDictado
{
    public bool Disponible { get; set; } = true;
    public Func<string?> Resultado { get; set; } = () => null;
    public int Llamadas;

    public Task<string?> DictarAsync()
    {
        Llamadas++;
        return Task.FromResult(Resultado());
    }
}
