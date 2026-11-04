using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cad.Server.Tests.Asistencia;

// S6-T08 — adaptador de Gemini con un HTTP simulado: las pruebas nunca llaman a la API (plan, 11). Comprueba la forma de la
// solicitud (modelo, clave en la cabecera, salida estructurada), la lectura de la respuesta, la cascada al modelo preciso, los
// reintentos ante el 429 y los cortacircuitos (cupo por minuto y presupuesto diario).
public class AsistenteGeminiTests
{
    private const string Clave = "clave-de-prueba";

    private sealed class HttpSimulado(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(Uri Url, string? Clave, JsonNode Cuerpo)> Solicitudes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
        {
            var cuerpo = await solicitud.Content!.ReadAsStringAsync(ct);
            Solicitudes.Add((solicitud.RequestUri!, solicitud.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null, JsonNode.Parse(cuerpo)!));
            return responder(solicitud, cuerpo);
        }
    }

    private static string Respuesta(string confianza = "Alta", string decision = "Admitir", int entrada = 900, int salida = 120, string? texto = null)
    {
        texto ??= JsonSerializer.Serialize(new
        {
            categoriaSugeridaId = DatosAsistencia.Robo,
            categoriaAlternativaId = (string?)null,
            confianza,
            justificacion = "Describe un arrebato de celular.",
            decision,
            motivoBorrador = "Hecho de seguridad ciudadana con ubicación.",
            destinoCodigo = (string?)null,
            accionBorrador = (string?)null,
            motivoDescarteCodigo = (string?)null,
            prioridad = "Media",
            posibleUrgencia = false,
            motivoUrgencia = (string?)null,
            duplicadoDeId = (string?)null,
            senales = new[] { "CategoriaCiudadanoDudosa" },
        });
        return JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = texto } } }, finishReason = "STOP" } },
            usageMetadata = new { promptTokenCount = entrada, candidatesTokenCount = salida },
        });
    }

    private static HttpResponseMessage Json(string cuerpo) => new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (AsistenteGemini Asistente, HttpSimulado Http, ControlConsumoIa Consumo) Crear(Func<HttpRequestMessage, string, HttpResponseMessage> responder,
        int maximoPorMinuto = 30, int presupuesto = 500_000, ControlConsumoIa? consumo = null)
    {
        var http = new HttpSimulado(responder);
        var opciones = Options.Create(new OpcionesIa
        {
            Habilitada = true,
            MaximoPorMinuto = maximoPorMinuto,
            PresupuestoDiarioTokens = presupuesto,
            Gemini = new OpcionesGemini { ClaveApi = Clave, ModeloRapido = "modelo-rapido", ModeloPreciso = "modelo-preciso" },
        });
        consumo ??= new ControlConsumoIa(TimeProvider.System);
        return (new AsistenteGemini(new HttpClient(http), opciones, consumo, TimeProvider.System, NullLogger<AsistenteGemini>.Instance), http, consumo);
    }

    [Fact]
    public async Task Solicitud_UsaElModeloRapidoLaClaveEnCabeceraYSalidaEstructurada()
    {
        var (asistente, http, _) = Crear((_, _) => Json(Respuesta()));

        var resultado = await asistente.AnalizarAsync(DatosAsistencia.Entrada("Me arrebataron el celular"), CancellationToken.None);

        var (url, clave, cuerpo) = Assert.Single(http.Solicitudes);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/modelo-rapido:generateContent", url.ToString());
        Assert.Equal(Clave, clave);
        Assert.DoesNotContain(Clave, url.ToString()); // la clave no viaja en la URL
        Assert.Equal("application/json", (string?)cuerpo["generationConfig"]!["responseMimeType"]);
        Assert.Equal("object", (string?)cuerpo["generationConfig"]!["responseJsonSchema"]!["type"]);
        // El texto del ciudadano va delimitado como dato y el catálogo, en las instrucciones del sistema.
        Assert.Contains("<<<REPORTE", (string?)cuerpo["contents"]![0]!["parts"]![0]!["text"]);
        Assert.Contains(DatosAsistencia.Robo.ToString(), (string?)cuerpo["systemInstruction"]!["parts"]![0]!["text"]);

        Assert.NotNull(resultado);
        Assert.Equal((DatosAsistencia.Robo, DecisionSugerida.Admitir, NivelConfianza.Alta), (resultado.Analisis.CategoriaSugeridaId, resultado.Analisis.Decision, resultado.Analisis.Confianza));
        Assert.Equal(("modelo-rapido", false, 900, 120), (resultado.Modelo, resultado.Escalada, resultado.TokensEntrada, resultado.TokensSalida));
        Assert.Equal([SenalTriaje.CategoriaCiudadanoDudosa], resultado.Analisis.Senales);
    }

    [Fact]
    public async Task ConfianzaNoAlta_EscalaAlModeloPrecisoYSumaLosTokens()
    {
        var (asistente, http, _) = Crear((solicitud, _) => Json(solicitud.RequestUri!.ToString().Contains("modelo-rapido")
            ? Respuesta(confianza: "Media", entrada: 900, salida: 100)
            : Respuesta(confianza: "Alta", entrada: 1000, salida: 150)));

        var resultado = await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None);

        Assert.Equal(2, http.Solicitudes.Count);
        Assert.Equal(("modelo-preciso", true, 1900, 250, NivelConfianza.Alta),
            (resultado!.Modelo, resultado.Escalada, resultado.TokensEntrada, resultado.TokensSalida, resultado.Analisis.Confianza));
    }

    [Fact]
    public async Task Cuota429_ReintentaConEsperaYLuegoResponde()
    {
        var intentos = 0;
        var (asistente, http, _) = Crear((_, _) =>
        {
            if (++intentos == 1)
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.Zero) } };
            return Json(Respuesta());
        });

        var resultado = await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None);

        Assert.Equal(2, http.Solicitudes.Count);
        Assert.NotNull(resultado);
    }

    [Fact]
    public async Task ErrorDelServidorORespuestaIlegible_SinSugerencia()
    {
        var (conError, _, _) = Crear((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (ilegible, _, _) = Crear((_, _) => Json(Respuesta(texto: "esto no es JSON")));
        var (decisionInventada, _, _) = Crear((_, _) => Json(Respuesta(decision: "Ignorar")));

        Assert.Null(await conError.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Null(await ilegible.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Null(await decisionInventada.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
    }

    [Fact]
    public async Task PresupuestoDiarioAgotado_NoLlamaALaApi()
    {
        var (asistente, http, consumo) = Crear((_, _) => Json(Respuesta(entrada: 900, salida: 200)), presupuesto: 1000);

        Assert.NotNull(await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Equal(1100, consumo.TokensDelDia);
        Assert.Null(await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Single(http.Solicitudes);
    }

    [Fact]
    public async Task CupoPorMinutoAgotado_NoLlamaALaApi()
    {
        var (asistente, http, _) = Crear((_, _) => Json(Respuesta()), maximoPorMinuto: 1);

        Assert.NotNull(await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Null(await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Single(http.Solicitudes);
    }

    [Fact]
    public void SinClave_NoEstaDisponible()
    {
        var asistente = new AsistenteGemini(new HttpClient(), Options.Create(new OpcionesIa()), new ControlConsumoIa(TimeProvider.System),
            TimeProvider.System, NullLogger<AsistenteGemini>.Instance);

        Assert.False(asistente.Disponible);
    }
}
