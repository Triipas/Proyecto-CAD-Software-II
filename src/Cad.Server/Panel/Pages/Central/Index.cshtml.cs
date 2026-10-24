using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Central;

// HU11 — Recibir los SOS en la sección de atención urgente (S1-T17). HU15-CA02 y HU20-CA07: los reportes admitidos se
// muestran aparte, en «Reportes admitidos», y se asignan con las mismas reglas.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarSosPendientes consultar, ConsultarSerenos consultarSerenos, ConsultarReportesAdmitidos consultarReportes,
    ConsultarCasosEnMapa consultarCasosEnMapa, ConsultarRutasEnCamino consultarRutas, ConsultarAcompanamientos consultarAcompanamientos, IConfiguration configuracion,
    Microsoft.Extensions.Options.IOptions<Modulos.Mediciones.OpcionesMedicion> medicion) : PageModel
{
    // RNF02: con la medición activada, el panel envía el acuse de presentación de cada SOS.
    public bool MedicionActiva => medicion.Value.Habilitada;

    public IReadOnlyList<SosPendiente> Pendientes { get; private set; } = [];

    public IReadOnlyList<ReporteAdmitido> ReportesAdmitidos { get; private set; } = [];

    // HU25: casos en curso con ubicación (los pendientes de asignación ya están en las listas).
    public IReadOnlyList<CasoEnMapa> CasosEnMapa { get; private set; } = [];

    // HU19 — serenos con su estado, disponibilidad y última ubicación.
    public IReadOnlyList<SerenoMonitoreo> Serenos { get; private set; } = [];

    // HU20 — resultado de la última asignación hecha desde esta página (patrón PRG).
    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    [TempData]
    public string? ErrorAccion { get; set; }

    // Mapa base mínimo (cláusula de mapa de HU11-CA01, opción aprobada). Valores de configuración, no de negocio.
    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -16.3988);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -71.5369);
    public string Zoom => Numero("Panel:Mapa:Zoom", 12);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Pendientes = await consultar.EjecutarAsync(ct);
        ReportesAdmitidos = await consultarReportes.EjecutarAsync(ct);
        Serenos = await consultarSerenos.EjecutarAsync(ct);
        CasosEnMapa = await consultarCasosEnMapa.EjecutarAsync(ct);
        AcompanamientosActivos = (await consultarAcompanamientos.ActivasAsync(ct)).Count;
    }

    // Indicadores de la barra (rediseño CAD v2, contrato «Mapa protagonista»): lo que el operador ve primero.
    public int AcompanamientosActivos { get; private set; }
    public int PorAsignar => Pendientes.Count + ReportesAdmitidos.Count;
    public int SerenosLibres => Serenos.Count(s => s.Disponible);

    // HU25-CA05: fragmento de los casos en curso del mapa, que el panel pide al resincronizar.
    public async Task<IActionResult> OnGetCasosMapaAsync(CancellationToken ct) =>
        Partial("_CasosMapa", await consultarCasosEnMapa.EjecutarAsync(ct));

    // Fragmento de los reportes admitidos: se pide junto con la lista de SOS al resincronizar.
    public async Task<IActionResult> OnGetReportesAsync(CancellationToken ct) =>
        Partial("_ListaReportes", new ListaReportes(await consultarReportes.EjecutarAsync(ct), ZonaHoraria()));

    // Fragmento de la lista: el panel lo pide al recibir un aviso o al reconectar (resincronización).
    public async Task<IActionResult> OnGetListaAsync(CancellationToken ct) =>
        Partial("_ListaSos", new ListaSos(await consultar.EjecutarAsync(ct), ZonaHoraria()));

    // HU19-CA03: fragmento de los serenos, que el panel pide al recibir «SerenoActualizado» o al reconectar.
    public async Task<IActionResult> OnGetSerenosAsync(CancellationToken ct) =>
        Partial("_ListaSerenos", new ListaSerenos(await consultarSerenos.EjecutarAsync(ct), ZonaHoraria()));

    // Recorrido por calles de cada sereno hasta su caso; el panel lo pide cuando un sereno con caso cambia de posición.
    // Puntos como [latitud, longitud], el formato de Leaflet.
    public async Task<IActionResult> OnGetRutasAsync(CancellationToken ct) =>
        new JsonResult((await consultarRutas.EjecutarAsync(ct)).Select(r => new
        {
            sereno = r.SerenoId,
            caso = r.CasoId,
            puntos = r.Puntos?.Select(p => new[] { Math.Round(p.Latitud, 6), Math.Round(p.Longitud, 6) }),
            metros = r.Metros is { } m ? Math.Round(m) : (double?)null,
        }));

    public ListaSos Lista => new(Pendientes, ZonaHoraria());
    public ListaReportes ListaDeReportes => new(ReportesAdmitidos, ZonaHoraria());
    public ListaSerenos ListaDeSerenos => new(Serenos, ZonaHoraria());

    private TimeZoneInfo ZonaHoraria() =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    private string Numero(string clave, double porDefecto) =>
        configuracion.GetValue(clave, porDefecto).ToString(CultureInfo.InvariantCulture);
}

public sealed record ListaSos(IReadOnlyList<SosPendiente> Pendientes, TimeZoneInfo ZonaHoraria);

public sealed record ListaReportes(IReadOnlyList<ReporteAdmitido> Admitidos, TimeZoneInfo ZonaHoraria);

public sealed record ListaSerenos(IReadOnlyList<SerenoMonitoreo> Serenos, TimeZoneInfo ZonaHoraria);
