using System.Globalization;
using Cad.Contratos.Casos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Un caso propio en la lista «Mis casos».
public sealed class ElementoMiCaso(MiCasoResumen caso, string fecha, ComandoAsincrono abrir)
{
    public Guid Id => caso.Id;
    public string Titulo => $"{caso.Origen} · {caso.Estado}";
    public string Fecha => fecha;
    public string? Resultado => caso.Resultado;
    public bool HayResultado => !string.IsNullOrEmpty(caso.Resultado);
    public ComandoAsincrono Abrir { get; } = abrir;
    public string Descripcion => $"{caso.Origen}, estado {caso.Estado}, registrado el {fecha}";
}

// Un avance público del caso (DP05), sin responsables.
public sealed record ElementoAvance(string Hora, string Estado, string? Detalle)
{
    public bool HayDetalle => !string.IsNullOrEmpty(Detalle);
}

// Base común: consulta con la sesión guardada y traduce los resultados de la API a mensajes.
public abstract class ConsultaCiudadanoVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona)
    : VistaModeloBase
{
    protected IApiCad Api { get; } = api;
    protected TimeZoneInfo Zona { get; } = zona ?? TimeZoneInfo.Local;
    private string mensaje = "";
    private bool cargando, sesionTerminada;

    public string Mensaje { get => mensaje; protected set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => mensaje.Length > 0;
    public bool Cargando { get => cargando; protected set => Asignar(ref cargando, value); }
    public bool SesionTerminada { get => sesionTerminada; private set => Asignar(ref sesionTerminada, value); }

    public ComandoAsincrono VolverAIngresar { get; } = new(async () => { sesiones.Cerrar(); await navegacion.IrAAsync(Rutas.Ingreso); });

    protected string Local(DateTimeOffset momento, string formato = "dd/MM/yyyy HH:mm") =>
        TimeZoneInfo.ConvertTime(momento, Zona).ToString(formato, CultureInfo.InvariantCulture);

    // null si no hay sesión vigente (y se avisa).
    protected async Task<string?> TokenAsync()
    {
        var sesion = await sesiones.LeerAsync();
        if (sesion is not null && sesion.Vigente(reloj.GetUtcNow()))
            return sesion.Token;
        SesionTerminada = true;
        Mensaje = "Tu sesión terminó. Vuelve a ingresar.";
        return null;
    }

    protected void SinExito<T>(ResultadoApi<T> resultado, string noEncontrado)
    {
        switch (resultado)
        {
            case ResultadoApi<T>.Rechazado { Estado: 401 }:
                SesionTerminada = true;
                Mensaje = "Tu sesión terminó. Vuelve a ingresar.";
                break;
            case ResultadoApi<T>.Rechazado { Estado: 404 }:
                Mensaje = noEncontrado;
                break;
            case ResultadoApi<T>.Rechazado:
                Mensaje = "No se pudo consultar. Inténtalo de nuevo.";
                break;
            default:
                Mensaje = "No hay respuesta del servidor. Desliza hacia abajo para reintentar.";
                break;
        }
    }
}

// HU30 — «Mis casos»: cada SOS y reporte propio con origen, fecha y estado público (CA01). Se vuelve a consultar al
// abrir la pantalla y al deslizar hacia abajo, para ver el estado actual (CA02).
public sealed class MisCasosVistaModelo : ConsultaCiudadanoVistaModelo
{
    private readonly INavegacion navegacion;
    private IReadOnlyList<ElementoMiCaso> casos = [];
    private bool consultado;

    public MisCasosVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
        : base(api, sesiones, navegacion, reloj, zona)
    {
        this.navegacion = navegacion;
        Actualizar = new ComandoAsincrono(ActualizarAsync);
    }

    public ComandoAsincrono Actualizar { get; }
    public IReadOnlyList<ElementoMiCaso> Casos { get => casos; private set { Asignar(ref casos, value); Notificar(nameof(SinCasos)); } }
    public bool SinCasos => consultado && casos.Count == 0 && !HayMensaje;

    public Task AlAparecerAsync() => ActualizarAsync();

    private async Task ActualizarAsync()
    {
        Cargando = true;
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            var resultado = await Api.ConsultarMisCasosAsync(token, CancellationToken.None);
            if (resultado is ResultadoApi<List<MiCasoResumen>>.Correcto correcto)
            {
                Mensaje = "";
                consultado = true;
                Casos = correcto.Valor.Select(c => new ElementoMiCaso(c, Local(c.RegistradoEn),
                    new ComandoAsincrono(() => navegacion.IrAAsync($"{Rutas.MiCaso}?id={c.Id}")))).ToList();
            }
            else
                SinExito(resultado, "No se encontraron tus casos.");
            Notificar(nameof(SinCasos));
        }
        finally
        {
            Cargando = false;
        }
    }
}

