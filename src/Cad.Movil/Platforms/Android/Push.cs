using Android.App;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using Cad.Movil.Servicios;
using Firebase;
using Firebase.Messaging;

namespace Cad.Movil.Platforms.Android;

// HU21-CA01 (ADR-07) — Firebase Cloud Messaging en Android.
public sealed class ServicioPushAndroid : IServicioPush
{
    // Debe coincidir con el canal que indica el servidor en la notificación (EnviadorPushFcm.CanalAndroid).
    public const string Canal = "despachos";
    private const string YaSolicitado = "cad.notificaciones.solicitadas";
    private static readonly TimeSpan EsperaToken = TimeSpan.FromSeconds(10);

    // El servicio de mensajes de FCM lo crea Android, no la inyección de dependencias: avisa por aquí.
    internal static event Action<string>? TokenNuevo;
    internal static void NotificarTokenNuevo(string token) => TokenNuevo?.Invoke(token);

    public event Action<string>? TokenRenovado
    {
        add => TokenNuevo += value;
        remove => TokenNuevo -= value;
    }

    // Firebase se inicializa solo si la app se compiló con google-services.json; además hace falta Google Play Services.
    public bool Disponible
    {
        get
        {
            var contexto = global::Android.App.Application.Context;
            return FirebaseApp.GetApps(contexto).Count > 0
                && GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(contexto) == ConnectionResult.Success;
        }
    }

    // Android 13+: permiso POST_NOTIFICATIONS. Antes, basta con que el usuario no haya desactivado las notificaciones.
    // Como con la ubicación, «Denied» sin haberlo pedido nunca significa «hay que explicarlo y pedirlo».
    public async Task<EstadoPermisoNotificaciones> ConsultarPermisoAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return NotificationManagerCompat.From(global::Android.App.Application.Context)!.AreNotificationsEnabled()
                ? EstadoPermisoNotificaciones.Concedido : EstadoPermisoNotificaciones.Denegado;

        var estado = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (estado == PermissionStatus.Granted)
            return EstadoPermisoNotificaciones.Concedido;
        if (!Preferences.Default.Get(YaSolicitado, false)
            || await MainThread.InvokeOnMainThreadAsync(Permissions.ShouldShowRationale<Permissions.PostNotifications>))
            return EstadoPermisoNotificaciones.Desconocido;
        return EstadoPermisoNotificaciones.Denegado;
    }

    public async Task<EstadoPermisoNotificaciones> SolicitarPermisoAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return await ConsultarPermisoAsync();
        Preferences.Default.Set(YaSolicitado, true);
        return await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>) == PermissionStatus.Granted
            ? EstadoPermisoNotificaciones.Concedido
            : EstadoPermisoNotificaciones.Denegado;
    }

    public async Task<string?> ObtenerTokenAsync(CancellationToken ct)
    {
        if (!Disponible)
            return null;
        try
        {
            // Firebase Messaging 25 marca getToken como obsoleto en favor de register() (identificador de instalación).
            // El servidor envía hoy al token de registro (EnviadorPushFcm): los dos lados deben migrar a la vez.
#pragma warning disable CS0618
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>().WaitAsync(EsperaToken, ct);
#pragma warning restore CS0618
            return token?.ToString();
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null; // sin red o sin Google Play Services: el despacho se consulta al abrir la app
        }
    }

    // Android 8+: las notificaciones necesitan un canal. Prioridad alta: es un aviso de despacho.
    public static void CrearCanal(Context contexto)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var canal = new NotificationChannel(Canal, "Despachos", NotificationImportance.High)
        {
            Description = "Avisos de casos asignados por la central",
        };
        ((NotificationManager)contexto.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(canal);
    }
}

// Recibe los mensajes de FCM. Con la app en segundo plano, Android muestra la notificación solo; con la app abierta
// llega aquí y se muestra igualmente (además, el canal en tiempo real ya actualiza la pantalla del sereno).
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ServicioMensajesFirebase : FirebaseMessagingService
{
    private static int siguienteId = 1000;

    // Obsoleto en Firebase Messaging 25 junto con getToken (ver ServicioPushAndroid.ObtenerTokenAsync).
    [Obsolete("Se mantiene mientras el servidor envíe al token de registro.")]
    public override void OnNewToken(string token) => ServicioPushAndroid.NotificarTokenNuevo(token);

    public override void OnMessageReceived(RemoteMessage mensaje)
    {
        if (mensaje.GetNotification() is not { } aviso)
            return;
        var abrir = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        abrir?.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendiente = PendingIntent.GetActivity(this, 0, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var constructor = new NotificationCompat.Builder(this, ServicioPushAndroid.Canal);
        constructor.SetSmallIcon(Resource.Mipmap.appicon);
        constructor.SetContentTitle(aviso.Title);
        constructor.SetContentText(aviso.Body);
        constructor.SetPriority(NotificationCompat.PriorityHigh);
        constructor.SetAutoCancel(true);
        constructor.SetContentIntent(pendiente);
        var notificacion = constructor.Build();
        try
        {
            NotificationManagerCompat.From(this)!.Notify(Interlocked.Increment(ref siguienteId), notificacion);
        }
        catch (Java.Lang.SecurityException)
        {
            // Sin permiso de notificaciones: el despacho se ve igualmente en la pantalla del sereno.
        }
    }
}
