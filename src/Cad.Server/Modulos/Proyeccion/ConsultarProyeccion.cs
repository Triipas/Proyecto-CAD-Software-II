using System.Diagnostics;
using Cad.Calles;
using Cad.Proyeccion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Proyeccion;

// Proyeccion:* (plan de asistencia con IA, 8.2 y 8.4).
public sealed class OpcionesProyeccion
{
    public const string Seccion = "Proyeccion";

    // Historia que se lee de la base: la línea base mira hasta 26 semanas atrás.
    public int SemanasHistoria { get; set; } = 26;
    // CA05: con menos casos que esto en las últimas 8 semanas, no se proyecta.
    public int MinimoCasos { get; set; } = 50;
    // CA04: una zona de nivel Alto sin sereno operativo a menos de esta distancia queda «Sin cobertura cercana».
    public int UmbralCoberturaMetros { get; set; } = 1500;
    public int ZonasPrincipales { get; set; } = 10;
    // Nivel absoluto (rediseño del 05/11/2026): casos esperados en un bloque de 4 h, en una zona de unos 550 m. Alto es de 0,30 en adelante
    // (un caso esperado cada 13 horas); Medio, de 0,15 a menos de 0,30. Con el dataset de demostración (4000 casos en 13 semanas), la
    // zona más alta espera 0,37 por bloque y la mediana de las zonas, 0,09. Una semana tranquila no marca «Alto» solo por ser la zona
    // más alta de la ciudad: son valores de presentación que la central puede ajustar en la sección Proyeccion de la configuración.
    public double UmbralAlto { get; set; } = 0.30;
    public double UmbralMedio { get; set; } = 0.15;
}

public enum NivelFoco { Bajo, Medio, Alto }

public enum VentanaProyeccion { ProximasSeisHoras, EstaNoche, Manana }

// Una zona proyectada: la celda (nunca un caso concreto, CA03), los casos esperados en la ventana, su nivel, por qué y la cobertura.
public sealed record ZonaProyectada(string Clave, string Zona, double Sur, double Oeste, double Norte, double Este, double Esperados, NivelFoco Nivel,
    string Explicacion, string? CategoriaDominante, int? MetrosSerenoCercano, Guid? SerenoCercanoId, string? SerenoCercano, bool SinCobertura,
    // Lo esperado en cada bloque de 4 h de la ventana (el bloque entero, no solo la parte que cae dentro), su nivel y por qué; la
    // explicación solo se calcula para las zonas principales de cada bloque.
    IReadOnlyList<double>? PorBloque = null, IReadOnlyList<NivelFoco>? NivelesPorBloque = null, IReadOnlyList<string?>? ExplicacionesPorBloque = null);

// Un bloque de 4 h que toca la ventana: su rango en hora local, qué parte cae dentro y los casos esperados en toda la ciudad.
public sealed record BloqueProyectado(long Id, DateTimeOffset Inicio, DateTimeOffset Fin, string Rotulo, string Dia, double Fraccion, double Total, bool EsAhora);

// Un sereno operativo con ubicación, para dibujarlo en el mapa de la proyección.
public sealed record SerenoProyeccion(string Nombre, double Latitud, double Longitud, bool Disponible);

public sealed record ResultadoProyeccion(VentanaProyeccion Ventana, string TextoVentana, DateTimeOffset Desde, DateTimeOffset Hasta, bool Suficiente,
    int CasosRecientes, bool ConModelo, double PesoModelo, IReadOnlyList<ZonaProyectada> Zonas, int UmbralCoberturaMetros,
    IReadOnlyList<BloqueProyectado>? Bloques = null, IReadOnlyList<SerenoProyeccion>? Serenos = null, double UmbralAlto = 0.30, double UmbralMedio = 0.15)
{
    public IReadOnlyList<ZonaProyectada> Principales(int n) => Zonas.OrderByDescending(z => z.Esperados).Take(n).ToList();
    // Ninguna zona llega al nivel Medio: se dice así en lugar de marcar la menos baja.
    public bool Tranquila => Zonas.All(z => z.Nivel == NivelFoco.Bajo);
    public string Metodo => ConModelo
        ? $"árboles de decisión con la línea base (peso {PesoModelo:0.00} del modelo)"
        : "línea base estadística (no hay modelo cargado)";
}