// HU30 — detalle de un caso propio: estado actual, avances públicos y, si terminó, el resultado público (CA01–CA03).
public sealed class MiCasoVistaModelo : ConsultaCiudadanoVistaModelo
{
    private Guid casoId;
    private MiCasoDetalle? caso;

    private bool formularioCancelacion, cancelando;
    private string motivoCancelacion = "", errorMotivo = "", confirmacion = "";

    public MiCasoVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
        : base(api, sesiones, navegacion, reloj, zona)
    {
        Actualizar = new ComandoAsincrono(ActualizarAsync);
        MostrarCancelacion = new ComandoAsincrono(() => { FormularioCancelacion = true; return Task.CompletedTask; });
        OcultarCancelacion = new ComandoAsincrono(() => { FormularioCancelacion = false; ErrorMotivo = ""; return Task.CompletedTask; });
        ConfirmarCancelacion = new ComandoAsincrono(CancelarAsync);
        EnviarEvaluacion = new ComandoAsincrono(EvaluarAsync);
        Opciones = CrearOpciones();
    }

    public ComandoAsincrono Actualizar { get; }
    public bool HayCaso => caso is not null;
    public string Titulo => caso is { } c ? $"{c.Origen} · {c.Estado}" : "";
    public string Registrado => caso is { } c ? $"Registrado el {Local(c.RegistradoEn)}" : "";
    public string Identificador => caso is { } c ? $"Identificador: {c.Id}" : "";
    public string? Resultado => caso?.Resultado;
    public bool HayResultado => !string.IsNullOrEmpty(caso?.Resultado);
    public IReadOnlyList<ElementoAvance> Avances =>
        caso?.Avances.Select(a => new ElementoAvance(Local(a.En), a.Estado, a.Detalle)).ToList() ?? [];

    // HU09 — cancelar un SOS propio mientras está pendiente (para su titular, «Recibido»: DP05), indicando el motivo (CA01, CA04).
    // Asignado o en un estado posterior ya no se ofrece; si cambió mientras tanto, el servidor lo rechaza (CA03).
    public const int MaximoMotivo = 500;
    public bool PuedeCancelar => caso is { Origen: "SOS", Estado: EstadosPublicos.Recibido };
    public bool OfrecerCancelacion => PuedeCancelar && !formularioCancelacion;
    public bool FormularioCancelacion
    {
        get => formularioCancelacion && PuedeCancelar;
        private set { Asignar(ref formularioCancelacion, value); Notificar(nameof(OfrecerCancelacion)); }
    }
    public string MotivoCancelacion { get => motivoCancelacion; set { Asignar(ref motivoCancelacion, value ?? ""); Notificar(nameof(ContadorMotivo)); } }
    public string ContadorMotivo => $"{motivoCancelacion.Length}/{MaximoMotivo}";
    public string ErrorMotivo { get => errorMotivo; private set { Asignar(ref errorMotivo, value); Notificar(nameof(HayErrorMotivo)); } }
    public bool HayErrorMotivo => errorMotivo.Length > 0;
    public bool Cancelando { get => cancelando; private set => Asignar(ref cancelando, value); }
    public string Confirmacion { get => confirmacion; private set { Asignar(ref confirmacion, value); Notificar(nameof(HayConfirmacion)); } }
    public bool HayConfirmacion => confirmacion.Length > 0;
    public ComandoAsincrono MostrarCancelacion { get; }
    public ComandoAsincrono OcultarCancelacion { get; }
    public ComandoAsincrono ConfirmarCancelacion { get; }

