using System.Net;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.Servicios.Diagnostico;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// SPIKE01 (S1-T03): la sonda registra fielmente cada condición y nunca pide el permiso ni guarda coordenadas.
public class SondaUbicacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);

    private sealed class EstadoAppFalso : IEstadoApp { public PlanoApp Plano { get; set; } }
    private sealed class EstadoRedFalso : IEstadoRed { public string Red { get; set; } = "Internet (WiFi)"; public string Describir() => Red; }
    private sealed class ServidorFalso : IComprobadorServidor
    {
        public ResultadoTransmision Resultado { get; set; } = new(true, 42, "HTTP 200");
        public int Llamadas { get; private set; }
        public Task<ResultadoTransmision> ComprobarAsync(CancellationToken ct) { Llamadas++; return Task.FromResult(Resultado); }
    }

    private readonly UbicacionFalsa ubicacion = new();
    private readonly EstadoAppFalso app = new();
    private readonly EstadoRedFalso red = new();
    private readonly ServidorFalso servidor = new();
    private SondaUbicacion Sonda() => new(ubicacion, servidor, app, red, new RelojFijo(Ahora));

    [Fact]
    public async Task PrimerPlanoConPermiso_RegistraPrecisionAntiguedadYTransmision()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, Ahora.AddSeconds(-2));

        var m = await Sonda().TomarMuestraAsync(CancellationToken.None);

        Assert.Equal(PlanoApp.Primero, m.Plano);
        Assert.Equal(ResultadoCaptura.Correcta, m.Captura);
        Assert.Equal(8, m.PrecisionMetros);
        Assert.Equal(2, m.AntiguedadSegundos);
        Assert.True(m.Transmision.Correcta);
        Assert.Equal(SondaUbicacion.EsperaCaptura, Assert.Single(ubicacion.Lecturas));
    }

    [Fact]
    public async Task SegundoPlano_SeAnotaEnLaMuestra()
    {
        app.Plano = PlanoApp.Segundo;
        ubicacion.Falla = true; // p. ej., Android niega la ubicación «mientras se usa» con la app oculta

        var m = await Sonda().TomarMuestraAsync(CancellationToken.None);

        Assert.Equal(PlanoApp.Segundo, m.Plano);
        Assert.Equal(ResultadoCaptura.Error, m.Captura);
        Assert.Equal(nameof(InvalidOperationException), m.DetalleCaptura);
    }

    [Fact]
    public async Task UbicacionQueNoResponde_SeCortaALos10sYLaSondaSigue()
    {
        var relojSimulado = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Ahora);
        ubicacion.NoResponde = true;
        var sonda = new SondaUbicacion(ubicacion, servidor, app, red, relojSimulado);

        var muestra = sonda.TomarMuestraAsync(CancellationToken.None);
        relojSimulado.Advance(SondaUbicacion.EsperaCaptura);
        var m = await muestra.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ResultadoCaptura.Error, m.Captura);
        Assert.Equal(nameof(TimeoutException), m.DetalleCaptura);
        Assert.True(m.Transmision.Correcta);
    }

    [Fact]
    public async Task FalloAlComprobarElServidor_SeAnotaSinDetenerLaSonda()
    {
        var sonda = new SondaUbicacion(ubicacion, new ServidorQueFalla(), app, red, new RelojFijo(Ahora));

        var m = await sonda.TomarMuestraAsync(CancellationToken.None);

        Assert.False(m.Transmision.Correcta);
        Assert.Equal("Error (IOException)", m.Transmision.Detalle);
    }

    private sealed class ServidorQueFalla : IComprobadorServidor
    {
        public Task<ResultadoTransmision> ComprobarAsync(CancellationToken ct) => throw new IOException("red caída");
    }

    [Theory]
    [InlineData(EstadoPermisoUbicacion.Denegado)]
    [InlineData(EstadoPermisoUbicacion.Desconocido)]
    public async Task PermisoNoConcedido_NoLoPideNiLeeLaPosicion_PeroCompruebaElServidor(EstadoPermisoUbicacion permiso)
    {
        ubicacion.Permiso = permiso;

        var m = await Sonda().TomarMuestraAsync(CancellationToken.None);

        Assert.Equal(ResultadoCaptura.SinPermiso, m.Captura);
        Assert.Equal(permiso, m.Permiso);
        Assert.Equal(0, ubicacion.Solicitudes);
        Assert.Empty(ubicacion.Lecturas);
        Assert.Equal(1, servidor.Llamadas);
    }

    [Fact]
    public async Task SinConexion_LaCapturaSigueYLaTransmisionFalla()
    {
        red.Red = "None";
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 15, Ahora);
        servidor.Resultado = new ResultadoTransmision(false, 3, "Sin conexión (NameResolutionError)");

        var m = await Sonda().TomarMuestraAsync(CancellationToken.None);

        Assert.Equal("None", m.Red);
        Assert.Equal(ResultadoCaptura.Correcta, m.Captura);
        Assert.False(m.Transmision.Correcta);
    }

    [Fact]
    public async Task ElCsv_NoContieneCoordenadas()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, Ahora.AddSeconds(-1));
        var m = await Sonda().TomarMuestraAsync(CancellationToken.None);

        var linea = SondaUbicacion.ACsv(m);

        Assert.DoesNotContain("12.04", linea);
        Assert.DoesNotContain("77.04", linea);
        Assert.Equal(SondaUbicacion.CabeceraCsv.Split(';').Length, linea.Split(';').Length);
        Assert.StartsWith("2026-10-13T15:00:00.000Z;Primero;Internet (WiFi);Concedido;Correcta;;8;1;", linea);
    }

    [Fact]
    public async Task Ejecutar_TomaMuestrasPeriodicasHastaCancelar()
    {
        var sonda = new SondaUbicacion(ubicacion, servidor, app, red, TimeProvider.System);
        using var cancelacion = new CancellationTokenSource();
        var muestras = 0;

        var ejecucion = sonda.EjecutarAsync(TimeSpan.FromMilliseconds(20), _ =>
        {
            if (++muestras == 3) cancelacion.Cancel();
            return Task.CompletedTask;
        }, cancelacion.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ejecucion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, muestras);
    }
}

