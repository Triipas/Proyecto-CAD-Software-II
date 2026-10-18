using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Acompanamientos;

// HU39-A — Consultar el historial de sesiones de acompañamiento (S4-T11): separado del historial de los casos (CA01), con los
// datos de inicio, vencimiento y cierre (CA02) y solo para el operador autorizado (CA03, DP04).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class HistorialModel(ConsultarAcompanamientos consultar, IConfiguration configuracion) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public PaginaSesiones Resultado { get; private set; } = null!;
    public TimeZoneInfo ZonaHoraria => Formato.Zona(configuracion);

    public async Task OnGetAsync(CancellationToken ct) => Resultado = await consultar.HistorialAsync(Pagina, ct);
}
