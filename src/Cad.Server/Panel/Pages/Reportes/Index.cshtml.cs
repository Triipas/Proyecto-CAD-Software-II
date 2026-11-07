using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// 06/11/2026: «Reportes» ya no es una sección del panel. Los reportes por revisar y por asignar están en la pestaña «Reportes» de
// la bandeja de la central (con «Nuevo reporte» y los criterios de triaje); esta ruta redirige allí y conserva la pausa de la
// admisión automática (HU45), que el aviso de la central envía aquí. Solo operadores.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel : PageModel
{
    public IActionResult OnGet() => Redirect("/panel/central?pestana=reportes");

    [TempData]
    public string? Mensaje { get; set; }

    // HU45 (7.2): el operador solo puede pausar la admisión automática (pasa a «Apagada»). Vuelve a la central.
    public async Task<IActionResult> OnPostPausarAsync([FromServices] CambiarAjusteIa cambiar, CancellationToken ct)
    {
        Mensaje = await cambiar.PausarAsync(User.IdUsuario(), ct) is ResultadoAjusteIa.Cambiado
            ? "Admisión automática pausada. Solo el administrador puede reactivarla."
            : "La admisión automática ya estaba apagada.";
        var origen = Request.Headers.Referer.ToString();
        return Uri.TryCreate(origen, UriKind.Absolute, out var url) && url.AbsolutePath == "/panel/central"
            ? Redirect("/panel/central")
            : Redirect("/panel/central?pestana=reportes");
    }
}
