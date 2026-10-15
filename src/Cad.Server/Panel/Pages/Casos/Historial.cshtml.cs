using Cad.Server.Compartido;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Casos;

// HU27 — Consultar el historial de un caso (S3-T11). Solo operadores (CA06): un administrador, un sereno o un
// ciudadano no la ven, tampoco con una solicitud directa; así las notas internas quedan restringidas (CA04).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class HistorialModel(ConsultarHistorialCaso consultar, IConfiguration configuracion) : PageModel
{
    public HistorialCaso Historial { get; private set; } = null!;

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } historial)
            return NotFound();
        Historial = historial;
        return Page();
    }
}
