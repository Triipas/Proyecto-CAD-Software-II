using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;

namespace Cad.Movil.Servicios;

// Dirección del servidor: configurable porque cambia entre el servidor local (emulador) y el entorno de validación.
public interface IConfiguracionServidor
{
    Uri? Direccion { get; }
    string? Guardar(string direccion); // devuelve un mensaje de error o null si es válida
}

// Cliente HTTP de la API del Servidor CAD. Todas las escrituras van por HTTPS (ADR-04, RNF09).
public sealed class ApiCad(HttpClient http, IConfiguracionServidor configuracion) : IApiCad
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ResultadoApi<RespuestaRegistroCiudadano>> RegistrarAsync(SolicitudRegistroCiudadano solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaRegistroCiudadano>("api/cuentas/registro", solicitud, null, ct);

    public Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaToken>("api/auth/token", solicitud, null, ct);

    public Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaSos>("api/sos", solicitud, token, ct);

    private async Task<ResultadoApi<T>> EnviarAsync<T>(string ruta, object cuerpo, string? token, CancellationToken ct)
    {
        if (configuracion.Direccion is not { } direccion)
            return new ResultadoApi<T>.SinRespuesta("Configura la dirección del servidor.");

        using var peticion = new HttpRequestMessage(HttpMethod.Post, new Uri(direccion, ruta))
        {
            Content = JsonContent.Create(cuerpo, options: Json),
        };
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.IsSuccessStatusCode)
                return new ResultadoApi<T>.Correcto((await respuesta.Content.ReadFromJsonAsync<T>(Json, ct))!);

            // Un error del servidor no registró nada (transacción): se puede reintentar sin duplicar.
            if ((int)respuesta.StatusCode >= 500 || respuesta.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                return new ResultadoApi<T>.SinRespuesta($"El servidor no pudo atender la solicitud ({(int)respuesta.StatusCode}).");

            var problema = await LeerProblemaAsync(respuesta, ct);
            return new ResultadoApi<T>.Rechazado((int)respuesta.StatusCode,
                problema?.Title ?? MensajePorEstado(respuesta.StatusCode),
                problema?.Errors ?? new Dictionary<string, string[]>());
        }
        catch (HttpRequestException)
        {
            return new ResultadoApi<T>.SinRespuesta("No hay conexión con el servidor.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoApi<T>.SinRespuesta("El servidor tardó demasiado en responder.");
        }
    }

    private static async Task<Problema?> LeerProblemaAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try { return await respuesta.Content.ReadFromJsonAsync<Problema>(Json, ct); }
        catch (Exception) { return null; }
    }

    private static string MensajePorEstado(HttpStatusCode estado) => estado switch
    {
        HttpStatusCode.Unauthorized => "Tu sesión no es válida. Vuelve a ingresar.",
        HttpStatusCode.Forbidden => "Tu cuenta no tiene permiso para esta acción.",
        _ => "El servidor rechazó la solicitud.",
    };

    private sealed record Problema(string? Title, Dictionary<string, string[]>? Errors);
}
