using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Acompanamientos;

// HU38 — Consultar los acompañamientos activos y sus posiciones (S4-T09, S4-T10). Solo el operador autorizado (DP04), también
// ante una solicitud directa (CA05). Página propia, separada de los SOS y los reportes (CA04).
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarAcompanamientos consultar, ConsultarAvisos consultarAvisos, IConfiguration configuracion) : PageModel
{
    public ListaAcompanamientos Lista { get; private set; } = null!;

    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -12.0464);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -77.0428);
    public string Zoom => Numero("Panel:Mapa:Zoom", 12);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task OnGetAsync(CancellationToken ct) => Lista = await ListaAsync(ct);

    // Fragmento que la página vuelve a pedir cada pocos segundos: la antigüedad y el estado cambian aunque no llegue nada.
    public async Task<IActionResult> OnGetListaAsync(CancellationToken ct) => Partial("_ListaAcompanamientos", await ListaAsync(ct));

    private async Task<ListaAcompanamientos> ListaAsync(CancellationToken ct) =>
        new(await consultar.ActivasAsync(ct), Formato.Zona(configuracion), consultar.Umbral, await consultarAvisos.AbiertosAsync(ct));

    private string Numero(string clave, double porDefecto) => configuracion.GetValue(clave, porDefecto).ToString(CultureInfo.InvariantCulture);
}

// HU42/HU43: los avisos sin resolver se muestran con la lista, separados de los SOS (HU42-CA04).
public sealed record ListaAcompanamientos(IReadOnlyList<AcompanamientoActivo> Sesiones, TimeZoneInfo ZonaHoraria, TimeSpan Umbral,
    IReadOnlyList<AvisoVista>? Avisos = null);

public static class Formato
{
    public static TimeZoneInfo Zona(IConfiguration configuracion) =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public static string Hora(DateTimeOffset momento, TimeZoneInfo zona, string formato = "HH:mm:ss") =>
        TimeZoneInfo.ConvertTime(momento, zona).ToString(formato, CultureInfo.InvariantCulture);

    // HU38-CA02: «hace 12 s», «hace 3 min 5 s».
    public static string Antiguedad(TimeSpan antiguedad) => antiguedad.TotalSeconds < 60
        ? $"hace {(int)antiguedad.TotalSeconds} s"
        : $"hace {(int)antiguedad.TotalMinutes} min {antiguedad.Seconds} s";

    public static string Numero(double valor, string formato = "0.000000") => valor.ToString(formato, CultureInfo.InvariantCulture);
}
