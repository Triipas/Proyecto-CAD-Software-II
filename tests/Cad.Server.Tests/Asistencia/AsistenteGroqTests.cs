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

// Adaptador de Groq (API compatible con OpenAI) con un HTTP simulado: las pruebas nunca llaman a la API.
public class AsistenteGroqTests
{
    private const string Clave = "clave-groq-de-prueba";

    private sealed class HttpSimulado(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(Uri Url, string? Autorizacion, JsonNode Cuerpo)> Solicitudes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
        {
            Solicitudes.Add((solicitud.RequestUri!, solicitud.Headers.Authorization?.ToString(), JsonNode.Parse(await solicitud.Content!.ReadAsStringAsync(ct))!));
            return responder(solicitud);
        }
    }

    private static HttpResponseMessage Respuesta(string confianza = "Alta", int entrada = 800, int salida = 90) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        role = "assistant",
                        content = JsonSerializer.Serialize(new
                        {
                            categoriaSugeridaId = DatosAsistencia.Robo, categoriaAlternativaId = (string?)null, confianza,
                            justificacion = "Describe un arrebato.", decision = "Admitir", motivoBorrador = "Hecho de seguridad ciudadana.",
                            destinoCodigo = (string?)null, accionBorrador = (string?)null, motivoDescarteCodigo = (string?)null, prioridad = "Media",
                            posibleUrgencia = false, motivoUrgencia = (string?)null, duplicadoDeId = (string?)null, senales = Array.Empty<string>(),
                        }),
                    },
                },
            },
            usage = new { prompt_tokens = entrada, completion_tokens = salida },
        }), Encoding.UTF8, "application/json"),
    };

    private static (AsistenteGroq Asistente, HttpSimulado Http) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = new HttpSimulado(responder);
        var opciones = Options.Create(new OpcionesIa
        {
            Habilitada = true,
            Proveedor = ProveedoresIa.Groq,
            Groq = new OpcionesGroq { ClaveApi = Clave, ModeloRapido = "rapido", ModeloPreciso = "preciso" },
        });
        return (new AsistenteGroq(new HttpClient(http), opciones, new ControlConsumoIa(TimeProvider.System), TimeProvider.System,
            NullLogger<AsistenteGroq>.Instance), http);
    }

    [Fact]
    public async Task Solicitud_ChatCompletionsConClaveBearerYEsquemaEstricto()
    {
        var (asistente, http) = Crear(_ => Respuesta());

        var resultado = await asistente.AnalizarAsync(DatosAsistencia.Entrada("Me arrebataron el celular"), CancellationToken.None);

        var (url, autorizacion, cuerpo) = Assert.Single(http.Solicitudes);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", url.ToString());
        Assert.Equal($"Bearer {Clave}", autorizacion);
        Assert.Equal("rapido", (string?)cuerpo["model"]);
        Assert.Equal("system", (string?)cuerpo["messages"]![0]!["role"]);
        Assert.Contains("<<<REPORTE", (string?)cuerpo["messages"]![1]!["content"]);
        Assert.Equal("json_schema", (string?)cuerpo["response_format"]!["type"]);
        Assert.True((bool)cuerpo["response_format"]!["json_schema"]!["strict"]!);
        Assert.False((bool)cuerpo["response_format"]!["json_schema"]!["schema"]!["additionalProperties"]!);
        Assert.Equal("low", (string?)cuerpo["reasoning_effort"]);

        Assert.Equal(("rapido", DecisionSugerida.Admitir, 800, 90),
            (resultado!.Modelo, resultado.Analisis.Decision, resultado.TokensEntrada, resultado.TokensSalida));
    }

    [Fact]
    public async Task ConfianzaMedia_EscalaAlModeloPreciso()
    {
        var (asistente, http) = Crear(s => (string?)JsonNode.Parse(s.Content!.ReadAsStringAsync().Result)!["model"] == "rapido"
            ? Respuesta(confianza: "Media") : Respuesta());

        var resultado = await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None);

        Assert.Equal(2, http.Solicitudes.Count);
        Assert.Equal(("preciso", true), (resultado!.Modelo, resultado.Escalada));
    }

    [Fact]
    public async Task Cuota429ConEsperaLarga_NoReintentaYNoHaySugerencia()
    {
        var (asistente, http) = Crear(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(60)) } });

        Assert.Null(await asistente.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None));
        Assert.Single(http.Solicitudes);
    }

    [Fact]
    public async Task VerificarAsync_UsaElModeloPrecisoConElPromptDelSegundoRevisor()
    {
        var (asistente, http) = Crear(_ => Respuesta());

        await asistente.VerificarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None);

        var (_, _, cuerpo) = Assert.Single(http.Solicitudes);
        Assert.Equal("preciso", (string?)cuerpo["model"]);
        Assert.Contains("segundo revisor independiente", (string?)cuerpo["messages"]![0]!["content"]);
    }

    [Fact]
    public async Task AsistenteConVerificador_AnalizaConUnoYVerificaConOtro()
    {
        var principal = new AsistenteFalso { Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(new(DatosAsistencia.Analisis(), "principal")) };
        var otro = new AsistenteFalso { Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(new(DatosAsistencia.Analisis(), "otro")) };
        var compuesto = new AsistenteConVerificador(principal, otro, TimeSpan.FromSeconds(5), NullLogger<AsistenteConVerificador>.Instance);

        Assert.Equal("principal", (await compuesto.AnalizarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None))!.Modelo);
        Assert.Equal("otro", (await compuesto.VerificarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None))!.Modelo);
        Assert.Empty(otro.Recibidas); // el verificador no analiza
    }

    [Theory]
    [InlineData("error")]
    [InlineData("tarde")]
    [InlineData("nulo")]
    public async Task AsistenteConVerificador_SiElOtroFallaOTarda_LaSegundaOpinionLaDaElPrincipal(string fallo)
    {
        var principal = new AsistenteFalso { Verificar = (_, _) => Task.FromResult<ResultadoAsistente?>(new(DatosAsistencia.Analisis(), "principal-preciso")) };
        var otro = new AsistenteFalso
        {
            Verificar = fallo switch
            {
                "error" => (_, _) => throw new HttpRequestException("503"),
                "tarde" => async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return null; },
                _ => (_, _) => Task.FromResult<ResultadoAsistente?>(null),
            },
        };
        var compuesto = new AsistenteConVerificador(principal, otro, TimeSpan.FromMilliseconds(300), NullLogger<AsistenteConVerificador>.Instance);

        var segunda = await compuesto.VerificarAsync(DatosAsistencia.Entrada("texto"), CancellationToken.None);

        Assert.Equal("principal-preciso", segunda!.Modelo);
    }
}
