using System.Globalization;
using Cad.Contratos.Serenos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Modo sereno — HU18: activar el estado operativo y compartir la ubicación; HU21: recibir y consultar el despacho.
public sealed class InicioSerenoVistaModelo : VistaModeloBase
{
    private readonly ServicioTurnoSereno turno;
    private readonly ServicioDespacho despacho;
    private readonly ServicioNotificaciones notificaciones;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private bool mostrarExplicacion, mostrarAjustes, mostrarCierre;
    private CategoriaCatalogo? categoriaCierre;
    private string? resultadoCierre, notasCierre;

    public InicioSerenoVistaModelo(ServicioTurnoSereno turno, ServicioDespacho despacho, ServicioNotificaciones notificaciones,
        IAlmacenSesion sesiones, INavegacion navegacion, Action abrirAjustes)
    {
        (this.turno, this.despacho, this.notificaciones, this.sesiones, this.navegacion) = (turno, despacho, notificaciones, sesiones, navegacion);
        PermitirNotificaciones = new ComandoAsincrono(() => notificaciones.PermitirAsync());
        Alternar = new ComandoAsincrono(AlternarAsync, () => !turno.SesionTerminada);
        PermitirUbicacion = new ComandoAsincrono(PermitirUbicacionAsync);
        AbrirAjustes = new ComandoAsincrono(() => { abrirAjustes(); return Task.CompletedTask; });
        Salir = new ComandoAsincrono(SalirAsync, () => PuedeSalir);
        ConfirmarDesplazamiento = new ComandoAsincrono(() => despacho.ConfirmarDesplazamientoAsync());
        IniciarAtencion = new ComandoAsincrono(() => despacho.IniciarAtencionAsync());
        AbrirCierre = new ComandoAsincrono(AbrirCierreAsync);
        CancelarCierre = new ComandoAsincrono(() => { MostrarCierre = false; ActualizarDespacho(); return Task.CompletedTask; });
        ConfirmarCierre = new ComandoAsincrono(ConfirmarCierreAsync);
        // El servicio avisa desde el hilo del envío; la interfaz se actualiza en el principal.
        turno.Cambio += () => _ = HiloPrincipal(Actualizar);
        despacho.Cambio += () => _ = HiloPrincipal(ActualizarDespacho);
        notificaciones.Cambio += () => _ = HiloPrincipal(ActualizarNotificaciones);
    }

    // Se puede sustituir en las pruebas (no hay hilo principal de MAUI en net10.0).
    public Func<Action, Task> HiloPrincipal { get; init; } = accion => MainThread.InvokeOnMainThreadAsync(accion);

    public ComandoAsincrono Alternar { get; }
    public ComandoAsincrono PermitirUbicacion { get; }
    public ComandoAsincrono AbrirAjustes { get; }
    public ComandoAsincrono Salir { get; }
    public ComandoAsincrono ConfirmarDesplazamiento { get; }
    public ComandoAsincrono IniciarAtencion { get; }
    public ComandoAsincrono AbrirCierre { get; }
    public ComandoAsincrono CancelarCierre { get; }
    public ComandoAsincrono ConfirmarCierre { get; }
    public ComandoAsincrono PermitirNotificaciones { get; }

    // ----- HU21 (ADR-07): aviso del despacho por notificación push -----
    // Primero se explica y después se pide el permiso (como la ubicación, HU06); si está denegado, se orienta a los ajustes.
    public bool MostrarExplicacionNotificaciones =>
        notificaciones.Disponible && notificaciones.Permiso == EstadoPermisoNotificaciones.Desconocido;
    public bool MostrarAjustesNotificaciones =>
        notificaciones.Disponible && notificaciones.Permiso == EstadoPermisoNotificaciones.Denegado;
    public bool NotificacionesActivas => notificaciones.Registrado;
    public string? AvisoNotificaciones => notificaciones.Aviso;
    public bool HayAvisoNotificaciones => notificaciones.Aviso is not null;

    public bool Operativo => turno.Operativo;
    public string TextoEstado => Operativo ? "Estás operativo" : "No estás operativo";
    public string TipoAvisoEstado => Operativo ? "exito" : "informacion";
    public string TextoDetalleEstado => Operativo
        ? $"Tu ubicación se envía a la central cada {turno.IntervaloSegundos} s mientras estés operativo y con la app abierta."
        : "Activa tu estado operativo para que la central sepa que estás disponible y dónde te encuentras.";
    public string TextoBoton => Operativo ? "Desactivar estado operativo" : "Activar estado operativo";

    public string TextoUltimoEnvio => turno.UltimoEnvio is { } envio
        ? $"Última posición enviada a las {envio.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}."
        : Operativo && turno.Transmitiendo ? "Enviando tu primera posición…" : "";
    public bool HayUltimoEnvio => TextoUltimoEnvio.Length > 0;

