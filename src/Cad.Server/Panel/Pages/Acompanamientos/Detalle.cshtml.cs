using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Acompanamientos;

// HU38-CA01 (cada posición con su hora y su precisión) y HU39-A-CA02/CA03 (datos y recorrido de una sesión terminada). Los
// recorridos son información restringida (RNF12): solo el operador autorizado, también ante una solicitud directa.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class DetalleModel(ConsultarAcompanamientos consultar, IConfiguration configuracion) : PageModel
{
    public DetalleAcompanamiento Sesion { get; private set; } = null!;
    public TimeZoneInfo ZonaHoraria => Formato.Zona(configuracion);
    public TimeSpan Umbral => consultar.Umbral;
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.DetalleAsync(id, ct) is not { } sesion)
            return NotFound();
        Sesion = sesion;
        return Page();
    }
}
