using System.Globalization;
using System.Text.Json;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Mapa;

// HU29 — Consultar el mapa de concentración de incidencias (S5-T13). Solo operadores, también ante una solicitud directa.
// La selección va en la dirección (GET), como en la búsqueda de «Casos».
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class ConcentracionModel(ConsultarConcentracion consultar, IConfiguration configuracion) : PageModel
{
    // Selección inicial de la página: los últimos 30 días y los dos orígenes. Es un valor de presentación; el operador la cambia.
    public const int DiasIniciales = 30;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Origen { get; set; }

    public MapaConcentracion? Mapa { get; private set; }
    public Dictionary<string, string> Errores { get; } = [];
    public string? ErrorDe(string campo) => Errores.GetValueOrDefault(campo);

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -16.3988);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -71.5369);
    public string Zoom => Numero("Panel:Mapa:Zoom", 12);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    // Celdas para el mapa (sin datos de ningún caso concreto).
    public string CeldasJson => JsonSerializer.Serialize((Mapa?.Celdas ?? []).Select(c => new
    {
        origen = c.Origen == OrigenCaso.Sos ? "SOS" : "Reporte",
        sur = c.Sur, oeste = c.Oeste, norte = c.Norte, este = c.Este, casos = c.Casos,
    }));

    public string TextoOrigen => Origen switch { "Sos" => "SOS", "Reporte" => "reportes", _ => "SOS y reportes" };

    public async Task OnGetAsync(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ZonaHoraria).DateTime);
        Desde ??= hoy.AddDays(-(DiasIniciales - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Hasta ??= hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // CA01: periodo (días completos en la hora local del panel, DP17) y origen.
        var desde = Fecha(Desde, "desde");
        var hasta = Fecha(Hasta, "hasta");
        if (desde is { } d && hasta is { } h && d > h)
            Errores["hasta"] = "La fecha final es anterior a la inicial.";
        OrigenCaso? origen = null;
        if (Origen is "Sos" or "Reporte")
            origen = Enum.Parse<OrigenCaso>(Origen);
        else if (!string.IsNullOrEmpty(Origen))
            Errores["origen"] = "Elige un origen de la lista.";
        if (Errores.Count > 0 || desde is null || hasta is null)
            return;

        Mapa = await consultar.EjecutarAsync(new FiltroConcentracion(Inicio(desde.Value), Inicio(hasta.Value.AddDays(1)), origen), ct);
    }

    private DateOnly? Fecha(string? texto, string campo)
    {
        if (DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return fecha;
        Errores[campo] = "Escribe una fecha válida.";
        return null;
    }

    private DateTimeOffset Inicio(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ZonaHoraria.GetUtcOffset(local)).ToUniversalTime();
    }

    private string Numero(string clave, double defecto) =>
        (double.TryParse(configuracion[clave], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defecto)
            .ToString(CultureInfo.InvariantCulture);
}
