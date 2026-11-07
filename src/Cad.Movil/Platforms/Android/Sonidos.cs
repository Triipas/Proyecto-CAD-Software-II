using Android.Media;
using Cad.Movil.Servicios;

namespace Cad.Movil.Platforms.Android;

// Sonidos cortos con SoundPool: se cargan una vez al crear el servicio y suenan sin demora, con el volumen de notificaciones
// (si el teléfono está en silencio, no suenan; el aviso en pantalla sigue).
public sealed class ReproductorSonidosAndroid : IReproductorSonidos
{
    private readonly SoundPool? sonidos;
    private readonly Dictionary<Sonido, int> cargados = [];

    public ReproductorSonidosAndroid()
    {
        try
        {
            var atributos = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Notification)!
                .SetContentType(AudioContentType.Sonification)!
                .Build();
            sonidos = new SoundPool.Builder().SetMaxStreams(2)!.SetAudioAttributes(atributos)!.Build()!;
            var recursos = global::Android.App.Application.Context.Assets!;
            foreach (var sonido in Enum.GetValues<Sonido>())
            {
                try
                {
                    using var archivo = recursos.OpenFd($"sonidos/{Archivo(sonido)}");
                    cargados[sonido] = sonidos.Load(archivo, 1);
                }
                catch (Exception)
                {
                    // Un sonido que no carga no deja sin los demás: ese solo queda en silencio.
                }
            }
        }
        catch (Exception)
        {
            sonidos = null; // sin sonidos en este equipo: la app funciona igual
        }
    }

    public void Reproducir(Sonido sonido)
    {
        if (sonidos is not null && cargados.TryGetValue(sonido, out var id))
            sonidos.Play(id, 1f, 1f, 1, 0, 1f);
    }

    private static string Archivo(Sonido sonido) => sonido switch
    {
        Sonido.NuevoDespacho => "nuevo-despacho.wav",
        Sonido.Llegaste => "llegaste.wav",
        Sonido.SosRegistrado => "sos-registrado.wav",
        Sonido.DespachoModificado => "despacho-modificado.mp3",
        Sonido.ConexionPerdida => "conexion-perdida.mp3",
        Sonido.ConexionRecuperada => "conexion-recuperada.mp3",
        Sonido.OperacionConfirmada => "operacion-confirmada.mp3",
        _ => "error.wav",
    };
}