// El modelo exportado por la herramienta (Recursos/modelo-focos.zip), cargado una vez. Se mide cuánta memoria añade (plan, 8.5):
// en el plan gratuito de Render hay poca, y si fuera excesiva el servidor usaría solo la línea base.
public sealed class ModeloProyeccion(ILogger<ModeloProyeccion> registro)
{
    private const string Recurso = "Cad.Server.Recursos.modelo-focos.zip";
    private readonly Lazy<ModeloFocos?> modelo = new(() => Cargar(registro));

    public ModeloFocos? Modelo => modelo.Value;
    public static long MemoriaBytes { get; private set; }

    private static ModeloFocos? Cargar(ILogger registro)
    {
        using var flujo = typeof(ModeloProyeccion).Assembly.GetManifestResourceStream(Recurso);
        if (flujo is null)
        {
            registro.LogWarning("Proyección de focos: no hay modelo incrustado; se usa solo la línea base.");
            return null;
        }
        var antes = GC.GetTotalMemory(forceFullCollection: true);
        var reloj = Stopwatch.StartNew();
        var cargado = ModeloFocos.Cargar(flujo);
        // Una predicción para que la memoria incluya el motor de predicción.
        cargado.Predecir([new FilaRasgos()]);
        MemoriaBytes = Math.Max(0, GC.GetTotalMemory(forceFullCollection: true) - antes);
        registro.LogInformation("Proyección de focos: modelo {Version} (mundo {Mundo}, peso {Peso}) cargado en {Ms} ms; ocupa unos {MB:0.0} MB.",
            cargado.Metadatos.Version, cargado.Metadatos.Mundo, cargado.Metadatos.PesoModelo, reloj.ElapsedMilliseconds, MemoriaBytes / 1048576.0);
        return cargado;
    }
}

