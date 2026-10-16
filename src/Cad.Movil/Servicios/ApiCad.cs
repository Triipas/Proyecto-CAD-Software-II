using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
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
        EnviarAsync<RespuestaRegistroCiudadano>(HttpMethod.Post, "api/cuentas/registro", solicitud, null, ct);

    public Task<ResultadoApi<RespuestaToken>> IniciarSesionAsync(SolicitudInicioSesion solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaToken>(HttpMethod.Post, "api/auth/token", solicitud, null, ct);

    public Task<ResultadoApi<RespuestaSos>> EmitirSosAsync(string token, SolicitudEmisionSos solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaSos>(HttpMethod.Post, "api/sos", solicitud, token, ct);

    public Task<ResultadoApi<RespuestaReporte>> RegistrarReporteAsync(string token, SolicitudRegistroReporte solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaReporte>(HttpMethod.Post, "api/reportes", solicitud, token, ct);

    public Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasReporteAsync(string token, CancellationToken ct) =>
        EnviarAsync<List<CategoriaCatalogo>>(HttpMethod.Get, "api/reportes/categorias", null, token, ct);

    public Task<ResultadoApi<List<MiCasoResumen>>> ConsultarMisCasosAsync(string token, CancellationToken ct) =>
        EnviarAsync<List<MiCasoResumen>>(HttpMethod.Get, "api/casos/mios", null, token, ct);

    public Task<ResultadoApi<MiCasoDetalle>> ConsultarMiCasoAsync(string token, Guid casoId, CancellationToken ct) =>
        EnviarAsync<MiCasoDetalle>(HttpMethod.Get, $"api/casos/mios/{casoId}", null, token, ct);

    public Task<ResultadoApi<BandejaNotificaciones>> ConsultarNotificacionesAsync(string token, CancellationToken ct) =>
        EnviarAsync<BandejaNotificaciones>(HttpMethod.Get, "api/notificaciones", null, token, ct);

    public Task<ResultadoApi<SinContenido>> MarcarNotificacionLeidaAsync(string token, Guid notificacionId, CancellationToken ct) =>
        EnviarAsync<SinContenido>(HttpMethod.Post, $"api/notificaciones/{notificacionId}/leida", null, token, ct);

    public Task<ResultadoApi<SesionAcompanamientoVista>> IniciarAcompanamientoAsync(string token, SolicitudInicioAcompanamiento solicitud, CancellationToken ct) =>
        EnviarAsync<SesionAcompanamientoVista>(HttpMethod.Post, "api/acompanamiento", solicitud, token, ct);

    public Task<ResultadoApi<SesionAcompanamientoVista?>> ConsultarAcompanamientoActualAsync(string token, CancellationToken ct) =>
        EnviarAsync<SesionAcompanamientoVista?>(HttpMethod.Get, "api/acompanamiento/actual", null, token, ct);

    public Task<ResultadoApi<SesionAcompanamientoVista>> ConsultarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct) =>
        EnviarAsync<SesionAcompanamientoVista>(HttpMethod.Get, $"api/acompanamiento/{sesionId}", null, token, ct);

    public Task<ResultadoApi<SinContenido>> EnviarPosicionAcompanamientoAsync(string token, Guid sesionId, PosicionAcompanamientoEnviada posicion, CancellationToken ct) =>
        EnviarAsync<SinContenido>(HttpMethod.Post, $"api/acompanamiento/{sesionId}/posiciones", posicion, token, ct);

    public Task<ResultadoApi<SesionAcompanamientoVista>> FinalizarAcompanamientoAsync(string token, Guid sesionId, CancellationToken ct) =>
        EnviarAsync<SesionAcompanamientoVista>(HttpMethod.Post, $"api/acompanamiento/{sesionId}/finalizacion", null, token, ct);

    public Task<ResultadoApi<RespuestaEstadoOperativo>> ConsultarEstadoOperativoAsync(string token, CancellationToken ct) =>
        EnviarAsync<RespuestaEstadoOperativo>(HttpMethod.Get, "api/sereno/estado", null, token, ct);

    public Task<ResultadoApi<RespuestaEstadoOperativo>> CambiarEstadoOperativoAsync(string token, SolicitudCambioEstadoOperativo solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaEstadoOperativo>(HttpMethod.Put, "api/sereno/estado", solicitud, token, ct);

    public Task<ResultadoApi<SinContenido>> EnviarPosicionSerenoAsync(string token, PosicionSerenoEnviada posicion, CancellationToken ct) =>
        EnviarAsync<SinContenido>(HttpMethod.Post, "api/sereno/posiciones", posicion, token, ct);

    public Task<ResultadoApi<DespachoSereno?>> ConsultarDespachoAsync(string token, CancellationToken ct) =>
        EnviarAsync<DespachoSereno?>(HttpMethod.Get, "api/sereno/despacho", null, token, ct);

    public Task<ResultadoApi<DespachoSereno?>> ConfirmarDesplazamientoAsync(string token, Guid casoId, CancellationToken ct) =>
        EnviarAsync<DespachoSereno?>(HttpMethod.Post, $"api/sereno/casos/{casoId}/desplazamiento", null, token, ct);

    public Task<ResultadoApi<DespachoSereno?>> IniciarAtencionAsync(string token, Guid casoId, CancellationToken ct) =>
        EnviarAsync<DespachoSereno?>(HttpMethod.Post, $"api/sereno/casos/{casoId}/atencion", null, token, ct);

    public Task<ResultadoApi<RespuestaCierreCaso>> CerrarCasoAsync(string token, Guid casoId, SolicitudCierreCaso solicitud, CancellationToken ct) =>
        EnviarAsync<RespuestaCierreCaso>(HttpMethod.Post, $"api/sereno/casos/{casoId}/cierre", solicitud, token, ct);

    public Task<ResultadoApi<List<CategoriaCatalogo>>> ConsultarCategoriasAsync(string token, CancellationToken ct) =>
        EnviarAsync<List<CategoriaCatalogo>>(HttpMethod.Get, "api/sereno/categorias", null, token, ct);

    public Task<ResultadoApi<SinContenido>> RegistrarDispositivoAsync(string token, SolicitudRegistroDispositivo solicitud, CancellationToken ct) =>
        EnviarAsync<SinContenido>(HttpMethod.Put, "api/sereno/dispositivo", solicitud, token, ct);

    public Task<ResultadoApi<SinContenido>> QuitarDispositivoAsync(string token, CancellationToken ct) =>
        EnviarAsync<SinContenido>(HttpMethod.Delete, "api/sereno/dispositivo", null, token, ct);

    private async Task<ResultadoApi<T>> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo, string? token, CancellationToken ct)
    {
        if (configuracion.Direccion is not { } direccion)
            return new ResultadoApi<T>.SinRespuesta("Configura la dirección del servidor.");

        using var peticion = new HttpRequestMessage(metodo, new Uri(direccion, ruta))
        {
            Content = cuerpo is null ? null : JsonContent.Create(cuerpo, options: Json),
        };
        if (token is not null)
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.IsSuccessStatusCode)
                return new ResultadoApi<T>.Correcto(typeof(T) == typeof(SinContenido)
                    ? (T)(object)new SinContenido()
                    : respuesta.StatusCode == HttpStatusCode.NoContent
                        ? default! // p. ej., un sereno sin despacho (HU21)
                        : (await respuesta.Content.ReadFromJsonAsync<T>(Json, ct))!);

            // Un error del servidor no registró nada (transacción): se puede reintentar sin duplicar.
            if ((int)respuesta.StatusCode >= 500 || respuesta.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                return new ResultadoApi<T>.SinRespuesta($"El servidor no pudo atender la solicitud ({(int)respuesta.StatusCode}).");

            var problema = await LeerProblemaAsync(respuesta, ct);
            return new ResultadoApi<T>.Rechazado((int)respuesta.StatusCode,
                problema?.Title ?? MensajePorEstado(respuesta.StatusCode),
                problema?.Errors ?? new Dictionary<string, string[]>());
        }
        // En Android, un cambio de red durante la petición llega como WebException o IOException, no siempre
        // como HttpRequestException (observado en SPIKE01): todas son «sin respuesta» y se pueden reintentar.
        catch (Exception e) when (e is HttpRequestException or WebException or IOException)
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
