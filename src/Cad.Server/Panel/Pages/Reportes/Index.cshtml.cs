using Cad.Server.Compartido;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// E5 — página «Reportes»: reportes recibidos y en revisión (HU14). Solo operadores.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarReportes consultar, IConfiguration configuracion) : PageModel
{
    public IReadOnlyList<ReporteEnBandeja> Reportes { get; private set; } = [];

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public async Task OnGetAsync(CancellationToken ct) => Reportes = await consultar.EjecutarAsync(ct);
}
