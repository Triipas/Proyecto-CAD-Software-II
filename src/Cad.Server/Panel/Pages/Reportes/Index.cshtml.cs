using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// E5 — página «Reportes»: reportes recibidos y en revisión (HU14). Solo operadores.
// HU44: la categoría que sugiere el asistente, cuando difiere de la vigente. HU45 (S6-T15): aviso del modo de la admisión
// automática y su pausa.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarReportes consultar, ConsultarAsistencia consultarAsistencia, IConfiguration configuracion) : PageModel
{
    public IReadOnlyList<ReporteEnBandeja> Reportes { get; private set; } = [];
    public IReadOnlyDictionary<Guid, string> CategoriasSugeridas { get; private set; } = new Dictionary<Guid, string>();
    public AjusteIaVista Ajuste { get; private set; } = null!;

    [TempData]
    public string? Mensaje { get; set; }

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public async Task OnGetAsync(CancellationToken ct)
    {
        Reportes = await consultar.EjecutarAsync(ct);
        CategoriasSugeridas = await consultarAsistencia.CategoriasDistintasAsync(Reportes.Select(r => r.Id).ToList(), ct);
        Ajuste = await consultarAsistencia.AjusteAsync(ct);
    }

    // HU45 (7.2): el operador solo puede pausar la admisión automática (pasa a «Apagada»). Vuelve a donde estaba (Reportes o Central).
    public async Task<IActionResult> OnPostPausarAsync([FromServices] CambiarAjusteIa cambiar, CancellationToken ct)
    {
        Mensaje = await cambiar.PausarAsync(User.IdUsuario(), ct) is ResultadoAjusteIa.Cambiado
            ? "Admisión automática pausada. Solo el administrador puede reactivarla."
            : "La admisión automática ya estaba apagada.";
        var origen = Request.Headers.Referer.ToString();
        return Uri.TryCreate(origen, UriKind.Absolute, out var url) && url.AbsolutePath == "/panel/central"
            ? Redirect("/panel/central")
            : RedirectToPage();
    }
}
