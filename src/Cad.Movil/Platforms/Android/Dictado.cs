using Android.App;
using Android.Content;
using Android.Speech;
using Cad.Movil.Servicios;

namespace Cad.Movil.Platforms.Android;

// Dictado con la pantalla de reconocimiento de voz del sistema (RecognizerIntent). No pide el permiso de micrófono a la app:
// lo gestiona el reconocedor, que devuelve solo el texto. Si el teléfono no tiene reconocedor, el botón no se muestra.
public sealed class DictadoAndroid : IDictado
{
    private const int CodigoSolicitud = 4201;
    private static TaskCompletionSource<string?>? pendiente;

    public bool Disponible =>
        Platform.CurrentActivity is { } actividad && Solicitud().ResolveActivity(actividad.PackageManager!) is not null;

    public Task<string?> DictarAsync()
    {
        if (Platform.CurrentActivity is not { } actividad)
            return Task.FromResult<string?>(null);
        pendiente?.TrySetResult(null);
        pendiente = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        actividad.StartActivityForResult(Solicitud(), CodigoSolicitud);
        return pendiente.Task;
    }

    // MainActivity reenvía aquí el resultado de la pantalla de dictado.
    public static void AlResultado(int codigo, Result resultado, Intent? datos)
    {
        if (codigo != CodigoSolicitud || pendiente is not { } tarea)
            return;
        pendiente = null;
        var textos = resultado == Result.Ok ? datos?.GetStringArrayListExtra(RecognizerIntent.ExtraResults) : null;
        tarea.TrySetResult(textos is { Count: > 0 } ? textos[0] : null);
    }

    private static Intent Solicitud()
    {
        var intencion = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intencion.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intencion.PutExtra(RecognizerIntent.ExtraLanguage, "es-PE");
        intencion.PutExtra(RecognizerIntent.ExtraPrompt, "Cuenta qué pasó");
        return intencion;
    }
}
