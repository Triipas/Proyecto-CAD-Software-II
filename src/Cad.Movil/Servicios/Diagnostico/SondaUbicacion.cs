using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Cad.Movil.Servicios.Diagnostico;

// SPIKE01 (S1-T03) — sonda de diagnóstico, solo en compilaciones Debug. No es funcionalidad entregada.
// Cada muestra intenta capturar la posición actual y comprobar la transmisión al servidor, y registra en
// qué condiciones se hizo: plano de la app, red y permiso. La transmisión se comprueba con GET /salud:
// no se envían coordenadas porque todavía no hay un caso de uso que lo justifique (RNF07). Tampoco se
// registran las coordenadas, solo la precisión y la antigüedad de la lectura.

public enum PlanoApp { Primero, Segundo }

public enum ResultadoCaptura { Correcta, SinPosicion, Error, SinPermiso }

public interface IEstadoApp
{
    PlanoApp Plano { get; }
}

public interface IEstadoRed
{
    // Descripción breve: «Internet (WiFi)», «Sin conexión»…
    string Describir();
}

public sealed record ResultadoTransmision(bool Correcta, double LatenciaMs, string Detalle);

public interface IComprobadorServidor
{
    Task<ResultadoTransmision> ComprobarAsync(CancellationToken ct);
}

public interface IRegistroSonda
{
    string Ruta { get; }
    Task AgregarAsync(MuestraSonda muestra);
    void Borrar();
}

public sealed record MuestraSonda(
    DateTimeOffset Momento,
    PlanoApp Plano,
    string Red,
    EstadoPermisoUbicacion Permiso,
    ResultadoCaptura Captura,
    string? DetalleCaptura,
    double? PrecisionMetros,
    double? AntiguedadSegundos,
    double DuracionCapturaMs,
    ResultadoTransmision Transmision);

public sealed class SondaUbicacion(
    IServicioUbicacion ubicacion, IComprobadorServidor servidor, IEstadoApp app, IEstadoRed red, TimeProvider reloj)
{
    // Misma espera máxima que el SOS (DP11), para que los resultados sean comparables.
    public static readonly TimeSpan EsperaCaptura = TimeSpan.FromSeconds(10);

    public async Task<MuestraSonda> TomarMuestraAsync(CancellationToken ct)
    {
        var momento = reloj.GetUtcNow();
        var plano = app.Plano;
        var estadoRed = red.Describir();
        // Solo se consulta el permiso: la sonda nunca lo solicita (eso es HU06).
        var permiso = EstadoPermisoUbicacion.Desconocido;
        string? detalle = null;
        try
        {
            permiso = await ubicacion.ConsultarPermisoAsync();
        }
        catch (Exception e)
        {
            detalle = $"Permiso: {e.GetType().Name}";
        }

        var captura = ResultadoCaptura.SinPermiso;
        double? precision = null, antiguedad = null;
        var cronometro = Stopwatch.StartNew();
        if (permiso == EstadoPermisoUbicacion.Concedido)
        {
            try
            {
                var posicion = await ubicacion.ObtenerActualAsync(EsperaCaptura, ct).WaitAsync(EsperaCaptura, reloj, ct);
                captura = posicion is null ? ResultadoCaptura.SinPosicion : ResultadoCaptura.Correcta;
                precision = posicion?.PrecisionMetros;
                antiguedad = posicion is null ? null : (reloj.GetUtcNow() - posicion.ObtenidaEn).TotalSeconds;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                captura = ResultadoCaptura.Error;
                detalle = e.GetType().Name;
            }
        }
        cronometro.Stop();

        ResultadoTransmision transmision;
        try
        {
            transmision = await servidor.ComprobarAsync(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            transmision = new ResultadoTransmision(false, 0, $"Error ({e.GetType().Name})");
        }
        return new MuestraSonda(momento, plano, estadoRed, permiso, captura, detalle, precision, antiguedad,
            cronometro.Elapsed.TotalMilliseconds, transmision);
    }

    // Toma una muestra cada «intervalo» hasta que se cancela. Las muestras se entregan al llamador, que
    // las muestra y las guarda; un fallo en una muestra no detiene la sonda.
    public async Task EjecutarAsync(TimeSpan intervalo, Func<MuestraSonda, Task> alTomar, CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(intervalo, reloj);
        do
        {
            await alTomar(await TomarMuestraAsync(ct));
        }
        while (await temporizador.WaitForNextTickAsync(ct));
    }

    public const string CabeceraCsv =
        "momento_utc;plano;red;permiso;captura;detalle_captura;precision_m;antiguedad_s;duracion_captura_ms;transmision;latencia_ms;detalle_transmision";

    public static string ACsv(MuestraSonda m)
    {
        static string N(double? v) => v is null ? "" : v.Value.ToString("0.#", CultureInfo.InvariantCulture);
        static string T(string? s) => (s ?? "").Replace(';', ',');
        return new StringBuilder()
            .Append(m.Momento.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(';')
            .Append(m.Plano).Append(';')
            .Append(T(m.Red)).Append(';')
            .Append(m.Permiso).Append(';')
            .Append(m.Captura).Append(';')
            .Append(T(m.DetalleCaptura)).Append(';')
            .Append(N(m.PrecisionMetros)).Append(';')
            .Append(N(m.AntiguedadSegundos)).Append(';')
            .Append(N(m.DuracionCapturaMs)).Append(';')
            .Append(m.Transmision.Correcta ? "Correcta" : "Fallida").Append(';')
            .Append(N(m.Transmision.LatenciaMs)).Append(';')
            .Append(T(m.Transmision.Detalle))
            .ToString();
    }
}
