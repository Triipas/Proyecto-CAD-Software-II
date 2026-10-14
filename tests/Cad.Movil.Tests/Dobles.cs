using System.Collections.Concurrent;
using Cad.Contratos.Cuentas;
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
    public ResultadoApi<RespuestaRegistroCiudadano> RespuestaRegistro { get; set; } =
        new ResultadoApi<RespuestaRegistroCiudadano>.Correcto(new RespuestaRegistroCiudadano(Guid.NewGuid()));
    public int Registros { get; private set; }
    public int Ingresos { get; private set; }

    public static ResultadoApi<RespuestaSos> Registrado(SolicitudEmisionSos s) =>
        new ResultadoApi<RespuestaSos>.Correcto(new RespuestaSos(Guid.NewGuid(), "Pendiente", DateTimeOffset.UtcNow, s.Ubicacion is not null));

    public Task<ResultadoApi<RespuestaRegistroCiudadano>> RegistrarAsync(SolicitudRegistroCiudadano solicitud, CancellationToken ct)
    {
        Registros++;
        return Task.FromResult(RespuestaRegistro);
    }

    public Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct)
    {
        Ingresos++;
        return Task.FromResult(RespuestaIngreso);
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

    public Task<ResultadoApi<DespachoSereno?>> ConsultarDespachoAsync(string token, CancellationToken ct)
    {
        Interlocked.Increment(ref ConsultasDespacho);
        return Task.FromResult(RespuestaDespacho());
    }

    public Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct)
    {
        SosEnviados.Enqueue(solicitud);
        var respuesta = RespuestasSos.Count > 1 ? RespuestasSos.Dequeue() : RespuestasSos.TryPeek(out var ultima) ? ultima : s => Task.FromResult(Registrado(s));
        return respuesta(solicitud);
    }
}

public sealed class SesionesFalsas : IAlmacenSesion
{
    public SesionApp? Sesion { get; set; } = new("token-ficticio", "Ciudadano", DateTimeOffset.UtcNow.AddHours(8));
    public Task GuardarAsync(SesionApp sesion) { Sesion = sesion; return Task.CompletedTask; }
    public Task<SesionApp?> LeerAsync() => Task.FromResult(Sesion);
    public void Cerrar() => Sesion = null;
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