// HU46 (S6-T19; plan, 8.2 a 8.5) — la proyección de focos con los datos vivos de la base: casos SOS y reportes con ubicación válida,
// sin cancelados ni descartados (los mismos que el mapa de concentración, CA02). Por celda y bloque de 4 h de la ventana: casos esperados
// con el modelo y la línea base en conjunto; luego el nivel absoluto (casos esperados por bloque de 4 h), una explicación calculada y la
// distancia al sereno operativo más cercano. Solo celdas: nunca el punto ni los datos de un caso (CA03).
public sealed class ConsultarProyeccion(CadDbContext db, ModeloProyeccion modelo, ConsultarSerenos serenos, IOptions<OpcionesProyeccion> opciones,
    TimeProvider reloj, IConfiguration configuracion)
{
    // El mismo nombre de zona que Analítica y el mapa de concentración («Zona K10»).
    private readonly Panel.Pages.Analitica.Zonas zonas = new(configuracion);

    public async Task<ResultadoProyeccion> EjecutarAsync(VentanaProyeccion ventana, CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = reloj.GetUtcNow();
        var (desde, hasta, texto) = Ventana(ventana, ahora);
        var casos = await CasosAsync(ahora.AddDays(-7 * o.SemanasHistoria), ahora, ct);
        var historial = new Historial(casos);
        var recientes = casos.Count(c => c.RegistradoEn >= ahora.AddDays(-56));
        var cargado = modelo.Modelo;
        var peso = cargado?.Metadatos.PesoModelo ?? 0;
        if (recientes < o.MinimoCasos || historial.Celdas.Count == 0)
            return new ResultadoProyeccion(ventana, texto, desde, hasta, false, recientes, cargado is not null, peso, [], o.UmbralCoberturaMetros);

        var linea = new LineaBase(historial, new ParametrosLineaBase(Semanas: o.SemanasHistoria));
        var celdas = historial.Celdas.ToList();
        var bloques = Fracciones(desde, hasta);
        var esperados = new double[celdas.Count];
        var principal = new long[celdas.Count]; // el bloque con más casos esperados de cada celda, para la explicación
        var mejor = new double[celdas.Count];
        var porBloque = new double[celdas.Count][];
        for (var i = 0; i < celdas.Count; i++)
            porBloque[i] = new double[bloques.Count];
        for (var b = 0; b < bloques.Count; b++)
        {
            var (bloque, fraccion) = bloques[b];
            var filas = celdas.Select(c => ConstructorRasgos.Construir(historial, linea, c, bloque)).ToList();
            var arboles = cargado?.Predecir(filas);
            for (var i = 0; i < celdas.Count; i++)
            {
                var valor = arboles is null ? filas[i].LineaBase : peso * arboles[i] + (1 - peso) * filas[i].LineaBase;
                porBloque[i][b] = Math.Max(0, valor);
                esperados[i] += valor * fraccion;
                if (valor > mejor[i] || principal[i] == 0)
                    (mejor[i], principal[i]) = (valor, bloque);
            }
        }

        // Nivel absoluto: lo esperado por bloque de 4 h. Para la ventana entera se promedia por bloque (casos esperados / bloques que cubre).
        var cobertura = bloques.Sum(x => x.Fraccion);
        var operativos = (await serenos.EjecutarAsync(ct)).Where(s => s.Operativo && s.UbicacionDisponible).ToList();
        // La explicación de cada bloque solo para sus zonas principales: es lo que el panel lista.
        var explicaciones = celdas.Select(_ => new string?[bloques.Count]).ToArray();
        for (var b = 0; b < bloques.Count; b++)
            foreach (var i in Enumerable.Range(0, celdas.Count).Where(i => porBloque[i][b] > 0).OrderByDescending(i => porBloque[i][b]).Take(o.ZonasPrincipales))
                explicaciones[i][b] = Explicar(historial, celdas[i], bloques[b].Bloque).Texto;

        var resultado = celdas.Select((c, i) =>
        {
            var cercano = operativos.Select(s => (Sereno: s, Metros: (int)Math.Round(Geo.DistanciaMetros(c.LatitudCentro, c.LongitudCentro, s.Latitud!.Value, s.Longitud!.Value))))
                .OrderBy(x => x.Metros).FirstOrDefault();
            var (explicacion, categoria) = Explicar(historial, c, principal[i]);
            var nivel = NivelDe(esperados[i] / cobertura, o);
            int? metros = cercano.Sereno is null ? null : cercano.Metros;
            return new ZonaProyectada(c.Clave, zonas.Nombre(c.Sur, c.Oeste), c.Sur, c.Oeste, c.Norte, c.Este, Math.Round(esperados[i], 2), nivel,
                explicacion, categoria, metros, cercano.Sereno?.Id, cercano.Sereno?.Nombre,
                nivel == NivelFoco.Alto && (metros is null || metros > o.UmbralCoberturaMetros),
                porBloque[i].Select(v => Math.Round(v, 2)).ToList(), porBloque[i].Select(v => NivelDe(v, o)).ToList(), explicaciones[i]);
        }).Where(z => z.Esperados > 0).OrderByDescending(z => z.Esperados).ToList();

        var detalle = bloques.Select((x, b) => Describir(x.Bloque, x.Fraccion, porBloque.Sum(v => v[b]), ahora)).ToList();
        var mapa = operativos.Select(s => new SerenoProyeccion(s.Nombre, s.Latitud!.Value, s.Longitud!.Value, s.Disponible)).ToList();
        return new ResultadoProyeccion(ventana, texto, desde, hasta, true, recientes, cargado is not null, peso, resultado, o.UmbralCoberturaMetros,
            detalle, mapa, o.UmbralAlto, o.UmbralMedio);
    }

    // Nivel absoluto, por bloque de 4 h: Alto desde UmbralAlto casos esperados; Medio desde UmbralMedio; el resto, Bajo.
    public static NivelFoco NivelDe(double casosPorBloque, OpcionesProyeccion o) =>
        casosPorBloque >= o.UmbralAlto ? NivelFoco.Alto : casosPorBloque >= o.UmbralMedio ? NivelFoco.Medio : NivelFoco.Bajo;

    // «20–24 h» y «Hoy», «Mañana» o el día de la semana, en la hora local.
    private static BloqueProyectado Describir(long bloque, double fraccion, double total, DateTimeOffset ahora)
    {
        var inicio = Bloques.Inicio(bloque);
        var fin = inicio.AddHours(Bloques.Horas);
        var hoy = DateOnly.FromDateTime(ahora.ToOffset(Bloques.HusoHorario).DateTime);
        var dia = DateOnly.FromDateTime(inicio.DateTime);
        var texto = dia == hoy ? "Hoy" : dia == hoy.AddDays(1) ? "Mañana" : System.Globalization.CultureInfo.GetCultureInfo("es-PE").DateTimeFormat.GetDayName(dia.DayOfWeek);
        return new BloqueProyectado(bloque, inicio, fin, $"{inicio.Hour:00}–{inicio.Hour + Bloques.Horas:00} h", char.ToUpperInvariant(texto[0]) + texto[1..],
            fraccion, Math.Round(total, 2), ahora >= inicio && ahora < fin);
    }

    // «Viernes 20–24 h: 9 casos en esa franja en las últimas 4 semanas (6 de robo o hurto)». La categoría se cuenta sobre los mismos
    // casos que la cifra: los de la franja o, si no hubo, los de la zona.
    private static (string Texto, string? Categoria) Explicar(Historial h, Celda celda, long bloque)
    {
        var franja = Bloques.TextoFranja(bloque);
        var desde = bloque - 4L * Bloques.PorSemana;
        var enLaFranja = h.Categorias(celda, desde, bloque).Where(c => Bloques.Franja(c.Bloque) == Bloques.Franja(bloque)).ToList();
        var mismaFranja = Enumerable.Range(1, 4).Sum(k => h.Casos(celda, bloque - (long)k * Bloques.PorSemana));
        var contadas = mismaFranja > 0 ? enLaFranja : h.Categorias(celda, desde, bloque).ToList();
        var dominante = contadas.Where(c => c.Categoria >= 0 && c.Categoria < CategoriaIncidencia.Iniciales.Count)
            .GroupBy(c => c.Categoria).OrderByDescending(g => g.Count()).FirstOrDefault();
        var categoria = dominante is null ? null : CategoriaIncidencia.Iniciales[dominante.Key].Nombre.ToLowerInvariant();
        var mayuscula = char.ToUpperInvariant(franja[0]) + franja[1..];
        var texto = mismaFranja > 0
            ? $"{mayuscula}: {mismaFranja} {(mismaFranja == 1 ? "caso" : "casos")} en esa franja en las últimas 4 semanas"
            : $"{mayuscula}: sin casos en esa franja; {h.Casos(celda, desde, bloque)} en la zona en las últimas 4 semanas";
        if (dominante is not null)
            texto += $" ({dominante.Count()} de {categoria})";
        return (texto + ".", categoria);
    }

    private async Task<List<CasoHistorico>> CasosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var categorias = CategoriaIncidencia.Iniciales.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var filas = await (from caso in db.Casos.AsNoTracking()
                           join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId into detalles
                           from detalle in detalles.DefaultIfEmpty()
                           where caso.RegistradoEn >= desde && caso.RegistradoEn < hasta
                                 && (caso.Origen == OrigenCaso.Sos || caso.Origen == OrigenCaso.Reporte)
                                 && !ConsultarConcentracion.EstadosExcluidos.Contains(caso.Estado)
                           select new
                           {
                               caso.RegistradoEn, caso.CategoriaId,
                               Latitud = caso.Origen == OrigenCaso.Sos ? caso.Latitud : (detalle == null ? null : detalle.LatitudHecho),
                               Longitud = caso.Origen == OrigenCaso.Sos ? caso.Longitud : (detalle == null ? null : detalle.LongitudHecho),
                           })
            .ToListAsync(ct);
        return filas.Where(f => f.Latitud is not null && f.Longitud is not null)
            .Select(f => new CasoHistorico(f.RegistradoEn, f.Latitud!.Value, f.Longitud!.Value,
                f.CategoriaId is { } id && categorias.TryGetValue(id, out var indice) ? indice : -1))
            .ToList();
    }

    // Próximas 6 h; esta noche (18–24 h de hoy, o lo que queda); mañana (todo el día).
    public static (DateTimeOffset Desde, DateTimeOffset Hasta, string Texto) Ventana(VentanaProyeccion ventana, DateTimeOffset ahora)
    {
        var local = ahora.ToOffset(Bloques.HusoHorario);
        var hoy = new DateTimeOffset(local.Date, Bloques.HusoHorario);
        return ventana switch
        {
            VentanaProyeccion.EstaNoche => (local.Hour >= 18 ? ahora : hoy.AddHours(18), hoy.AddDays(1), "esta noche (18–24 h)"),
            VentanaProyeccion.Manana => (hoy.AddDays(1), hoy.AddDays(2), "mañana"),
            _ => (ahora, ahora.AddHours(6), "las próximas 6 horas"),
        };
    }

    // Los bloques de 4 h que toca la ventana y qué parte de cada uno cae dentro.
    public static List<(long Bloque, double Fraccion)> Fracciones(DateTimeOffset desde, DateTimeOffset hasta)
    {
        var lista = new List<(long, double)>();
        for (var b = Bloques.De(desde); Bloques.Inicio(b) < hasta; b++)
        {
            var inicio = Bloques.Inicio(b);
            var fin = inicio.AddHours(Bloques.Horas);
            var horas = (Min(fin, hasta) - Max(inicio, desde)).TotalHours;
            if (horas > 0)
                lista.Add((b, horas / Bloques.Horas));
        }
        return lista;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
