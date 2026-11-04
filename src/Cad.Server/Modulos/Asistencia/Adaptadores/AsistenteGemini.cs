using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Adaptadores;

// S6-T08 (plan de asistencia con IA, 5.3 a 5.5 y 5.9) — asistente con la API de Gemini, por REST (models.generateContent) con
// salida estructurada (responseJsonSchema). Cascada: el modelo rápido analiza todo y el preciso interviene si la confianza no es
// alta, si hay señales de urgencia o duplicado, o si la respuesta se contradice. La verificación (admisión automática) es una
// pasada independiente con el modelo preciso y otro prompt. Controles: cupo por minuto, presupuesto diario de tokens y reintentos
// con espera ante el error 429. Nunca registra el texto enviado ni la respuesta (RNF07).
public sealed class AsistenteGemini(HttpClient http, IOptions<OpcionesIa> opciones, ControlConsumoIa consumo, TimeProvider reloj,
    ILogger<AsistenteGemini> registro) : IAsistenteReportes
{
    private const int Reintentos = 2;

    private OpcionesGemini Gemini => opciones.Value.Gemini;

    public bool Disponible => !string.IsNullOrWhiteSpace(Gemini.ClaveApi);

    public async Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct)
    {
        var rapido = await LlamarAsync(Gemini.ModeloRapido, PromptAsistente.Sistema(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);
        if (rapido is null)
            return null;
        if (!PromptAsistente.DebeEscalar(rapido.Analisis))
            return rapido;

        var preciso = await LlamarAsync(Gemini.ModeloPreciso, PromptAsistente.Sistema(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);
        // Si el modelo preciso no responde, vale la primera opinión (con su confianza, que ya no es «Alta» o trae señales).
        return preciso is null
            ? rapido
            : preciso with { Escalada = true, TokensEntrada = rapido.TokensEntrada + preciso.TokensEntrada, TokensSalida = rapido.TokensSalida + preciso.TokensSalida };
    }

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) =>
        LlamarAsync(Gemini.ModeloPreciso, PromptAsistente.Verificador(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);

    private async Task<ResultadoAsistente?> LlamarAsync(string modelo, string sistema, string usuario, CancellationToken ct)
    {
        if (!consumo.Reservar(opciones.Value.MaximoPorMinuto, opciones.Value.PresupuestoDiarioTokens))
        {
            registro.LogWarning("Asistente Gemini: cupo por minuto o presupuesto diario de tokens agotado; no se llama a {Modelo}.", modelo);
            return null;
        }

        var cuerpo = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = sistema }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = usuario }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = PromptAsistente.Esquema(),
            },
        };

        for (var intento = 0; ; intento++)
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Gemini.UrlBase), $"models/{Uri.EscapeDataString(modelo)}:generateContent"))
            {
                Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            solicitud.Headers.Add("x-goog-api-key", Gemini.ClaveApi);
            using var respuesta = await http.SendAsync(solicitud, ct);

            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests && intento < Reintentos)
            {
                var espera = respuesta.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, intento));
                registro.LogWarning("Asistente Gemini: cuota agotada en {Modelo} (429); nuevo intento en {Segundos} s.", modelo, espera.TotalSeconds);
                await Task.Delay(espera, reloj, ct);
                continue;
            }
            if (!respuesta.IsSuccessStatusCode)
            {
                registro.LogWarning("Asistente Gemini: {Modelo} respondió {Estado}.", modelo, (int)respuesta.StatusCode);
                return null;
            }

            var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            var (entrada, salida) = Uso(json);
            consumo.Anotar(entrada + salida);
            var analisis = PromptAsistente.Leer(Texto(json));
            if (analisis is null)
                registro.LogWarning("Asistente Gemini: la respuesta de {Modelo} no se pudo leer.", modelo);
            return analisis is null ? null : new ResultadoAsistente(analisis, modelo, false, entrada, salida);
        }
    }

    // El texto generado: las partes de texto del primer candidato (sin las de razonamiento, si las hubiera).
    private static string? Texto(JsonElement json)
    {
        if (!json.TryGetProperty("candidates", out var candidatos) || candidatos.GetArrayLength() == 0
            || !candidatos[0].TryGetProperty("content", out var contenido) || !contenido.TryGetProperty("parts", out var partes))
            return null;
        var texto = new StringBuilder();
        foreach (var parte in partes.EnumerateArray())
        {
            if (parte.TryGetProperty("thought", out var pensamiento) && pensamiento.ValueKind == JsonValueKind.True)
                continue;
            if (parte.TryGetProperty("text", out var t))
                texto.Append(t.GetString());
        }
        return texto.Length == 0 ? null : texto.ToString();
    }

    private static (int Entrada, int Salida) Uso(JsonElement json)
    {
        if (!json.TryGetProperty("usageMetadata", out var uso))
            return (0, 0);
        int Leer(string nombre) => uso.TryGetProperty(nombre, out var v) && v.TryGetInt32(out var n) ? n : 0;
        return (Leer("promptTokenCount"), Leer("candidatesTokenCount") + Leer("thoughtsTokenCount"));
    }
}
