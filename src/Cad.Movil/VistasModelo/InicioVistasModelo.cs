using System.Globalization;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Pantalla principal del ciudadano: preparación de la ubicación (HU06), control SOS (HU07, HU08), acceso al reporte (HU10) y
// acompañamiento (HU34–HU37: control independiente; su estado y su tiempo restante siguen visibles en el inicio, CA08).
public sealed class InicioCiudadanoVistaModelo : VistaModeloBase
{
    private readonly EmisorSos emisor;
    private readonly PreparacionUbicacion preparacion;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private readonly Action abrirAjustes;
    private readonly ServicioBandeja? bandeja;
    private readonly ServicioAcompanamiento? acompanamiento;
    private ResultadoPreparacion? estadoUbicacion;
    private int? noLeidas;
    private bool preparada;

    public InicioCiudadanoVistaModelo(EmisorSos emisor, PreparacionUbicacion preparacion, IAlmacenSesion sesiones,
        INavegacion navegacion, Action abrirAjustes, ServicioBandeja? bandeja = null, ServicioAcompanamiento? acompanamiento = null)
    {
        this.bandeja = bandeja;
        this.acompanamiento = acompanamiento;
        (this.emisor, this.preparacion, this.sesiones, this.navegacion, this.abrirAjustes) = (emisor, preparacion, sesiones, navegacion, abrirAjustes);
        EmitirSos = new ComandoAsincrono(() => emisor.ActivarAsync(), () => !SosEnCurso);
        Reintentar = new ComandoAsincrono(() => emisor.ReintentarAsync(), () => emisor.PuedeReintentar);
        PermitirUbicacion = new ComandoAsincrono(PermitirUbicacionAsync);
        AbrirAjustes = new ComandoAsincrono(() => { abrirAjustes(); return Task.CompletedTask; });
        Salir = new ComandoAsincrono(SalirAsync, () => !AcompanamientoEnCurso);
        Acompanar = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Acompanamiento));
        Reportar = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Reporte));
        VerMisCasos = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.MisCasos));
        VerNotificaciones = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Notificaciones));
        emisor.Cambio += _ => ActualizarSos();
        if (acompanamiento is not null)
            acompanamiento.Cambio += () => _ = HiloPrincipal(ActualizarAcompanamiento);
    }

    public Func<Action, Task> HiloPrincipal { get; init; } = accion => MainThread.InvokeOnMainThreadAsync(accion);

    public ComandoAsincrono Acompanar { get; }

    // HU34-CA08: con un acompañamiento activo, el inicio lo muestra con su tiempo restante. Mientras esté activo, o con su
    // cierre pendiente de enviar, no se puede salir de la sesión: primero se finaliza (como el estado operativo del sereno).
    public bool AcompanamientoEnCurso => acompanamiento?.Estado is EstadoAcompanamiento.Activo or EstadoAcompanamiento.CierrePendiente
        or EstadoAcompanamiento.Finalizando;
    public string TextoAcompanamiento => acompanamiento switch
    {
        { Estado: EstadoAcompanamiento.CierrePendiente } => "Dejaste de compartir tu ubicación; el cierre del acompañamiento se enviará al recuperar la conexión.",
        { Restante: { } r } => $"Acompañamiento activo: tu ubicación se comparte con la central. Quedan {Math.Ceiling(r.TotalMinutes):0} min.",
        _ => "",
    };

    private void ActualizarAcompanamiento()
    {
        Notificar(nameof(AcompanamientoEnCurso));
        Notificar(nameof(TextoAcompanamiento));
        Salir.ActualizarDisponibilidad();
    }

    public ComandoAsincrono EmitirSos { get; }
    public ComandoAsincrono Reintentar { get; }
    public ComandoAsincrono PermitirUbicacion { get; }
    public ComandoAsincrono AbrirAjustes { get; }
    public ComandoAsincrono Salir { get; }
    public ComandoAsincrono Reportar { get; }
    public ComandoAsincrono VerMisCasos { get; }
    public ComandoAsincrono VerNotificaciones { get; }

    // HU31: aviso de notificaciones sin leer (sin número si no se pudo consultar).
    public string TextoNotificaciones => noLeidas is > 0 ? $"Notificaciones ({noLeidas} sin leer)" : "Notificaciones";
    public bool HayNoLeidas => noLeidas is > 0;

    // ----- HU06: preparación al abrir la aplicación -----
    public bool MostrarExplicacion => estadoUbicacion == ResultadoPreparacion.NecesitaExplicacion;
    public bool MostrarAjustes => estadoUbicacion == ResultadoPreparacion.Denegado;
    public string TipoAvisoUbicacion => estadoUbicacion switch
    {
        ResultadoPreparacion.Disponible => "exito",
        ResultadoPreparacion.NecesitaExplicacion or null => "informacion",
        _ => "advertencia",
    };
    public string TextoUbicacion => estadoUbicacion switch
    {
        null => "Comprobando el permiso de ubicación…",
        ResultadoPreparacion.NecesitaExplicacion =>
            "Tu ubicación ayuda a la central a encontrarte si emites un SOS. Solo se envía en ese momento o si activas un acompañamiento; " +
            "abrir la aplicación no la comparte.",
        ResultadoPreparacion.Disponible => "Ubicación disponible. Solo se enviará si emites un SOS o activas un acompañamiento.",
        ResultadoPreparacion.SinPosicion =>
            "No se pudo obtener tu ubicación. Activa la ubicación del teléfono. Si emites un SOS, se enviará igualmente.",
        _ => "Sin permiso de ubicación. Puedes concederlo en los ajustes del teléfono. " +
             "Si emites un SOS, se enviará igualmente, sin ubicación.",
    };

    // Una sola comprobación por apertura de la app (CA01, CA02).
    public async Task AlAparecerAsync()
    {
        // HU31: cada vez que se muestra el inicio (también al volver de la bandeja) se actualiza el número sin leer.
        if (bandeja is not null)
        {
            noLeidas = await bandeja.ContarNoLeidasAsync();
            Notificar(nameof(TextoNotificaciones));
            Notificar(nameof(HayNoLeidas));
        }
        if (acompanamiento is not null)
        {
            await acompanamiento.CargarAsync();
            ActualizarAcompanamiento();
        }
        if (preparada) return;
        preparada = true;
        CambiarUbicacion(await preparacion.ComprobarAlAbrirAsync());
    }

    private async Task PermitirUbicacionAsync() => CambiarUbicacion(await preparacion.SolicitarTrasExplicarAsync());

    private void CambiarUbicacion(ResultadoPreparacion resultado)
    {
        estadoUbicacion = resultado;
        foreach (var p in new[] { nameof(MostrarExplicacion), nameof(MostrarAjustes), nameof(TipoAvisoUbicacion), nameof(TextoUbicacion) })
            Notificar(p);
    }

    // ----- HU07/HU08: SOS con una única activación -----
    public bool SosEnCurso => emisor.Estado is EstadoSos.ObteniendoUbicacion or EstadoSos.Enviando;
    public string TextoBotonSos => SosEnCurso ? "Enviando…" : "SOS";
    public bool HayEstadoSos => emisor.Estado is not EstadoSos.Listo;
    public bool PuedeReintentar => emisor.PuedeReintentar;
    public bool SesionTerminada => emisor.Estado is EstadoSos.Rechazado { Estado: 401 };
    public string TipoAvisoSos => emisor.Estado switch
    {
        EstadoSos.Confirmado => "exito",
        EstadoSos.NoConfirmado or EstadoSos.Rechazado => "error",
        _ => "informacion",
    };
    public string TextoSos => emisor.Estado switch
    {
        EstadoSos.ObteniendoUbicacion => "Obteniendo tu ubicación actual…",
        EstadoSos.Enviando { Intento: 1 } => "Enviando el SOS a la central…",
        EstadoSos.Enviando e => $"Sin respuesta todavía. Reintentando ({e.Intento})…",
        // CA03: el éxito se muestra solo con la respuesta del servidor.
        EstadoSos.Confirmado c => $"SOS registrado a las {c.Respuesta.RegistradoEn.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)} " +
            $"(estado: {c.Respuesta.Estado}). " + (c.Respuesta.UbicacionDisponible ? "Con tu ubicación." : "Ubicación no disponible."),
        EstadoSos.NoConfirmado n => n.Mensaje,
        EstadoSos.Rechazado r => r.Mensaje,
        _ => "",
    };

    private void ActualizarSos()
    {
        foreach (var p in new[] { nameof(SosEnCurso), nameof(TextoBotonSos), nameof(HayEstadoSos), nameof(PuedeReintentar),
                     nameof(SesionTerminada), nameof(TipoAvisoSos), nameof(TextoSos) })
            Notificar(p);
        EmitirSos.ActualizarDisponibilidad();
        Reintentar.ActualizarDisponibilidad();
    }

    private async Task SalirAsync()
    {
        if (acompanamiento is not null)
            await acompanamiento.ReiniciarAsync();
        sesiones.Cerrar();
        await navegacion.IrAAsync(Rutas.Ingreso);
    }
}
