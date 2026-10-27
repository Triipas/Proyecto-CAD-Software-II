using System.Globalization;
using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Un caso propio en la lista «Mis casos».
public sealed class ElementoMiCaso(MiCasoResumen caso, string fecha, ComandoAsincrono abrir)
{
    public Guid Id => caso.Id;
    public string Titulo => $"{caso.Origen} · {caso.Estado}";
    // Rediseño CAD v2: la tarjeta muestra el origen como etiqueta y el estado como chip.
    public string Origen => caso.Origen;
    public string Estado => caso.Estado;
    public bool EsSos => caso.Origen == "SOS";
    public bool EsReporte => !EsSos;
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
            Notificar(nameof(PrimeraCarga));
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
    private Cad.Contratos.Casos.OpcionCatalogo? motivoElegido;

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

    // Decisión del PO del 26/10/2026: lo que el ciudadano dio en su reporte, en solo lectura (no se edita), con sus fotos.
    public bool HayReporte => caso?.Reporte is not null;
    public string ReporteCategoria => caso?.Reporte?.Categoria ?? "";
    public string ReporteDescripcion => caso?.Reporte?.Descripcion ?? "";
    public string ReporteOcurrido => caso?.Reporte is { } r ? Local(r.OcurridoAproximadamenteEn) : "";
    public string ReporteLugar => caso?.Reporte switch
    {
        { FuenteUbicacion: FuentesUbicacionHecho.Texto } => "Indicado con una referencia",
        { Latitud: { } lat, Longitud: { } lon } r => (r.FuenteUbicacion == FuentesUbicacionHecho.PosicionActual
            ? "Tu posición al reportar" : "Punto marcado en el mapa") + $" ({lat.ToString("0.00000", CultureInfo.InvariantCulture)}, {lon.ToString("0.00000", CultureInfo.InvariantCulture)})",
        _ => "",
    };
    public string ReporteReferencia => caso?.Reporte?.Referencia ?? "";
    public bool HayReferencia => ReporteReferencia.Length > 0;
    public System.Collections.ObjectModel.ObservableCollection<FotoDelCaso> FotosReporte { get; } = [];
    public bool HayFotosReporte => caso?.Reporte is { Fotos: > 0 };
    public string TextoFotosReporte => caso?.Reporte?.Fotos switch
    {
        null or 0 => "No adjuntaste fotos.",
        1 => "1 foto adjunta",
        var n => $"{n} fotos adjuntas",
    };

    // Las fotos se piden una por una después del detalle; una que no llegue simplemente no se muestra. Si otra carga empezó
    // mientras tanto (al abrir la página y al deslizar para actualizar), solo la última reemplaza las fotos: no se duplican.
    private int cargaFotos;

    private async Task CargarFotosAsync(string token)
    {
        var esta = ++cargaFotos;
        var fotos = new List<FotoDelCaso>();
        for (var n = 1; n <= (caso?.Reporte?.Fotos ?? 0); n++)
        {
            if (await Api.ConsultarFotoMiCasoAsync(token, casoId, n, CancellationToken.None) is ResultadoApi<byte[]>.Correcto { Valor: { Length: > 0 } bytes })
                fotos.Add(new FotoDelCaso(n, bytes));
        }
        if (esta != cargaFotos)
            return;
        FotosReporte.Clear();
        foreach (var foto in fotos)
            FotosReporte.Add(foto);
    }

