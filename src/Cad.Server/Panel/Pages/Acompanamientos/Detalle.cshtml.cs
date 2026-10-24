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
    Modulos.Acompanamiento.Aplicacion.ConfirmarBienestar confirmar, ConsultarAvisos consultarAvisos, Modulos.Consultas.ConsultarFichaCiudadano consultarFicha,
    IConfiguration configuracion) : PageModel
{
    // HU42: avisos de esta sesión.
    public IReadOnlyList<AvisoVista> Avisos { get; private set; } = [];

    // HU40: identificador del formulario de solicitud; un reenvío no crea otra solicitud.
    [BindProperty]
    public Guid IdSolicitudConfirmacion { get; set; }

    public string? ErrorConfirmacion { get; private set; }

    [BindProperty]
    public FormularioConservacion Conservacion { get; set; } = new();

    public Dictionary<string, string[]> Errores { get; private set; } = [];
    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    [TempData]
    public string? Mensaje { get; set; }

    public int ConservacionDias => conservar.ConservacionDias;

    public DetalleAcompanamiento Sesion { get; private set; } = null!;

    // 24/10/2026: la tabla de posiciones va por páginas (la más reciente primero); el mapa sigue mostrando todo el recorrido.
    public const int PosicionesPorPagina = 20;

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    public int Paginas => Math.Max(1, (int)Math.Ceiling(Sesion.Posiciones.Count / (double)PosicionesPorPagina));
    public int PrimeraDeLaPagina => (Pagina - 1) * PosicionesPorPagina;
    public IEnumerable<(PosicionVista Posicion, int Indice)> PosicionesDeLaPagina =>
        Sesion.Posiciones.Select((p, i) => (p, i)).Skip(PrimeraDeLaPagina).Take(PosicionesPorPagina);

    // ADR-08, fase D: celular verificado y datos opcionales del ciudadano (p. ej., su contacto de emergencia).
    public Modulos.Consultas.FichaCiudadano? Ficha { get; private set; }
    public TimeZoneInfo ZonaHoraria => Formato.Zona(configuracion);
    public TimeSpan Umbral => consultar.Umbral;
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.DetalleAsync(id, ct) is not { } sesion)
            return NotFound();
        Sesion = sesion;
        Pagina = Math.Clamp(Pagina, 1, Paginas);
        Ficha = await consultarFicha.PorSesionAsync(id, ct);
        Avisos = await consultarAvisos.DeSesionAsync(id, ct);
        IdSolicitudConfirmacion = Guid.NewGuid();
        return Page();
    }

    // HU40: pedir al ciudadano una confirmación de bienestar. No crea un SOS ni un despacho (CA03).
    public async Task<IActionResult> OnPostSolicitarConfirmacionAsync(Guid id, CancellationToken ct)
    {
        switch (await confirmar.SolicitarAsync(id, User.IdUsuario(), IdSolicitudConfirmacion, ct))
        {
            case Modulos.Acompanamiento.Aplicacion.ResultadoSolicitudBienestar.NoEncontrada:
                return NotFound();
            case Modulos.Acompanamiento.Aplicacion.ResultadoSolicitudBienestar.Solicitada or Modulos.Acompanamiento.Aplicacion.ResultadoSolicitudBienestar.YaSolicitada:
                Mensaje = "Confirmación solicitada. El ciudadano la verá en su aplicación.";
                return RedirectToPage(new { id });
            case Modulos.Acompanamiento.Aplicacion.ResultadoSolicitudBienestar.SesionNoActiva:
                ErrorConfirmacion = "La sesión ya no está activa: no se puede pedir una confirmación.";
                break;
            default:
                ErrorConfirmacion = "Ya hay una confirmación esperando respuesta en esta sesión.";
                break;
        }
        return await OnGetAsync(id, ct);
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
