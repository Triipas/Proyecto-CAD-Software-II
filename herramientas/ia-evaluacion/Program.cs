using Cad.IaEvaluacion;

// Herramienta de evaluación de la asistencia con IA (plan de asistencia con IA, 4.6 y 9).
//   reportes  S6-T14: clasificación y triaje sugeridos frente a lo que decidieron los operadores.
//   generar, backtest, entrenar, exportar  S6-T24: mundos simulados y modelo de la proyección de focos (HU46).
var comando = args.FirstOrDefault();
var opciones = Opciones.Leer(args.Skip(1).ToArray());
return comando switch
{
    "reportes" => await EvaluacionReportes.EjecutarAsync(opciones),
    "generar" => Focos.Generar(opciones),
    "backtest" => Focos.Backtest(opciones),
    "entrenar" => Focos.Entrenar(opciones),
    "exportar" => Focos.Exportar(opciones),
    _ => Ayuda(),
};

static int Ayuda()
{
    Console.WriteLine("""
        Uso: dotnet run --project herramientas/ia-evaluacion -- <comando> [opciones]

          reportes   Evalúa las sugerencias de categoría y decisión con reportes ya decididos por operadores.
                     --conexion <archivo>   cadena de Npgsql de la base (por omisión, ConnectionStrings:Cad de los user-secrets)
                     --proveedor <nombre>   Groq, Gemini o Reglas (por omisión, Groq)
                     --muestra <n>          reportes a evaluar, repartidos entre admitidos, derivados y descartados (30)
                     --semilla <n>          semilla de la muestra (20261104)
                     --tpm <n>              tokens por minuto que no se superan, por la cuota gratuita (7000)
                     --salida <carpeta>     dónde escribir el informe .md y .json (herramientas/ia-evaluacion/resultados)

          generar    Genera un mundo simulado: --mundo A|B --semanas 78 --semilla <n> --salida datos/mundo-a
          backtest   Compara métodos en las últimas semanas: --entrada datos/mundo-a --semanas 12 --cada 4
          entrenar   Entrena el modelo final: --entrada datos/mundo-a --salida <modelo.zip> [--peso <0..1>]
          exportar   Copia el modelo al servidor: --modelo <modelo.zip> --destino src/Cad.Server/Recursos/modelo-focos.zip
        """);
    return 1;
}

namespace Cad.IaEvaluacion
{
    public sealed record Opciones(IReadOnlyDictionary<string, string> Valores)
    {
        public static Opciones Leer(string[] args)
        {
            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--"))
                    continue;
                var nombre = args[i][2..];
                valores[nombre] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            }
            return new Opciones(valores);
        }

        public string? Texto(string nombre) => Valores.TryGetValue(nombre, out var v) ? v : null;
        public int Entero(string nombre, int porDefecto) => int.TryParse(Texto(nombre), out var n) ? n : porDefecto;
    }
}
