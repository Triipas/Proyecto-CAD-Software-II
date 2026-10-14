using System.Net;
using System.Text;
using System.Text.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;

namespace Cad.Movil.Tests;

// Cliente HTTP de la app: distingue «el servidor respondió» (no se reintenta) de «sin respuesta» (se reintenta).
public class ApiCadTests
{
    private sealed class ServidorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Peticion, string Cuerpo)> Recibidas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Recibidas.Add((peticion, peticion.Content is null ? "" : await peticion.Content.ReadAsStringAsync(ct)));
            return responder(peticion);
        }
    }

    private sealed class ConfiguracionFija(Uri? direccion) : IConfiguracionServidor
    {
        public Uri? Direccion => direccion;
        public string? Guardar(string direccion) => null;
    }

    private static readonly SolicitudEmisionSos Sos = new(Guid.NewGuid(), DateTimeOffset.UtcNow, null);

    private static HttpResponseMessage Json(HttpStatusCode estado, string json) =>
        new(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (ApiCad Api, ServidorFalso Servidor) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder,
        string direccion = "https://cad.ejemplo.test/")
    {
        var servidor = new ServidorFalso(responder);
        return (new ApiCad(new HttpClient(servidor), new ConfiguracionFija(new Uri(direccion))), servidor);
    }

    [Fact]
    public async Task EmitirSos_EnviaPostConBearerYElIdSolicitud()
    {
        var (api, servidor) = Crear(_ => Json(HttpStatusCode.Created,
            """{"id":"6f1d0f7e-1111-4c4c-9d9d-000000000001","estado":"Pendiente","registradoEn":"2026-10-13T09:00:00Z","ubicacionDisponible":false}"""));

        var resultado = await api.EmitirSosAsync("token-ficticio", Sos, CancellationToken.None);

        var correcto = Assert.IsType<ResultadoApi<RespuestaSos>.Correcto>(resultado);
        Assert.Equal("Pendiente", correcto.Valor.Estado);
        var (peticion, cuerpo) = Assert.Single(servidor.Recibidas);
        Assert.Equal(HttpMethod.Post, peticion.Method);
        Assert.Equal("https://cad.ejemplo.test/api/sos", peticion.RequestUri!.ToString());
        Assert.Equal("Bearer", peticion.Headers.Authorization!.Scheme);
        Assert.Equal("token-ficticio", peticion.Headers.Authorization.Parameter);
        Assert.Equal(Sos.IdSolicitud, JsonDocument.Parse(cuerpo).RootElement.GetProperty("idSolicitud").GetGuid());
    }

    [Fact]
    public async Task IniciarSesion_NoEnviaToken()
    {
        var (api, servidor) = Crear(_ => Json(HttpStatusCode.OK,
            """{"token":"t","expiraEn":"2026-10-13T17:00:00Z","rol":"Ciudadano"}"""));

        var resultado = await api.IniciarSesionAsync(new SolicitudInicioSesion("ana@ejemplo.test", "Clave-ficticia-1"), CancellationToken.None);

        Assert.Equal("Ciudadano", Assert.IsType<ResultadoApi<RespuestaToken>.Correcto>(resultado).Valor.Rol);
        Assert.Null(servidor.Recibidas.Single().Peticion.Headers.Authorization);
        Assert.EndsWith("/api/auth/token", servidor.Recibidas.Single().Peticion.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ErroresTransitorios_SonSinRespuesta_YSePuedenReintentar(HttpStatusCode estado)
    {
        var (api, _) = Crear(_ => new HttpResponseMessage(estado));

        Assert.IsType<ResultadoApi<RespuestaSos>.SinRespuesta>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
    }

    [Fact]
    public async Task SinConexion_EsSinRespuesta()
    {
        var (api, _) = Crear(_ => throw new HttpRequestException("conexión rechazada"));

        Assert.IsType<ResultadoApi<RespuestaSos>.SinRespuesta>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
    }

    [Theory]
    [InlineData(typeof(WebException))]
    [InlineData(typeof(IOException))]
    public async Task CambioDeRedEnAndroid_EsSinRespuesta(Type excepcion)
    {
        // SPIKE01: durante un cambio de red, el manejador de Android lanza estas excepciones.
        var (api, _) = Crear(_ => throw (Exception)Activator.CreateInstance(excepcion, "red cambiada")!);

        Assert.IsType<ResultadoApi<RespuestaSos>.SinRespuesta>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
    }

    [Fact]
    public async Task TiempoAgotado_EsSinRespuesta()
    {
        var (api, _) = Crear(_ => throw new TaskCanceledException("timeout"));

        Assert.IsType<ResultadoApi<RespuestaSos>.SinRespuesta>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
    }

    [Fact]
    public async Task ProblemDetails_SeConvierteEnRechazoConLosErroresPorCampo()
    {
        var (api, _) = Crear(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"Revisa los datos del formulario.","status":400,"errors":{"Correo":["Ya existe una cuenta con este correo."]}}"""));

        var resultado = await api.RegistrarAsync(
            new SolicitudRegistroCiudadano("Ana", "Pérez", "ana@ejemplo.test", "999000111", "Clave-ficticia-1"), CancellationToken.None);

        var rechazo = Assert.IsType<ResultadoApi<RespuestaRegistroCiudadano>.Rechazado>(resultado);
        Assert.Equal(400, rechazo.Estado);
        Assert.Equal("Revisa los datos del formulario.", rechazo.Mensaje);
        Assert.Equal(["Ya existe una cuenta con este correo."], rechazo.Errores["Correo"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Tu sesión no es válida. Vuelve a ingresar.")]
    [InlineData(HttpStatusCode.Forbidden, "Tu cuenta no tiene permiso para esta acción.")]
    public async Task RechazoSinCuerpo_UsaUnMensajeComprensible(HttpStatusCode estado, string mensaje)
    {
        var (api, _) = Crear(_ => new HttpResponseMessage(estado));

        var rechazo = Assert.IsType<ResultadoApi<RespuestaSos>.Rechazado>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
        Assert.Equal(mensaje, rechazo.Mensaje);
    }

    [Fact]
    public async Task SinDireccionConfigurada_NoLlamaAlServidor()
    {
        var servidor = new ServidorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var api = new ApiCad(new HttpClient(servidor), new ConfiguracionFija(null));

        Assert.IsType<ResultadoApi<RespuestaSos>.SinRespuesta>(await api.EmitirSosAsync("t", Sos, CancellationToken.None));
        Assert.Empty(servidor.Recibidas);
    }
}

// RNF09: la app solo habla con el servidor por HTTPS (salvo el servidor local de desarrollo).
public class ValidacionDireccionTests
{
    [Theory]
    [InlineData("https://cad-servidor.onrender.com", "https://cad-servidor.onrender.com/")]
    [InlineData("  https://cad.ejemplo.test/  ", "https://cad.ejemplo.test/")]
    [InlineData("http://10.0.2.2:5220", "http://10.0.2.2:5220/")]
    [InlineData("http://localhost:5220/", "http://localhost:5220/")]
    [InlineData("http://127.0.0.1:5220", "http://127.0.0.1:5220/")]
    public void DireccionesAceptadas(string entrada, string esperada)
    {
        Assert.Null(ValidacionDireccion.Validar(entrada, out var uri));
        Assert.Equal(esperada, uri!.ToString());
    }

    [Theory]
    [InlineData("http://cad.ejemplo.test")]
    [InlineData("http://192.168.1.20:5220")]
    [InlineData("ftp://cad.ejemplo.test")]
    [InlineData("cad.ejemplo.test")]
    [InlineData("")]
    public void DireccionesRechazadas(string entrada)
    {
        Assert.NotNull(ValidacionDireccion.Validar(entrada, out var uri));
        Assert.Null(uri);
    }
}
