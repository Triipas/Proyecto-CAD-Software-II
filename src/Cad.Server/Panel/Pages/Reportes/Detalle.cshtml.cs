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
// curso, como en un SOS.
public class DetalleModel(ConsultarDetalleReporte consultar, RevisarReporte revisar, DecidirReporte decidir, AsignarCaso asignar,
    ConsultarCategorias consultarCategorias, ConsultarDetalleSos consultarSeguimiento, AvanzarCaso avanzar, CerrarCaso cerrar,
    ConsultarTiemposCaso consultarTiempos, ConsultarFichaCiudadano consultarFicha, IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioCierre Cierre { get; set; } = new();

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
        EstadoCaso.Derivada or EstadoCaso.Descartada => SiguienteAccion.VerDecision,
        _ when Seguimiento?.Cierre is not null => SiguienteAccion.VerCierre,
        _ when Seguimiento is { EnAtencion: true } => SiguienteAccion.Cerrar,
        _ when Seguimiento is { EnCamino: true } => SiguienteAccion.EsperarAtencion,
        _ when Seguimiento?.Asignacion is not null => SiguienteAccion.EsperarDesplazamiento,
        EstadoCaso.Pendiente => SiguienteAccion.Asignar,
        _ => SiguienteAccion.SinAcciones,
    };

    // Recibido → En revisión → Admitido → Asignado → En camino → En atención → Cerrado; un reporte derivado o descartado
    // termina en el tercer paso.
    public IReadOnlyList<PasoCaso> Pasos
    {
        get
        {
            if (Reporte.Estado is EstadoCaso.Derivada or EstadoCaso.Descartada)
                return ProgresoCaso.Construir(
                    ["Recibido", "En revisión", Reporte.Estado == EstadoCaso.Derivada ? "Derivado" : "Descartado"], [true, true, true], -1);

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
            string[] textos = ["Recibido", "En revisión", "Admitido", "Asignado", "En camino", "En atención",
                Reporte.Estado == EstadoCaso.Cancelada ? "Cancelado" : "Cerrado"];
            return ProgresoCaso.Construir(textos, hechos, indice < 6 ? indice + 1 : -1);
        }
    }

    public enum SiguienteAccion { IniciarRevision, Decidir, Asignar, EsperarDesplazamiento, EsperarAtencion, Cerrar, VerCierre, VerDecision, SinAcciones }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct) =>
        await CargarAsync(id, ct) ? Page() : NotFound();

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
        var resultado = await decidir.EjecutarAsync(id, new DatosDecision(Decision.Tipo, Decision.Motivo, Decision.Destino, Decision.Accion),
            User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoDecision.Registrado r:
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

    // HU20-CA07: un reporte admitido se asigna desde la central con las mismas reglas que un SOS. Vuelve a la central.
    public async Task<IActionResult> OnPostAsignarAsync(Guid id, CancellationToken ct)
    {
        switch (await asignar.EjecutarAsync(id, Asignacion.SerenoId, User.IdUsuario(), Asignacion.IdSolicitud, ct))
        {
            case ResultadoAsignacion.Asignado or ResultadoAsignacion.YaAsignado:
                TempData[nameof(Central.IndexModel.Mensaje)] = "Reporte asignado.";
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
        switch (await avanzar.EjecutarAsync(id, Avance.IniciarAtencion, User.IdUsuario(), ActorAvance.Operador, ct))
        {
            case ResultadoAvance.Registrado or ResultadoAvance.YaRegistrado:
                return Hecho(id, "Inicio de atención registrado.", "Quedas como responsable del registro.");
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
        var resultado = await cerrar.EjecutarAsync(id, new DatosCierre(Cierre.CategoriaId, Cierre.ResultadoPublico, Cierre.NotasInternas),
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
        ? "Asígnalo a un sereno disponible desde la central."
        : "Es un estado final.";

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
        return true;
    }

    public sealed class FormularioDecision
    {
        public TipoDecisionReporte Tipo { get; set; } = TipoDecisionReporte.Admision;
        public string? Motivo { get; set; }
        public string? Destino { get; set; }
        public string? Accion { get; set; }
    }

    public sealed class FormularioCierre
    {
        public Guid? CategoriaId { get; set; }
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
