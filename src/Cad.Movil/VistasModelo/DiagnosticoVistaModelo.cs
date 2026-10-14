using System.Collections.ObjectModel;
using System.Globalization;
using Cad.Movil.Servicios;
using Cad.Movil.Servicios.Diagnostico;

namespace Cad.Movil.VistasModelo;

// SPIKE01 (S1-T03): pantalla de diagnóstico, solo en compilaciones Debug. Toma una muestra cada pocos
// segundos mientras está activa, también si la app pasa a segundo plano, y resume por condición.
public sealed class DiagnosticoVistaModelo : VistaModeloBase
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);
    private const int MaximoEnPantalla = 200;

    private readonly SondaUbicacion sonda;
    private readonly IRegistroSonda registro;
    private readonly List<MuestraSonda> todas = [];
    private CancellationTokenSource? ejecucion;
    private Task? tarea;

    public DiagnosticoVistaModelo(SondaUbicacion sonda, IRegistroSonda registro)
    {
        (this.sonda, this.registro) = (sonda, registro);
        Iniciar = new ComandoAsincrono(IniciarAsync, () => !EnCurso);
        Detener = new ComandoAsincrono(DetenerAsync, () => EnCurso);
        Borrar = new ComandoAsincrono(BorrarAsync, () => !EnCurso);
    }

    public ComandoAsincrono Iniciar { get; }
    public ComandoAsincrono Detener { get; }
    public ComandoAsincrono Borrar { get; }

    public ObservableCollection<string> Muestras { get; } = [];
    public bool EnCurso => ejecucion is not null;
    public string RutaRegistro => registro.Ruta;

    // Resumen por plano: capturas correctas y transmisiones correctas sobre el total de muestras.
    public string Resumen => todas.Count == 0
        ? "Sin muestras."
        : string.Join(Environment.NewLine, todas.GroupBy(m => m.Plano).OrderBy(g => g.Key).Select(g =>
            $"{(g.Key == PlanoApp.Primero ? "Primer plano" : "Segundo plano")}: {g.Count()} muestras · " +
            $"ubicación {g.Count(m => m.Captura == ResultadoCaptura.Correcta)}/{g.Count()} · " +
            $"transmisión {g.Count(m => m.Transmision.Correcta)}/{g.Count()}"));

    private Task IniciarAsync()
    {
        ejecucion = new CancellationTokenSource();
        var ct = ejecucion.Token;
        tarea = Task.Run(() => sonda.EjecutarAsync(Intervalo, AnotarAsync, ct), ct);
        Actualizar();
        return Task.CompletedTask;
    }

    private async Task DetenerAsync()
    {
        if (ejecucion is null) return;
        await ejecucion.CancelAsync();
        try { await tarea!; } catch (OperationCanceledException) { }
        ejecucion.Dispose();
        (ejecucion, tarea) = (null, null);
        Actualizar();
    }

    private Task BorrarAsync()
    {
        registro.Borrar();
        todas.Clear();
        Muestras.Clear();
        Notificar(nameof(Resumen));
        return Task.CompletedTask;
    }

    // Las muestras se toman fuera del hilo de la interfaz; la colección se actualiza en el principal.
    private async Task AnotarAsync(MuestraSonda muestra)
    {
        await registro.AgregarAsync(muestra);
        await HiloPrincipal(() =>
        {
            todas.Add(muestra);
            Muestras.Insert(0, Describir(muestra));
            if (Muestras.Count > MaximoEnPantalla) Muestras.RemoveAt(Muestras.Count - 1);
            Notificar(nameof(Resumen));
        });
    }

    // Se puede sustituir en las pruebas (no hay hilo principal de MAUI en net10.0).
    public Func<Action, Task> HiloPrincipal { get; init; } = accion => MainThread.InvokeOnMainThreadAsync(accion);

    public static string Describir(MuestraSonda m)
    {
        var captura = m.Captura switch
        {
            ResultadoCaptura.Correcta => string.Create(CultureInfo.InvariantCulture,
                $"ubicación ± {m.PrecisionMetros:0} m, {m.AntiguedadSegundos:0.#} s de antigüedad"),
            ResultadoCaptura.SinPosicion => "sin posición",
            ResultadoCaptura.Error => $"error de ubicación ({m.DetalleCaptura})",
            _ => $"sin permiso ({m.Permiso})",
        };
        var transmision = m.Transmision.Correcta
            ? string.Create(CultureInfo.InvariantCulture, $"servidor OK en {m.Transmision.LatenciaMs:0} ms")
            : $"servidor sin respuesta ({m.Transmision.Detalle})";
        return $"{m.Momento.ToLocalTime():HH:mm:ss} · {(m.Plano == PlanoApp.Primero ? "1.er plano" : "2.º plano")} · {m.Red}\n{captura} · {transmision}";
    }

    private void Actualizar()
    {
        Notificar(nameof(EnCurso));
        Iniciar.ActualizarDisponibilidad();
        Detener.ActualizarDisponibilidad();
        Borrar.ActualizarDisponibilidad();
    }
}
