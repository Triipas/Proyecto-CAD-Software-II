using System.Diagnostics;

namespace Cad.Movil.Servicios.Diagnostico;

// SPIKE01: comprueba la transmisión con una petición real al servidor configurado (GET /salud: pública,
// sin datos ni coordenadas). Mide la latencia y distingue «respondió» de «sin red» y «tiempo agotado».
public sealed class ComprobadorServidorHttp(HttpClient http, IConfiguracionServidor configuracion) : IComprobadorServidor
{
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    public async Task<ResultadoTransmision> ComprobarAsync(CancellationToken ct)
    {
        if (configuracion.Direccion is not { } direccion)
            return new ResultadoTransmision(false, 0, "Sin dirección del servidor");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(Espera);
        var cronometro = Stopwatch.StartNew();
        try
        {
            using var respuesta = await http.GetAsync(new Uri(direccion, "salud"), limite.Token);
            return new ResultadoTransmision(respuesta.IsSuccessStatusCode, cronometro.Elapsed.TotalMilliseconds,
                $"HTTP {(int)respuesta.StatusCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoTransmision(false, cronometro.Elapsed.TotalMilliseconds, "Tiempo agotado");
        }
        catch (HttpRequestException e)
        {
            return new ResultadoTransmision(false, cronometro.Elapsed.TotalMilliseconds, $"Sin conexión ({e.HttpRequestError})");
        }
    }
}
