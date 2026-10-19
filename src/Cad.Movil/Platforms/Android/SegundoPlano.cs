using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Cad.Movil.Servicios;

namespace Cad.Movil.Platforms.Android;

// DP13 (decisión del PO del 16/10/2026): el acompañamiento transmite con o sin la app abierta, solo con conexión. Android
// solo permite leer la ubicación con la app fuera de la pantalla desde un servicio en primer plano de tipo «ubicación»,
// con una notificación visible mientras dura. Como lo inicia el ciudadano con la app en uso, basta el permiso de ubicación
// «mientras se usa la app» (no se pide ubicación en segundo plano). El envío lo sigue haciendo ServicioAcompanamiento: este
// servicio solo mantiene vivo el proceso (y la CPU, con un bloqueo parcial) hasta que el acompañamiento termina.
public sealed class SegundoPlanoAndroid : IServicioSegundoPlano
{
    public void Iniciar(DateTimeOffset venceEn)
    {
        var contexto = global::Android.App.Application.Context;
        var intencion = new Intent(contexto, typeof(ServicioUbicacionAcompanamiento));
        intencion.PutExtra(ServicioUbicacionAcompanamiento.ExtraVence, venceEn.ToUnixTimeMilliseconds());
        try
        {
            ContextCompat.StartForegroundService(contexto, intencion);
        }
        catch (Java.Lang.Exception)
        {
            // Android no permite iniciarlo (p. ej., la app ya no está en primer plano): se sigue transmitiendo mientras la
            // app esté abierta, como antes de DP13.
        }
    }

    public void Detener()
    {
        var contexto = global::Android.App.Application.Context;
        contexto.StopService(new Intent(contexto, typeof(ServicioUbicacionAcompanamiento)));
    }

    // Android 8+: canal de prioridad baja (sin sonido): es un aviso permanente, no una alerta.
    public static void CrearCanal(Context contexto)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var canal = new NotificationChannel(ServicioUbicacionAcompanamiento.Canal, "Acompañamiento", NotificationImportance.Low)
        {
            Description = "Aviso permanente mientras compartes tu ubicación con la central",
        };
        ((NotificationManager)contexto.GetSystemService(Context.NotificationService)!).CreateNotificationChannel(canal);
    }
}

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public sealed class ServicioUbicacionAcompanamiento : Service
{
    public const string Canal = "acompanamiento";
    public const string ExtraVence = "cad.acompanamiento.vence";
    private const int IdNotificacion = 3001;
    private PowerManager.WakeLock? bloqueo;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var vence = DateTimeOffset.FromUnixTimeMilliseconds(intent?.GetLongExtra(ExtraVence, 0) ?? 0).ToLocalTime();
        var abrir = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        abrir?.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendiente = PendingIntent.GetActivity(this, 0, abrir, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var constructor = new NotificationCompat.Builder(this, Canal);
        constructor.SetSmallIcon(Resource.Mipmap.appicon);
        constructor.SetContentTitle("Acompañamiento activo");
        constructor.SetContentText($"Compartes tu ubicación con la central hasta las {vence.ToString("HH:mm", CultureInfo.InvariantCulture)}. Toca para abrir.");
        constructor.SetOngoing(true);
        constructor.SetOnlyAlertOnce(true);
        constructor.SetCategory(NotificationCompat.CategoryService);
        constructor.SetContentIntent(pendiente);
        var notificacion = constructor.Build()!;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(IdNotificacion, notificacion, ForegroundService.TypeLocation);
        else
            StartForeground(IdNotificacion, notificacion);

        // La CPU sigue despierta con la pantalla apagada mientras dura la sesión (como máximo 31 minutos).
        if (bloqueo is null && GetSystemService(PowerService) is PowerManager energia)
        {
            bloqueo = energia.NewWakeLock(WakeLockFlags.Partial, "cad:acompanamiento");
            bloqueo?.Acquire((long)TimeSpan.FromMinutes(31).TotalMilliseconds);
        }
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (bloqueo is { IsHeld: true })
            bloqueo.Release();
        bloqueo = null;
        base.OnDestroy();
    }
}
