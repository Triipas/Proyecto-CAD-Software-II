using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// HU14 — Iniciar la revisión de un reporte y corregir su categoría (S3-T05); HU15–HU17 — admitirlo, derivarlo o
// descartarlo (S3-T06, S3-T07, S3-T09); HU20-CA07 — asignar un reporte admitido desde la central (S3-T08). Solo operadores (también en el servidor:
// la política de la página rechaza cualquier otra sesión, incluida una solicitud directa).
[Authorize(Policy = Politicas.OperadorAutorizado)]
// Decisión del PO del 16/10/2026: el operador registra el inicio de atención (HU23-CA02) y cierra (HU24-CA05) un reporte en
// curso, como en un SOS. Desde el 23/10/2026, la central también descarta un reporte falso o erróneo ya admitido.
public class DetalleModel(ConsultarDetalleReporte consultar, RevisarReporte revisar, DecidirReporte decidir, AsignarCaso asignar,
    ConsultarCategorias consultarCategorias, ConsultarDetalleSos consultarSeguimiento, AvanzarCaso avanzar, CerrarCaso cerrar,
    ConsultarTiemposCaso consultarTiempos, ConsultarFichaCiudadano consultarFicha, DescartarCaso descartar, ConsultarDescarte consultarDescarte, ReasignarCaso reasignar, ConsultarSerenos consultarSerenos,
    IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioCierre Cierre { get; set; } = new();

    // 24/10/2026: por qué registra el operador el inicio de atención por el sereno (obligatorio).
    [BindProperty]
    public string? MotivoInicio { get; set; }
    public string? ErrorMotivoInicio { get; private set; }

    [BindProperty]
    public FormularioDescarte Descarte { get; set; } = new();

    // Reasignación por la central (24/10/2026).
    [BindProperty]
    public FormularioReasignacion Reasignacion { get; set; } = new();
    public Dictionary<string, string[]> ErroresReasignacion { get; private set; } = [];
    public string? ErrorReasignacion { get; private set; }
    public IReadOnlyList<SerenoMonitoreo> SerenosDisponibles { get; private set; } = [];
    public bool PuedeReasignarse => Seguimiento?.Asignacion is { FinalizadaEn: null } && Reporte.Estado is EstadoCaso.Asignada or EstadoCaso.EnCamino;
    public VistaReasignacion VistaReasignacion => new($"/panel/reportes/{Reporte.Id}", Seguimiento?.Asignacion?.Sereno ?? "",
        SerenosDisponibles.Select(s => new SerenoParaReasignar(s.Id, s.Nombre, s.UltimaPosicionEn is { } h
            ? $"última ubicación a las {TimeZoneInfo.ConvertTime(h, ZonaHoraria):HH:mm:ss}" : "ubicación no disponible")).ToList(),
        Reasignacion.IdSolicitud == Guid.Empty ? new FormularioReasignacion { IdSolicitud = Guid.NewGuid() } : Reasignacion,
        ErroresReasignacion, ErrorReasignacion);
    public string? ErrorDescarte { get; private set; }
    public string? ErrorAsignacion { get; private set; }

    // Asignación desde este detalle (27/10/2026): los serenos disponibles con su última ubicación.
    public IReadOnlyList<SerenoParaReasignar> SerenosParaAsignar => SerenosDisponibles
        .Select(s => new SerenoParaReasignar(s.Id, s.Nombre, s.UltimaPosicionEn is { } h
            ? $"última ubicación a las {TimeZoneInfo.ConvertTime(h, ZonaHoraria):HH:mm}" : "ubicación no disponible"))
        .ToList();
    public Guid IdAsignacion => Asignacion.IdSolicitud == Guid.Empty ? Guid.NewGuid() : Asignacion.IdSolicitud;
    public string? ErrorMotivoDescarte { get; private set; }
    public string? ErrorMotivoCodigoDescarte { get; private set; }

    // Descarte por la central (un reporte recibido o en revisión se descarta con su decisión, HU17).
    public DescarteVista? Descartado { get; private set; }
    public bool PuedeDescartarse => Reporte.Estado is EstadoCaso.Pendiente or EstadoCaso.Asignada or EstadoCaso.EnCamino or EstadoCaso.EnAtencion;
    public VistaDescarte VistaDescarte => new($"/panel/reportes/{Reporte.Id}",
        Seguimiento?.Asignacion is { FinalizadaEn: null } vigente ? vigente.Sereno : null, Descarte.Motivo, ErrorDescarte, ErrorMotivoDescarte,
        Descarte.MotivoCodigo, ErrorMotivoCodigoDescarte);

    // Asignación, avances y cierre de un reporte admitido (null antes de la admisión).
    public DetalleSos? Seguimiento { get; private set; }

    // ADR-08, fase D: celular verificado y datos opcionales del ciudadano.
    public FichaCiudadano? Ficha { get; private set; }
    public string? ErrorAtencion { get; private set; }
    public string? ErrorCierre { get; private set; }
    public Dictionary<string, string[]> ErroresCierre { get; private set; } = [];

    public string? ErrorCierreDe(string campo) => ErroresCierre.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    [BindProperty]
    public Guid? CategoriaId { get; set; }

    [BindProperty]
    public FormularioDecision Decision { get; set; } = new();

    [BindProperty]
    public FormularioAsignacion Asignacion { get; set; } = new();

    public string? ErrorDecision { get; private set; }
    public Dictionary<string, string[]> ErroresDecision { get; private set; } = [];

    public string? ErrorDecisionDe(string campo) => ErroresDecision.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public DetalleReporteVista Reporte { get; private set; } = null!;

    // HU28: tiempos hasta la decisión de la revisión y hasta el inicio de la atención.
    public TiemposCaso? Tiempos { get; private set; }
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];
    public string? ErrorRevision { get; private set; }
    public string? ErrorCorreccion { get; private set; }
    public string? ErrorCategoria { get; private set; }

    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    // Rediseño CAD v2: la tarjeta «Siguiente acción» muestra lo único que toca hacer ahora según el estado.
    public SiguienteAccion Siguiente => Reporte.Estado switch
    {
        EstadoCaso.Recibido => SiguienteAccion.IniciarRevision,
        EstadoCaso.EnRevision => SiguienteAccion.Decidir,
        EstadoCaso.Descartada when Descartado is not null => SiguienteAccion.VerDescarte,
        EstadoCaso.Cancelada when Reporte.Retiro is not null => SiguienteAccion.VerRetiro,
        EstadoCaso.Derivada or EstadoCaso.Descartada => SiguienteAccion.VerDecision,
        _ when Seguimiento?.Cierre is not null => SiguienteAccion.VerCierre,
        _ when Seguimiento is { EnAtencion: true } => SiguienteAccion.Cerrar,
        _ when Seguimiento is { EnCamino: true } => SiguienteAccion.EsperarAtencion,
        _ when Seguimiento?.Asignacion is not null => SiguienteAccion.EsperarDesplazamiento,
        EstadoCaso.Pendiente => SiguienteAccion.Asignar,
        _ => SiguienteAccion.SinAcciones,
    };

    // Recibido → En revisión → Admitido → Asignada → En camino → En atención → Cerrada; un reporte derivado o descartado
    // termina en el tercer paso.
    public IReadOnlyList<PasoCaso> Pasos
    {
        get
        {
            if (Reporte.Estado is EstadoCaso.Derivada or EstadoCaso.Descartada)
                return ProgresoCaso.Construir(
                    ["Recibido", "En revisión", EstadosCaso.Texto(Reporte.Estado)], [true, true, true], -1);
            // Retirado por el ciudadano antes de la decisión (26/10/2026): la revisión pudo no haber empezado.
            if (Reporte.Estado == EstadoCaso.Cancelada)
                return ProgresoCaso.Construir(
                    ["Recibido", "En revisión", EstadosCaso.Texto(Reporte.Estado)], [true, Reporte.RevisionIniciadaEn is not null, true], -1);

            var indice = Reporte.Estado switch
            {
                EstadoCaso.Recibido => 0,
                EstadoCaso.EnRevision => 1,
                EstadoCaso.Pendiente => 2,
                EstadoCaso.Asignada => 3,
                EstadoCaso.EnCamino => 4,
                EstadoCaso.EnAtencion => 5,
                _ => 6,
            };
            var avances = Seguimiento?.Avances ?? [];
            bool[] hechos =
            [
                true,
                indice >= 1,
                indice >= 2,
                indice >= 3,
                indice == 4 || (indice > 4 && avances.Any(a => a.Descripcion == "En camino")),
                indice == 5 || (indice > 5 && avances.Any(a => a.Descripcion == "En atención")),
                indice == 6,
            ];
            // Los pasos que son estados se llaman como el estado: el chip y el paso marcado dicen lo mismo (24/10/2026).
            // 27/10/2026: mientras no hay decisión, el tercer paso es «Decisión» (todavía no se sabe si será admitido).
            var tercero = Reporte.Estado is EstadoCaso.Recibido or EstadoCaso.EnRevision ? "Decisión" : EstadosCaso.ReporteAdmitido;
            string[] textos = [EstadosCaso.Texto(EstadoCaso.Recibido), EstadosCaso.Texto(EstadoCaso.EnRevision), tercero,
                EstadosCaso.Texto(EstadoCaso.Asignada), EstadosCaso.Texto(EstadoCaso.EnCamino), EstadosCaso.Texto(EstadoCaso.EnAtencion),
                EstadosCaso.Texto(Reporte.Estado == EstadoCaso.Cancelada ? EstadoCaso.Cancelada : EstadoCaso.Cerrada)];
            return ProgresoCaso.Construir(textos, hechos, indice < 6 ? indice + 1 : -1);
        }
    }

    public enum SiguienteAccion { IniciarRevision, Decidir, Asignar, EsperarDesplazamiento, EsperarAtencion, Cerrar, VerCierre, VerDecision, VerDescarte, VerRetiro, SinAcciones }

    public async Task<IActionResult> OnGetAsync(Guid id, [FromServices] Modulos.Analitica.MarcasCentral marcas, CancellationToken ct)
    {
        if (!await CargarAsync(id, ct))
            return NotFound();
        await marcas.AbiertoAsync(id, ct); // la primera vez que la central abre el reporte (analítica, 27/10/2026)
        return Page();
    }

    // DP08 ampliada (26/10/2026): una foto del reporte, solo para el operador (la política de la página). No se guarda en
    // cachés ni se interpreta como otro tipo de contenido.
    public async Task<IActionResult> OnGetFotoAsync(Guid id, int numero, [FromServices] ConsultarFotoReporte consultarFoto, CancellationToken ct)
    {
        if (await consultarFoto.EjecutarAsync(id, numero, ct) is not { } contenido)
            return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(contenido, "image/jpeg");
    }

    // CA01, CA02 y CA05.
    public async Task<IActionResult> OnPostIniciarRevisionAsync(Guid id, CancellationToken ct)
    {
        switch (await revisar.IniciarAsync(id, User.IdUsuario(), ct))
        {
            case ResultadoRevision.Registrado r:
                return Hecho(id, "Revisión iniciada.", r.Detalle);
            case ResultadoRevision.YaRegistrado r:
                return Hecho(id, "Revisión iniciada.", r.Detalle);
            case ResultadoRevision.Rechazado r when await CargarAsync(id, ct):
                ErrorRevision = r.Motivo;
                return Page();
            default:
                return NotFound();
        }
    }

    // CA03 y CA04.
    public async Task<IActionResult> OnPostCorregirAsync(Guid id, CancellationToken ct)
    {
        var resultado = await revisar.CorregirCategoriaAsync(id, CategoriaId, User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoRevision.Registrado r:
                return Hecho(id, "Categoría corregida.", r.Detalle);
            case ResultadoRevision.YaRegistrado r:
                return Hecho(id, "Sin cambios en la categoría.", r.Detalle);
            case ResultadoRevision.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorCategoria = (resultado as ResultadoRevision.Invalido)?.Motivo;
                ErrorCorreccion = (resultado as ResultadoRevision.Rechazado)?.Motivo;
                return Page();
        }
    }

    // HU15, HU16 y HU17.
    public async Task<IActionResult> OnPostDecidirAsync(Guid id, CancellationToken ct)
    {
        var resultado = await decidir.EjecutarAsync(id, new DatosDecision(Decision.Tipo, Decision.Motivo, Decision.Destino, Decision.Accion,
            Decision.DetalleDestino, Decision.Prioridad, Decision.Heridos, Decision.MotivoDescarte),
            User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoDecision.Registrado r:
                if (r.Estado == EstadosCaso.Texto(EstadoCaso.Pendiente)) // admitido: queda listo para asignar
                    TempData[SonidoPanel.Clave] = SonidoPanel.ReporteAdmitido;
                return Hecho(id, "Decisión registrada.", QueSigue(r.Estado));
            case ResultadoDecision.YaRegistrado r:
                return Hecho(id, "Decisión registrada.", QueSigue(r.Estado));
            case ResultadoDecision.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErroresDecision = (resultado as ResultadoDecision.Invalido)?.Errores ?? [];
                ErrorDecision = (resultado as ResultadoDecision.Rechazado)?.Motivo;
                return Page();
        }
    }

    // HU20-CA07: un reporte admitido se asigna con las mismas reglas que un SOS. Desde la central vuelve a ella; desde este
    // detalle (Volver = "detalle", 27/10/2026) vuelve al reporte.
    public async Task<IActionResult> OnPostAsignarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await asignar.EjecutarAsync(id, Asignacion.SerenoId, User.IdUsuario(), Asignacion.IdSolicitud, ct);
        if (Asignacion.Volver == "detalle")
        {
            switch (resultado)
            {
                case ResultadoAsignacion.Asignado or ResultadoAsignacion.YaAsignado:
                    TempData[SonidoPanel.Clave] = SonidoPanel.CasoAsignado;
                    return Hecho(id, "Reporte asignado.", "El sereno recibió el despacho y ya no figura como disponible.");
                case ResultadoAsignacion.CasoNoEncontrado:
                    return NotFound();
                default:
                    if (!await CargarAsync(id, ct))
                        return NotFound();
                    ErrorAsignacion = (resultado as ResultadoAsignacion.Rechazado)?.Motivo
                        ?? (resultado as ResultadoAsignacion.Invalido)?.Motivo ?? "No se pudo asignar el reporte.";
                    Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid(), Volver = "detalle" };
                    return Page();
            }
        }
        switch (resultado)
        {
            case ResultadoAsignacion.Asignado or ResultadoAsignacion.YaAsignado:
                TempData[nameof(Central.IndexModel.Mensaje)] = "Reporte asignado.";
                TempData[SonidoPanel.Clave] = SonidoPanel.CasoAsignado;
                TempData[nameof(Central.IndexModel.DetalleMensaje)] = "El sereno ya no figura como disponible.";
                break;
            case ResultadoAsignacion.CasoNoEncontrado:
                return NotFound();
            case ResultadoAsignacion.Rechazado r:
                TempData[nameof(Central.IndexModel.ErrorAccion)] = r.Motivo;
                break;
            case ResultadoAsignacion.Invalido i:
                TempData[nameof(Central.IndexModel.ErrorAccion)] = i.Motivo;
                break;
        }
        return RedirectToPage("/Central/Index");
    }

    // HU23-CA02: el operador registra el inicio de atención y queda como responsable.
    public async Task<IActionResult> OnPostIniciarAtencionAsync(Guid id, CancellationToken ct)
    {
        switch (await avanzar.EjecutarAsync(id, Avance.IniciarAtencion, User.IdUsuario(), ActorAvance.Operador, ct,
                    new Cad.Contratos.Serenos.SolicitudInicioAtencion(Detalle: MotivoInicio)))
        {
            case ResultadoAvance.Registrado or ResultadoAvance.YaRegistrado:
                return Hecho(id, "Inicio de atención registrado.", "Quedas como responsable del registro, con el motivo que indicaste.");
            case ResultadoAvance.Invalido i when await CargarAsync(id, ct):
                ErrorMotivoInicio = i.Errores.Values.First()[0];
                return Page();
            case ResultadoAvance.Rechazado r when await CargarAsync(id, ct):
                ErrorAtencion = r.Motivo;
                return Page();
            default:
                return NotFound();
        }
    }

    // HU24-CA05: el operador cierra el reporte en atención y queda como responsable.
    public async Task<IActionResult> OnPostCerrarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await cerrar.EjecutarAsync(id, new DatosCierre(Cierre.CategoriaId, Cierre.ResultadoAtencion, Cierre.ResultadoPublico, Cierre.NotasInternas),
            User.IdUsuario(), ActorAvance.Operador, ct);
        switch (resultado)
        {
            case ResultadoCierre.Cerrado or ResultadoCierre.YaCerrado:
                return Hecho(id, "Reporte cerrado.", "El sereno vuelve a figurar como disponible si sigue operativo.");
            case ResultadoCierre.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErroresCierre = (resultado as ResultadoCierre.Invalido)?.Errores ?? [];
                ErrorCierre = (resultado as ResultadoCierre.Rechazado)?.Motivo;
                return Page();
        }
    }

    // El aviso ya muestra el estado; aquí, solo lo que sigue.
    private static string QueSigue(string estado) => estado == "Pendiente"
        ? "Ya puedes asignarlo a un sereno disponible desde aquí."
        : "Es un estado final.";

    // Descarte por la central (23/10/2026): un reporte admitido falso o erróneo termina como «Descartada», con motivo obligatorio.
    public async Task<IActionResult> OnPostDescartarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await descartar.EjecutarAsync(id, Descarte.MotivoCodigo, Descarte.Motivo, User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoDescarte.Descartado or ResultadoDescarte.YaDescartado:
                return Hecho(id, "Reporte descartado.", "Salió de la central y del mapa; si tenía sereno, quedó libre para otro caso.");
            case ResultadoDescarte.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorMotivoDescarte = (resultado as ResultadoDescarte.Invalido)?.Errores.GetValueOrDefault("motivo")?[0];
                ErrorMotivoCodigoDescarte = (resultado as ResultadoDescarte.Invalido)?.Errores.GetValueOrDefault("motivoCodigo")?[0];
                ErrorDescarte = (resultado as ResultadoDescarte.Rechazado)?.Motivo;
                return Page();
        }
    }

    // Reasignación por la central (24/10/2026): a otro sereno disponible, con motivo; solo «Asignada» o «En camino».
    public async Task<IActionResult> OnPostReasignarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await reasignar.EjecutarAsync(id, Reasignacion.SerenoId, Reasignacion.MotivoCodigo, Reasignacion.Motivo, User.IdUsuario(), Reasignacion.IdSolicitud, ct);
        switch (resultado)
        {
            case ResultadoReasignacion.Reasignado or ResultadoReasignacion.YaReasignado:
                TempData[SonidoPanel.Clave] = SonidoPanel.CasoAsignado;
                return Hecho(id, "Reporte reasignado.", "El sereno anterior quedó libre; el nuevo recibió el despacho.");
            case ResultadoReasignacion.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErroresReasignacion = (resultado as ResultadoReasignacion.Invalido)?.Errores ?? [];
                ErrorReasignacion = (resultado as ResultadoReasignacion.Rechazado)?.Motivo;
                return Page();
        }
    }

    private RedirectToPageResult Hecho(Guid id, string mensaje, string detalle)
    {
        Mensaje = mensaje;
        DetalleMensaje = detalle;
        return RedirectToPage(new { id }); // PRG: recargar no vuelve a enviar el formulario
    }

    private async Task<bool> CargarAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } reporte)
            return false;
        Reporte = reporte;
        Ficha = await consultarFicha.PorCasoAsync(id, ct);
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        Seguimiento = reporte.EnAtencionOperativa ? await consultarSeguimiento.EjecutarAsync(id, ct, cualquierOrigen: true) : null;
        CategoriaId ??= reporte.CategoriaActualId;
        Tiempos = await consultarTiempos.EjecutarAsync(id, ct);
        // 27/10/2026: también admitido (Pendiente), para asignarlo desde aquí sin ir a la central.
        SerenosDisponibles = Seguimiento?.Asignacion is { FinalizadaEn: null } || reporte.Estado == EstadoCaso.Pendiente
            ? (await consultarSerenos.EjecutarAsync(ct)).Where(s => s.Disponible).ToList()
            : [];
        Descartado = reporte.Estado == EstadoCaso.Descartada ? await consultarDescarte.EjecutarAsync(id, ct) : null;
        return true;
    }

    public sealed class FormularioDecision
    {
        public TipoDecisionReporte Tipo { get; set; } = TipoDecisionReporte.Admision;
        public string? Motivo { get; set; }
        public string? Destino { get; set; }
        public string? DetalleDestino { get; set; }
        public string? Accion { get; set; }
        public string? Prioridad { get; set; }
        public string? Heridos { get; set; }
        public string? MotivoDescarte { get; set; }
    }

    public sealed class FormularioCierre
    {
        public Guid? CategoriaId { get; set; }
        public string? ResultadoAtencion { get; set; }
        public string? ResultadoPublico { get; set; }
        public string? NotasInternas { get; set; }
    }

    public sealed class FormularioAsignacion
    {
        public Guid IdSolicitud { get; set; }
        public Guid SerenoId { get; set; }
        public string? Volver { get; set; }
    }
}
