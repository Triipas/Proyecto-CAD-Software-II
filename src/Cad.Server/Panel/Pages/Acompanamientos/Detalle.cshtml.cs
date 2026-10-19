using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Acompanamientos;

// HU38-CA01 (cada posición con su hora y su precisión) y HU39-A-CA02/CA03 (datos y recorrido de una sesión terminada). Los
// recorridos son información restringida (RNF12): solo el operador autorizado, también ante una solicitud directa.
// HU39-B (CA05): la conservación excepcional del recorrido se registra desde esta misma página.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class DetalleModel(ConsultarAcompanamientos consultar, Modulos.Acompanamiento.Aplicacion.ConservarRecorridos conservar,
    IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioConservacion Conservacion { get; set; } = new();

    public Dictionary<string, string[]> Errores { get; private set; } = [];
    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    [TempData]
    public string? Mensaje { get; set; }

    public int ConservacionDias => conservar.ConservacionDias;

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

    // CA05: vincula el recorrido con un caso, con su finalidad; desde entonces no se elimina por plazo.
    public async Task<IActionResult> OnPostConservarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await conservar.ConservarAsync(id, Conservacion.CasoId, Conservacion.Finalidad, User.IdUsuario(), ct);
        switch (resultado)
        {
            case Modulos.Acompanamiento.Aplicacion.ResultadoConservacion.NoEncontrada:
                return NotFound();
            case Modulos.Acompanamiento.Aplicacion.ResultadoConservacion.Registrada or Modulos.Acompanamiento.Aplicacion.ResultadoConservacion.YaRegistrada:
                Mensaje = "Recorrido conservado: no se eliminará al cumplirse el plazo.";
                return RedirectToPage(new { id });
            default:
                Errores = (resultado as Modulos.Acompanamiento.Aplicacion.ResultadoConservacion.Invalida)?.Errores
                    ?? new() { ["casoId"] = ["Las posiciones de esta sesión ya se eliminaron por plazo: no hay recorrido que conservar."] };
                return await OnGetAsync(id, ct);
        }
    }

    public sealed class FormularioConservacion
    {
        public Guid? CasoId { get; set; }
        public string? Finalidad { get; set; }
    }
}
