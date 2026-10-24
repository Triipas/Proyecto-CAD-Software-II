using System.Globalization;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// CA03 y CA04: cómo indica el ciudadano la ubicación del hecho. Nada se elige por defecto.
public enum ModoUbicacionHecho { Mapa, PosicionActual, Texto }

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
    private IReadOnlyList<OpcionCategoria> opciones = [];
    private CategoriaCatalogo? categoria;
    private string descripcion = "", referencia = "", mensajeCategorias = "", mensajeUbicacion = "";
    private DateTime? fecha;
    private TimeSpan? hora;
    // 24/10/2026: sin punto, el lugar es la referencia (el cuadro de referencia está siempre a la vista).
    private ModoUbicacionHecho modo = ModoUbicacionHecho.Texto;
    private double? latitud, longitud, precision;
    private bool obteniendoPosicion, cargandoCategorias, eligiendoTipo = true;
    private string mensajeDictado = "";
    private Dictionary<string, string> errores = [];
    private readonly IDictado dictado;

    public ReporteVistaModelo(IApiCad api, RegistradorReporte registrador, IServicioUbicacion ubicacion, IAlmacenSesion sesiones,
        INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null, IDictado? dictado = null)
    {
        (this.api, this.registrador, this.ubicacion, this.sesiones, this.navegacion, this.reloj) = (api, registrador, ubicacion, sesiones, navegacion, reloj);
        this.zona = zona ?? TimeZoneInfo.Local;
        this.dictado = dictado ?? new DictadoNoDisponible();

        // Por defecto, el momento actual (el ciudadano lo ajusta si el hecho fue antes).
        var ahora = TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), this.zona);
        fecha = ahora.Date;
        hora = new TimeSpan(ahora.Hour, ahora.Minute, 0);
        FechaMaxima = ahora.Date;

        CargarCategorias = new ComandoAsincrono(CargarCategoriasAsync);
        ElegirCategoria = new ComandoAsincrono<OpcionCategoria>(o =>
        {
            if (!Editable) return Task.CompletedTask;
            Categoria = o.Categoria;
            EligiendoTipo = false; // paso 2: el formulario, con el tipo a la vista
            return Task.CompletedTask;
        });
        CambiarTipo = new ComandoAsincrono(() => { EligiendoTipo = true; return Task.CompletedTask; }, () => Editable);
        Volver = new ComandoAsincrono(async () => { if (!VolverAlMosaico()) await IrAlInicioAsync(); });
        Dictar = new ComandoAsincrono(DictarAsync, () => Editable && this.dictado.Disponible);
        ElegirMapa = new ComandoAsincrono(() => { CambiarModo(ModoUbicacionHecho.Mapa, limpiarPunto: modo != ModoUbicacionHecho.Mapa); return Task.CompletedTask; }, () => Editable);
        QuitarPunto = new ComandoAsincrono(() => { CambiarModo(ModoUbicacionHecho.Texto, limpiarPunto: true); return Task.CompletedTask; }, () => Editable);
        UsarPosicionActual = new ComandoAsincrono(UsarPosicionActualAsync, () => Editable);
        Enviar = new ComandoAsincrono(() => registrador.EnviarAsync(Datos()), () => Editable);
        Reintentar = new ComandoAsincrono(() => registrador.ReintentarAsync(), () => registrador.PuedeReintentar);
        IrAlSos = new ComandoAsincrono(IrAlInicioAsync);
        VolverAIngresar = new ComandoAsincrono(async () => { sesiones.Cerrar(); await navegacion.IrAAsync(Rutas.Ingreso); });
        registrador.Cambio += _ => ActualizarEstado();
    }

    // 24/10/2026: tras registrar el reporte, la app vuelve sola al inicio después de mostrar la confirmación un momento
    // (en esa pantalla ya no queda nada por hacer). «Volver al inicio» lo hace al instante.
    public TimeSpan EsperaTrasRegistro { get; init; } = TimeSpan.FromSeconds(2.5);
    private bool volviendoAlInicio, regresoProgramado;
    private readonly CancellationTokenSource salida = new();

    private async Task IrAlInicioAsync()
    {
        if (volviendoAlInicio) return;
        volviendoAlInicio = true;
        await salida.CancelAsync(); // el regreso automático ya no hace falta
        await navegacion.IrAAsync(Rutas.Ciudadano);
    }

    // La página dejó de verse (p. ej., «atrás» del sistema): el regreso automático pendiente no debe interrumpir la
    // pantalla a la que fue el ciudadano.
    public void AlDesaparecer() => salida.Cancel();

    private async void VolverTrasRegistro()
    {
        regresoProgramado = true;
        try
        {
            await Task.Delay(EsperaTrasRegistro, salida.Token);
            await IrAlInicioAsync();
        }
        catch (Exception)
        {
            // Sin navegación (la página ya se cerró): queda el botón «Volver al inicio».
        }
    }

    public ComandoAsincrono CargarCategorias { get; }
    public ComandoAsincrono<OpcionCategoria> ElegirCategoria { get; }
    public ComandoAsincrono CambiarTipo { get; }
    public ComandoAsincrono Volver { get; }
    public ComandoAsincrono Dictar { get; }
    public ComandoAsincrono ElegirMapa { get; }
    // Quita el punto (o cierra el mapa sin punto): el lugar vuelve a ser solo la referencia.
    public ComandoAsincrono QuitarPunto { get; }
    public ComandoAsincrono UsarPosicionActual { get; }
    public ComandoAsincrono Enviar { get; }
    public ComandoAsincrono Reintentar { get; }
    public ComandoAsincrono IrAlSos { get; }
    public ComandoAsincrono VolverAIngresar { get; }

    // La página mueve el marcador del mapa cuando el punto lo fija la app (posición actual).
    public event Action<double, double>? PuntoCambiado;

    public string TextoAdvertenciaSos => AdvertenciaSos;

    // ----- Tipo (CA01, DP07) -----
    public IReadOnlyList<CategoriaCatalogo> Categorias
    {
        get => categorias;
        private set
        {
            if (!Asignar(ref categorias, value)) return;
            Opciones = value.Select(c => new OpcionCategoria(c)).ToList();
            // Al recargar el catálogo se conserva la elección si la categoría sigue existiendo.
            var elegida = categoria;
            categoria = null;
            Categoria = value.FirstOrDefault(c => c.Id == elegida?.Id);
        }
    }

    // Catálogo ampliado (PO, 22/10/2026): mosaico de tipos con icono, en vez de una lista desplegable.
    public IReadOnlyList<OpcionCategoria> Opciones { get => opciones; private set => Asignar(ref opciones, value); }

    public CategoriaCatalogo? Categoria
    {
        get => categoria;
        set
        {
            if (!Asignar(ref categoria, value)) return;
            foreach (var opcion in opciones)
                opcion.Seleccionada = opcion.Categoria.Id == value?.Id;
            Notificar(nameof(IndicacionDescripcion));
            Notificar(nameof(NombreCategoria));
            Notificar(nameof(IconoCategoria));
        }
    }

    // El tipo elegido, en la cabecera del formulario.
    public string NombreCategoria => categoria?.Nombre ?? "";
    public string IconoCategoria => categoria is { Icono.Length: > 0 } c ? c.Icono : "otros";

    // Flujo en dos pasos (22/10/2026): primero el tipo, a pantalla completa; luego el formulario con el tipo a la vista.
    // Volver desde el formulario regresa al mosaico sin perder lo escrito.
    public bool EligiendoTipo
    {
        get => eligiendoTipo;
        private set
        {
            if (!Asignar(ref eligiendoTipo, value)) return;
            Notificar(nameof(EnFormulario));
            Notificar(nameof(TituloPaso));
            Notificar(nameof(TextoVolverPaso));
        }
    }
    public bool EnFormulario => !eligiendoTipo;
    public string TituloPaso => eligiendoTipo ? "¿Qué quieres reportar?" : "Cuéntanos qué pasó";
    public string TextoVolverPaso => eligiendoTipo ? "Inicio" : "Tipo";

    // Volver (botón del sistema o del encabezado): en el formulario, al mosaico; en el mosaico, fuera de la página.
    public bool VolverAlMosaico()
    {
        if (eligiendoTipo || !Editable) return false;
        EligiendoTipo = true;
        return true;
    }

    // Indicación del campo según el tipo: sus ejemplos orientan qué contar. «Otros» no tiene ejemplos que sirvan de guía.
    public string IndicacionDescripcion => categoria is { Ayuda.Length: > 0 } c && c.Icono != "otros"
        ? $"Cuéntanos qué pasó. Por ejemplo: {char.ToLower(c.Ayuda[0], CultureInfo.GetCultureInfo("es-PE"))}{c.Ayuda[1..]}…"
        : "Describe lo ocurrido";
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

    // Dictado por voz con el reconocedor del teléfono: el texto se añade a lo escrito y el ciudadano lo revisa antes de enviar.
    public bool PuedeDictar => dictado.Disponible;
    public string MensajeDictado { get => mensajeDictado; private set { Asignar(ref mensajeDictado, value); Notificar(nameof(HayMensajeDictado)); } }
    public bool HayMensajeDictado => mensajeDictado.Length > 0;

    private async Task DictarAsync()
    {
        MensajeDictado = "";
        string? texto;
        try
        {
            texto = await dictado.DictarAsync();
        }
        catch (Exception)
        {
            MensajeDictado = "No se pudo usar el dictado. Escribe lo ocurrido.";
            return;
        }
        if (string.IsNullOrWhiteSpace(texto) || !Editable)
            return;
        var unido = descripcion.Length == 0 ? texto.Trim() : $"{descripcion.TrimEnd()} {texto.Trim()}";
        Descripcion = unido.Length > MaximoDescripcion ? unido[..MaximoDescripcion] : unido;
    }

    // ----- Fecha y hora aproximadas (DP08) -----
    public DateTime? Fecha { get => fecha; set => Asignar(ref fecha, value); }
    public TimeSpan? Hora { get => hora; set => Asignar(ref hora, value); }
    public DateTime FechaMaxima { get; }

    // ----- Ubicación del hecho (CA03, CA04, DP10) -----
    public ModoUbicacionHecho Modo => modo;
    public bool MostrarMapa => modo is ModoUbicacionHecho.Mapa or ModoUbicacionHecho.PosicionActual;
    public bool HayPunto => latitud is not null && longitud is not null;
    public string TextoQuitarPunto => HayPunto ? "Quitar el punto" : "Cerrar el mapa";
    public bool ObteniendoPosicion { get => obteniendoPosicion; private set => Asignar(ref obteniendoPosicion, value); }
    public double? Latitud => latitud;
    public double? Longitud => longitud;
    public string EtiquetaReferencia => HayPunto ? "Referencia (opcional)" : "Referencia del lugar";
    public string Referencia { get => referencia; set => Asignar(ref referencia, value ?? ""); }
    public string MensajeUbicacion { get => mensajeUbicacion; private set { Asignar(ref mensajeUbicacion, value); Notificar(nameof(HayMensajeUbicacion)); } }
    public bool HayMensajeUbicacion => mensajeUbicacion.Length > 0;
    public string TextoUbicacion => modo switch
    {
        ModoUbicacionHecho.Mapa when latitud is null => "Toca el mapa en el lugar del hecho.",
        ModoUbicacionHecho.Mapa => "Punto elegido en el mapa. Toca otro lugar para moverlo.",
        ModoUbicacionHecho.PosicionActual => $"Se usará tu posición actual como lugar del hecho (± {precision?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} m). " +
            "Si el hecho ocurrió en otro lugar, toca el mapa para moverlo.",
        _ => "Marca el lugar en el mapa, usa tu posición actual o escribe abajo una referencia. Tu posición solo se usa si eliges «Usar mi posición actual».",
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
        foreach (var p in new[] { nameof(Modo), nameof(MostrarMapa), nameof(HayPunto), nameof(TextoQuitarPunto), nameof(Latitud), nameof(Longitud),
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
        // Sin punto (también con el mapa abierto sin tocarlo), el lugar es la referencia.
        var lugar = modo switch
        {
            _ when !HayPunto => new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, referencia),
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
            $"(estado: {r.Respuesta.Estado}). La central lo revisará. Volviendo al inicio…",
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
        foreach (var comando in new[] { ElegirMapa, QuitarPunto, UsarPosicionActual, Enviar, Reintentar, CambiarTipo, Dictar })
            comando.ActualizarDisponibilidad();
        if (Registrado && !regresoProgramado)
            VolverTrasRegistro();
    }
}

// Una casilla del mosaico de tipos. Sin icono conocido (servidor anterior al catálogo ampliado), el genérico.
public sealed class OpcionCategoria(CategoriaCatalogo categoria) : VistaModeloBase
{
    private bool seleccionada;

    public CategoriaCatalogo Categoria { get; } = categoria;
    public string Nombre => Categoria.Nombre;
    public string Icono => Categoria.Icono.Length > 0 ? Categoria.Icono : "otros";
    public string Ayuda => Categoria.Ayuda;

    public bool Seleccionada
    {
        get => seleccionada;
        set { if (Asignar(ref seleccionada, value)) Notificar(nameof(DescripcionAccesible)); }
    }

    // El lector de pantalla anuncia cuál está elegida; el borde de color no basta.
    public string DescripcionAccesible => Seleccionada ? $"{Nombre}, elegido" : Nombre;
}
