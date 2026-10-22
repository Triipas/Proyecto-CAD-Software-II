using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace Cad.Movil;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Fondo claro de la app (tokens): iconos oscuros en la barra de estado para que sean legibles.
        if (Window is { } ventana && WindowCompat.GetInsetsController(ventana, ventana.DecorView) is { } barras)
            barras.AppearanceLightStatusBars = true;
    }

    // Dictado del reporte: la pantalla de reconocimiento de voz devuelve aquí el texto.
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Platforms.Android.DictadoAndroid.AlResultado(requestCode, resultCode, data);
    }
}