public class ComprobadorServidorHttpTests
{
    private sealed class Manejador(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Recibidas { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Recibidas.Add(peticion);
            return responder(peticion);
        }
    }

    private sealed class Configuracion(Uri? direccion) : IConfiguracionServidor
    {
        public Uri? Direccion => direccion;
        public string? Guardar(string direccion) => null;
    }

    [Fact]
    public async Task ConsultaSalud_SinEnviarDatos()
    {
        var manejador = new Manejador(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var comprobador = new ComprobadorServidorHttp(new HttpClient(manejador), new Configuracion(new Uri("http://localhost:5220/")));

        var resultado = await comprobador.ComprobarAsync(CancellationToken.None);

        Assert.True(resultado.Correcta);
        Assert.Equal("HTTP 200", resultado.Detalle);
        var peticion = Assert.Single(manejador.Recibidas);
        Assert.Equal(HttpMethod.Get, peticion.Method);
        Assert.Equal("http://localhost:5220/salud", peticion.RequestUri!.ToString());
        Assert.Null(peticion.Content);
    }

    [Fact]
    public async Task SinConexion_SeRegistraComoFallida()
    {
        var manejador = new Manejador(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "sin red"));
        var comprobador = new ComprobadorServidorHttp(new HttpClient(manejador), new Configuracion(new Uri("https://cad.ejemplo.test/")));

        var resultado = await comprobador.ComprobarAsync(CancellationToken.None);

        Assert.False(resultado.Correcta);
        Assert.Equal("Sin conexión (NameResolutionError)", resultado.Detalle);
    }

