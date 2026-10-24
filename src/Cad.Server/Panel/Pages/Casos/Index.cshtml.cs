using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Casos;

// HU26 — Buscar y filtrar casos (S3-T03, S3-T04). Solo operadores (CA06; también ante una solicitud directa).
// Los filtros van en la dirección (GET): se pueden recargar y compartir, y «Limpiar filtros» es volver a /panel/casos.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarCasos consultar, ConsultarCategorias consultarCategorias, IConfiguration configuracion) : PageModel
{
    public const string ValorSinTipo = "sin-tipo";

    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public string? Origen { get; set; }
    [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Estado { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sereno { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public PaginaCasos? Resultado { get; private set; }
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];
    public IReadOnlyList<SerenoParaFiltro> Serenos { get; private set; } = [];
    public Dictionary<string, string> Errores { get; } = [];

    public bool HayFiltros => new[] { Desde, Hasta, Origen, Tipo, Estado, Sereno }.Any(v => !string.IsNullOrEmpty(v));

    public static readonly IReadOnlyList<EstadoCaso> EstadosFiltrables =
    [
        EstadoCaso.Recibido, EstadoCaso.EnRevision, EstadoCaso.Pendiente, EstadoCaso.Asignada, EstadoCaso.EnCamino,
        EstadoCaso.EnAtencion, EstadoCaso.Cerrada, EstadoCaso.Derivada, EstadoCaso.Descartada, EstadoCaso.Cancelada,
    ];

    // «Pendiente» filtra los SOS sin sereno y los reportes admitidos que esperan sereno (en el resultado, «Admitido»).
    public static string TextoEstadoFiltro(EstadoCaso estado) =>
        estado == EstadoCaso.Pendiente ? $"{EstadosCaso.Texto(estado)} (SOS) / {EstadosCaso.ReporteAdmitido} (reporte)" : EstadosCaso.Texto(estado);

    public TimeZoneInfo ZonaHoraria =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public async Task OnGetAsync(CancellationToken ct)
    {
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        Serenos = await consultar.SerenosAsync(ct);

        var filtro = Interpretar();
        if (Errores.Count == 0)
            Resultado = await consultar.EjecutarAsync(filtro, Pagina, ct);
    }

    // Dirección de otra página con los mismos filtros.
    public string RutaPagina(int pagina)
    {
        var parametros = new Dictionary<string, string?>
        {
            ["desde"] = Desde, ["hasta"] = Hasta, ["origen"] = Origen, ["tipo"] = Tipo, ["estado"] = Estado, ["sereno"] = Sereno,
            ["pagina"] = pagina.ToString(CultureInfo.InvariantCulture),
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/panel/casos",
            parametros.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value));
    }

    // Rediseño CAD v2 (fase 3): los filtros aplicados se muestran como chips; cada uno se quita con un enlace
    // que conserva los demás y vuelve a la primera página.
    public sealed record FiltroActivo(string Etiqueta, string Valor, string RutaSinEste);

    public IReadOnlyList<FiltroActivo> FiltrosActivos
    {
        get
        {
            var valores = new (string Clave, string Etiqueta, string? Valor, Func<string, string?> Texto)[]
            {
                ("desde", "Desde", Desde, FechaLegible),
                ("hasta", "Hasta", Hasta, FechaLegible),
                ("origen", "Origen", Origen, v => v switch { "Sos" => "SOS", "Reporte" => "Reporte", "Acompanamiento" => "Acompañamiento", _ => null }),
                ("tipo", "Tipo", Tipo, v => v == ValorSinTipo ? CasoEncontrado.TipoPorDeterminar
                    : Categorias.FirstOrDefault(c => c.Id.ToString() == v)?.Nombre),
                ("estado", "Estado", Estado, v => Enum.TryParse<EstadoCaso>(v, out var e) ? TextoEstadoFiltro(e) : null),
                ("sereno", "Sereno", Sereno, v => Serenos.FirstOrDefault(s => s.Id.ToString() == v)?.Nombre),
            };
            return valores
                .Where(f => !string.IsNullOrEmpty(f.Valor) && f.Texto(f.Valor!) is not null)
                .Select(f => new FiltroActivo(f.Etiqueta, f.Texto(f.Valor!)!, RutaSin(f.Clave)))
                .ToList();
        }
    }

    private static string? FechaLegible(string valor) =>
        DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            ? f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : null;

    private string RutaSin(string clave)
    {
        var parametros = new Dictionary<string, string?>
        {
            ["desde"] = Desde, ["hasta"] = Hasta, ["origen"] = Origen, ["tipo"] = Tipo, ["estado"] = Estado, ["sereno"] = Sereno,
        };
        parametros.Remove(clave);
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/panel/casos",
            parametros.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value));
    }

    private FiltroCasos Interpretar()
    {
        // DP17: días completos en la hora local del panel (desde las 00:00 del primer día hasta el final del último).
        var desde = Fecha(Desde, "desde");
        var hasta = Fecha(Hasta, "hasta");
        if (desde is { } d && hasta is { } h && d > h)
            Errores["hasta"] = "La fecha final es anterior a la inicial.";

        OrigenCaso? origen = Origen switch
        {
            null or "" => null,
            "Sos" => OrigenCaso.Sos,
            "Reporte" => OrigenCaso.Reporte,
            "Acompanamiento" => OrigenCaso.Acompanamiento,
            _ => Invalido<OrigenCaso>("origen", "Elige un origen de la lista."),
        };

        Guid? categoria = null;
        var sinTipo = Tipo == ValorSinTipo;
        if (!string.IsNullOrEmpty(Tipo) && !sinTipo)
        {
            if (Guid.TryParse(Tipo, out var id) && Categorias.Any(c => c.Id == id))
                categoria = id;
            else
                Errores["tipo"] = "Elige un tipo del catálogo.";
        }

        EstadoCaso? estado = null;
        if (!string.IsNullOrEmpty(Estado))
        {
            if (Enum.TryParse<EstadoCaso>(Estado, out var e) && EstadosFiltrables.Contains(e))
                estado = e;
            else
                Errores["estado"] = "Elige un estado de la lista.";
        }

        Guid? sereno = null;
        if (!string.IsNullOrEmpty(Sereno))
        {
            if (Guid.TryParse(Sereno, out var s) && Serenos.Any(x => x.Id == s))
                sereno = s;
            else
                Errores["sereno"] = "Elige un sereno de la lista.";
        }

        return new FiltroCasos(
            desde is { } inicio ? Inicio(inicio) : null,
            hasta is { } fin ? Inicio(fin.AddDays(1)) : null,
            origen, categoria, sinTipo, estado, sereno);
    }

    private DateOnly? Fecha(string? texto, string campo)
    {
        if (string.IsNullOrEmpty(texto))
            return null;
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

    private T? Invalido<T>(string campo, string mensaje) where T : struct
    {
        Errores[campo] = mensaje;
        return null;
    }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensaje) ? mensaje : null;

    public static string Detalle(CasoEncontrado caso) =>
        caso.Origen != OrigenCaso.Reporte ? $"/panel/central/sos/{caso.Id}" : $"/panel/reportes/{caso.Id}";
}