    // HU09 — cancelar un SOS propio mientras está pendiente (para su titular, «Recibido»: DP05), indicando el motivo (CA01, CA04).
    // Asignado o en un estado posterior ya no se ofrece; si cambió mientras tanto, el servidor lo rechaza (CA03).
    // Decisión del PO del 26/10/2026: un reporte se retira igual, con motivo, mientras está «Recibido» o «En revisión».
    public const int MaximoMotivo = 500;
    public bool EsReporte => caso is { Origen: "Reporte" };
    public bool PuedeCancelar => caso is { Origen: "SOS", Estado: EstadosPublicos.Recibido }
        or { Origen: "Reporte", Estado: EstadosPublicos.Recibido or EstadosPublicos.EnRevision };
    public string TextoCancelar => EsReporte ? "Retirar este reporte" : "Cancelar este SOS";
    public string AyudaCancelacion => EsReporte
        ? "Solo se puede retirar mientras la central no haya decidido sobre él. Lo que reportaste no se borra: queda como retirado."
        : "Solo se puede cancelar mientras la central no lo haya asignado. Si tienes un acompañamiento activo, sigue activo.";
    public string IndicacionMotivo => EsReporte ? "Por qué retiras el reporte" : "Por qué ya no necesitas ayuda";
    public string TextoConfirmarCancelacion => EsReporte ? "Confirmar el retiro" : "Confirmar la cancelación";
    public string TextoNoCancelar => EsReporte ? "No retirar" : "No cancelar";
    public bool OfrecerCancelacion => PuedeCancelar && !formularioCancelacion;
    public bool FormularioCancelacion
    {
        get => formularioCancelacion && PuedeCancelar;
        private set { Asignar(ref formularioCancelacion, value); Notificar(nameof(OfrecerCancelacion)); }
    }
    // 27/10/2026: el motivo se elige de una lista (compartida con el servidor); escribirlo solo hace falta con «Otro motivo».
    public IReadOnlyList<Cad.Contratos.Casos.OpcionCatalogo> OpcionesMotivo => Cad.Contratos.Casos.MotivosCancelacion.Todos;
    public Cad.Contratos.Casos.OpcionCatalogo? MotivoElegido { get => motivoElegido; set { Asignar(ref motivoElegido, value); Notificar(nameof(EtiquetaDetalleMotivo)); } }
    public string EtiquetaDetalleMotivo => motivoElegido?.Codigo == Cad.Contratos.Casos.MotivosCancelacion.Otro ? "Cuéntanos el motivo" : "Detalle (opcional)";
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
                await CargarFotosAsync(token);
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
                     nameof(PuedeEvaluar), nameof(HayEvaluacion), nameof(TextoEvaluacion), nameof(ComentarioRegistrado),
                     nameof(HayReporte), nameof(ReporteCategoria), nameof(ReporteDescripcion), nameof(ReporteOcurrido), nameof(ReporteLugar),
                     nameof(ReporteReferencia), nameof(HayReferencia), nameof(HayFotosReporte), nameof(TextoFotosReporte), nameof(EsReporte),
                     nameof(TextoCancelar), nameof(AyudaCancelacion), nameof(IndicacionMotivo), nameof(TextoConfirmarCancelacion), nameof(TextoNoCancelar) })
            Notificar(p);
    }

    private async Task CancelarAsync()
    {
        var motivo = motivoCancelacion.Trim();
        if (motivoElegido is null)
        {
            ErrorMotivo = EsReporte ? "Elige el motivo del retiro." : "Elige el motivo de la cancelación.";
            return;
        }
        if (motivo.Length == 0 && motivoElegido.Codigo == Cad.Contratos.Casos.MotivosCancelacion.Otro)
        {
            ErrorMotivo = EsReporte ? "Indica el motivo del retiro." : "Indica el motivo de la cancelación.";
            return;
        }
        ErrorMotivo = "";
        if (await TokenAsync() is not { } token)
            return;

        Cancelando = true;
        try
        {
            var resultado = await Api.CancelarSosAsync(token, casoId, new SolicitudCancelacionSos(motivo.Length == 0 ? null : motivo, motivoElegido.Codigo), CancellationToken.None);
            switch (resultado)
            {
                case ResultadoApi<MiCasoDetalle>.Correcto correcto:
                    caso = correcto.Valor;
                    FormularioCancelacion = false;
                    MotivoCancelacion = "";
                    MotivoElegido = null;
                    Mensaje = "";
                    Confirmacion = EsReporte ? "Retiraste el reporte. La central fue avisada." : "Cancelaste el SOS. La central fue avisada.";
                    break;
                // CA03: la central ya lo asignó; se muestra el estado actual.
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 409 } rechazo:
                    FormularioCancelacion = false;
                    await ActualizarAsync();
                    Mensaje = rechazo.Mensaje;
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 400 } invalido:
                    ErrorMotivo = (invalido.Errores.TryGetValue("motivoCodigo", out var errores) || invalido.Errores.TryGetValue("motivo", out errores))
                        && errores.Length > 0 ? errores[0] : invalido.Mensaje;
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 404 }:
                    caso = null;
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 401 }:
                    SinExito(resultado, "No se encontró el caso.");
                    break;
                case ResultadoApi<MiCasoDetalle>.Rechazado:
                    Mensaje = EsReporte ? "No se pudo retirar el reporte. Inténtalo de nuevo." : "No se pudo cancelar el SOS. Inténtalo de nuevo.";
                    break;
                default:
                    // Sin respuesta: se puede reintentar sin duplicar (el servidor registra una sola cancelación, CA08).
                    Mensaje = EsReporte ? "No hay respuesta del servidor: el reporte no se retiró. Inténtalo de nuevo."
                        : "No hay respuesta del servidor: el SOS no se canceló. Inténtalo de nuevo.";
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

// 26/10/2026: una foto del reporte propio, para verla en «Mi caso».
public sealed class FotoDelCaso(int numero, byte[] contenido)
{
    public int Numero { get; } = numero;
    public ImageSource Vista { get; } = ImageSource.FromStream(() => new MemoryStream(contenido));
    public string DescripcionAccesible => $"Foto {Numero} que adjuntaste";
}