    [Fact]
    public async Task ServidorQueNoResponde_SeCortaPorTiempo()
    {
        var comprobador = new ComprobadorServidorHttp(new HttpClient(new CancelableHandler()), new Configuracion(new Uri("https://cad.ejemplo.test/")));

        var resultado = await comprobador.ComprobarAsync(CancellationToken.None);

        Assert.False(resultado.Correcta);
        Assert.Equal("Tiempo agotado", resultado.Detalle);
        Assert.InRange(resultado.LatenciaMs, ComprobadorServidorHttp.Espera.TotalMilliseconds - 100, 15_000);
    }

    // Responde solo cuando se cancela la petición, como un servidor que no contesta.
    private sealed class CancelableHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task SinDireccion_NoLlamaAlServidor()
    {
        var manejador = new Manejador(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var resultado = await new ComprobadorServidorHttp(new HttpClient(manejador), new Configuracion(null)).ComprobarAsync(CancellationToken.None);

        Assert.False(resultado.Correcta);
        Assert.Empty(manejador.Recibidas);
    }
}

public class DiagnosticoVistaModeloTests
{
    private sealed class RegistroEnMemoria : IRegistroSonda
    {
        public List<MuestraSonda> Lineas { get; } = [];
        public string Ruta => "memoria";
        public Task AgregarAsync(MuestraSonda muestra) { lock (Lineas) Lineas.Add(muestra); return Task.CompletedTask; }
        public void Borrar() => Lineas.Clear();
    }

    private sealed class EstadoAppFalso : IEstadoApp { public PlanoApp Plano { get; set; } }
    private sealed class EstadoRedFalso : IEstadoRed { public string Describir() => "Internet (WiFi)"; }
    private sealed class ServidorOk : IComprobadorServidor
    {
        public Task<ResultadoTransmision> ComprobarAsync(CancellationToken ct) => Task.FromResult(new ResultadoTransmision(true, 10, "HTTP 200"));
    }

    [Fact]
    public async Task IniciarYDetener_GuardaLasMuestrasYResumePorPlano()
    {
        var ubicacion = new UbicacionFalsa { Posicion = () => new UbicacionSos(-12.05, -77.04, 9, DateTimeOffset.UtcNow) };
        var app = new EstadoAppFalso();
        var registro = new RegistroEnMemoria();
        var vistaModelo = new DiagnosticoVistaModelo(
            new SondaUbicacion(ubicacion, new ServidorOk(), app, new EstadoRedFalso(), TimeProvider.System), registro)
        {
            HiloPrincipal = accion => { lock (registro.Lineas) accion(); return Task.CompletedTask; },
        };

        await vistaModelo.Iniciar.EjecutarAsync();
        Assert.True(vistaModelo.EnCurso);
        Assert.False(vistaModelo.Iniciar.CanExecute(null));
        Assert.True(await CentralDeEspera.HastaAsync(() => registro.Lineas.Count >= 1));
        await vistaModelo.Detener.EjecutarAsync();

        Assert.False(vistaModelo.EnCurso);
        Assert.Equal(registro.Lineas.Count, vistaModelo.Muestras.Count);
        Assert.StartsWith($"Primer plano: {registro.Lineas.Count} muestras · ubicación {registro.Lineas.Count}/", vistaModelo.Resumen);
    }

    [Fact]
    public void Describir_MuestraLaCondicionSinCoordenadas()
    {
        var muestra = new MuestraSonda(DateTimeOffset.UtcNow, PlanoApp.Segundo, "None", EstadoPermisoUbicacion.Concedido,
            ResultadoCaptura.Error, "PermissionException", null, null, 12, new ResultadoTransmision(false, 4, "Sin conexión (ConnectionError)"));

        var texto = DiagnosticoVistaModelo.Describir(muestra);

        Assert.Contains("2.º plano", texto);
        Assert.Contains("error de ubicación (PermissionException)", texto);
        Assert.Contains("servidor sin respuesta (Sin conexión (ConnectionError))", texto);
    }
}

internal static class CentralDeEspera
{
    public static async Task<bool> HastaAsync(Func<bool> condicion, int segundos = 5)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (condicion()) return true;
            await Task.Delay(20);
        }
        return condicion();
    }
}
