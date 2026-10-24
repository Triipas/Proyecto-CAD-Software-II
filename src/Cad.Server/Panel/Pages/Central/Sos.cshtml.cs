using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Panel.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Central;

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01) y
// HU20 — Asignar el caso pendiente a un sereno disponible (S2-T05) y
// HU13 — Clasificar el SOS después de su registro (S2-T08) y
// HU23 — Registrar el inicio de atención como operador (S2-T11, CA02) y
// HU24 — Cerrar el caso como operador (S2-T13, CA05) y descartar un caso falso o erróneo (23/10/2026). Solo operadores.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class SosModel(ConsultarDetalleSos consultar, RegistrarIntentoContacto registrar, AsignarCaso asignar,
    ClasificarCaso clasificar, AvanzarCaso avanzar, CerrarCaso cerrar, ConsultarSerenos consultarSerenos, ConsultarCategorias consultarCategorias,
    ConsultarTiemposCaso consultarTiempos, ConsultarFichaCiudadano consultarFicha, DescartarCaso descartar, ConsultarDescarte consultarDescarte, ReasignarCaso reasignar,
    IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioIntento Formulario { get; set; } = new();

    [BindProperty]
    public FormularioAsignacion Asignacion { get; set; } = new();

    [BindProperty]
    public FormularioClasificacion Clasificacion { get; set; } = new();

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
    public bool PuedeReasignarse => Sos.Asignacion is { FinalizadaEn: null } && (Sos.Estado == EstadosCaso.Texto(EstadoCaso.Asignada) || Sos.EnCamino);
    public VistaReasignacion VistaReasignacion => new($"/panel/central/sos/{Sos.Id}", Sos.Asignacion?.Sereno ?? "",
        SerenosDisponibles.Select(s => new SerenoParaReasignar(s.Id, s.Nombre, s.UltimaPosicionEn is { } h
            ? $"última ubicación a las {TimeZoneInfo.ConvertTime(h, ZonaHoraria):HH:mm:ss}" : "ubicación no disponible")).ToList(),
        Reasignacion.IdSolicitud == Guid.Empty ? new FormularioReasignacion { IdSolicitud = Guid.NewGuid() } : Reasignacion,
        ErroresReasignacion, ErrorReasignacion);

    public DetalleSos Sos { get; private set; } = null!;

    // ADR-08, fase D: celular verificado y datos opcionales del ciudadano.
    public FichaCiudadano? Ficha { get; private set; }

    // HU28: tiempo de respuesta del SOS.
    public TiemposCaso? Tiempos { get; private set; }

    // HU20: serenos que pueden recibir el caso (operativos y sin asignación vigente).
    public IReadOnlyList<SerenoMonitoreo> SerenosDisponibles { get; private set; } = [];

    // HU13-CA04: solo las categorías del catálogo vigente (DP07).
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];

    public Dictionary<string, string[]> Errores { get; private set; } = [];
    public string? ErrorAsignacion { get; private set; }
    public string? ErrorCategoria { get; private set; }
    public string? ErrorClasificacion { get; private set; }
    public string? ErrorAvance { get; private set; }
    public string? ErrorCierre { get; private set; }
    public Dictionary<string, string[]> ErroresCierre { get; private set; } = [];

    public string? ErrorDescarte { get; private set; }
    public string? ErrorMotivoDescarte { get; private set; }

    // Descarte por la central: el registrado (si el caso ya se descartó) y el bloque para descartarlo mientras está abierto.
    public DescarteVista? Descartado { get; private set; }
    public bool PuedeDescartarse => new[] { EstadoCaso.Pendiente, EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion }
        .Any(e => Sos.Estado == EstadosCaso.Texto(e));
    public VistaDescarte VistaDescarte => new($"/panel/central/sos/{Sos.Id}",
        Sos.Asignacion is { FinalizadaEn: null } vigente ? vigente.Sereno : null, Descarte.Motivo, ErrorDescarte, ErrorMotivoDescarte);

    public string? ErrorCierreDe(string campo) => ErroresCierre.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    // Etiquetas visibles de los tres resultados válidos (HU12-CA02).
    public static readonly IReadOnlyList<(ResultadoContacto Valor, string Texto)> Resultados =
    [
        (ResultadoContacto.Contactado, "Contactado"),
        (ResultadoContacto.SinRespuesta, "Sin respuesta"),
        (ResultadoContacto.NoFuePosibleComunicar, "No fue posible comunicar"),
    ];

    public static string Texto(ResultadoContacto resultado) => Resultados.Single(r => r.Valor == resultado).Texto;

    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    // Rediseño CAD v2 (fase 3): el detalle se guía por una sola «Siguiente acción» y una línea de progreso.
    // Verificar y clasificar son opcionales para asignar (HU13-CA02): si se saltan, quedan como omitidos.
    public bool Contactado => Sos.Intentos.Any(i => i.Resultado == ResultadoContacto.Contactado);

    public SiguienteAccion Siguiente => Sos.Estado switch
    {
        "Cancelada" => SiguienteAccion.VerCancelacion,
        "Descartada" => SiguienteAccion.VerDescarte,
        "Cerrada" => SiguienteAccion.VerCierre,
        _ when Sos.EnAtencion => SiguienteAccion.Cerrar,
        _ when Sos.EnCamino => SiguienteAccion.EsperarAtencion,
        _ when Sos.Asignacion is not null => SiguienteAccion.EsperarDesplazamiento,
        "Pendiente" when !Contactado => SiguienteAccion.Contactar,
        "Pendiente" when Sos.Clasificacion is null => SiguienteAccion.Clasificar,
        "Pendiente" => SiguienteAccion.Asignar,
        _ => SiguienteAccion.NoAsignable,
    };

    public IReadOnlyList<PasoCaso> Pasos
    {
        get
        {
            var avances = Sos.Avances ?? [];
            var cancelado = Sos.Estado == "Cancelada";
            var descartado = Sos.Estado == "Descartada";
            bool[] hechos =
            [
                true,
                Contactado,
                Sos.Clasificacion is not null,
                Sos.Asignacion is not null,
                Sos.EnCamino || Sos.EnAtencion || avances.Any(a => a.Descripcion == "En camino") || Sos.Cierre is not null,
                Sos.EnAtencion || avances.Any(a => a.Descripcion == "En atención") || Sos.Cierre is not null,
                Sos.Cierre is not null || cancelado || descartado,
            ];
            string[] textos = ["Pendiente", "Contactado", "Clasificado", "Asignada", "En camino", "En atención", cancelado ? "Cancelada" : descartado ? "Descartada" : "Cerrada"];
            var actual = Siguiente switch
            {
                SiguienteAccion.Contactar => 1,
                SiguienteAccion.Clasificar => 2,
                SiguienteAccion.Asignar => 3,
                SiguienteAccion.EsperarDesplazamiento => 4,
                SiguienteAccion.EsperarAtencion => 5,
                SiguienteAccion.Cerrar => 6,
                _ => -1,
            };
            return ProgresoCaso.Construir(textos, hechos, actual);
        }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!await CargarAsync(id, ct))
            return NotFound();
        // Identificadores de envío: un reenvío del mismo formulario no duplica el intento ni la asignación.
        Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
        Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
        return Page();
    }

    // HU12: registrar un intento de contacto.
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(id, User.IdUsuario(), new DatosIntentoContacto(
            Formulario.IdSolicitud, Formulario.Resultado, Formulario.InformacionHecho, Formulario.ReferenciaUbicacion), ct);

        switch (resultado)
        {
            case ResultadoIntentoContacto.Registrado or ResultadoIntentoContacto.YaRegistrado:
                Mensaje = "Intento de contacto registrado.";
                DetalleMensaje = "Puedes registrar más intentos.";
                return RedirectToPage(new { id }); // PRG: recargar no vuelve a enviar el formulario
            case ResultadoIntentoContacto.Invalido invalido when await CargarAsync(id, ct):
                Errores = invalido.Errores;
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
            default:
                return NotFound();
        }
    }

    // HU20: asignar el caso a un sereno disponible. Desde la central (Volver = "central") vuelve a ella.
    public async Task<IActionResult> OnPostAsignarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await asignar.EjecutarAsync(id, Asignacion.SerenoId, User.IdUsuario(), Asignacion.IdSolicitud, ct);
        var volverACentral = Asignacion.Volver == "central";

        switch (resultado)
        {
            case ResultadoAsignacion.Asignado or ResultadoAsignacion.YaAsignado:
                Mensaje = "SOS asignado.";
                TempData[SonidoPanel.Clave] = SonidoPanel.CasoAsignado;
                DetalleMensaje = "El sereno ya no figura como disponible.";
                return volverACentral ? RedirectToPage("/Central/Index") : RedirectToPage(new { id });
            case ResultadoAsignacion.CasoNoEncontrado:
                return NotFound();
            case ResultadoAsignacion.Rechazado rechazo when volverACentral:
                TempData[nameof(IndexModel.ErrorAccion)] = rechazo.Motivo;
                return RedirectToPage("/Central/Index");
            case ResultadoAsignacion.Invalido invalido when volverACentral:
                TempData[nameof(IndexModel.ErrorAccion)] = invalido.Motivo;
                return RedirectToPage("/Central/Index");
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorAsignacion = resultado switch
                {
                    ResultadoAsignacion.Rechazado r => r.Motivo,
                    ResultadoAsignacion.Invalido i => i.Motivo,
                    _ => "No se pudo asignar el caso.",
                };
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    // HU13: clasificar el SOS con una categoría del catálogo.
    public async Task<IActionResult> OnPostClasificarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await clasificar.EjecutarAsync(id, Clasificacion.CategoriaId, User.IdUsuario(), ct);

        switch (resultado)
        {
            case ResultadoClasificacion.Clasificado c:
                return Clasificado(id, c.Categoria);
            case ResultadoClasificacion.YaClasificado c:
                return Clasificado(id, c.Categoria);
            case ResultadoClasificacion.CasoNoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorCategoria = (resultado as ResultadoClasificacion.Invalido)?.Motivo;
                ErrorClasificacion = (resultado as ResultadoClasificacion.Rechazado)?.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    // HU23-CA02: el operador registra el inicio de atención y queda como responsable.
    public async Task<IActionResult> OnPostIniciarAtencionAsync(Guid id, CancellationToken ct)
    {
        switch (await avanzar.EjecutarAsync(id, Avance.IniciarAtencion, User.IdUsuario(), ActorAvance.Operador, ct,
                    new Cad.Contratos.Serenos.SolicitudInicioAtencion(Detalle: MotivoInicio)))
        {
            case ResultadoAvance.Registrado or ResultadoAvance.YaRegistrado:
                Mensaje = "Inicio de atención registrado.";
                DetalleMensaje = "Quedas como responsable del registro, con el motivo que indicaste.";
                return RedirectToPage(new { id });
            case ResultadoAvance.Invalido i when await CargarAsync(id, ct):
                ErrorMotivoInicio = i.Errores.Values.First()[0];
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
            case ResultadoAvance.Rechazado r when await CargarAsync(id, ct):
                ErrorAvance = r.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
            default:
                return NotFound();
        }
    }

    // HU24-CA05: el operador cierra el caso y queda como responsable.
    public async Task<IActionResult> OnPostCerrarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await cerrar.EjecutarAsync(id, new DatosCierre(Cierre.CategoriaId, Cierre.ResultadoPublico, Cierre.NotasInternas),
            User.IdUsuario(), ActorAvance.Operador, ct);
        switch (resultado)
        {
            case ResultadoCierre.Cerrado or ResultadoCierre.YaCerrado:
                Mensaje = "Caso cerrado.";
                DetalleMensaje = "El sereno vuelve a figurar como disponible si sigue operativo.";
                return RedirectToPage(new { id });
            case ResultadoCierre.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErroresCierre = (resultado as ResultadoCierre.Invalido)?.Errores ?? [];
                ErrorCierre = (resultado as ResultadoCierre.Rechazado)?.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    // Descarte por la central (23/10/2026): un caso falso o erróneo termina como «Descartada», con motivo obligatorio.
    public async Task<IActionResult> OnPostDescartarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await descartar.EjecutarAsync(id, Descarte.Motivo, User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoDescarte.Descartado or ResultadoDescarte.YaDescartado:
                Mensaje = "Caso descartado.";
                DetalleMensaje = "Salió de la cola y del mapa; si tenía sereno, quedó libre para otro caso.";
                return RedirectToPage(new { id });
            case ResultadoDescarte.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorMotivoDescarte = (resultado as ResultadoDescarte.Invalido)?.Errores.GetValueOrDefault("motivo")?[0];
                ErrorDescarte = (resultado as ResultadoDescarte.Rechazado)?.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    // Reasignación por la central (24/10/2026): a otro sereno disponible, con motivo; solo «Asignada» o «En camino».
    public async Task<IActionResult> OnPostReasignarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await reasignar.EjecutarAsync(id, Reasignacion.SerenoId, Reasignacion.Motivo, User.IdUsuario(), Reasignacion.IdSolicitud, ct);
        switch (resultado)
        {
            case ResultadoReasignacion.Reasignado or ResultadoReasignacion.YaReasignado:
                Mensaje = "Caso reasignado.";
                TempData[SonidoPanel.Clave] = SonidoPanel.CasoAsignado;
                DetalleMensaje = "El sereno anterior quedó libre; el nuevo recibió el despacho.";
                return RedirectToPage(new { id });
            case ResultadoReasignacion.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErroresReasignacion = (resultado as ResultadoReasignacion.Invalido)?.Errores ?? [];
                ErrorReasignacion = (resultado as ResultadoReasignacion.Rechazado)?.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    private RedirectToPageResult Clasificado(Guid id, string categoria)
    {
        Mensaje = "SOS clasificado.";
        DetalleMensaje = $"Tipo: {categoria}.";
        return RedirectToPage(new { id }); // PRG
    }

    private async Task<bool> CargarAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } sos)
            return false;
        Sos = sos;
        Ficha = await consultarFicha.PorCasoAsync(id, ct);
        SerenosDisponibles = (await consultarSerenos.EjecutarAsync(ct)).Where(s => s.Disponible).ToList();
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        Tiempos = await consultarTiempos.EjecutarAsync(id, ct);
        Descartado = await consultarDescarte.EjecutarAsync(id, ct);
        return true;
    }

    public enum SiguienteAccion { Contactar, Clasificar, Asignar, EsperarDesplazamiento, EsperarAtencion, Cerrar, VerCierre, VerCancelacion, VerDescarte, NoAsignable }

    public sealed class FormularioIntento
    {
        public Guid IdSolicitud { get; set; }
        public string? Resultado { get; set; }
        public string? InformacionHecho { get; set; }
        public string? ReferenciaUbicacion { get; set; }
    }

    public sealed class FormularioAsignacion
    {
        public Guid IdSolicitud { get; set; }
        public Guid SerenoId { get; set; }
        public string? Volver { get; set; }
    }

    public sealed class FormularioClasificacion
    {
        public Guid? CategoriaId { get; set; }
    }

    public sealed class FormularioCierre
    {
        public Guid? CategoriaId { get; set; }
        public string? ResultadoPublico { get; set; }
        public string? NotasInternas { get; set; }
    }
}
