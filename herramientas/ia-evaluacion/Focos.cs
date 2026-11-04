using System.Globalization;
using System.Text;
using System.Text.Json;
using Cad.DatosDemo;
using Cad.Proyeccion;

namespace Cad.IaEvaluacion;

// HU48 (S6-T24; plan, 4.6 y 9.3) — generar mundos, comparar modelos con un backtest hacia delante, entrenar el modelo final y
// exportarlo al servidor. Los rasgos los calcula Cad.Proyeccion: el mismo código que usa el servidor al predecir.
public static class Focos
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // Semanas de historia que necesitan los rasgos antes de la primera fila de entrenamiento (el más largo mira 8 semanas atrás).
    private const int SemanasPrevias = 8;

    public static int Generar(Opciones o)
    {
        var mundo = o.Texto("mundo") ?? "A";
        var semanas = o.Entero("semanas", 78);
        var semilla = o.Entero("semilla", mundo == "B" ? 20261104 : 20261103);
        var salida = o.Texto("salida") ?? Path.Combine("datos", $"mundo-{mundo.ToLowerInvariant()}");
        var m = GeneradorMundo.Generar(mundo, semanas, semilla);
        Directory.CreateDirectory(salida);
        Escribir(m, salida);
        var dias = semanas * 7.0;
        Console.WriteLine($"Mundo {m.Nombre}: {m.Casos.Count} casos en {semanas} semanas ({m.Casos.Count / dias:0.0} por día), " +
            $"{m.Casos.Count(c => c.EsSos)} SOS, {m.Celdas.Count} celdas. Desde {m.Inicio:yyyy-MM-dd}. → {salida}");
        return 0;
    }

    public static void Escribir(Mundo m, string carpeta)
    {
        using (var casos = new StreamWriter(Path.Combine(carpeta, "casos.csv"), false, new UTF8Encoding(false)))
        {
            casos.WriteLine("id,origen,registradoEn,lat,lon,categoria,celda,fuente");
            foreach (var c in m.Casos)
                casos.WriteLine(string.Join(',', c.Id, c.EsSos ? "Sos" : "Reporte", c.RegistradoEn.ToString("O", Inv), c.Latitud.ToString(Inv),
                    c.Longitud.ToString(Inv), c.Categoria, Celda.De(c.Latitud, c.Longitud).Clave, $"\"{c.Origen}\""));
        }
        using (var intensidad = new StreamWriter(Path.Combine(carpeta, "intensidad.csv"), false, new UTF8Encoding(false)))
        {
            intensidad.WriteLine("celda,bloque,intensidad");
            foreach (var ((celda, bloque), valor) in m.Intensidad)
                intensidad.WriteLine($"{celda.Clave},{bloque},{valor.ToString("0.######", Inv)}");
        }
        File.WriteAllText(Path.Combine(carpeta, "meta.json"), JsonSerializer.Serialize(new
        {
            mundo = m.Nombre, semilla = m.Semilla, inicio = m.Inicio, semanas = m.Semanas, casos = m.Casos.Count,
            celdas = m.Celdas.Select(c => c.Clave),
            focos = GeneradorMundo.Focos(m.Nombre).Select(f => new { f.Nombre, f.Latitud, f.Longitud, f.CasosPorDia, f.SemanaInicio, f.SemanaFin, f.Emergente }),
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    public sealed record Datos(string Mundo, List<CasoHistorico> Casos, Dictionary<(Celda, long), double> Intensidad, List<Celda> Celdas, long PrimerBloque, int Semanas);

    public static Datos Leer(string carpeta)
    {
        var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(carpeta, "meta.json"))).RootElement;
        var casos = File.ReadLines(Path.Combine(carpeta, "casos.csv")).Skip(1).Select(l => l.Split(','))
            .Select(p => new CasoHistorico(DateTimeOffset.Parse(p[2], Inv), double.Parse(p[3], Inv), double.Parse(p[4], Inv), int.Parse(p[5])))
            .ToList();
        var intensidad = File.ReadLines(Path.Combine(carpeta, "intensidad.csv")).Skip(1).Select(l => l.Split(','))
            .ToDictionary(p => (Celda.DeClave(p[0]), long.Parse(p[1])), p => double.Parse(p[2], Inv));
        var celdas = meta.GetProperty("celdas").EnumerateArray().Select(c => Celda.DeClave(c.GetString()!)).ToList();
        return new Datos(meta.GetProperty("mundo").GetString()!, casos, intensidad, celdas,
            Bloques.De(meta.GetProperty("inicio").GetDateTimeOffset()), meta.GetProperty("semanas").GetInt32());
    }

    // Filas de entrenamiento: cada celda del distrito en cada bloque de [desde, hasta), con los casos reales como etiqueta.
    public static List<FilaRasgos> Filas(Historial h, LineaBase linea, IReadOnlyList<Celda> celdas, long desde, long hasta)
    {
        var filas = new List<FilaRasgos>((int)(hasta - desde) * celdas.Count);
        for (var b = desde; b < hasta; b++)
            foreach (var c in celdas)
                filas.Add(ConstructorRasgos.Construir(h, linea, c, b, h.Casos(c, b)));
        return filas;
    }

    public sealed record ResultadoMetodo(string Metodo, double Acierto, double Pai, double Desviacion, double Error, double FraccionOraculo);

    public static int Backtest(Opciones o)
    {
        var carpeta = o.Texto("entrada") ?? Path.Combine("datos", "mundo-a");
        var semanasPrueba = o.Entero("semanas", 12);
        var cada = Math.Max(1, o.Entero("cada", 4)); // cada cuántas semanas se vuelve a entrenar (entrenar cada semana es más lento)
        var d = Leer(carpeta);
        var h = new Historial(d.Casos);
        var linea = new LineaBase(h);
        var fin = d.PrimerBloque + (long)d.Semanas * Bloques.PorSemana;
        var inicioPrueba = fin - (long)semanasPrueba * Bloques.PorSemana;
        var primeraFila = d.PrimerBloque + (long)SemanasPrevias * Bloques.PorSemana;
        Console.WriteLine($"Backtest del mundo {d.Mundo}: {d.Casos.Count} casos; se predicen las últimas {semanasPrueba} semanas, reentrenando cada {cada}.");

        var esperados = new Dictionary<string, List<double[]>>();
        var reales = new List<int[]>();
        void Anotar(string metodo, double[] valores)
        {
            if (!esperados.TryGetValue(metodo, out var lista))
                esperados[metodo] = lista = [];
            lista.Add(valores);
        }

        var azar = new Random(1);
        ModeloFocos? modelo = null;
        for (var semana = 0; semana < semanasPrueba; semana++)
        {
            var corte = inicioPrueba + (long)semana * Bloques.PorSemana;
            if (semana % cada == 0)
            {
                var reloj = System.Diagnostics.Stopwatch.StartNew();
                modelo = ModeloFocos.Entrenar(Filas(h, linea, d.Celdas, primeraFila, corte), Metadatos(d, 1));
                Console.WriteLine($"  semana {semana + 1,2}: entrenado con {(corte - primeraFila) / Bloques.PorSemana} semanas en {reloj.Elapsed.TotalSeconds:0.0} s");
            }
            var totales = d.Celdas.Select(c => (double)h.Casos(c, d.PrimerBloque, corte)).ToArray();
            var sinMemoria = d.Celdas.Select(c => h.Casos(c, d.PrimerBloque, corte) / (double)Math.Max(1, corte - d.PrimerBloque)).ToArray();
            for (var b = corte; b < corte + Bloques.PorSemana; b++)
            {
                var filas = d.Celdas.Select(c => ConstructorRasgos.Construir(h, linea, c, b)).ToList();
                var arboles = modelo!.Predecir(filas).Select(x => (double)x).ToArray();
                var lineaBase = filas.Select(f => (double)f.LineaBase).ToArray();
                reales.Add(d.Celdas.Select(c => h.Casos(c, b)).ToArray());
                Anotar("Azar", d.Celdas.Select(_ => azar.NextDouble()).ToArray());
                Anotar("Más casos en total", totales);
                Anotar("Promedio sin memoria", sinMemoria);
                Anotar("La semana pasada se repite", d.Celdas.Select(c => (double)h.Casos(c, b - Bloques.PorSemana)).ToArray());
                Anotar("Línea base", lineaBase);
                Anotar("Árboles (FastTree Tweedie)", arboles);
                foreach (var w in new[] { 0.25, 0.5, 0.75 })
                    Anotar($"Conjunto {w:0.00}", arboles.Zip(lineaBase, (a, l) => w * a + (1 - w) * l).ToArray());
                Anotar("Oráculo", d.Celdas.Select(c => d.Intensidad.GetValueOrDefault((c, b))).ToArray());
            }
        }

        var oraculo = Evaluar(esperados["Oráculo"], reales);
        var resultados = esperados.Select(e =>
        {
            var r = Evaluar(e.Value, reales);
            return new ResultadoMetodo(e.Key, r.Acierto, r.Pai, r.DesviacionPoisson, r.ErrorAbsoluto, oraculo.Pai == 0 ? 0 : r.Pai / oraculo.Pai);
        }).ToList();
        // El conjunto que se despliega: el de menor desviación (mide el número, que es lo que se muestra), entre los conjuntos y los extremos.
        var candidatos = resultados.Where(r => r.Metodo.StartsWith("Conjunto") || r.Metodo is "Línea base" or "Árboles (FastTree Tweedie)").ToList();
        var mejor = candidatos.MinBy(r => r.Desviacion)!;
        var peso = mejor.Metodo switch { "Línea base" => 0, "Árboles (FastTree Tweedie)" => 1, var m => double.Parse(m["Conjunto ".Length..], Inv) };
        var emergente = Emergente(d, h, linea, inicioPrueba, semanasPrueba);

        var md = Informe(d, resultados, semanasPrueba, cada, mejor.Metodo, peso, emergente);
        File.WriteAllText(Path.Combine(carpeta, "backtest.md"), md, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(carpeta, "backtest.json"), JsonSerializer.Serialize(new { mundo = d.Mundo, peso, resultados, emergente },
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine();
        Console.WriteLine(md);
        return 0;
    }

    private static Metricas.Resultado Evaluar(List<double[]> esperados, List<int[]> reales) =>
        Metricas.Promedio(esperados.Zip(reales, (e, r) => Metricas.Evaluar(e, r)).ToList());

    // CA06: la celda del foco emergente, ¿en qué puesto la pone cada método semana a semana? (1 = la más probable).
    private static object Emergente(Datos d, Historial h, LineaBase linea, long inicioPrueba, int semanas)
    {
        var foco = GeneradorMundo.Focos(d.Mundo).FirstOrDefault(f => f.Emergente);
        if (foco is null)
            return new { };
        var celda = Celda.De(foco.Latitud, foco.Longitud);
        var puestos = new List<object>();
        for (var s = 0; s < semanas; s++)
        {
            var b = inicioPrueba + (long)s * Bloques.PorSemana;
            int Puesto(Func<Celda, double> valor) => d.Celdas.Count(c => valor(c) > valor(celda)) + 1;
            puestos.Add(new
            {
                semana = s + 1,
                lineaBase = Puesto(c => Enumerable.Range(0, Bloques.PorSemana).Sum(i => linea.Calcular(c, b + i))),
                sinMemoria = Puesto(c => h.Casos(c, d.PrimerBloque, b)),
            });
        }
        return new { foco = foco.Nombre, celda = celda.Clave, puestos };
    }

    private static MetadatosModelo Metadatos(Datos d, double peso) =>
        new("2026-11-a", peso, d.Mundo, d.Semanas, DateTimeOffset.UtcNow, FilaRasgos.Nombres);

    public static int Entrenar(Opciones o)
    {
        var carpeta = o.Texto("entrada") ?? Path.Combine("datos", "mundo-a");
        var salida = o.Texto("salida") ?? Path.Combine(carpeta, "modelo-focos.zip");
        var d = Leer(carpeta);
        var peso = double.TryParse(o.Texto("peso"), NumberStyles.Float, Inv, out var p) ? p
            : File.Exists(Path.Combine(carpeta, "backtest.json"))
                ? JsonDocument.Parse(File.ReadAllText(Path.Combine(carpeta, "backtest.json"))).RootElement.GetProperty("peso").GetDouble()
                : 0.5;
        var h = new Historial(d.Casos);
        var linea = new LineaBase(h);
        var fin = d.PrimerBloque + (long)d.Semanas * Bloques.PorSemana;
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var filas = Filas(h, linea, d.Celdas, d.PrimerBloque + (long)SemanasPrevias * Bloques.PorSemana, fin);
        var modelo = ModeloFocos.Entrenar(filas, Metadatos(d, peso));
        modelo.Guardar(salida);
        // Calibración: la suma de lo predicho frente a la de lo ocurrido en las filas de entrenamiento.
        var predichos = modelo.Predecir(filas).Sum();
        Console.WriteLine($"Modelo entrenado con {filas.Count} filas en {reloj.Elapsed.TotalSeconds:0.0} s (peso en el conjunto: {peso:0.00}). " +
            $"Casos predichos {predichos:0} frente a {filas.Sum(f => f.Label):0} ocurridos. → {salida} ({new FileInfo(salida).Length / 1024} KB)");
        return 0;
    }

    public static int Exportar(Opciones o)
    {
        var origen = o.Texto("modelo") ?? throw new ArgumentException("Falta --modelo <archivo .zip>.");
        var destino = o.Texto("destino") ?? Path.Combine("src", "Cad.Server", "Recursos", "modelo-focos.zip");
        using (var flujo = File.OpenRead(origen))
            _ = ModeloFocos.Cargar(flujo); // que se pueda cargar antes de reemplazar el del servidor
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(origen, destino, overwrite: true);
        Console.WriteLine($"Modelo exportado a {destino}.");
        return 0;
    }

    private static string Informe(Datos d, List<ResultadoMetodo> resultados, int semanas, int cada, string elegido, double peso, object emergente)
    {
        string P(double x) => (100 * x).ToString("0.0", Inv) + " %";
        var md = new StringBuilder();
        md.AppendLine($"# Backtest de la proyección de focos: mundo {d.Mundo} ({DateTime.Now:dd/MM/yyyy})");
        md.AppendLine();
        md.AppendLine("> **Datos simulados** con el generador del proyecto (Cad.DatosDemo). Las cifras comparan métodos entre sí y frente al oráculo;");
        md.AppendLine("> no predicen delitos reales (plan, 14).");
        md.AppendLine();
        md.AppendLine($"- {d.Casos.Count} casos en {d.Semanas} semanas y {d.Celdas.Count} celdas de 0,005°. Se predicen las últimas {semanas} semanas,");
        md.AppendLine($"  bloque a bloque (4 h), entrenando con todo lo anterior y reentrenando cada {cada} semanas.");
        md.AppendLine("- Acierto: parte de los casos que cayó en el 10 % de celdas con más casos esperados. PAI = acierto ÷ 10 % (1 es azar).");
        md.AppendLine("- Fracción del oráculo: PAI del método ÷ PAI de la intensidad verdadera del generador.");
        md.AppendLine();
        md.AppendLine("| Método | Acierto (10 % del área) | PAI | Fracción del oráculo | Desviación de Poisson | Error absoluto |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in resultados.OrderBy(r => r.Metodo == "Oráculo" ? 1 : 0).ThenByDescending(r => r.Pai))
            md.AppendLine($"| {r.Metodo} | {P(r.Acierto)} | {r.Pai.ToString("0.00", Inv)} | {P(r.FraccionOraculo)} | {r.Desviacion.ToString("0.0000", Inv)} | {r.Error.ToString("0.0000", Inv)} |");
        md.AppendLine();
        md.AppendLine($"Se despliega **{elegido}** (peso del modelo en el conjunto: {peso.ToString("0.00", Inv)}), el de menor desviación de Poisson.");
        md.AppendLine();
        md.AppendLine("Foco emergente (CA06), puesto de su celda cada semana (1 = la más probable):");
        md.AppendLine();
        md.AppendLine("```json");
        md.AppendLine(JsonSerializer.Serialize(emergente, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        md.AppendLine("```");
        return md.ToString();
    }
}