    public string? AvisoEnvio => turno.AvisoEnvio;
    public bool HayAvisoEnvio => turno.AvisoEnvio is not null && Operativo;
    public string? Error => turno.Error;
    public bool HayError => turno.Error is not null;
    public bool SesionTerminada => turno.SesionTerminada;

    // HU06 aplicado al sereno: se explica antes de pedir el permiso; si está denegado, se orienta a los ajustes.
    public bool MostrarExplicacion { get => mostrarExplicacion; private set => Asignar(ref mostrarExplicacion, value); }
    public bool MostrarAjustes { get => mostrarAjustes; private set => Asignar(ref mostrarAjustes, value); }

    // Mientras está operativo no se sale de la sesión: la central lo vería operativo sin recibir posiciones.
    // Primero se desactiva (no se desactiva solo, para no añadir un efecto que la HU no define).
    public bool PuedeSalir => !Operativo || SesionTerminada;
    public string TextoAyudaSalir => PuedeSalir ? "" : "Desactiva tu estado operativo antes de salir.";
    public bool HayAyudaSalir => !PuedeSalir;

    // ----- HU21: despacho (datos de DP06) -----
    public bool HayDespacho => despacho.Actual is not null;
    public bool SinDespacho => despacho.Actual is null && despacho.Consultado;
    public string TextoDespacho => despacho.Actual is { } d ? $"{d.Origen} · {d.Estado}" : "";
    public string TextoAsignado => despacho.Actual is { } d
        ? $"Asignado a las {Hora(d.AsignadoEn)}. Registrado a las {Hora(d.RegistradoEn)}." : "";
    public string TextoIdentificador => despacho.Actual is { } d ? $"Identificador del caso: {d.CasoId}" : "";
    // CA03: sin ubicación del dispositivo no se muestran coordenadas.
    public bool HayUbicacionDespacho => despacho.Actual?.Ubicacion is not null;
    public bool SinUbicacionDespacho => despacho.Actual is { Ubicacion: null };
    public string TextoUbicacionDespacho => despacho.Actual?.Ubicacion is { } u
        ? $"{u.Latitud.ToString("0.00000", CultureInfo.InvariantCulture)}, {u.Longitud.ToString("0.00000", CultureInfo.InvariantCulture)} · ± {u.PrecisionMetros.ToString("0", CultureInfo.InvariantCulture)} m · obtenida a las {Hora(u.ObtenidaEn)}"
        : "";
    public IReadOnlyList<ElementoInformacionDespacho> InformacionDespacho =>
        despacho.Actual?.Informacion.Select(i => new ElementoInformacionDespacho(Hora(i.RegistradaEn), i.InformacionHecho, i.ReferenciaUbicacion)).ToList()
        ?? [];
    public bool HayInformacionDespacho => despacho.Actual is { Informacion.Count: > 0 };
    public bool SinInformacionDespacho => despacho.Actual is { Informacion.Count: 0 };
    public bool AvisoNuevoDespacho => despacho.AvisoNuevo && HayDespacho;
    // HU22: «Voy en camino» solo con el caso «Asignada»; HU23: «Iniciar atención» solo «En camino».
    public bool PuedeConfirmarDesplazamiento => despacho.Actual?.Estado == DespachoSereno.EstadoAsignada;
    public bool PuedeIniciarAtencion => despacho.Actual?.Estado == DespachoSereno.EstadoEnCamino;
    public string TextoAvances => despacho.Actual is { } a
        ? string.Join(" ", new[]
          {
              a.EnCaminoEn is { } c ? $"En camino desde las {Hora(c)}." : null,
              a.EnAtencionEn is { } t ? $"En atención desde las {Hora(t)}." : null,
          }.Where(t => t is not null))
        : "";
    public bool HayAvances => TextoAvances.Length > 0;

    // ----- HU24: cierre -----
    private bool EnAtencion => despacho.Actual?.Estado == DespachoSereno.EstadoEnAtencion;
    public bool MostrarCierre { get => mostrarCierre && EnAtencion; private set => Asignar(ref mostrarCierre, value); }
    public bool PuedeAbrirCierre => EnAtencion && !mostrarCierre;
    public IReadOnlyList<CategoriaCatalogo> CategoriasCierre => despacho.Categorias;
    public CategoriaCatalogo? CategoriaCierre { get => categoriaCierre; set => Asignar(ref categoriaCierre, value); }
    public string? ResultadoCierre { get => resultadoCierre; set => Asignar(ref resultadoCierre, value); }
    public string? NotasCierre { get => notasCierre; set => Asignar(ref notasCierre, value); }
    public string? ErrorCategoriaCierre => PrimerError("categoriaId");
    public bool HayErrorCategoriaCierre => ErrorCategoriaCierre is not null;
    public string? ErrorResultadoCierre => PrimerError("resultadoPublico");
    public bool HayErrorResultadoCierre => ErrorResultadoCierre is not null;
    public string? ErrorNotasCierre => PrimerError("notasInternas");
    public bool HayErrorNotasCierre => ErrorNotasCierre is not null;
    public string? CierreReciente => despacho.CierreReciente;
    public bool HayCierreReciente => despacho.CierreReciente is not null && !HayDespacho;