    // HU32 — evaluación voluntaria de un caso cerrado: puntuación de 1 a 5 y comentario opcional (CA01, CA02). Una por caso
    // (CA03): ya evaluado, se muestra la evaluación registrada en lugar del formulario. No evaluar no restringe nada (CA06).
    public const int MaximoComentario = 2000;
    private int? puntuacion;
    private string comentario = "", errorEvaluacion = "";
    private bool evaluando;
    private Guid idEvaluacion = Guid.NewGuid();

    public bool PuedeEvaluar => caso is { Estado: EstadosPublicos.Cerrado, Evaluacion: null };
    public bool HayEvaluacion => caso?.Evaluacion is not null;
    public string TextoEvaluacion => caso?.Evaluacion is { } e ? $"Tu evaluación: {e.Puntuacion} de 5" : "";
    public string ComentarioRegistrado => caso?.Evaluacion is { } e ? e.Comentario is { } c ? $"Comentario: {c}" : "Sin comentario." : "";
    public IReadOnlyList<OpcionPuntuacion> Opciones { get; private set; }
    public int? Puntuacion
    {
        get => puntuacion;
        set { if (Asignar(ref puntuacion, value)) { Opciones = CrearOpciones(); Notificar(nameof(Opciones)); } }
    }
    public string Comentario { get => comentario; set { Asignar(ref comentario, value ?? ""); Notificar(nameof(ContadorComentario)); } }
    public string ContadorComentario => $"{comentario.Length}/{MaximoComentario}";
    public string ErrorEvaluacion { get => errorEvaluacion; private set { Asignar(ref errorEvaluacion, value); Notificar(nameof(HayErrorEvaluacion)); } }
    public bool HayErrorEvaluacion => errorEvaluacion.Length > 0;
    public bool Evaluando { get => evaluando; private set => Asignar(ref evaluando, value); }
    public ComandoAsincrono EnviarEvaluacion { get; }

    private IReadOnlyList<OpcionPuntuacion> CrearOpciones() =>
        Enumerable.Range(1, 5).Select(v => new OpcionPuntuacion(v, v == puntuacion,
            new ComandoAsincrono(() => { Puntuacion = v; ErrorEvaluacion = ""; return Task.CompletedTask; }))).ToList();

    public Task CargarAsync(Guid id)
    {
        casoId = id;
        return ActualizarAsync();
    }

