using System.Globalization;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// HU34–HU37 — pantalla «Acompañamiento» del ciudadano. El control es independiente del SOS y del reporte (HU34-CA05).
public sealed class AcompanamientoVistaModelo : VistaModeloBase
{
    // HU35-CA05 y DP13 (16/10/2026): con o sin la app abierta, solo con conexión. La interfaz no promete más que eso.
    public const string TextoEntorno =
        "Tu ubicación se sigue enviando aunque cierres la aplicación o apagues la pantalla, mientras veas la notificación «Acompañamiento activo». Necesita conexión: sin ella, tu posición no llega a la central.";

    private readonly ServicioAcompanamiento servicio;
    private readonly IServicioUbicacion ubicacion;
    private readonly INavegacion navegacion;
    private readonly TimeProvider reloj;
    private ResultadoInicioAcompanamiento? ultimoIntento;
    private bool finalidadAceptada;
    private CancellationTokenSource? segundero;
    private bool visible;

    public AcompanamientoVistaModelo(ServicioAcompanamiento servicio, IServicioUbicacion ubicacion, INavegacion navegacion,
        Action abrirAjustes, TimeProvider reloj)
    {
        (this.servicio, this.ubicacion, this.navegacion, this.reloj) = (servicio, ubicacion, navegacion, reloj);
        Iniciar = new ComandoAsincrono(IniciarAsync, () => PuedeIniciar);
        PermitirUbicacion = new ComandoAsincrono(PermitirUbicacionAsync);
        AbrirAjustes = new ComandoAsincrono(() => { abrirAjustes(); return Task.CompletedTask; });
        Finalizar = new ComandoAsincrono(() => servicio.FinalizarAsync(), () => servicio.Activo);
        Volver = new ComandoAsincrono(() => navegacion.IrAAsync(".."));
        VolverAIngresar = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Ingreso));
        ResponderEstoyBien = new ComandoAsincrono(() => servicio.ResponderConfirmacionAsync(necesitoAyuda: false), () => !servicio.RespondiendoConfirmacion);
        ResponderNecesitoAyuda = new ComandoAsincrono(() => servicio.ResponderConfirmacionAsync(necesitoAyuda: true), () => !servicio.RespondiendoConfirmacion);
        InformarEstoyBien = Informar(EstadosCiudadano.EstoyBien);
        InformarAlgoNoEstaBien = Informar(EstadosCiudadano.AlgoNoEstaBien);
        InformarNecesitoAyuda = Informar(EstadosCiudadano.NecesitoAyuda);
        InformarLlegue = Informar(EstadosCiudadano.Llegue);
        servicio.Cambio += () => _ = HiloPrincipal(Actualizar);
    }

    // Los avisos del servicio llegan desde el bucle de envío: los enlaces de MAUI se actualizan en el hilo principal.
    public Func<Action, Task> HiloPrincipal { get; init; } = accion => MainThread.InvokeOnMainThreadAsync(accion);

    public ComandoAsincrono Iniciar { get; }
    public ComandoAsincrono PermitirUbicacion { get; }
    public ComandoAsincrono AbrirAjustes { get; }
    public ComandoAsincrono Finalizar { get; }
    public ComandoAsincrono Volver { get; }
    public ComandoAsincrono VolverAIngresar { get; }
    public ComandoAsincrono ResponderEstoyBien { get; }
    public ComandoAsincrono ResponderNecesitoAyuda { get; }
    public ComandoAsincrono InformarEstoyBien { get; }
    public ComandoAsincrono InformarAlgoNoEstaBien { get; }
    public ComandoAsincrono InformarNecesitoAyuda { get; }
    public ComandoAsincrono InformarLlegue { get; }

    private ComandoAsincrono Informar(string estado) =>
        new(() => servicio.InformarEstadoAsync(estado), () => servicio.Activo && !servicio.InformandoEstado);

    // 26/10/2026 — «¿Cómo vas?»: el ciudadano avisa a la central sin esperar a que le pregunte. Mientras la central espera una
    // confirmación, se muestra esa (tiene las mismas respuestas).
    public bool MostrarComoVas => servicio.Activo && servicio.Confirmacion is null;
    public bool InformandoEstado => servicio.InformandoEstado;
    public bool HayResultadoEstado => servicio.ResultadoEstado is not null;
    public string ResultadoEstado => servicio.ResultadoEstado ?? "";
    public string TipoResultadoEstado => servicio.EstadoNoConfirmado ? "error" : "exito";

    // HU41-CA01: la confirmación que pidió la central, con sus dos respuestas.
    public bool HayConfirmacion => servicio.Confirmacion is not null;
    public string TextoConfirmacion => servicio.Confirmacion is { } c
        ? $"La central te pide confirmar cómo estás (a las {c.SolicitadaEn.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)})."
        : "";
    public bool RespondiendoConfirmacion => servicio.RespondiendoConfirmacion;
    public bool HayResultadoConfirmacion => servicio.ResultadoConfirmacion is not null;
    public string ResultadoConfirmacion => servicio.ResultadoConfirmacion ?? "";
    public string TipoResultadoConfirmacion => servicio.ConfirmacionNoConfirmada ? "error" : "exito";

    // HU34-CA02: el ciudadano marca expresamente que acepta compartir su ubicación con la central durante el trayecto.
    public bool FinalidadAceptada
    {
        get => finalidadAceptada;
        set
        {
            if (Asignar(ref finalidadAceptada, value) && ultimoIntento == ResultadoInicioAcompanamiento.FaltaFinalidad)
            {
                ultimoIntento = null;
                Notificar(nameof(MensajeInicio));
                Notificar(nameof(HayMensajeInicio));
            }
        }
    }

    public EstadoAcompanamiento Estado => servicio.Estado;
    public bool MostrarInicio => servicio.Estado is EstadoAcompanamiento.SinSesion or EstadoAcompanamiento.NoConfirmado
        or EstadoAcompanamiento.Finalizado or EstadoAcompanamiento.Vencido or EstadoAcompanamiento.Iniciando;
    public bool MostrarActivo => servicio.Activo;
    // Sin acompañamiento activo, la pantalla vuelve a ser el formulario de inicio (con cómo terminó el anterior).
    public bool MostrarFormulario => !MostrarActivo;
    // Se espera la respuesta de la central a una confirmación o a un aviso propio.
    public bool Ocupado => servicio.RespondiendoConfirmacion || servicio.InformandoEstado;
    public bool PuedeIniciar => MostrarInicio && servicio.Estado != EstadoAcompanamiento.Iniciando;
    public string TextoBotonIniciar => servicio.Estado switch
    {
        EstadoAcompanamiento.Iniciando => "Iniciando…",
        EstadoAcompanamiento.NoConfirmado => "Reintentar el inicio",
        _ => "Iniciar acompañamiento",
    };

    // HU34-CA01/CA08: estado activo y tiempo restante.
    public string TiempoRestante => servicio.Restante is { } r ? $"{(int)r.TotalMinutes:00}:{r.Seconds:00}" : "";
    public string TextoActivo => servicio.Sesion is { } s
        ? $"Acompañamiento activo hasta las {s.VenceEn.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}. Termina solo a los 30 minutos; no se renueva."
        : "";

    // 26/10/2026 — rediseño: lo que hace sentir acompañado sin prometer de más. El «latido» dice cuándo llegó la última posición
    // a la central (solo lo que el servidor confirmó) y, si algo falla, la condición real (HU35-CA04). El tiempo pasa a segundo
    // plano: una barra y la hora de fin.
    public bool LatidoAlDia => servicio.Condicion is null && servicio.UltimoEnvio is not null;
    public string TextoLatido => servicio.Condicion
        ?? (servicio.UltimoEnvio is { } u ? $"Tu ubicación llegó a la central {Hace(reloj.GetUtcNow() - u)}." : "Enviando tu ubicación a la central…");
    public double ProgresoRestante => servicio.Restante is { } r ? Math.Clamp(r / TimeSpan.FromMinutes(30), 0, 1) : 0;
    public string TextoRestante => servicio.Restante is { } r && servicio.Sesion is { } s
        ? $"Quedan {Math.Max(1, (int)Math.Ceiling(r.TotalMinutes))} min · hasta las {s.VenceEn.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}"
        : "";

    // Lo último entre el ciudadano y la central, lo más reciente primero (el inicio siempre está al final).
    public IReadOnlyList<LineaActividad> Actividad
    {
        get
        {
            var lineas = servicio.Actividad.Select(a => new LineaActividad(Hora(a.Momento), a.Texto)).Reverse().Take(3).ToList();
            if (servicio.Sesion is { } s && lineas.Count < 3)
                lineas.Add(new LineaActividad(Hora(s.IniciadaEn), "Iniciaste el acompañamiento."));
            return lineas;
        }
    }

    // El recorrido que ya recibió la central, para el mapa de la pantalla.
    public IReadOnlyList<UbicacionSos> Recorrido => servicio.Recorrido;
    public event Action? RecorridoCambio;
    private int puntosMostrados = -1;
    private bool alDiaMostrado;

    private static string Hora(DateTimeOffset momento) => momento.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Hace(TimeSpan tiempo) => tiempo.TotalSeconds < 5 ? "hace un momento"
        : tiempo.TotalSeconds < 60 ? $"hace {(int)tiempo.TotalSeconds} s"
        : $"hace {(int)tiempo.TotalMinutes} min";

    // HU35-CA04: la condición real de la transmisión.
    public string TextoTransmision => servicio.Condicion
        ?? (servicio.UltimoEnvio is { } u ? $"Última posición enviada a las {u.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}." : "Enviando tu posición…");
    public string TipoAvisoTransmision => servicio.Condicion is null ? "exito" : "advertencia";

    // Resultado del estado general (inicio no confirmado, finalización, vencimiento, cierre pendiente).
    public bool HayEstado => servicio.Estado is not (EstadoAcompanamiento.SinSesion or EstadoAcompanamiento.Activo) || servicio.Error is not null;
    public string TipoAvisoEstado => servicio.Estado switch
    {
        EstadoAcompanamiento.Finalizado or EstadoAcompanamiento.Vencido => "exito",
        EstadoAcompanamiento.NoConfirmado or EstadoAcompanamiento.CierrePendiente => "advertencia",
        _ when servicio.Error is not null => "error",
        _ => "informacion",
    };
    public string TextoEstado => servicio.Estado switch
    {
        EstadoAcompanamiento.Iniciando => "Iniciando el acompañamiento con la central…",
        EstadoAcompanamiento.Finalizando => "Dejaste de compartir tu ubicación. Comunicando el cierre a la central…",
        // HU36-CA03: la captura ya se detuvo; el cierre se envía al recuperar la conexión (efecto en la central: DP20).
        EstadoAcompanamiento.CierrePendiente =>
            "Dejaste de compartir tu ubicación. No hay conexión con la central: el cierre se enviará en cuanto vuelva la conexión.",
        EstadoAcompanamiento.Finalizado => "Acompañamiento finalizado. Tu ubicación ya no se comparte.",
        // HU37: termina solo a los 30 minutos y no se renueva.
        EstadoAcompanamiento.Vencido => "El acompañamiento terminó a los 30 minutos. Tu ubicación ya no se comparte. Si lo necesitas, inicia uno nuevo.",
        _ => servicio.Error ?? "",
    };

    public bool SesionTerminada => servicio.SesionTerminada;

    // Resultado del último intento de inicio que no llegó al servidor (finalidad, permiso, ubicación).
    public bool HayMensajeInicio => MensajeInicio.Length > 0;
    public string MensajeInicio => ultimoIntento switch
    {
        ResultadoInicioAcompanamiento.FaltaFinalidad => "Para iniciar, marca que aceptas compartir tu ubicación con la central durante el trayecto.",
        ResultadoInicioAcompanamiento.NecesitaExplicarPermiso =>
            "El acompañamiento necesita el permiso de ubicación. Tu ubicación solo se comparte mientras el acompañamiento esté activo.",
        ResultadoInicioAcompanamiento.PermisoDenegado => "Sin permiso de ubicación no se puede iniciar el acompañamiento. Puedes concederlo en los ajustes del teléfono.",
        ResultadoInicioAcompanamiento.SinUbicacion or ResultadoInicioAcompanamiento.OtraSesionActiva or ResultadoInicioAcompanamiento.Rechazado
            => servicio.Error ?? "",
        _ => "",
    };
    public bool MostrarPermitir => ultimoIntento == ResultadoInicioAcompanamiento.NecesitaExplicarPermiso;
    public bool MostrarAjustes => ultimoIntento == ResultadoInicioAcompanamiento.PermisoDenegado;

    public async Task AlAparecerAsync()
    {
        visible = true;
        await servicio.CargarAsync();
        Actualizar();
        IniciarSegundero();
    }

    public void AlDesaparecer()
    {
        visible = false;
        segundero?.Cancel();
        segundero = null;
    }

    private async Task IniciarAsync()
    {
        ultimoIntento = await servicio.IniciarAsync(FinalidadAceptada);
        if (ultimoIntento == ResultadoInicioAcompanamiento.OtraSesionActiva)
            await servicio.CargarAsync(); // muestra la sesión que ya estaba activa
        Actualizar();
    }

    private async Task PermitirUbicacionAsync()
    {
        if (await ubicacion.SolicitarPermisoAsync() == EstadoPermisoUbicacion.Concedido)
            await IniciarAsync();
        else
        {
            ultimoIntento = ResultadoInicioAcompanamiento.PermisoDenegado;
            Actualizar();
        }
    }

    // El tiempo restante se actualiza cada segundo mientras la pantalla está visible.
    private void IniciarSegundero()
    {
        segundero?.Cancel();
        var propio = segundero = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(1), reloj);
            try
            {
                while (await temporizador.WaitForNextTickAsync(propio.Token))
                    await HiloPrincipal(() =>
                    {
                        Notificar(nameof(TiempoRestante));
                        Notificar(nameof(TextoLatido));
                        Notificar(nameof(ProgresoRestante));
                        Notificar(nameof(TextoRestante));
                    });
            }
            catch (OperationCanceledException) { }
        });
    }

    private void Actualizar()
    {
        foreach (var p in new[] { nameof(Estado), nameof(MostrarInicio), nameof(MostrarActivo), nameof(PuedeIniciar), nameof(TextoBotonIniciar),
                     nameof(TiempoRestante), nameof(TextoActivo), nameof(TextoTransmision), nameof(TipoAvisoTransmision), nameof(HayEstado),
                     nameof(TipoAvisoEstado), nameof(TextoEstado), nameof(SesionTerminada), nameof(HayMensajeInicio), nameof(MensajeInicio),
                     nameof(MostrarPermitir), nameof(MostrarAjustes), nameof(HayConfirmacion), nameof(TextoConfirmacion),
                     nameof(RespondiendoConfirmacion), nameof(HayResultadoConfirmacion), nameof(ResultadoConfirmacion), nameof(TipoResultadoConfirmacion),
                     nameof(MostrarComoVas), nameof(InformandoEstado), nameof(HayResultadoEstado), nameof(ResultadoEstado), nameof(TipoResultadoEstado),
                     nameof(MostrarFormulario), nameof(Ocupado), nameof(LatidoAlDia), nameof(TextoLatido), nameof(ProgresoRestante), nameof(TextoRestante), nameof(Actividad) })
            Notificar(p);
        // El mapa solo se vuelve a dibujar si llegó una posición nueva o cambió el estado de la transmisión.
        if (servicio.Recorrido.Count != puntosMostrados || LatidoAlDia != alDiaMostrado)
        {
            (puntosMostrados, alDiaMostrado) = (servicio.Recorrido.Count, LatidoAlDia);
            RecorridoCambio?.Invoke();
        }
        Iniciar.ActualizarDisponibilidad();
        Finalizar.ActualizarDisponibilidad();
        ResponderEstoyBien.ActualizarDisponibilidad();
        ResponderNecesitoAyuda.ActualizarDisponibilidad();
        foreach (var comando in new[] { InformarEstoyBien, InformarAlgoNoEstaBien, InformarNecesitoAyuda, InformarLlegue })
            comando.ActualizarDisponibilidad();
        // HU41-CA01: se registra la presentación cuando la pantalla la muestra.
        if (visible && HayConfirmacion)
            _ = servicio.RegistrarPresentacionAsync();
    }

}

public sealed record LineaActividad(string Hora, string Texto);
