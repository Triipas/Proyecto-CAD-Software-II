using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Central;

// HU11 — Recibir los SOS en la sección de atención urgente (S1-T17).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarSosPendientes consultar, ConsultarSerenos consultarSerenos, IConfiguration configuracion,
    Microsoft.Extensions.Options.IOptions<Modulos.Mediciones.OpcionesMedicion> medicion) : PageModel
{
    // RNF02: con la medición activada, el panel envía el acuse de presentación de cada SOS.
    public bool MedicionActiva => medicion.Value.Habilitada;

    public IReadOnlyList<SosPendiente> Pendientes { get; private set; } = [];

    // HU19 — serenos con su estado, disponibilidad y última ubicación.
    public IReadOnlyList<SerenoMonitoreo> Serenos { get; private set; } = [];

    // Mapa base mínimo (cláusula de mapa de HU11-CA01, opción aprobada). Valores de configuración, no de negocio.
    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -12.0464);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -77.0428);
    public string Zoom => Numero("Panel:Mapa:Zoom", 12);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Pendientes = await consultar.EjecutarAsync(ct);
        Serenos = await consultarSerenos.EjecutarAsync(ct);
    }

    // Fragmento de la lista: el panel lo pide al recibir un aviso o al reconectar (resincronización).
    public async Task<IActionResult> OnGetListaAsync(CancellationToken ct) =>
        Partial("_ListaSos", new ListaSos(await consultar.EjecutarAsync(ct), ZonaHoraria()));

    // HU19-CA03: fragmento de los serenos, que el panel pide al recibir «SerenoActualizado» o al reconectar.
    public async Task<IActionResult> OnGetSerenosAsync(CancellationToken ct) =>
        Partial("_ListaSerenos", new ListaSerenos(await consultarSerenos.EjecutarAsync(ct), ZonaHoraria()));

    public ListaSos Lista => new(Pendientes, ZonaHoraria());
    public ListaSerenos ListaDeSerenos => new(Serenos, ZonaHoraria());

    private TimeZoneInfo ZonaHoraria() =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    private string Numero(string clave, double porDefecto) =>
        configuracion.GetValue(clave, porDefecto).ToString(CultureInfo.InvariantCulture);
}

public sealed record ListaSos(IReadOnlyList<SosPendiente> Pendientes, TimeZoneInfo ZonaHoraria);

public sealed record ListaSerenos(IReadOnlyList<SerenoMonitoreo> Serenos, TimeZoneInfo ZonaHoraria);
