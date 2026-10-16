using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// HU14 — Iniciar la revisión de un reporte y corregir su categoría (S3-T05). Solo operadores (también en el servidor:
// la política de la página rechaza cualquier otra sesión, incluida una solicitud directa).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class DetalleModel(ConsultarDetalleReporte consultar, RevisarReporte revisar, ConsultarCategorias consultarCategorias,
    IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public Guid? CategoriaId { get; set; }

    public DetalleReporteVista Reporte { get; private set; } = null!;
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];
    public string? ErrorRevision { get; private set; }
    public string? ErrorCorreccion { get; private set; }
    public string? ErrorCategoria { get; private set; }

    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct) =>
        await CargarAsync(id, ct) ? Page() : NotFound();

    // CA01, CA02 y CA05.
    public async Task<IActionResult> OnPostIniciarRevisionAsync(Guid id, CancellationToken ct)
    {
        switch (await revisar.IniciarAsync(id, User.IdUsuario(), ct))
        {
            case ResultadoRevision.Registrado r:
                return Hecho(id, "Revisión iniciada.", r.Detalle);
            case ResultadoRevision.YaRegistrado r:
                return Hecho(id, "Revisión iniciada.", r.Detalle);
            case ResultadoRevision.Rechazado r when await CargarAsync(id, ct):
                ErrorRevision = r.Motivo;
                return Page();
            default:
                return NotFound();
        }
    }

    // CA03 y CA04.
    public async Task<IActionResult> OnPostCorregirAsync(Guid id, CancellationToken ct)
    {
        var resultado = await revisar.CorregirCategoriaAsync(id, CategoriaId, User.IdUsuario(), ct);
        switch (resultado)
        {
            case ResultadoRevision.Registrado r:
                return Hecho(id, "Categoría corregida.", r.Detalle);
            case ResultadoRevision.YaRegistrado r:
                return Hecho(id, "Sin cambios en la categoría.", r.Detalle);
            case ResultadoRevision.NoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorCategoria = (resultado as ResultadoRevision.Invalido)?.Motivo;
                ErrorCorreccion = (resultado as ResultadoRevision.Rechazado)?.Motivo;
                return Page();
        }
    }

    private RedirectToPageResult Hecho(Guid id, string mensaje, string detalle)
    {
        Mensaje = mensaje;
        DetalleMensaje = detalle;
        return RedirectToPage(new { id }); // PRG: recargar no vuelve a enviar el formulario
    }

    private async Task<bool> CargarAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } reporte)
            return false;
        Reporte = reporte;
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        CategoriaId ??= reporte.CategoriaActualId;
        return true;
    }
}
