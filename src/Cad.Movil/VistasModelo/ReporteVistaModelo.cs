using System.Globalization;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// CA03 y CA04: cómo indica el ciudadano la ubicación del hecho. Nada se elige por defecto.
public enum ModoUbicacionHecho { SinElegir, Mapa, PosicionActual, Texto }

// HU10 — Registrar un reporte de incidencia (S3-T01 y S3-T02, app).
public sealed class ReporteVistaModelo : VistaModeloBase
{
    // CA05: advertencia visible en el formulario.
    public const string AdvertenciaSos =
        "Si tú u otra persona están en peligro inmediato, no uses este formulario: vuelve al inicio y pulsa SOS. " +
        "Un reporte lo revisa la central después; no es una alerta urgente.";

    private static readonly TimeSpan EsperaPosicion = EmisorSos.EsperaUbicacion;

    private readonly IApiCad api;
    private readonly RegistradorReporte registrador;
    private readonly IServicioUbicacion ubicacion;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private readonly TimeProvider reloj;
    private readonly TimeZoneInfo zona;

    private IReadOnlyList<CategoriaCatalogo> categorias = [];
    private CategoriaCatalogo? categoria;
    private string descripcion = "", referencia = "", mensajeCategorias = "", mensajeUbicacion = "";
    private DateTime? fecha;
    private TimeSpan? hora;
    private ModoUbicacionHecho modo;
    private double? latitud, longitud, precision;
    private bool obteniendoPosicion, cargandoCategorias;
    private Dictionary<string, string> errores = [];

    public ReporteVistaModelo(IApiCad api, RegistradorReporte registrador, IServicioUbicacion ubicacion, IAlmacenSesion sesiones,
        INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
    {
        (this.api, this.registrador, this.ubicacion, this.sesiones, this.navegacion, this.reloj) = (api, registrador, ubicacion, sesiones, navegacion, reloj);
        this.zona = zona ?? TimeZoneInfo.Local;

        // Por defecto, el momento actual (el ciudadano lo ajusta si el hecho fue antes).
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), this.zona);
        fecha = ahora.Date;
        hora = new TimeSpan(ahora.Hour, ahora.Minute, 0);
        FechaMaxima = ahora.Date;

