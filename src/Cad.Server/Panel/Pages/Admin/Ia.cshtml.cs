using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Dominio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Cad.Server.Panel.Pages.Admin;

// HU45 (S6-T15; plan de asistencia con IA, 7.1 y 7.2) — el administrador cambia el modo de la admisión automática y sus
// parámetros, con un motivo que queda en el historial. Solo administradores (también en el servidor).
[Authorize(Policy = Politicas.Administrador)]
public class IaModel(ConsultarAsistencia consultar, CambiarAjusteIa cambiar, IOptions<OpcionesIa> opciones, IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioAjuste Formulario { get; set; } = new();

    public AjusteIaVista Ajuste { get; private set; } = null!;
    public IReadOnlyList<CambioAjusteVista> Historial { get; private set; } = [];
    public Dictionary<string, string[]> Errores { get; private set; } = [];

    [TempData]
    public string? Mensaje { get; set; }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    // De dónde salen las sugerencias (Ia:*, en la configuración del servidor). La clave nunca se muestra.
    public string Asistente
    {
        get
        {
            var o = opciones.Value;
            if (!o.Habilitada)
                return "Apagado (Ia:Habilitada = false): no hay sugerencias.";
            if (string.Equals(o.Proveedor, ProveedoresIa.Reglas, StringComparison.OrdinalIgnoreCase))
                return "Reglas por palabras clave, sin modelo de lenguaje (para demostrar el flujo).";
            return string.IsNullOrWhiteSpace(o.Gemini.ClaveApi)
                ? "Gemini sin clave de API configurada: no hay sugerencias."
                : $"Gemini ({o.Gemini.ModeloRapido}; escala a {o.Gemini.ModeloPreciso}).";
        }
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarAsync(ct);
        Formulario = FormularioAjuste.De(Ajuste);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var f = Formulario;
        var parametros = new ParametrosAdmision(f.RetrasoSegundos, f.MaximoPorHora, f.LongitudMinimaDescripcion, f.PermitirCorregirCategoria,
            f.FrenoMinimoAdmisiones, f.FrenoPorcentajeDescartes);
        switch (await cambiar.AdministrarAsync(f.Modo, parametros, f.Motivo, User.IdUsuario(), ct))
        {
            case ResultadoAjusteIa.Cambiado c:
                Mensaje = $"Admisión automática: «{ConsultarAsistencia.Texto(c.Modo)}». El cambio quedó en el historial.";
                return RedirectToPage();
            case ResultadoAjusteIa.SinCambios:
                Mensaje = "No había nada que cambiar: el modo y los parámetros ya eran esos.";
                return RedirectToPage();
            case ResultadoAjusteIa.Invalido i:
                Errores = i.Errores;
                await CargarAsync(ct);
                return Page();
            default:
                return BadRequest();
        }
    }

    private async Task CargarAsync(CancellationToken ct)
    {
        Ajuste = await consultar.AjusteAsync(ct);
        Historial = await consultar.HistorialAsync(20, ct);
    }

    public sealed class FormularioAjuste
    {
        public ModoAdmision Modo { get; set; }
        public int RetrasoSegundos { get; set; }
        public int MaximoPorHora { get; set; }
        public int LongitudMinimaDescripcion { get; set; }
        public bool PermitirCorregirCategoria { get; set; }
        public int FrenoMinimoAdmisiones { get; set; }
        public int FrenoPorcentajeDescartes { get; set; }
        public string? Motivo { get; set; }

        public static FormularioAjuste De(AjusteIaVista a) => new()
        {
            Modo = a.Modo,
            RetrasoSegundos = a.Parametros.RetrasoSegundos,
            MaximoPorHora = a.Parametros.MaximoPorHora,
            LongitudMinimaDescripcion = a.Parametros.LongitudMinimaDescripcion,
            PermitirCorregirCategoria = a.Parametros.PermitirCorregirCategoria,
            FrenoMinimoAdmisiones = a.Parametros.FrenoMinimoAdmisiones,
            FrenoPorcentajeDescartes = a.Parametros.FrenoPorcentajeDescartes,
        };
    }
}
