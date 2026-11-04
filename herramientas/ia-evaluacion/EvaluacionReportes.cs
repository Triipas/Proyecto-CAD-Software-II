using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cad.IaEvaluacion;

// S6-T14 (plan de asistencia con IA, 9.2) — evalúa las sugerencias con reportes que los operadores ya decidieron: toma una muestra
// repartida entre admitidos, derivados y descartados, arma la misma entrada que el servidor (anonimizada, con candidatos y ejemplos
// que excluyen al propio caso) y compara la categoría y la decisión sugeridas con las finales. Compara con dos líneas base: la
// categoría que eligió el ciudadano y el asistente por reglas. Respeta la cuota de tokens por minuto del nivel gratuito.
public static class EvaluacionReportes
{
    private sealed record Caso(Guid Id, Guid CategoriaFinal, TipoDecisionReporte Decision, DateTimeOffset RegistradoEn);

    public sealed record Fila(Guid CasoId, string DecisionReal, string CategoriaReal, string CategoriaCiudadano, string? CategoriaModelo,
        string? DecisionModelo, string? Confianza, bool Escalada, bool PosibleUrgencia, int LatenciaMs, int Tokens, string CategoriaReglas,
        string DecisionReglas, string? Error);

    public static async Task<int> EjecutarAsync(Opciones opciones)
    {
        var proveedor = opciones.Texto("proveedor") ?? ProveedoresIa.Groq;
        var muestra = opciones.Entero("muestra", 30);
        var semilla = opciones.Entero("semilla", 20261104);
        var tpm = opciones.Entero("tpm", 7000);
        var salida = opciones.Texto("salida") ?? Path.Combine("herramientas", "ia-evaluacion", "resultados");

        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(CadDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ia:Habilitada"] = "true",
                ["Ia:Proveedor"] = proveedor,
                ["Ia:ProveedorVerificador"] = "",
                ["Ia:MaximoPorMinuto"] = "30",
                ["Ia:PresupuestoDiarioTokens"] = "2000000",
            })
            .Build();
        var conexion = opciones.Texto("conexion") is { } archivo ? (await File.ReadAllTextAsync(archivo)).Trim() : config.GetConnectionString("Cad");
        if (string.IsNullOrWhiteSpace(conexion))
        {
            Console.Error.WriteLine("Falta la conexión: --conexion <archivo> o ConnectionStrings:Cad en los user-secrets del servidor.");
            return 2;
        }

