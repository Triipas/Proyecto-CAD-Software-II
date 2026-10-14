using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Central;

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01) y
// HU20 — Asignar el caso pendiente a un sereno disponible (S2-T05) y
// HU13 — Clasificar el SOS después de su registro (S2-T08). Solo operadores (HU12-CA08, HU20-CA09, DP09).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class SosModel(ConsultarDetalleSos consultar, RegistrarIntentoContacto registrar, AsignarCaso asignar,
    ClasificarCaso clasificar, ConsultarSerenos consultarSerenos, ConsultarCategorias consultarCategorias,
    IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioIntento Formulario { get; set; } = new();

    [BindProperty]
    public FormularioAsignacion Asignacion { get; set; } = new();

    [BindProperty]
    public FormularioClasificacion Clasificacion { get; set; } = new();

    public DetalleSos Sos { get; private set; } = null!;

    // HU20: serenos que pueden recibir el caso (operativos y sin asignación vigente).
    public IReadOnlyList<SerenoMonitoreo> SerenosDisponibles { get; private set; } = [];

    // HU13-CA04: solo las categorías del catálogo vigente (DP07).
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];

    public Dictionary<string, string[]> Errores { get; private set; } = [];
    public string? ErrorAsignacion { get; private set; }
    public string? ErrorCategoria { get; private set; }
    public string? ErrorClasificacion { get; private set; }

    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    // Etiquetas visibles de los tres resultados válidos (HU12-CA02).
    public static readonly IReadOnlyList<(ResultadoContacto Valor, string Texto)> Resultados =
    [
        (ResultadoContacto.Contactado, "Contactado"),
        (ResultadoContacto.SinRespuesta, "Sin respuesta"),
        (ResultadoContacto.NoFuePosibleComunicar, "No fue posible comunicar"),
    ];

    public static string Texto(ResultadoContacto resultado) => Resultados.Single(r => r.Valor == resultado).Texto;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!await CargarAsync(id, ct))
            return NotFound();
        // Identificadores de envío: un reenvío del mismo formulario no duplica el intento ni la asignación.
        Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
        Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
        return Page();
    }

    // HU12: registrar un intento de contacto.
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(id, User.IdUsuario(), new DatosIntentoContacto(
            Formulario.IdSolicitud, Formulario.Resultado, Formulario.InformacionHecho, Formulario.ReferenciaUbicacion), ct);

        switch (resultado)
        {
            case ResultadoIntentoContacto.Registrado or ResultadoIntentoContacto.YaRegistrado:
                Mensaje = "Intento de contacto registrado.";
                DetalleMensaje = "Puedes registrar más intentos.";
                return RedirectToPage(new { id }); // PRG: recargar no vuelve a enviar el formulario
            case ResultadoIntentoContacto.Invalido invalido when await CargarAsync(id, ct):
                Errores = invalido.Errores;
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
            default:
                return NotFound();
        }
    }

    // HU20: asignar el caso a un sereno disponible. Desde la central (Volver = "central") vuelve a ella.
    public async Task<IActionResult> OnPostAsignarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await asignar.EjecutarAsync(id, Asignacion.SerenoId, User.IdUsuario(), Asignacion.IdSolicitud, ct);
        var volverACentral = Asignacion.Volver == "central";

        switch (resultado)
        {
            case ResultadoAsignacion.Asignado or ResultadoAsignacion.YaAsignado:
                Mensaje = "SOS asignado.";
                DetalleMensaje = "El sereno ya no figura como disponible.";
                return volverACentral ? RedirectToPage("/Central/Index") : RedirectToPage(new { id });
            case ResultadoAsignacion.CasoNoEncontrado:
                return NotFound();
            case ResultadoAsignacion.Rechazado rechazo when volverACentral:
                TempData[nameof(IndexModel.ErrorAccion)] = rechazo.Motivo;
                return RedirectToPage("/Central/Index");
            case ResultadoAsignacion.Invalido invalido when volverACentral:
                TempData[nameof(IndexModel.ErrorAccion)] = invalido.Motivo;
                return RedirectToPage("/Central/Index");
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorAsignacion = resultado switch
                {
                    ResultadoAsignacion.Rechazado r => r.Motivo,
                    ResultadoAsignacion.Invalido i => i.Motivo,
                    _ => "No se pudo asignar el caso.",
                };
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    // HU13: clasificar el SOS con una categoría del catálogo.
    public async Task<IActionResult> OnPostClasificarAsync(Guid id, CancellationToken ct)
    {
        var resultado = await clasificar.EjecutarAsync(id, Clasificacion.CategoriaId, User.IdUsuario(), ct);

        switch (resultado)
        {
            case ResultadoClasificacion.Clasificado c:
                return Clasificado(id, c.Categoria);
            case ResultadoClasificacion.YaClasificado c:
                return Clasificado(id, c.Categoria);
            case ResultadoClasificacion.CasoNoEncontrado:
                return NotFound();
            default:
                if (!await CargarAsync(id, ct))
                    return NotFound();
                ErrorCategoria = (resultado as ResultadoClasificacion.Invalido)?.Motivo;
                ErrorClasificacion = (resultado as ResultadoClasificacion.Rechazado)?.Motivo;
                Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
                Asignacion = new FormularioAsignacion { IdSolicitud = Guid.NewGuid() };
                return Page();
        }
    }

    private RedirectToPageResult Clasificado(Guid id, string categoria)
    {
        Mensaje = "SOS clasificado.";
        DetalleMensaje = $"Tipo: {categoria}.";
        return RedirectToPage(new { id }); // PRG
    }

    private async Task<bool> CargarAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } sos)
            return false;
        Sos = sos;
        SerenosDisponibles = (await consultarSerenos.EjecutarAsync(ct)).Where(s => s.Disponible).ToList();
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        return true;
    }

    public sealed class FormularioIntento
    {
        public Guid IdSolicitud { get; set; }
        public string? Resultado { get; set; }
        public string? InformacionHecho { get; set; }
        public string? ReferenciaUbicacion { get; set; }
    }

    public sealed class FormularioAsignacion
    {
        public Guid IdSolicitud { get; set; }
        public Guid SerenoId { get; set; }
        public string? Volver { get; set; }
    }

    public sealed class FormularioClasificacion
    {
        public Guid? CategoriaId { get; set; }
    }
}
