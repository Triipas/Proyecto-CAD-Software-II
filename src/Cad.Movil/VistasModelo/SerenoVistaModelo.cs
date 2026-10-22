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
    private readonly Func<Uri, Task<bool>> abrirEnlace;
    private bool mostrarExplicacion, mostrarAjustes, mostrarCierre, mostrarInformacion;
    private CategoriaCatalogo? categoriaCierre;
    private string? resultadoCierre, notasCierre, avisoComoLlegar;

    public InicioSerenoVistaModelo(ServicioTurnoSereno turno, ServicioDespacho despacho, ServicioNotificaciones notificaciones,
        IAlmacenSesion sesiones, INavegacion navegacion, Action abrirAjustes, Func<Uri, Task<bool>>? abrirEnlace = null)
    {
        (this.turno, this.despacho, this.notificaciones, this.sesiones, this.navegacion) = (turno, despacho, notificaciones, sesiones, navegacion);
        this.abrirEnlace = abrirEnlace ?? (_ => Task.FromResult(false));
        ComoLlegar = new ComandoAsincrono(ComoLlegarAsync, () => HayDestino);
        VerInformacion = new ComandoAsincrono(() => { MostrarInformacion = !MostrarInformacion; return Task.CompletedTask; });
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
    public ComandoAsincrono ComoLlegar { get; }
    public ComandoAsincrono VerInformacion { get; }

    // ----- Despacho con mapa (diseño C, 22/10/2026) -----
    // El punto del caso: la ubicación del teléfono del ciudadano (SOS) o el lugar del hecho que marcó (reporte). Null si no hay
    // punto (SOS sin ubicación, reporte solo con referencia): el mapa muestra solo al sereno y no se inventan coordenadas (CA03).
    public PuntoMapa? Destino => despacho.Actual switch
    {
        { Ubicacion: { } u } => new PuntoMapa(u.Latitud, u.Longitud, u.PrecisionMetros, PuntoMapa.Sos),
        { LugarHecho: { Latitud: { } lat, Longitud: { } lon } l } => new PuntoMapa(lat, lon, l.PrecisionMetros, PuntoMapa.Reporte),
        _ => null,
    };
    public bool HayDestino => Destino is not null;
    // Sin despacho, la pantalla del turno; con despacho, el mapa y el panel.
    public bool SinModoDespacho => !HayDespacho;
    public bool PuedeActuar => HayDestino || PuedeConfirmarDesplazamiento || PuedeIniciarAtencion || PuedeAbrirCierre;
    public bool SinPuntoEnMapa => HayDespacho && !HayDestino;
    // La posición de este teléfono: se lee para enviarla a la central y aquí solo se dibuja.
    public Contratos.Sos.UbicacionSos? PosicionPropia => turno.UltimaPosicion;

    public string TituloUbicacion => despacho.Actual switch
    {
        null => "",
        { Ubicacion: not null } => "Ubicación del teléfono del ciudadano",
        { LugarHecho: { Latitud: not null } } => "Lugar del hecho (lo marcó el ciudadano)",
        { LugarHecho: not null } => "Lugar del hecho: solo una referencia",
        _ => "Ubicación no disponible",
    };
    public string DetalleUbicacion => despacho.Actual switch
    {
        { Ubicacion: { } u } => $"± {Metros(u.PrecisionMetros)} m · obtenida a las {HoraCorta(u.ObtenidaEn)}",
        { LugarHecho: { Referencia.Length: > 0 } l } => $"Referencia: {l.Referencia}",
        { LugarHecho: { PrecisionMetros: { } p } } => $"± {Metros(p)} m",
        { LugarHecho: not null } => "Punto marcado en el mapa",
        null => "",
        _ => "El ciudadano no compartió su ubicación: guíate por lo que registró la central.",
    };

    // Distancia en línea recta entre el sereno y el punto del caso; la ruta real la da «Cómo llegar».
    public string TextoDistancia => Destino is { } d && PosicionPropia is { } p
        ? FormatoDistancia(DistanciaMetros(p.Latitud, p.Longitud, d.Latitud, d.Longitud)) : "";
    public bool HayDistancia => TextoDistancia.Length > 0;

    // Etapas del caso con su hora: el sereno ve en qué paso está.
    public IReadOnlyList<EtapaDespacho> Etapas => despacho.Actual is { } a
        ?
        [
            new("Asignada", HoraCorta(a.AsignadoEn), Hecha: true, Actual: a.Estado == DespachoSereno.EstadoAsignada),
            new("En camino", a.EnCaminoEn is { } c ? HoraCorta(c) : "", a.EnCaminoEn is not null, a.Estado == DespachoSereno.EstadoEnCamino),
            new("En atención", a.EnAtencionEn is { } t ? HoraCorta(t) : "", a.EnAtencionEn is not null, a.Estado == DespachoSereno.EstadoEnAtencion && !mostrarCierre),
            new("Cierre", "", Hecha: false, Actual: MostrarCierre),
        ]
        : [];

    // «Lo que registró la central»: resumen de una línea; al tocarlo se despliegan la información, la descripción y los avances.
    public string ResumenCentral => despacho.Actual switch
    {
        { Informacion: [.., { InformacionHecho.Length: > 0 } ultima] } => ultima.InformacionHecho!,
        { DescripcionReporte.Length: > 0 } d => d.DescripcionReporte!,
        null => "",
        _ => "Sin información registrada.",
    };
    public bool MostrarInformacion { get => mostrarInformacion; private set { Asignar(ref mostrarInformacion, value); Notificar(nameof(IconoInformacion)); } }
    public string IconoInformacion => mostrarInformacion ? "arriba" : "abajo";

    public string? AvisoComoLlegar { get => avisoComoLlegar; private set { Asignar(ref avisoComoLlegar, value); Notificar(nameof(HayAvisoComoLlegar)); } }
    public bool HayAvisoComoLlegar => avisoComoLlegar is not null;

    // Abre Google Maps (la app si está instalada; si no, el navegador) con el destino ya puesto: la ruta giro a giro sin un
    // servicio de rutas de pago en el CAD. Solo se envía el punto del caso, nunca datos del ciudadano.
    public static Uri EnlaceComoLlegar(double latitud, double longitud) =>
        new(string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/dir/?api=1&destination={latitud:0.######},{longitud:0.######}"));

    private async Task ComoLlegarAsync()
    {
        AvisoComoLlegar = null;
        if (Destino is not { } d)
            return;
        if (!await abrirEnlace(EnlaceComoLlegar(d.Latitud, d.Longitud)))
            AvisoComoLlegar = "No se pudo abrir la app de mapas del teléfono.";
    }

    // Fórmula del haversine (radio medio de la Tierra): suficiente para distancias urbanas.
    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double Radio = 6_371_000;
        static double Rad(double grados) => grados * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * Radio * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    // Menos de 1 km: en metros redondeados a 10; desde 1 km: en kilómetros con un decimal (formato es-PE: punto decimal).
    public static string FormatoDistancia(double metros)
    {
        var redondeados = Math.Round(metros / 10, MidpointRounding.AwayFromZero) * 10;
        return redondeados < 1000
            ? redondeados.ToString("0", CultureInfo.InvariantCulture) + " m"
            : (metros / 1000).ToString("0.0", CultureInfo.GetCultureInfo("es-PE")) + " km";
    }

    private static string Metros(double valor) => valor.ToString("0", CultureInfo.InvariantCulture);
    private static string HoraCorta(DateTimeOffset momento) => momento.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

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
    // Rediseño CAD v2: la tarjeta del despacho muestra el origen como etiqueta y el estado como chip.
    public string EstadoDespacho => despacho.Actual?.Estado ?? "";
    public bool DespachoEsSos => despacho.Actual is { } d && d.Origen != "Reporte";
    public bool DespachoEsReporte => despacho.Actual is { Origen: "Reporte" };
    public string TextoAsignado => despacho.Actual is { } d
        ? $"Asignado a las {Hora(d.AsignadoEn)}. Registrado a las {Hora(d.RegistradoEn)}." : "";
    public string TextoIdentificador => despacho.Actual is { } d ? $"Identificador del caso: {d.CasoId}" : "";
    // CA03: sin ubicación del dispositivo no se muestran coordenadas.
    public bool HayUbicacionDespacho => despacho.Actual?.Ubicacion is not null;
    public bool SinUbicacionDespacho => despacho.Actual is { Ubicacion: null, LugarHecho: null };
    // HU20-CA07: un reporte admitido no tiene ubicación del dispositivo; trae el lugar del hecho que indicó el ciudadano.
    public bool EsReporteDespacho => despacho.Actual?.LugarHecho is not null;
    public bool MostrarUbicacionDispositivo => HayDespacho && !EsReporteDespacho;
    public bool HayPuntoLugarHecho => despacho.Actual?.LugarHecho is { Latitud: not null, Longitud: not null };
    public string TextoLugarHecho => despacho.Actual?.LugarHecho is { Latitud: { } lat, Longitud: { } lon } l
        ? $"{lat.ToString("0.00000", CultureInfo.InvariantCulture)}, {lon.ToString("0.00000", CultureInfo.InvariantCulture)}" +
          (l.PrecisionMetros is { } p ? $" · ± {p.ToString("0", CultureInfo.InvariantCulture)} m" : "")
        : "";
    public bool HayReferenciaLugarHecho => despacho.Actual?.LugarHecho?.Referencia is { Length: > 0 };
    public string TextoReferenciaLugarHecho => despacho.Actual?.LugarHecho?.Referencia is { } r ? $"Referencia: {r}" : "";
    // DP06 ampliada por el PO (16/10/2026): la descripción que escribió el ciudadano en su reporte.
    public bool HayDescripcionReporte => despacho.Actual?.DescripcionReporte is { Length: > 0 };
    public string TextoDescripcionReporte => despacho.Actual?.DescripcionReporte ?? "";
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
                     nameof(Error), nameof(HayError), nameof(SesionTerminada), nameof(PuedeSalir), nameof(TextoAyudaSalir), nameof(HayAyudaSalir),
                     nameof(PosicionPropia), nameof(TextoDistancia), nameof(HayDistancia) })
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
        foreach (var p in new[] { nameof(HayDespacho), nameof(SinDespacho), nameof(TextoDespacho), nameof(EstadoDespacho), nameof(DespachoEsSos),
                     nameof(DespachoEsReporte), nameof(TextoAsignado), nameof(TextoIdentificador),
                     nameof(HayUbicacionDespacho), nameof(SinUbicacionDespacho), nameof(TextoUbicacionDespacho), nameof(InformacionDespacho),
                     nameof(EsReporteDespacho), nameof(MostrarUbicacionDispositivo), nameof(HayPuntoLugarHecho), nameof(TextoLugarHecho),
                     nameof(HayReferenciaLugarHecho), nameof(TextoReferenciaLugarHecho), nameof(HayDescripcionReporte), nameof(TextoDescripcionReporte),
                     nameof(HayInformacionDespacho), nameof(SinInformacionDespacho), nameof(AvisoNuevoDespacho), nameof(ErrorDespacho), nameof(HayErrorDespacho),
                     nameof(PuedeConfirmarDesplazamiento), nameof(PuedeIniciarAtencion), nameof(TextoAvances), nameof(HayAvances),
                     nameof(MostrarCierre), nameof(PuedeAbrirCierre), nameof(CategoriasCierre), nameof(ErrorCategoriaCierre), nameof(HayErrorCategoriaCierre),
                     nameof(ErrorResultadoCierre), nameof(HayErrorResultadoCierre), nameof(ErrorNotasCierre), nameof(HayErrorNotasCierre),
                     nameof(CierreReciente), nameof(HayCierreReciente),
                     nameof(Destino), nameof(HayDestino), nameof(SinPuntoEnMapa), nameof(TituloUbicacion), nameof(DetalleUbicacion),
                     nameof(TextoDistancia), nameof(HayDistancia), nameof(Etapas), nameof(ResumenCentral), nameof(SinModoDespacho), nameof(PuedeActuar) })
            Notificar(p);
        ComoLlegar.ActualizarDisponibilidad();
    }
}

// Información que registró la central (HU12). La referencia indicada por el ciudadano se muestra aparte de la
// ubicación del dispositivo (HU21-CA03).
public sealed record ElementoInformacionDespacho(string Hora, string? InformacionHecho, string? ReferenciaUbicacion)
{
    public bool HayHecho => InformacionHecho is not null;
    public bool HayReferencia => ReferenciaUbicacion is not null;
}

// Punto del caso en el mapa del despacho: ubicación del teléfono del ciudadano (SOS) o lugar del hecho (reporte).
public sealed record PuntoMapa(double Latitud, double Longitud, double? PrecisionMetros, string Tipo)
{
    public const string Sos = "sos";
    public const string Reporte = "reporte";
}

// Una etapa del despacho en la barra superior: hecha (barra verde) y/o actual (texto destacado).
public sealed record EtapaDespacho(string Nombre, string Hora, bool Hecha, bool Actual)
{
    public bool HayHora => Hora.Length > 0;
    public string Descripcion => $"{Nombre}{(HayHora ? $", {Hora}" : "")}{(Actual ? ", etapa actual" : Hecha ? ", completada" : ", pendiente")}";
}