        var servicios = new ServiceCollection()
            .AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning))
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IConfiguration>(config)
            .AddDbContext<CadDbContext>(o => o.UseNpgsql(conexion));
        servicios.AddModuloAsistencia(config);
        await using var raiz = servicios.BuildServiceProvider();
        using var alcance = raiz.CreateScope();
        var sp = alcance.ServiceProvider;
        var db = sp.GetRequiredService<CadDbContext>();
        var asistente = sp.GetRequiredService<IAsistenteReportes>();
        if (!asistente.Disponible)
        {
            Console.Error.WriteLine($"El proveedor {proveedor} no está disponible (¿falta Ia:{proveedor}:ClaveApi en los user-secrets?).");
            return 2;
        }

        var casos = await MuestraAsync(db, muestra, semilla);
        var categorias = await db.CategoriasIncidencia.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre);
        var armar = sp.GetRequiredService<ArmarEntrada>();
        var ventana = new Queue<(DateTime En, int Tokens)>();
        var filas = new List<Fila>();
        Console.WriteLine($"Evaluando {casos.Count} reportes con {proveedor} (cuota: {tpm} tokens por minuto)…");

        foreach (var (caso, n) in casos.Select((c, i) => (c, i + 1)))
        {
            var detalle = await db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == caso.Id);
            var entrada = await armar.EjecutarAsync(detalle, caso.RegistradoEn, CancellationToken.None);
            var reglas = AsistenteReglas.Analizar(entrada);

            await EsperarCuotaAsync(ventana, tpm, ventana.Count == 0 ? 3000 : (int)ventana.Average(v => v.Tokens));
            var reloj = Stopwatch.StartNew();
            ResultadoAsistente? resultado = null;
            string? error = null;
            try
            {
                using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                resultado = await asistente.AnalizarAsync(entrada, limite.Token);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
            }
            var valido = resultado is null ? null : ValidarAnalisis.Aplicar(resultado.Analisis, entrada);
            var tokens = (resultado?.TokensEntrada ?? 0) + (resultado?.TokensSalida ?? 0);
            ventana.Enqueue((DateTime.UtcNow, tokens));
            if (resultado is not null && valido is null)
                error = "respuesta no válida";
            else if (resultado is null && error is null)
                error = "sin respuesta";

            filas.Add(new Fila(caso.Id, Texto(caso.Decision), categorias[caso.CategoriaFinal], categorias[entrada.CategoriaCiudadanoId],
                valido is null ? null : categorias.GetValueOrDefault(valido.CategoriaSugeridaId), valido?.Decision.ToString(), valido?.Confianza.ToString(),
                resultado?.Escalada ?? false, valido?.PosibleUrgencia ?? false, (int)reloj.ElapsedMilliseconds, tokens,
                categorias[reglas.CategoriaSugeridaId], reglas.Decision.ToString(), error));
            Console.WriteLine($"  {n,3}/{casos.Count}  real: {Texto(caso.Decision),-10} modelo: {valido?.Decision.ToString() ?? error,-18} " +
                $"{reloj.ElapsedMilliseconds,6} ms {tokens,6} tokens");
        }

        Directory.CreateDirectory(salida);
        var nombre = $"ia-reportes-{proveedor.ToLowerInvariant()}-{DateTime.Now:yyyy-MM-dd-HHmm}";
        var informe = Informe(filas, proveedor, semilla, CriteriosTriaje.Version, raiz.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcionesIa>>().Value);
        await File.WriteAllTextAsync(Path.Combine(salida, nombre + ".md"), informe, new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(salida, nombre + ".json"),
            JsonSerializer.Serialize(filas, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));
        Console.WriteLine();
        Console.WriteLine(informe);
        Console.WriteLine($"Informe: {Path.Combine(salida, nombre)}.md y .json");
        return 0;
    }

    // Reportes de ciudadanos decididos por un operador (no por la central al registrarlos ni por la admisión automática), con la
    // misma cantidad de cada decisión: así se ve si el modelo distingue derivar y descartar, que son pocos.
    private static async Task<List<Caso>> MuestraAsync(CadDbContext db, int muestra, int semilla)
    {
        var candidatos = await (from d in db.DecisionesReporte.AsNoTracking()
                                join caso in db.Casos.AsNoTracking() on d.CasoId equals caso.Id
                                join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId
                                where d.OperadorId != UsuarioSistema.Id && detalle.RegistradoPorOperadorId == null
                                select new Caso(caso.Id, caso.CategoriaId!.Value, d.Tipo, caso.RegistradoEn))
            .ToListAsync();
        var azar = new Random(semilla);
        var porTipo = muestra / 3;
        return candidatos.GroupBy(c => c.Decision)
            .SelectMany(g => g.OrderBy(c => c.Id).OrderBy(_ => azar.Next()).Take(g.Key == TipoDecisionReporte.Admision ? muestra - 2 * porTipo : porTipo))
            .OrderBy(_ => azar.Next())
            .ToList();
    }

    private static async Task EsperarCuotaAsync(Queue<(DateTime En, int Tokens)> ventana, int tpm, int estimado)
    {
        while (true)
        {
            while (ventana.Count > 0 && DateTime.UtcNow - ventana.Peek().En > TimeSpan.FromMinutes(1))
                ventana.Dequeue();
            if (ventana.Sum(v => v.Tokens) + estimado <= tpm || ventana.Count == 0)
                return;
            var espera = ventana.Peek().En.AddMinutes(1) - DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
            await Task.Delay(espera > TimeSpan.Zero ? espera : TimeSpan.FromMilliseconds(200));
        }
    }

    private static string Texto(TipoDecisionReporte tipo) => tipo switch
    {
        TipoDecisionReporte.Admision => "Admitir",
        TipoDecisionReporte.Derivacion => "Derivar",
        _ => "Descartar",
    };

    private static string Informe(List<Fila> filas, string proveedor, int semilla, string criterios, OpcionesIa o)
    {
        var inv = CultureInfo.InvariantCulture;
        string Pct(int a, int b) => b == 0 ? "—" : $"{100.0 * a / b:0.#} % ({a}/{b})";
        var respondidas = filas.Where(f => f.DecisionModelo is not null).ToList();
        var cubiertas = respondidas.Where(f => f.DecisionModelo != nameof(DecisionSugerida.RevisarConCuidado)).ToList();
        var latencias = filas.Select(f => f.LatenciaMs).Order().ToList();
        int P(double p) => latencias.Count == 0 ? 0 : latencias[(int)Math.Min(latencias.Count - 1, Math.Floor(p * latencias.Count))];
        var modelos = proveedor.Equals(ProveedoresIa.Groq, StringComparison.OrdinalIgnoreCase) ? $"{o.Groq.ModeloRapido} → {o.Groq.ModeloPreciso}"
            : proveedor.Equals(ProveedoresIa.Gemini, StringComparison.OrdinalIgnoreCase) ? $"{o.Gemini.ModeloRapido} → {o.Gemini.ModeloPreciso}" : AsistenteReglas.Modelo;

        var md = new StringBuilder();
        md.AppendLine($"# Evaluación de la asistencia con IA: reportes ({DateTime.Now:dd/MM/yyyy HH:mm})");
        md.AppendLine();
        md.AppendLine("> **Datos simulados.** Los reportes y las decisiones son del dataset de demostración JLByR (generado). Las cifras sirven para");
        md.AppendLine("> comparar variantes entre sí y ver que el flujo funciona, **no** como promesa de rendimiento real (plan, 14).");
        md.AppendLine();
        md.AppendLine($"- Proveedor: **{proveedor}** ({modelos}). Prompt `{o.VersionPrompt}`, criterios `{criterios}`, {o.Ejemplos} ejemplos por reporte.");
        md.AppendLine($"- Muestra: {filas.Count} reportes decididos por operadores, repartidos entre admitidos, derivados y descartados (semilla {semilla}).");
        md.AppendLine($"- Verdad: la categoría final del caso y la decisión que registró el operador. Los ejemplos del prompt excluyen al propio caso.");
        md.AppendLine();
        md.AppendLine("## Resultados");
        md.AppendLine();
        md.AppendLine("| Medida | Modelo | Reglas (línea base) | Categoría del ciudadano |");
        md.AppendLine("|---|---|---|---|");
        md.AppendLine($"| Respondió | {Pct(respondidas.Count, filas.Count)} | 100 % | — |");
        md.AppendLine($"| Categoría correcta | {Pct(respondidas.Count(f => f.CategoriaModelo == f.CategoriaReal), respondidas.Count)} | " +
            $"{Pct(filas.Count(f => f.CategoriaReglas == f.CategoriaReal), filas.Count)} | {Pct(filas.Count(f => f.CategoriaCiudadano == f.CategoriaReal), filas.Count)} |");
        var reglasCubiertas = filas.Where(f => f.DecisionReglas != nameof(DecisionSugerida.RevisarConCuidado)).ToList();
        md.AppendLine($"| Decisiones tomadas (no «revisar con cuidado») | {Pct(cubiertas.Count, respondidas.Count)} | {Pct(reglasCubiertas.Count, filas.Count)} | — |");
        md.AppendLine($"| Decisión correcta, entre las tomadas | {Pct(cubiertas.Count(f => f.DecisionModelo == f.DecisionReal), cubiertas.Count)} | " +
            $"{Pct(reglasCubiertas.Count(f => f.DecisionReglas == f.DecisionReal), reglasCubiertas.Count)} | — |");
        md.AppendLine();
        md.AppendLine("### Decisión por tipo real (modelo)");
        md.AppendLine();
        md.AppendLine("| Real \\ Sugerida | Admitir | Derivar | Descartar | Revisar con cuidado | Sin respuesta |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var real in new[] { "Admitir", "Derivar", "Descartar" })
        {
            var g = filas.Where(f => f.DecisionReal == real).ToList();
            int C(string? d) => g.Count(f => f.DecisionModelo == d);
            md.AppendLine($"| {real} | {C("Admitir")} | {C("Derivar")} | {C("Descartar")} | {C("RevisarConCuidado")} | {C(null)} |");
        }
        md.AppendLine();
        md.AppendLine("### Calibración: aciertos de decisión por nivel de confianza (modelo)");
        md.AppendLine();
        md.AppendLine("| Confianza | Reportes | Decisión correcta |");
        md.AppendLine("|---|---|---|");
        foreach (var nivel in new[] { "Alta", "Media", "Baja" })
        {
            var g = cubiertas.Where(f => f.Confianza == nivel).ToList();
            md.AppendLine($"| {nivel} | {respondidas.Count(f => f.Confianza == nivel)} | {Pct(g.Count(f => f.DecisionModelo == f.DecisionReal), g.Count)} |");
        }
        md.AppendLine();
        md.AppendLine("### Costo y tiempo");
        md.AppendLine();
        md.AppendLine($"- Pasaron al modelo preciso: {Pct(filas.Count(f => f.Escalada), filas.Count)}. Marcadas con posible urgencia: {filas.Count(f => f.PosibleUrgencia)}.");
        md.AppendLine($"- Tiempo por reporte: mediana {(P(0.5) / 1000.0).ToString("0.0", inv)} s, p90 {(P(0.9) / 1000.0).ToString("0.0", inv)} s.");
        md.AppendLine($"- Tokens: {filas.Sum(f => f.Tokens).ToString("N0", inv)} en total, {(filas.Count == 0 ? 0 : filas.Sum(f => f.Tokens) / filas.Count).ToString("N0", inv)} por reporte.");
        md.AppendLine($"- Errores: {string.Join(", ", filas.Where(f => f.Error is not null).GroupBy(f => f.Error).Select(g => $"{g.Key} ({g.Count()})").DefaultIfEmpty("ninguno"))}.");
        md.AppendLine();
        md.AppendLine("## Límites");
        md.AppendLine();
        md.AppendLine("- Muestra pequeña por la cuota del nivel gratuito: los porcentajes tienen mucho margen de error.");
        md.AppendLine("- La «verdad» la puso el generador del dataset, no un etiquetado independiente de tres personas (plan, 9.2): falta el techo humano.");
        md.AppendLine("- Los ejemplos pueden incluir decisiones posteriores al reporte evaluado (en el uso real, solo hay anteriores).");
        return md.ToString();
    }
}