    private async Task ActualizarAsync()
    {
        Cargando = true;
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            var resultado = await Api.ConsultarMiCasoAsync(token, casoId, CancellationToken.None);
            if (resultado is ResultadoApi<MiCasoDetalle>.Correcto correcto)
            {
                Mensaje = "";
                caso = correcto.Valor;
            }
            else
            {
                // CA04: un caso ajeno o inexistente no se muestra.
                if (resultado is ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 404 })
                    caso = null;
                SinExito(resultado, "No se encontró el caso.");
            }
            NotificarCaso();
        }
        finally
        {
            Cargando = false;
        }
    }

    private void NotificarCaso()
    {
        foreach (var p in new[] { nameof(HayCaso), nameof(Titulo), nameof(Registrado), nameof(Identificador), nameof(Resultado),
                     nameof(HayResultado), nameof(Avances), nameof(PuedeCancelar), nameof(OfrecerCancelacion), nameof(FormularioCancelacion),
                     nameof(PuedeEvaluar), nameof(HayEvaluacion), nameof(TextoEvaluacion), nameof(ComentarioRegistrado) })
            Notificar(p);
    }

    private async Task CancelarAsync()
    {
        var motivo = motivoCancelacion.Trim();
        if (motivo.Length == 0)
        {
            ErrorMotivo = "Indica el motivo de la cancelación.";
            return;
        }
        ErrorMotivo = "";
        if (await TokenAsync() is not { } token)
            return;

        Cancelando = true;
        try
        {
            var resultado = await Api.CancelarSosAsync(token, casoId, new SolicitudCancelacionSos(motivo), CancellationToken.None);
            switch (resultado)
            {
                case ResultadoApi<MiCasoDetalle>.Correcto correcto:
                    caso = correcto.Valor;
                    FormularioCancelacion = false;
                    MotivoCancelacion = "";
                    Mensaje = "";
                    Confirmacion = "Cancelaste el SOS. La central fue avisada.";
                    break;
                // CA03: la central ya lo asignó; se muestra el estado actual.
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 409 } rechazo:
                    FormularioCancelacion = false;
                    await ActualizarAsync();
                    Mensaje = rechazo.Mensaje;
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 400 } invalido:
                    ErrorMotivo = invalido.Errores.TryGetValue("motivo", out var errores) && errores.Length > 0 ? errores[0] : invalido.Mensaje;
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 404 }:
                    caso = null;
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 401 }:
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado:
                    Mensaje = "No se pudo cancelar el SOS. Inténtalo de nuevo.";
                    break;
                default:
                    // Sin respuesta: se puede reintentar sin duplicar (el servidor registra una sola cancelación, CA08).
                    Mensaje = "No hay respuesta del servidor: el SOS no se canceló. Inténtalo de nuevo.";
                    break;
            }
            NotificarCaso();
        }
        finally
        {
            Cancelando = false;
        }
    }

    private async Task EvaluarAsync()
    {
        // CA02: sin puntuación no se envía.
        if (puntuacion is not { } valor)
        {
            ErrorEvaluacion = "Elige una puntuación de 1 a 5.";
            return;
        }
        ErrorEvaluacion = "";
        if (await TokenAsync() is not { } token)
            return;

        Evaluando = true;
        try
        {
            // El mismo identificador en cada reintento técnico: el servidor no registra una segunda evaluación (CA03).
            var resultado = await Api.EvaluarCasoAsync(token, casoId,
                new SolicitudEvaluacionCaso(idEvaluacion, valor, string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim()), CancellationToken.None);
            switch (resultado)
            {
                case ResultadoApi<MiEvaluacion>.Correcto correcto when caso is not null:
                    caso = caso with { Evaluacion = correcto.Valor };
                    Mensaje = "";
                    Confirmacion = "Gracias por tu evaluación.";
                    break;
                // CA03 y CA04: ya evaluado (por ejemplo, desde otro dispositivo) o el caso no está cerrado: se muestra lo actual.
                case ResultadoApi<MiEvaluacion>.Rechazado { Estado: 409 } rechazo:
                    await ActualizarAsync();
                    Mensaje = rechazo.Mensaje;
                    break;
                case ResultadoApi<MiEvaluacion>.Rechazado { Estado: 400 } invalida:
                    ErrorEvaluacion = invalida.Errores.Values.SelectMany(e => e).FirstOrDefault() ?? invalida.Mensaje;
                    break;
                case ResultadoApi<MiEvaluacion>.Rechazado { Estado: 404 }:
                    caso = null;
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiEvaluacion>.Rechazado { Estado: 401 }:
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiEvaluacion>.Rechazado:
                    Mensaje = "No se pudo registrar la evaluación. Inténtalo de nuevo.";
                    break;
                default:
                    Mensaje = "No hay respuesta del servidor: la evaluación no se registró. Inténtalo de nuevo.";
                    break;
            }
            NotificarCaso();
        }
        finally
        {
            Evaluando = false;
        }
    }
}

// HU32: una opción de puntuación (1 a 5) del formulario de evaluación.
public sealed record OpcionPuntuacion(int Valor, bool Elegida, ComandoAsincrono Elegir)
{
    public string Texto => Valor.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Descripcion => $"Puntuación {Valor} de 5{(Elegida ? ", elegida" : "")}";
}
