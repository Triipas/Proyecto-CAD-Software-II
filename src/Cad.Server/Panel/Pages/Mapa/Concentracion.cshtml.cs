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
// HU46 (S6-T20): la misma página tiene la vista «Proyección» (Vista=proyeccion), con la ventana como parámetro GET.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class ConcentracionModel(ConsultarConcentracion consultar, IConfiguration configuracion) : PageModel
{
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Vista { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Ventana { get; set; }

    public bool EsProyeccion => Vista == "proyeccion";
    public int MinimoCasos => int.TryParse(configuracion["Proyeccion:MinimoCasos"], out var n) ? n : 50;
    public Modulos.Proyeccion.ResultadoProyeccion? Proyeccion { get; private set; }
    public Modulos.Proyeccion.ParteVista? Parte { get; private set; }

    public static readonly (string Codigo, string Texto, Modulos.Proyeccion.VentanaProyeccion Ventana)[] Ventanas =
    [
        ("6h", "Próximas 6 h", Modulos.Proyeccion.VentanaProyeccion.ProximasSeisHoras),
        ("noche", "Esta noche", Modulos.Proyeccion.VentanaProyeccion.EstaNoche),
        ("manana", "Mañana", Modulos.Proyeccion.VentanaProyeccion.Manana),
    ];

    // Celdas de la proyección para el mapa: límites, nivel, casos esperados y cobertura (nunca datos de un caso, CA03). Con lo esperado en
    // cada bloque de 4 h de la ventana, su nivel y por qué, el mapa cambia de bloque sin volver al servidor.
    public string ProyeccionJson => JsonSerializer.Serialize((Proyeccion?.Zonas ?? []).Select(z => new
    {
        clave = z.Clave, zona = z.Zona, sur = z.Sur, oeste = z.Oeste, norte = z.Norte, este = z.Este,
        nivel = z.Nivel.ToString(), esperados = z.Esperados, sinCobertura = z.SinCobertura,
        porQue = z.Explicacion, metros = z.MetrosSerenoCercano, sereno = z.SerenoCercano,
        bloques = z.PorBloque, niveles = z.NivelesPorBloque?.Select(n => n.ToString()), porQueBloques = z.ExplicacionesPorBloque,
    }));

    // Los bloques de 4 h de la ventana, los umbrales de nivel y los serenos operativos con ubicación, para el mapa de la proyección.
    public string ContextoProyeccionJson => JsonSerializer.Serialize(new
    {
        umbralAlto = Proyeccion?.UmbralAlto, umbralMedio = Proyeccion?.UmbralMedio, umbralCobertura = Proyeccion?.UmbralCoberturaMetros,
        bloques = (Proyeccion?.Bloques ?? []).Select(b => new { rotulo = b.Rotulo, dia = b.Dia, fraccion = b.Fraccion, total = b.Total, ahora = b.EsAhora }),
        serenos = (Proyeccion?.Serenos ?? []).Select(s => new { nombre = s.Nombre, lat = s.Latitud, lon = s.Longitud, disponible = s.Disponible }),
    });

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
        sur = c.Sur, oeste = c.Oeste, norte = c.Norte, este = c.Este, casos = c.Casos, zona = Zonas.Nombre(c.Sur, c.Oeste),
    }));

    // Puntos del mapa de calor: el centro de una celda fina y sus casos (sin datos de ningún caso concreto).
    public string CalorJson => JsonSerializer.Serialize((Mapa?.Calor ?? []).Select(p => new { lat = p.Latitud, lon = p.Longitud, casos = p.Casos }));

    // Las zonas con más casos, con el mismo nombre que usa Analítica («Zona K10»): el ranking del panel y su equivalente para el lector de
    // pantalla. Cada una lleva su centro para que el mapa vuele hasta ella.
    public sealed record ZonaDestacada(string Zona, string Origen, int Casos, double Latitud, double Longitud);

    public IReadOnlyList<ZonaDestacada> ZonasDestacadas =>
        (Mapa?.Celdas ?? []).OrderByDescending(c => c.Casos).Take(10)
            .Select(c => new ZonaDestacada(Zonas.Nombre(c.Sur, c.Oeste), c.Origen == OrigenCaso.Sos ? "SOS" : "Reportes", c.Casos,
                (c.Sur + c.Norte) / 2, (c.Oeste + c.Este) / 2)).ToList();

    // Atajos de periodo: los últimos 7, 30 o 90 días, con el origen elegido. El activo es el que coincide con la selección.
    public sealed record PeriodoRapido(string Texto, string Enlace, bool Activo);

    public IReadOnlyList<PeriodoRapido> PeriodosRapidos
    {
        get
        {
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ZonaHoraria).DateTime);
            return new[] { 7, 30, 90 }.Select(dias =>
            {
                var desde = hoy.AddDays(-(dias - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var hasta = hoy.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var origen = Origen is "Sos" or "Reporte" ? $"&Origen={Origen}" : "";
                return new PeriodoRapido($"{dias} días", $"/panel/mapa-concentracion?Desde={desde}&Hasta={hasta}{origen}", Desde == desde && Hasta == hasta);
            }).ToList();
        }
    }

    public Analitica.Zonas Zonas => new(configuracion);

    // La misma selección en Analítica: lo que se repite en esas zonas y cómo se responde ahí.
    public string EnlaceAnalitica => $"/panel/analitica/repite?periodo=rango&desde={Desde}&hasta={Hasta}";

    public string TextoOrigen => Origen switch { "Sos" => "SOS", "Reporte" => "reportes", _ => "SOS y reportes" };

    public async Task OnGetAsync([Microsoft.AspNetCore.Mvc.FromServices] Modulos.Proyeccion.ConsultarProyeccion consultarProyeccion,
        [Microsoft.AspNetCore.Mvc.FromServices] Modulos.Proyeccion.RedactarParte redactar, CancellationToken ct)
    {
        if (EsProyeccion)
        {
            var ventana = Ventanas.FirstOrDefault(v => v.Codigo == Ventana);
            if (ventana.Codigo is null)
                (Ventana, ventana) = ("6h", Ventanas[0]);
            Proyeccion = await consultarProyeccion.EjecutarAsync(ventana.Ventana, ct);
            if (Proyeccion.Suficiente)
                Parte = await redactar.EjecutarAsync(Proyeccion, int.TryParse(configuracion["Proyeccion:ZonasPrincipales"], out var n) ? n : 10, ct);
            return;
        }

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
