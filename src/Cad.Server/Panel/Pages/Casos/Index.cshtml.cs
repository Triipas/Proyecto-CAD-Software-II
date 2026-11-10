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
    // Rediseño del 05/11/2026: la vista («En curso», «Finalizados», «Sin efecto») agrupa varios estados.
    [BindProperty(SupportsGet = true)] public string? Grupo { get; set; }
    // Una zona del mapa de concentración, como «sur,oeste» (la esquina de la celda de unos 550 m).
    [BindProperty(SupportsGet = true)] public string? Zona { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public PaginaCasos? Resultado { get; private set; }
    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];
    public IReadOnlyList<SerenoParaFiltro> Serenos { get; private set; } = [];
    public Dictionary<string, string> Errores { get; } = [];

    public bool HayFiltros => new[] { Desde, Hasta, Origen, Tipo, Estado, Sereno, Grupo, Zona }.Any(v => !string.IsNullOrEmpty(v));

    // Las vistas de la lista: cada una agrupa estados. «En curso» es lo que la central sigue atendiendo; «Finalizados», lo que terminó con
    // una atención o una derivación; «Sin efecto», lo que se descartó o se canceló.
    public sealed record GrupoEstados(string Codigo, string Texto, IReadOnlyList<EstadoCaso> Estados);

    public static readonly IReadOnlyList<GrupoEstados> Grupos =
    [
        new("en-curso", "En curso", [EstadoCaso.Recibido, EstadoCaso.EnRevision, EstadoCaso.Pendiente, EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion]),
        new("finalizados", "Finalizados", [EstadoCaso.Cerrada, EstadoCaso.Derivada]),
        new("sin-efecto", "Sin efecto", [EstadoCaso.Descartada, EstadoCaso.Cancelada]),
    ];

    // Casos por origen y estado con el filtro puesto, sin la vista ni el estado: de ahí salen las cifras de las vistas y el resumen.
    public IReadOnlyDictionary<(OrigenCaso Origen, EstadoCaso Estado), int>? Resumen { get; private set; }
    public VistaPreviaCaso? Vista { get; private set; }
    private FiltroCasos? filtroActual;

    public sealed record VistaPreviaCaso(CasoEncontrado Caso, HistorialCaso Historial);
    public sealed record PestanaVista(string Codigo, string Texto, int? Casos, string Enlace, bool Activa);
    public sealed record ResumenSeleccion(int Total, int Sos, int Reportes, int Acompanamientos, int EnCurso);
    public sealed record OpcionRapida(string Texto, string Enlace, bool Activa);

    private int Cuenta(Func<(OrigenCaso Origen, EstadoCaso Estado), bool> condicion) =>
        Resumen?.Where(f => condicion(f.Key)).Sum(f => f.Value) ?? 0;

    public IReadOnlyList<PestanaVista> Pestanas =>
    [
        new(string.Empty, "Todos", Resumen is null ? null : Cuenta(_ => true), RutaCon(("estado", null), ("grupo", null), ("pagina", null)), string.IsNullOrEmpty(Grupo)),
        .. Grupos.Select(g => new PestanaVista(g.Codigo, g.Texto, Resumen is null ? null : Cuenta(f => g.Estados.Contains(f.Estado)),
            RutaCon(("estado", null), ("grupo", g.Codigo), ("pagina", null)), Grupo == g.Codigo)),
    ];

    // Lo que se ve con la vista y el estado elegidos: el total y su desglose por origen.
    public ResumenSeleccion? Seleccion
    {
        get
        {
            if (Resumen is null || filtroActual is null)
                return null;
            bool Entra((OrigenCaso Origen, EstadoCaso Estado) f) =>
                (filtroActual.Estados is null || filtroActual.Estados.Contains(f.Estado)) && (filtroActual.Estado is null || filtroActual.Estado == f.Estado);
            return new(Cuenta(Entra), Cuenta(f => Entra(f) && f.Origen == OrigenCaso.Sos), Cuenta(f => Entra(f) && f.Origen == OrigenCaso.Reporte),
                Cuenta(f => Entra(f) && f.Origen == OrigenCaso.Acompanamiento), Cuenta(f => Entra(f) && Grupos[0].Estados.Contains(f.Estado)));
        }
    }

    // El origen como selector de cuatro opciones: cada una es un enlace que conserva los demás filtros.
    public IReadOnlyList<OpcionRapida> OpcionesOrigen =>
    [
        new("Todos", RutaCon(("origen", null), ("pagina", null)), string.IsNullOrEmpty(Origen)),
        new("SOS", RutaCon(("origen", "Sos"), ("pagina", null)), Origen == "Sos"),
        new("Reportes", RutaCon(("origen", "Reporte"), ("pagina", null)), Origen == "Reporte"),
        new("Acomp.", RutaCon(("origen", "Acompanamiento"), ("pagina", null)), Origen == "Acompanamiento"),
    ];

    // Atajos de periodo: hoy, los últimos 7 y los últimos 30 días (días completos en la hora local del panel, DP17).
    public IReadOnlyList<OpcionRapida> PeriodosRapidos
    {
        get
        {
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ZonaHoraria).DateTime);
            string Dia(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new[] { ("Hoy", 1), ("7 días", 7), ("30 días", 30) }.Select(p =>
            {
                var desde = Dia(hoy.AddDays(1 - p.Item2));
                return new OpcionRapida(p.Item1, RutaCon(("desde", desde), ("hasta", Dia(hoy)), ("pagina", null)), Desde == desde && Hasta == Dia(hoy));
            }).ToList();
        }
    }

    private readonly Analitica.Zonas zonas = new(configuracion);

    // La zona del caso con el mismo nombre que usan Analítica y el mapa de concentración («Zona K10»); null si no tiene ubicación (CA05).
    public string? ZonaDe(CasoEncontrado caso)
    {
        if (caso.Latitud is not { } lat || caso.Longitud is not { } lon)
            return null;
        var celda = Cad.Proyeccion.Celda.De(lat, lon);
        return zonas.Nombre(celda.Sur, celda.Oeste);
    }

    // «Hoy, 5 de noviembre», «Ayer, 4 de noviembre», «martes 3 de noviembre» (con el año si no es el actual).
    public string EtiquetaDia(DateTimeOffset momento)
    {
        var es = CultureInfo.GetCultureInfo("es-PE");
        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, ZonaHoraria).DateTime);
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ZonaHoraria).DateTime);
        var fecha = $"{local.Day} de {es.DateTimeFormat.GetMonthName(local.Month).ToLower(es)}" + (local.Year == hoy.Year ? "" : $" de {local.Year}");
        return local == hoy ? $"Hoy, {fecha}" : local == hoy.AddDays(-1) ? $"Ayer, {fecha}" : $"{es.DateTimeFormat.GetDayName(local.DayOfWeek).ToLower(es)} {fecha}";
    }

    // La dirección de esta misma lista con otros valores en algunos filtros (un valor null lo quita).
    private string RutaCon(params (string Clave, string? Valor)[] cambios)
    {
        var parametros = new Dictionary<string, string?>
        {
            ["desde"] = Desde, ["hasta"] = Hasta, ["origen"] = Origen, ["tipo"] = Tipo, ["estado"] = Estado, ["sereno"] = Sereno, ["grupo"] = Grupo, ["zona"] = Zona,
        };
        foreach (var (clave, valor) in cambios)
            parametros[clave] = valor;
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/panel/casos",
            parametros.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value));
    }

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
        {
            filtroActual = filtro;
            Resumen = await consultar.ResumenAsync(filtro, ct);
            Resultado = await consultar.EjecutarAsync(filtro, Pagina, ct);
        }
    }

    // La vista previa de un caso (panel de la derecha): un fragmento con su resumen y sus últimas actuaciones. Solo operadores, como la página.
    public async Task<IActionResult> OnGetVistaAsync(Guid id, [FromServices] ConsultarHistorialCaso historial, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(id, ct) is not { } caso || await historial.EjecutarAsync(id, ct) is not { } h)
            return NotFound();
        Vista = new VistaPreviaCaso(caso, h);
        return Partial("_VistaPrevia", this);
    }

    // Dirección de otra página con los mismos filtros.
    public string RutaPagina(int pagina)
    {
        var parametros = new Dictionary<string, string?>
        {
            ["desde"] = Desde, ["hasta"] = Hasta, ["origen"] = Origen, ["tipo"] = Tipo, ["estado"] = Estado, ["sereno"] = Sereno, ["grupo"] = Grupo, ["zona"] = Zona,
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
                ("tipo", "Tipo", Tipo, v => v == ValorSinTipo ? CasoEncontrado.TipoPorDeterminar
                    : Categorias.FirstOrDefault(c => c.Id.ToString() == v)?.Nombre),
                ("estado", "Estado", Estado, v => Enum.TryParse<EstadoCaso>(v, out var e) ? TextoEstadoFiltro(e) : null),
                ("sereno", "Sereno", Sereno, v => Serenos.FirstOrDefault(s => s.Id.ToString() == v)?.Nombre),
                ("zona", "Zona", Zona, v => CeldaDe(v) is { } c ? new Analitica.Zonas(configuracion).Nombre(c.Sur, c.Oeste) : null),
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
            ["desde"] = Desde, ["hasta"] = Hasta, ["origen"] = Origen, ["tipo"] = Tipo, ["estado"] = Estado, ["sereno"] = Sereno, ["grupo"] = Grupo, ["zona"] = Zona,
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

        // Una zona que no se entiende se ignora (no hay campo donde mostrar un error: llega por un enlace del mapa).
        (double Sur, double Oeste)? zona = CeldaDe(Zona ?? "");

        IReadOnlyList<EstadoCaso>? estados = null;
        if (!string.IsNullOrEmpty(Grupo))
        {
            if (Grupos.FirstOrDefault(g => g.Codigo == Grupo) is { } grupo)
                estados = grupo.Estados;
            else
                Errores["grupo"] = "Elige una vista de la lista.";
        }

        return new FiltroCasos(
            desde is { } inicio ? Inicio(inicio) : null,
            hasta is { } fin ? Inicio(fin.AddDays(1)) : null,
            origen, categoria, sinTipo, estado, estados, sereno, zona);
    }

    // «sur,oeste» en grados con punto decimal, dentro de rangos de latitud y longitud; si no, no es una zona.
    private static (double Sur, double Oeste)? CeldaDe(string texto)
    {
        var partes = texto.Split(',');
        return partes.Length == 2
            && double.TryParse(partes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sur)
            && double.TryParse(partes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var oeste)
            && sur is >= -90 and <= 90 && oeste is >= -180 and <= 180
            ? (sur, oeste) : null;
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
