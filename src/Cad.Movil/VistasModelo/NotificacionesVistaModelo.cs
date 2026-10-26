using Cad.Contratos.Casos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Una notificación de la bandeja: vista previa sin detalles del hecho (CA03), fecha y si ya se leyó.
public sealed class ElementoNotificacion(NotificacionCiudadanoVista notificacion, string fecha, ComandoAsincrono abrir)
{
    public string Texto => notificacion.Texto;
    public string Fecha => fecha;
    public bool NoLeida => !notificacion.Leida;
    public string Estado => notificacion.Leida ? "Leída" : "Nueva";
    public ComandoAsincrono Abrir { get; } = abrir;
    public string Descripcion => $"{(notificacion.Leida ? "" : "Nueva. ")}{notificacion.Texto} {fecha}";
}

// HU31 — bandeja de notificaciones (S3-T14, S3-T15). Al abrir una notificación se marca como leída y se muestra el caso
// vinculado en «Mis casos» (CA01), donde se ven los datos públicos permitidos (DP05).
public sealed class NotificacionesVistaModelo : ConsultaCiudadanoVistaModelo
{
    private readonly INavegacion navegacion;
    private IReadOnlyList<ElementoNotificacion> notificaciones = [];
    private int noLeidas;
    private bool consultado;

    public NotificacionesVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
        : base(api, sesiones, navegacion, reloj, zona)
    {
        this.navegacion = navegacion;
        Actualizar = new ComandoAsincrono(ActualizarAsync);
    }

    public ComandoAsincrono Actualizar { get; }
    public IReadOnlyList<ElementoNotificacion> Notificaciones { get => notificaciones; private set => Asignar(ref notificaciones, value); }
    public string Resumen => noLeidas switch
    {
        0 => "No tienes notificaciones sin leer.",
        1 => "1 notificación sin leer.",
        _ => $"{noLeidas} notificaciones sin leer.",
    };
    public bool SinNotificaciones => consultado && notificaciones.Count == 0 && !HayMensaje;
    // Movimiento (fase 4): mientras llega la primera respuesta se muestra el esqueleto de la lista (no en los refrescos).
    public bool PrimeraCarga => Cargando && !consultado;

    public Task AlAparecerAsync() => ActualizarAsync();

    private async Task ActualizarAsync()
    {
        Cargando = true;
        Notificar(nameof(PrimeraCarga));
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            var resultado = await Api.ConsultarNotificacionesAsync(token, CancellationToken.None);
            if (resultado is ResultadoApi<BandejaNotificaciones>.Correcto correcto)
            {
                Mensaje = "";
                consultado = true;
                noLeidas = correcto.Valor.NoLeidas;
                Notificaciones = correcto.Valor.Notificaciones
                    .Select(n => new ElementoNotificacion(n, Local(n.CreadaEn), new ComandoAsincrono(() => AbrirAsync(n))))
                    .ToList();
            }
            else
                SinExito(resultado, "No se encontraron tus notificaciones.");
            Notificar(nameof(Resumen));
            Notificar(nameof(SinNotificaciones));
        }
        finally
        {
            Cargando = false;
            Notificar(nameof(PrimeraCarga));
        }
    }

    private async Task AbrirAsync(NotificacionCiudadanoVista notificacion)
    {
        // Marcarla como leída no impide abrir el caso aunque falle (se volverá a intentar la próxima vez).
        if (!notificacion.Leida && await TokenAsync() is { } token)
        {
            try
            {
                await Api.MarcarNotificacionLeidaAsync(token, notificacion.Id, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        }
        await navegacion.IrAAsync($"{Rutas.MiCaso}?id={notificacion.CasoId}");
    }
}
