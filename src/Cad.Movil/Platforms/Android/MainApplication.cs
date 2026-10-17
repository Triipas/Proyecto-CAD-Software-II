using Android.App;
using Android.Runtime;

namespace Cad.Movil;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override void OnCreate()
	{
		base.OnCreate();
		// HU21 (ADR-07): canal de las notificaciones del despacho (Android 8+).
		Platforms.Android.ServicioPushAndroid.CrearCanal(this);
		// DP13: canal del aviso permanente del acompañamiento.
		Platforms.Android.SegundoPlanoAndroid.CrearCanal(this);
	}
}
