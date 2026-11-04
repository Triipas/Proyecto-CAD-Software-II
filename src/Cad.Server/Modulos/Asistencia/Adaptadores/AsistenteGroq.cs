using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Adaptadores;

// Asistente con la API de Groq (nivel gratuito; decisión del 03/11/2026). Es compatible con la de OpenAI: chat/completions con
// salida estructurada estricta (response_format json_schema) y modelos abiertos (gpt-oss 20b y 120b, según la documentación de
// Groq del 03/11/2026). La misma cascada, verificación y controles que el de Gemini, con el mismo prompt (PromptAsistente).
public sealed class AsistenteGroq(HttpClient http, IOptions<OpcionesIa> opciones, ControlConsumoIa consumo, TimeProvider reloj,
    ILogger<AsistenteGroq> registro) : IAsistenteReportes, IModeloLenguaje
{
    private const int Reintentos = 2;
    // Espera máxima que se acepta del encabezado retry-after: más que eso agota el tiempo del análisis igual.
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(5);

    private OpcionesGroq Groq => opciones.Value.Groq;

    public bool Disponible => !string.IsNullOrWhiteSpace(Groq.ClaveApi);

    public async Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct)
    {
        var rapido = await LlamarAsync(Groq.ModeloRapido, PromptAsistente.Sistema(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);
        if (rapido is null || !PromptAsistente.DebeEscalar(rapido.Analisis))
            return rapido;

        var preciso = await LlamarAsync(Groq.ModeloPreciso, PromptAsistente.Sistema(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);
        return preciso is null
            ? rapido
            : preciso with { Escalada = true, TokensEntrada = rapido.TokensEntrada + preciso.TokensEntrada, TokensSalida = rapido.TokensSalida + preciso.TokensSalida };
    }

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) =>
        LlamarAsync(Groq.ModeloPreciso, PromptAsistente.Verificador(entrada.Catalogo), PromptAsistente.Usuario(entrada), ct);

    private async Task<ResultadoAsistente?> LlamarAsync(string modelo, string sistema, string usuario, CancellationToken ct)
    {
        if (await PedirAsync(modelo, sistema, usuario, "analisis_reporte", PromptAsistente.Esquema(), ct) is not { } respuesta)
            return null;
        var analisis = PromptAsistente.Leer(respuesta.Texto);
        if (analisis is null)
            registro.LogWarning("Asistente Groq: la respuesta de {Modelo} no se pudo leer.", modelo);
        return analisis is null ? null : new ResultadoAsistente(analisis, modelo, false, respuesta.TokensEntrada, respuesta.TokensSalida);
    }

    public Task<RespuestaModelo?> CompletarJsonAsync(string sistema, string usuario, JsonObject esquema, CancellationToken ct) =>
        PedirAsync(Groq.ModeloRapido, sistema, usuario, "respuesta", esquema, ct);

    private async Task<RespuestaModelo?> PedirAsync(string modelo, string sistema, string usuario, string nombreEsquema, JsonObject esquema, CancellationToken ct)
    {
        if (!consumo.Reservar(opciones.Value.MaximoPorMinuto, opciones.Value.PresupuestoDiarioTokens))
        {
            registro.LogWarning("Asistente Groq: cupo por minuto o presupuesto diario de tokens agotado; no se llama a {Modelo}.", modelo);
            return null;
        }

        var cuerpo = new JsonObject
        {
            ["model"] = modelo,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = sistema },
                new JsonObject { ["role"] = "user", ["content"] = usuario }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = nombreEsquema, ["strict"] = true, ["schema"] = esquema },
            },
            ["temperature"] = 0,
        };
        // Los modelos gpt-oss razonan antes de responder: con esfuerzo bajo responden antes y gastan menos tokens.
        if (!string.IsNullOrWhiteSpace(Groq.EsfuerzoRazonamiento))
            cuerpo["reasoning_effort"] = Groq.EsfuerzoRazonamiento;

        for (var intento = 0; ; intento++)
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Groq.UrlBase), "chat/completions"))
            {
                Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Groq.ClaveApi);
            using var respuesta = await http.SendAsync(solicitud, ct);

            if (respuesta.StatusCode == HttpStatusCode.TooManyRequests && intento < Reintentos)
            {
                var espera = respuesta.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, intento));
                if (espera > EsperaMaxima)
                {
                    registro.LogWarning("Asistente Groq: cuota agotada en {Modelo} (429) por {Segundos} s; no se reintenta.", modelo, espera.TotalSeconds);
                    return null;
                }
                registro.LogWarning("Asistente Groq: cuota agotada en {Modelo} (429); nuevo intento en {Segundos} s.", modelo, espera.TotalSeconds);
                await Task.Delay(espera, reloj, ct);
                continue;
            }
            if (!respuesta.IsSuccessStatusCode)
            {
                registro.LogWarning("Asistente Groq: {Modelo} respondió {Estado}.", modelo, (int)respuesta.StatusCode);
                return null;
            }

            var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
            var (entrada, salida) = Uso(json);
            consumo.Anotar(entrada + salida);
            return Texto(json) is { } texto ? new RespuestaModelo(texto, modelo, entrada, salida) : null;
        }
    }

    private static string? Texto(JsonElement json) =>
        json.TryGetProperty("choices", out var opciones) && opciones.GetArrayLength() > 0
        && opciones[0].TryGetProperty("message", out var mensaje) && mensaje.TryGetProperty("content", out var contenido)
            ? contenido.GetString()
            : null;

    private static (int Entrada, int Salida) Uso(JsonElement json)
    {
        if (!json.TryGetProperty("usage", out var uso))
            return (0, 0);
        int Leer(string nombre) => uso.TryGetProperty(nombre, out var v) && v.TryGetInt32(out var n) ? n : 0;
        return (Leer("prompt_tokens"), Leer("completion_tokens"));
    }
}
