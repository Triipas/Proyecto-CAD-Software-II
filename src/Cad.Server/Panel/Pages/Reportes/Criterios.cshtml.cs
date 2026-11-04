using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// S6-T01 — los criterios de triaje, tal como los recibe el asistente (CriteriosTriaje). Solo operadores.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public partial class CriteriosModel : PageModel
{
    public string Version => CriteriosTriaje.Version;

    // El documento es Markdown sencillo (títulos, párrafos y listas); se codifica todo el texto antes de dar formato.
    public IHtmlContent Contenido { get; } = new HtmlString(AHtml(CriteriosTriaje.Texto));

    public void OnGet() { }

    private static string AHtml(string markdown)
    {
        var html = new StringBuilder();
        string? lista = null;
        void CerrarLista()
        {
            if (lista is null) return;
            html.Append($"</{lista}>");
            lista = null;
        }
        foreach (var crudo in markdown.Split('\n'))
        {
            var linea = crudo.TrimEnd();
            if (linea.Length == 0) { CerrarLista(); continue; }
            if (linea.StartsWith("# ")) { CerrarLista(); continue; } // el título lo pone la página
            if (linea.StartsWith("## ")) { CerrarLista(); html.Append($"<h2 class=\"criterios__titulo\">{Formato(linea[3..])}</h2>"); continue; }
            var numerada = Numerada().Match(linea);
            var tipo = linea.StartsWith("- ") ? "ul" : numerada.Success ? "ol" : null;
            if (tipo is null) { CerrarLista(); html.Append($"<p>{Formato(linea)}</p>"); continue; }
            if (lista != tipo) { CerrarLista(); html.Append($"<{tipo}>"); lista = tipo; }
            html.Append($"<li>{Formato(tipo == "ul" ? linea[2..] : linea[numerada.Length..])}</li>");
        }
        CerrarLista();
        return html.ToString();
    }

    private static string Formato(string texto) => Negrita().Replace(WebUtility.HtmlEncode(texto), "<strong>$1</strong>");

    [GeneratedRegex(@"^\d+\.\s")]
    private static partial Regex Numerada();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Negrita();
}
