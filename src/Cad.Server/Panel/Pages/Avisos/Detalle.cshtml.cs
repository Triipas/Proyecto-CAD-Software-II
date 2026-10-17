using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Cad.Server.Panel.Pages.Acompanamientos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Avisos;

// HU42 y HU43 — un aviso del acompañamiento: sus datos de origen, su revisión, sus actuaciones, su resolución y su vínculo con
// un caso. Solo el operador autorizado (DP04), también ante una solicitud directa. Ninguna acción asigna un sereno (HU43-CA06).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class DetalleModel(ConsultarAvisos consultar, AtenderAvisos atender, EvaluarAvisos evaluar, IConfiguration configuracion) : PageModel
{
    [BindProperty] public Guid IdSolicitud { get; set; }
    [BindProperty] public string? TipoActuacion { get; set; }
    [BindProperty] public string? Descripcion { get; set; }
    [BindProperty] public Guid? CasoId { get; set; }
    [BindProperty] public string? Justificacion { get; set; }

    [TempData] public string? Mensaje { get; set; }
    public string? Error { get; private set; }
    public Dictionary<string, string[]> Errores { get; private set; } = [];
    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public DetalleAviso Aviso { get; private set; } = null!;
    public TimeZoneInfo ZonaHoraria => Formato.Zona(configuracion);
    public int UmbralSegundos => (int)evaluar.Umbral.TotalSeconds;
    public int PlazoConfirmacionSegundos => (int)evaluar.PlazoConfirmacion.TotalSeconds;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.DetalleAsync(id, ct) is not { } aviso)
            return NotFound();
        Aviso = aviso;
        IdSolicitud = Guid.NewGuid();
        return Page();
    }

    public Task<IActionResult> OnPostRevisarAsync(Guid id, CancellationToken ct) =>
        ResponderAsync(id, atender.RevisarAsync(id, User.IdUsuario(), ct), "Aviso en revisión: quedas como responsable.", ct);

    public Task<IActionResult> OnPostActuacionAsync(Guid id, CancellationToken ct) =>
        ResponderAsync(id, atender.RegistrarActuacionAsync(id, User.IdUsuario(), TipoActuacion, Descripcion, IdSolicitud, ct), "Actuación registrada.", ct);

    public Task<IActionResult> OnPostResolverAsync(Guid id, CancellationToken ct) =>
        ResponderAsync(id, atender.ResolverAsync(id, User.IdUsuario(), ct), "Aviso resuelto.", ct);

    public Task<IActionResult> OnPostVincularAsync(Guid id, CancellationToken ct) =>
        ResponderAsync(id, atender.VincularAsync(id, CasoId, User.IdUsuario(), ct), "Aviso vinculado con el caso.", ct);

    public Task<IActionResult> OnPostCrearCasoAsync(Guid id, CancellationToken ct) =>
        ResponderAsync(id, atender.CrearCasoUrgenteAsync(id, Justificacion, User.IdUsuario(), IdSolicitud, ct),
            "Caso urgente creado y vinculado. Está pendiente en la central para asignarlo a un sereno.", ct);

    private async Task<IActionResult> ResponderAsync(Guid id, Task<ResultadoAvisoAtendido> accion, string exito, CancellationToken ct)
    {
        switch (await accion)
        {
            case ResultadoAvisoAtendido.NoEncontrado:
                return NotFound();
            case ResultadoAvisoAtendido.Correcto:
                Mensaje = exito;
                return RedirectToPage(new { id });
            case ResultadoAvisoAtendido.Rechazado r:
                Error = r.Motivo;
                break;
            case ResultadoAvisoAtendido.Invalido i:
                Errores = i.Errores;
                break;
        }
        // Se conservan los datos escritos al volver a mostrar el formulario con su error.
        var (tipo, descripcion, caso, justificacion) = (TipoActuacion, Descripcion, CasoId, Justificacion);
        var resultado = await OnGetAsync(id, ct);
        (TipoActuacion, Descripcion, CasoId, Justificacion) = (tipo, descripcion, caso, justificacion);
        return resultado;
    }
}
