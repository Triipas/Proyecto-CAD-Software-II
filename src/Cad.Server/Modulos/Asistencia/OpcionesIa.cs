namespace Cad.Server.Modulos.Asistencia;

// Sección «Ia» de la configuración (plan de asistencia con IA, 5.8). Las claves de API nunca van en el repositorio: user-secrets
// en desarrollo y variables Ia__Gemini__ClaveApi en el entorno publicado (render.yaml, sync: false).
public sealed class OpcionesIa
{
    public const string Seccion = "Ia";

    // Interruptor de infraestructura: hay o no hay asistente. La admisión automática es otro interruptor (AjusteIa, en la base).
    public bool Habilitada { get; set; }

    // «Gemini» o «Groq» (modelos de lenguaje con nivel gratuito) o «Reglas» (palabras clave, sin red; demuestra el flujo sin clave).
    public string Proveedor { get; set; } = ProveedoresIa.Gemini;

    // Quién da la segunda opinión de la admisión automática (S6-T16). Vacío: el modelo preciso del mismo proveedor. Otro proveedor
    // hace las dos opiniones más independientes y reparte las cuotas gratuitas.
    public string? ProveedorVerificador { get; set; }

    public OpcionesGemini Gemini { get; set; } = new();
    public OpcionesGroq Groq { get; set; } = new();

    // S6-T09: decisiones anteriores parecidas que acompañan cada análisis (3 por parecido y el resto para equilibrar las decisiones).
    public int Ejemplos { get; set; } = 6;

    public int TiempoMaximoSegundos { get; set; } = 10;
    // La segunda opinión no apura a nadie (en «Activa» igual se espera el retraso): un modelo mayor puede tardar más. Medido el
    // 04/11/2026: Gemini 3.8 Flash tardó 9,5 s.
    public int TiempoMaximoVerificadorSegundos { get; set; } = 25;
    public int MaximoPorMinuto { get; set; } = 30;
    // Cortacircuitos: al agotarse, el asistente deja de llamar a la API hasta el día siguiente (UTC).
    public int PresupuestoDiarioTokens { get; set; } = 500_000;
    // Cambia cuando cambia el prompt: hay una sugerencia por reporte y versión, y se pueden comparar versiones.
    public string VersionPrompt { get; set; } = "2026-11-b";
    // Versión del documento de criterios de triaje (docs/criterios-triaje.md, S6-T01) con que se decidió.
    public string VersionCriterios { get; set; } = "borrador";
    public OpcionesDuplicados Duplicados { get; set; } = new();
    // Cada cuánto se buscan admisiones automáticas vencidas (S6-T16).
    public int IntervaloAdmisionSegundos { get; set; } = 5;
}

public static class ProveedoresIa
{
    public const string Gemini = "Gemini";
    public const string Groq = "Groq";
    public const string Reglas = "Reglas";
}

public sealed class OpcionesGemini
{
    public string? ClaveApi { get; set; }
    // Identificadores de la API confirmados en la documentación de Google AI Studio el 03/11/2026.
    public string ModeloRapido { get; set; } = "gemini-3.1-flash-lite";
    public string ModeloPreciso { get; set; } = "gemini-3.8-flash";
    public string UrlBase { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
}

// Groq (API compatible con OpenAI). Modelos con salida estructurada estricta según su documentación del 03/11/2026.
public sealed class OpcionesGroq
{
    public string? ClaveApi { get; set; }
    public string ModeloRapido { get; set; } = "openai/gpt-oss-20b";
    public string ModeloPreciso { get; set; } = "openai/gpt-oss-120b";
    public string UrlBase { get; set; } = "https://api.groq.com/openai/v1/";
    // «low», «medium» o «high» (modelos que razonan); vacío para no enviarlo.
    public string? EsfuerzoRazonamiento { get; set; } = "low";
}

// HU45: candidatos a duplicado, sin IA (≤ 150 m y ≤ 2 h, hasta 5).
public sealed class OpcionesDuplicados
{
    public int Metros { get; set; } = 150;
    public int Minutos { get; set; } = 120;
    public int Candidatos { get; set; } = 5;
}
