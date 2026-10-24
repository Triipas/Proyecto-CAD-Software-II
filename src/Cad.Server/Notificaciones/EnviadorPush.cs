using Cad.Server.Compartido;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Cad.Server.Notificaciones;

// ADR-07: la credencial de servidor de FCM llega solo por configuración (variable de entorno Push__CredencialFcm en
// Render, user-secrets en desarrollo). Nunca está en el repositorio.
public sealed class OpcionesPush
{
    public const string Seccion = "Push";

    // Contenido JSON de la cuenta de servicio de Firebase. Vacío = notificaciones push desactivadas.
    public string? CredencialFcm { get; set; }
}

// Envío por Firebase Cloud Messaging (HTTP v1, plan gratuito).
public sealed class EnviadorPushFcm : IEnviadorPush
{
    // Canal de notificaciones de la app Android (debe coincidir con el que crea la app).
    public const string CanalAndroid = "despachos-v2"; // 24/10/2026: canal con el sonido de nuevo despacho
    public const string Titulo = "Nuevo despacho";
    public const string Texto = "Tienes un caso asignado. Abre la app para ver los datos.";

    private readonly FirebaseMessaging mensajeria;
    private readonly ILogger<EnviadorPushFcm> registro;

    public EnviadorPushFcm(string credencialJson, ILogger<EnviadorPushFcm> registro)
    {
        var credencial = CredentialFactory.FromJson<ServiceAccountCredential>(credencialJson).ToGoogleCredential();
        var app = FirebaseApp.GetInstance(nameof(EnviadorPushFcm))
            ?? FirebaseApp.Create(new AppOptions { Credential = credencial }, nameof(EnviadorPushFcm));
        mensajeria = FirebaseMessaging.GetMessaging(app);
        this.registro = registro;
    }

    public bool Configurado => true;

    // Sin datos del caso (ADR-07, RNF12): solo el aviso y el tipo, para que la app abra la pantalla del sereno.
    public static Message CrearMensaje(string tokenDispositivo) => new()
    {
        // La app obtiene un token de registro de FCM (FirebaseMessaging.getToken en Android). FirebaseAdmin 3.7
        // propone dirigir por identificador de instalación (Fid); se revisará si la app pasa a obtener ese dato.
#pragma warning disable CS0618
        Token = tokenDispositivo,
#pragma warning restore CS0618
        Notification = new Notification { Title = Titulo, Body = Texto },
        Data = new Dictionary<string, string> { ["tipo"] = "despacho" },
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            Notification = new AndroidNotification { ChannelId = CanalAndroid },
        },
    };

    public async Task<ResultadoPush> EnviarDespachoAsync(string tokenDispositivo, CancellationToken ct)
    {
        try
        {
            await mensajeria.SendAsync(CrearMensaje(tokenDispositivo), ct);
            return ResultadoPush.Enviada;
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
        {
            return ResultadoPush.TokenInvalido;
        }
        catch (FirebaseMessagingException ex)
        {
            // Solo el código de error: el mensaje de FCM podría incluir el token.
            registro.LogWarning("FCM rechazó la notificación del despacho ({Codigo}).", ex.MessagingErrorCode?.ToString() ?? ex.ErrorCode.ToString());
            return ResultadoPush.NoEnviada;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            registro.LogWarning("No se pudo contactar con FCM para notificar el despacho ({Tipo}).", ex.GetType().Name);
            return ResultadoPush.NoEnviada;
        }
    }
}

// Sin credencial configurada: no se envía nada. El despacho sigue siendo consultable en la app (ADR-07, punto 4).
public sealed class EnviadorPushDesactivado : IEnviadorPush
{
    public bool Configurado => false;

    public Task<ResultadoPush> EnviarDespachoAsync(string tokenDispositivo, CancellationToken ct) =>
        Task.FromResult(ResultadoPush.NoEnviada);
}