        CargarCategorias = new ComandoAsincrono(CargarCategoriasAsync);
        ElegirMapa = new ComandoAsincrono(() => { CambiarModo(ModoUbicacionHecho.Mapa, limpiarPunto: modo != ModoUbicacionHecho.Mapa); return Task.CompletedTask; }, () => Editable);
        ElegirReferencia = new ComandoAsincrono(() => { CambiarModo(ModoUbicacionHecho.Texto, limpiarPunto: true); return Task.CompletedTask; }, () => Editable);
        UsarPosicionActual = new ComandoAsincrono(UsarPosicionActualAsync, () => Editable);
        Enviar = new ComandoAsincrono(() => registrador.EnviarAsync(Datos()), () => Editable);
        Reintentar = new ComandoAsincrono(() => registrador.ReintentarAsync(), () => registrador.PuedeReintentar);
        IrAlSos = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Ciudadano));
        VolverAIngresar = new ComandoAsincrono(async () => { sesiones.Cerrar(); await navegacion.IrAAsync(Rutas.Ingreso); });
        registrador.Cambio += _ => ActualizarEstado();
    }

    public ComandoAsincrono CargarCategorias { get; }
    public ComandoAsincrono ElegirMapa { get; }
    public ComandoAsincrono ElegirReferencia { get; }
    public ComandoAsincrono UsarPosicionActual { get; }
    public ComandoAsincrono Enviar { get; }
    public ComandoAsincrono Reintentar { get; }
    public ComandoAsincrono IrAlSos { get; }
    public ComandoAsincrono VolverAIngresar { get; }

    // La página mueve el marcador del mapa cuando el punto lo fija la app (posición actual).
    public event Action<double, double>? PuntoCambiado;

    public string TextoAdvertenciaSos => AdvertenciaSos;

    // ----- Tipo (CA01, DP07) -----
    public IReadOnlyList<CategoriaCatalogo> Categorias { get => categorias; private set => Asignar(ref categorias, value); }
    public CategoriaCatalogo? Categoria { get => categoria; set => Asignar(ref categoria, value); }
    public string MensajeCategorias { get => mensajeCategorias; private set { Asignar(ref mensajeCategorias, value); Notificar(nameof(HayMensajeCategorias)); } }
    public bool HayMensajeCategorias => mensajeCategorias.Length > 0;

    public async Task AlAparecerAsync()
    {
        if (categorias.Count == 0 && !cargandoCategorias)
            await CargarCategoriasAsync();
    }

    private async Task CargarCategoriasAsync()
    {
        cargandoCategorias = true;
        try
        {
            MensajeCategorias = "";
            var sesion = await sesiones.LeerAsync();
            if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
            {
                MensajeCategorias = "Tu sesión terminó. Vuelve a ingresar.";
                return;
            }
            switch (await api.ConsultarCategoriasReporteAsync(sesion.Token, CancellationToken.None))
            {
                case ResultadoApi<List<CategoriaCatalogo>>.Correcto c:
                    Categorias = c.Valor;
                    break;
                case ResultadoApi<List<CategoriaCatalogo>>.Rechazado r:
                    MensajeCategorias = r.Estado == 401 ? "Tu sesión terminó. Vuelve a ingresar." : "No se pudieron cargar los tipos de incidencia.";
                    break;
                default:
                    MensajeCategorias = "No se pudieron cargar los tipos de incidencia: no hay respuesta del servidor.";
                    break;
            }
        }
        finally
        {
            cargandoCategorias = false;
        }
    }

    // ----- Descripción (DP08) -----
    public string Descripcion
    {
        get => descripcion;
        set { if (Asignar(ref descripcion, value ?? "")) Notificar(nameof(ContadorDescripcion)); }
    }
    public string ContadorDescripcion => $"{descripcion.Length} / {RegistradorReporte.MaximoDescripcion}";
    public int MaximoDescripcion => RegistradorReporte.MaximoDescripcion;
    public int MaximoReferencia => RegistradorReporte.MaximoReferencia;

    // ----- Fecha y hora aproximadas (DP08) -----
    public DateTime? Fecha { get => fecha; set => Asignar(ref fecha, value); }
    public TimeSpan? Hora { get => hora; set => Asignar(ref hora, value); }
    public DateTime FechaMaxima { get; }

    // ----- Ubicación del hecho (CA03, CA04, DP10) -----
    public ModoUbicacionHecho Modo => modo;
    public bool MostrarMapa => modo is ModoUbicacionHecho.Mapa or ModoUbicacionHecho.PosicionActual;
    public bool MostrarReferencia => modo != ModoUbicacionHecho.SinElegir;
    public bool ObteniendoPosicion { get => obteniendoPosicion; private set => Asignar(ref obteniendoPosicion, value); }
    public double? Latitud => latitud;
    public double? Longitud => longitud;
    public string EtiquetaReferencia => modo == ModoUbicacionHecho.Texto ? "Referencia del lugar" : "Referencia (opcional)";
    public string Referencia { get => referencia; set => Asignar(ref referencia, value ?? ""); }
    public string MensajeUbicacion { get => mensajeUbicacion; private set { Asignar(ref mensajeUbicacion, value); Notificar(nameof(HayMensajeUbicacion)); } }
    public bool HayMensajeUbicacion => mensajeUbicacion.Length > 0;
    public string TextoUbicacion => modo switch
    {
        ModoUbicacionHecho.Mapa when latitud is null => "Toca el mapa en el lugar del hecho.",
        ModoUbicacionHecho.Mapa => "Punto elegido en el mapa. Toca otro lugar para moverlo.",
        ModoUbicacionHecho.PosicionActual => $"Se usará tu posición actual como lugar del hecho (± {precision?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} m). " +
            "Si el hecho ocurrió en otro lugar, toca el mapa para moverlo.",
        ModoUbicacionHecho.Texto => "Describe el lugar del hecho; no se enviará ninguna posición.",
        _ => "Elige cómo indicar dónde ocurrió. Tu posición solo se usa si eliges «Usar mi posición actual».",
    };

    // La página llama a este método cuando el ciudadano toca el mapa.
    public void ElegirPuntoEnMapa(double lat, double lon)
    {
        if (!Editable || !MostrarMapa) return;
        // Un punto movido a mano ya no es la posición del dispositivo.
        modo = ModoUbicacionHecho.Mapa;
        (latitud, longitud, precision) = (lat, lon, null);
        MensajeUbicacion = "";
        NotificarUbicacion();
    }

    private void CambiarModo(ModoUbicacionHecho nuevo, bool limpiarPunto)
    {
        modo = nuevo;
        if (limpiarPunto)
            (latitud, longitud, precision) = (null, null, null);
        MensajeUbicacion = "";
        NotificarUbicacion();
    }

    // CA04: el ÚNICO lugar donde la app lee la posición del dispositivo para un reporte, y solo por elección del ciudadano.
    private async Task UsarPosicionActualAsync()
    {
        MensajeUbicacion = "";
        var permiso = await ubicacion.ConsultarPermisoAsync();
        if (permiso == EstadoPermisoUbicacion.Desconocido)
            permiso = await ubicacion.SolicitarPermisoAsync();
        if (permiso != EstadoPermisoUbicacion.Concedido)
        {
            MensajeUbicacion = "Sin permiso de ubicación. Marca el lugar en el mapa o escribe una referencia.";
            return;
        }

        ObteniendoPosicion = true;
        UbicacionSos? actual;
        try
        {
            actual = await ubicacion.ObtenerActualAsync(EsperaPosicion, CancellationToken.None).WaitAsync(EsperaPosicion, reloj);
        }
        catch (Exception)
        {
            actual = null;
        }
        finally
        {
            ObteniendoPosicion = false;
        }

        if (actual is null)
        {
            MensajeUbicacion = "No se pudo obtener tu posición. Marca el lugar en el mapa o escribe una referencia.";
            return;
        }
        modo = ModoUbicacionHecho.PosicionActual;
        (latitud, longitud, precision) = (actual.Latitud, actual.Longitud, actual.PrecisionMetros);
        NotificarUbicacion();
        PuntoCambiado?.Invoke(actual.Latitud, actual.Longitud);
    }

    private void NotificarUbicacion()
    {
        foreach (var p in new[] { nameof(Modo), nameof(MostrarMapa), nameof(MostrarReferencia), nameof(Latitud), nameof(Longitud),
                     nameof(EtiquetaReferencia), nameof(TextoUbicacion) })
            Notificar(p);
    }

    // ----- Envío (CA01, CA02, CA08) -----
    public DatosReporte Datos()
    {
        DateTimeOffset? ocurrido = null;
        if (fecha is { } f && hora is { } h)
        {
            var local = DateTime.SpecifyKind(f.Date + h, DateTimeKind.Unspecified);
            ocurrido = new DateTimeOffset(local, zona.GetUtcOffset(local));
        }
        var lugar = modo switch
        {
            ModoUbicacionHecho.Texto => new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, referencia),
            ModoUbicacionHecho.Mapa => new UbicacionHecho(FuentesUbicacionHecho.Mapa, latitud, longitud, null, referencia),
            ModoUbicacionHecho.PosicionActual => new UbicacionHecho(FuentesUbicacionHecho.PosicionActual, latitud, longitud, precision, referencia),
            _ => null,
        };
        return new DatosReporte(categoria?.Id, descripcion, ocurrido, lugar);
    }

    public bool Enviando => registrador.Estado is EstadoReporte.Enviando;
    public bool Editable => registrador.PuedeEnviar && !Enviando;
    public bool Registrado => registrador.Estado is EstadoReporte.Registrado;
    public bool PuedeReintentar => registrador.PuedeReintentar;
    public bool SesionTerminada => registrador.Estado is EstadoReporte.Rechazado { Estado: 401 };
    public bool HayEstado => registrador.Estado is not EstadoReporte.Editando;
    public string TextoBotonEnviar => Enviando ? "Enviando…" : "Enviar reporte";
    public string TipoAviso => registrador.Estado switch
    {
        EstadoReporte.Registrado => "exito",
        EstadoReporte.NoConfirmado or EstadoReporte.Rechazado => "error",
        _ => "informacion",
    };
    public string TextoEstado => registrador.Estado switch
    {
        EstadoReporte.Enviando { Intento: 1 } => "Enviando el reporte…",
        EstadoReporte.Enviando e => $"Sin respuesta todavía. Reintentando ({e.Intento})…",
        // Solo con la respuesta del servidor.
        EstadoReporte.Registrado r => $"Reporte registrado a las {TimeZoneInfo.ConvertTime(r.Respuesta.RegistradoEn, zona).ToString("HH:mm", CultureInfo.InvariantCulture)} " +
            $"(estado: {r.Respuesta.Estado}). La central lo revisará.",
        EstadoReporte.NoConfirmado n => n.Mensaje,
        EstadoReporte.Rechazado { Estado: 400 } => "Revisa los datos marcados.",
        EstadoReporte.Rechazado r => r.Mensaje,
        _ => "",
    };

    // Motivo por campo (de la comprobación previa o del servidor).
    public string ErrorCategoria => Error("categoriaId");
    public string ErrorDescripcion => Error("descripcion");
    public string ErrorFecha => Error("ocurridoAproximadamenteEn");
    public string ErrorUbicacion => errores.Where(e => e.Key.StartsWith("ubicacion", StringComparison.Ordinal)).Select(e => e.Value).FirstOrDefault() ?? "";
    public bool HayErrorCategoria => ErrorCategoria.Length > 0;
    public bool HayErrorDescripcion => ErrorDescripcion.Length > 0;
    public bool HayErrorFecha => ErrorFecha.Length > 0;
    public bool HayErrorUbicacion => ErrorUbicacion.Length > 0;

    private string Error(string campo) => errores.TryGetValue(campo, out var mensaje) ? mensaje : "";

    private void ActualizarEstado()
    {
        errores = registrador.Estado is EstadoReporte.Rechazado r
            ? r.Errores.Where(e => e.Value.Length > 0).ToDictionary(e => e.Key, e => e.Value[0], StringComparer.OrdinalIgnoreCase)
            : [];
        foreach (var p in new[] { nameof(Enviando), nameof(Editable), nameof(Registrado), nameof(PuedeReintentar), nameof(SesionTerminada),
                     nameof(HayEstado), nameof(TextoBotonEnviar), nameof(TipoAviso), nameof(TextoEstado),
                     nameof(ErrorCategoria), nameof(ErrorDescripcion), nameof(ErrorFecha), nameof(ErrorUbicacion),
                     nameof(HayErrorCategoria), nameof(HayErrorDescripcion), nameof(HayErrorFecha), nameof(HayErrorUbicacion) })
            Notificar(p);
        foreach (var comando in new[] { ElegirMapa, ElegirReferencia, UsarPosicionActual, Enviar, Reintentar })
            comando.ActualizarDisponibilidad();
    }
}