    private string? PrimerError(string campo) => despacho.ErroresCierre.TryGetValue(campo, out var m) && m.Length > 0 ? m[0] : null;

    private async Task AbrirCierreAsync()
    {
        await despacho.CargarCategoriasAsync();
        MostrarCierre = true;
        ActualizarDespacho();
    }

    private async Task ConfirmarCierreAsync()
    {
        if (await despacho.CerrarAsync(CategoriaCierre?.Id, ResultadoCierre, NotasCierre))
        {
            MostrarCierre = false;
            (CategoriaCierre, ResultadoCierre, NotasCierre) = (null, null, null);
        }
        ActualizarDespacho();
    }
    public string? ErrorDespacho => despacho.Error;
    public bool HayErrorDespacho => despacho.Error is not null;

    private static string Hora(DateTimeOffset momento) => momento.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    // Cada vez que aparece la pantalla: Shell reutiliza la página entre sesiones, y el estado vive en el servidor.
    public async Task AlAparecerAsync()
    {
        await turno.CargarAsync();
        Actualizar();
        await despacho.IniciarAsync();
        await notificaciones.PrepararAsync();
    }

    private async Task AlternarAsync()
    {
        if (Operativo)
        {
            await turno.DesactivarAsync();
            return;
        }
        Interpretar(await turno.ActivarAsync());
    }

    private async Task PermitirUbicacionAsync()
    {
        MostrarExplicacion = false;
        if (await turno.SolicitarPermisoAsync() == EstadoPermisoUbicacion.Concedido)
            Interpretar(await turno.ActivarAsync());
        else
            MostrarAjustes = true;
    }

    private void Interpretar(ResultadoActivacion resultado)
    {
        MostrarExplicacion = resultado == ResultadoActivacion.NecesitaExplicarPermiso;
        MostrarAjustes = resultado == ResultadoActivacion.PermisoDenegado;
        Actualizar();
    }

    private async Task SalirAsync()
    {
        await turno.ReiniciarAsync();
        await despacho.ReiniciarAsync();
        await notificaciones.QuitarAsync(); // antes de cerrar la sesión: necesita su token
        sesiones.Cerrar();
        await navegacion.IrAAsync(Rutas.Ingreso);
    }

    private void Actualizar()
    {
        foreach (var p in new[] { nameof(Operativo), nameof(TextoEstado), nameof(TipoAvisoEstado), nameof(TextoDetalleEstado),
                     nameof(TextoBoton), nameof(TextoUltimoEnvio), nameof(HayUltimoEnvio), nameof(AvisoEnvio), nameof(HayAvisoEnvio),
                     nameof(Error), nameof(HayError), nameof(SesionTerminada), nameof(PuedeSalir), nameof(TextoAyudaSalir), nameof(HayAyudaSalir) })
            Notificar(p);
        Alternar.ActualizarDisponibilidad();
        Salir.ActualizarDisponibilidad();
    }

    private void ActualizarNotificaciones()
    {
        foreach (var p in new[] { nameof(MostrarExplicacionNotificaciones), nameof(MostrarAjustesNotificaciones),
                     nameof(NotificacionesActivas), nameof(AvisoNotificaciones), nameof(HayAvisoNotificaciones) })
            Notificar(p);
    }

    private void ActualizarDespacho()
    {
        foreach (var p in new[] { nameof(HayDespacho), nameof(SinDespacho), nameof(TextoDespacho), nameof(TextoAsignado), nameof(TextoIdentificador),
                     nameof(HayUbicacionDespacho), nameof(SinUbicacionDespacho), nameof(TextoUbicacionDespacho), nameof(InformacionDespacho),
                     nameof(HayInformacionDespacho), nameof(SinInformacionDespacho), nameof(AvisoNuevoDespacho), nameof(ErrorDespacho), nameof(HayErrorDespacho),
                     nameof(PuedeConfirmarDesplazamiento), nameof(PuedeIniciarAtencion), nameof(TextoAvances), nameof(HayAvances),
                     nameof(MostrarCierre), nameof(PuedeAbrirCierre), nameof(CategoriasCierre), nameof(ErrorCategoriaCierre), nameof(HayErrorCategoriaCierre),
                     nameof(ErrorResultadoCierre), nameof(HayErrorResultadoCierre), nameof(ErrorNotasCierre), nameof(HayErrorNotasCierre),
                     nameof(CierreReciente), nameof(HayCierreReciente) })
            Notificar(p);
    }
}

// Información que registró la central (HU12). La referencia indicada por el ciudadano se muestra aparte de la
// ubicación del dispositivo (HU21-CA03).
public sealed record ElementoInformacionDespacho(string Hora, string? InformacionHecho, string? ReferenciaUbicacion)
{
    public bool HayHecho => InformacionHecho is not null;
    public bool HayReferencia => ReferenciaUbicacion is not null;
}
