using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Central;

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01). Solo operadores (CA08).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class SosModel(ConsultarDetalleSos consultar, RegistrarIntentoContacto registrar, IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioIntento Formulario { get; set; } = new();

    public DetalleSos Sos { get; private set; } = null!;

    public Dictionary<string, string[]> Errores { get; private set; } = [];

    [TempData]
    public string? Mensaje { get; set; }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    // Etiquetas visibles de los tres resultados válidos (CA02).
    public static readonly IReadOnlyList<(ResultadoContacto Valor, string Texto)> Resultados =
    [
        (ResultadoContacto.Contactado, "Contactado"),
        (ResultadoContacto.SinRespuesta, "Sin respuesta"),
        (ResultadoContacto.NoFuePosibleComunicar, "No fue posible comunicar"),
    ];

    public static string Texto(ResultadoContacto resultado) => Resultados.Single(r => r.Valor == resultado).Texto;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.EjecutarAsync(id, ct) is not { } sos)
            return NotFound();

        Sos = sos;
        // Identificador del envío: un reenvío del mismo formulario no duplica el intento.
        Formulario = new FormularioIntento { IdSolicitud = Guid.NewGuid() };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(id, User.IdUsuario(), new DatosIntentoContacto(
            Formulario.IdSolicitud, Formulario.Resultado, Formulario.InformacionHecho, Formulario.ReferenciaUbicacion), ct);

        switch (resultado)
        {
            case ResultadoIntentoContacto.Registrado or ResultadoIntentoContacto.YaRegistrado:
                Mensaje = "Intento de contacto registrado.";
                return RedirectToPage(new { id }); // PRG: recargar no vuelve a enviar el formulario
            case ResultadoIntentoContacto.Invalido invalido when await consultar.EjecutarAsync(id, ct) is { } sos:
                Sos = sos;
                Errores = invalido.Errores;
                return Page();
            default:
                return NotFound();
        }
    }

    public sealed class FormularioIntento
    {
        public Guid IdSolicitud { get; set; }
        public string? Resultado { get; set; }
        public string? InformacionHecho { get; set; }
        public string? ReferenciaUbicacion { get; set; }
    }
}
