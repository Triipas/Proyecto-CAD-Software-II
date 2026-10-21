using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// ADR-08 (DP23): opciones del envío de códigos al celular del ciudadano.
public sealed class OpcionesCodigos
{
    public const string Seccion = "Codigos";

    // Proveedor simulado: el servidor genera el código y no envía ningún SMS. Solo en desarrollo y pruebas.
    public bool PermitirSimulado { get; set; }
    // Con el simulado, la respuesta incluye el código para que la app Debug lo muestre (nunca en el entorno publicado).
    public bool MostrarCodigoSimulado { get; set; }
    // Números que siempre usan el simulado aunque Twilio esté configurado (p. ej. «90000» para el emulador).
    public string[] PrefijosSimulados { get; set; } = [];

    public int VigenciaMinutos { get; set; } = 10;
    public int MaximoIntentos { get; set; } = 5;
    public int EsperaReenvioSegundos { get; set; } = 30;
    public int MaximoEnviosPorHora { get; set; } = 5;
    public int VigenciaTokenMinutos { get; set; } = 10;
    // Límite por dirección IP para todas las rutas de /api/auth/celular.
    public int SolicitudesPorMinutoIp { get; set; } = 30;

    public OpcionesTwilio Twilio { get; set; } = new();
}

// Credenciales de Twilio Verify: nunca en el repositorio (user-secrets o Codigos__Twilio__*).
public sealed class OpcionesTwilio
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string ServiceSid { get; set; } = "";

    public bool Configurado => AccountSid.Length > 0 && AuthToken.Length > 0 && ServiceSid.Length > 0;
}

// SMS real: envía y comprueba el código de un celular en formato E.164 (+51…).
public interface IVerificacionSms
{
    bool Configurada { get; }
    Task<bool> EnviarAsync(string celularE164, CancellationToken ct);
    Task<bool> ComprobarAsync(string celularE164, string codigo, CancellationToken ct);
}

// Twilio Verify (API REST v2): Twilio genera el código, lo envía por SMS y lo valida.
public sealed class VerificacionSmsTwilio(HttpClient http, IOptions<OpcionesCodigos> opciones, ILogger<VerificacionSmsTwilio> registro)
    : IVerificacionSms
{
    private OpcionesTwilio Twilio => opciones.Value.Twilio;

    public bool Configurada => Twilio.Configurado;

    public async Task<bool> EnviarAsync(string celularE164, CancellationToken ct)
    {
        using var respuesta = await PublicarAsync("Verifications",
            [new("To", celularE164), new("Channel", "sms"), new("Locale", "es")], ct);
        if (respuesta.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            return true;
        // Solo el estado: el número es un dato personal y no se registra.
        registro.LogWarning("Twilio Verify rechazó el envío del código (HTTP {Estado}).", (int)respuesta.StatusCode);
        return false;
    }

    public async Task<bool> ComprobarAsync(string celularE164, string codigo, CancellationToken ct)
    {
        using var respuesta = await PublicarAsync("VerificationCheck", [new("To", celularE164), new("Code", codigo)], ct);
        // 404: la verificación expiró, ya se aprobó o agotó sus intentos en Twilio.
        if (respuesta.StatusCode != HttpStatusCode.OK)
            return false;
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("status", out var estado) && estado.GetString() == "approved";
    }

    private async Task<HttpResponseMessage> PublicarAsync(string recurso, KeyValuePair<string, string>[] campos, CancellationToken ct)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post,
            $"https://verify.twilio.com/v2/Services/{Uri.EscapeDataString(Twilio.ServiceSid)}/{recurso}")
        {
            Content = new FormUrlEncodedContent(campos),
        };
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Twilio.AccountSid}:{Twilio.AuthToken}")));
        return await http.SendAsync(solicitud, ct);
    }
}
