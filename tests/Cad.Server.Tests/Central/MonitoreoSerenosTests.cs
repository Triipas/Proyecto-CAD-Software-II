using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Central;

// HU19 — Monitorear el estado y la disponibilidad de los serenos (S2-T04), y HU18-CA04 (efecto en la
// disponibilidad que ve el operador). Coordenadas ficticias. La parte de CA02/CA03 que depende de las
// asignaciones y los cierres se verifica con HU20 y HU24.
[Collection(ColeccionServidor.Nombre)]
public class MonitoreoSerenosTests(FabricaCad fabrica)
{
    private const string Fragmento = "/panel/central?handler=Serenos";

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        return (await fabrica.CrearClienteAppAsync(cuenta), cuenta);
    }

    private static Task<HttpResponseMessage> CambiarAsync(HttpClient app, bool operativo) =>
        app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(operativo, true));

    private static Task<HttpResponseMessage> PosicionAsync(HttpClient app, double lat, double lon) =>
        app.PostAsJsonAsync("/api/sereno/posiciones", new PosicionSerenoEnviada(lat, lon, 7, DateTimeOffset.UtcNow));

    private async Task<HttpClient> OperadorAsync()
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        return navegador;
    }

    // El elemento <li> del sereno en el fragmento.
    private static string Elemento(string html, Guid serenoId)
    {
        var inicio = html.IndexOf($"data-sereno=\"{serenoId}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"El sereno {serenoId} no aparece en el monitoreo.");
        inicio = html.LastIndexOf("<li", inicio, StringComparison.Ordinal);
        return html[inicio..html.IndexOf("</li>", inicio, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task CA01_SerenoOperativoConUbicacion_SeMuestraConIdentificacionEstadoDisponibilidadUbicacionYHora()
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true);
        await PosicionAsync(app, -12.0512, -77.0345);
        var operador = await OperadorAsync();

        var elemento = Elemento(await operador.GetStringAsync(Fragmento), sereno.Id);

        Assert.Contains("data-nombre=\"Prueba Sereno\"", elemento);
        Assert.Contains("Operativo", elemento);
        Assert.Contains("Disponible", elemento);
        Assert.Contains("data-lat=\"-12.0512\"", elemento);
        Assert.Contains("data-lon=\"-77.0345\"", elemento);
        Assert.Contains("Última ubicación a las", elemento);
        Assert.Matches("data-hora=\"\\d{2}:\\d{2}:\\d{2}\"", elemento);
        // En el mismo mapa que los SOS: la página de la central incluye la sección y el mapa.
        var pagina = await operador.GetStringAsync("/panel/central");
        Assert.Contains("data-region-serenos", pagina);
        Assert.Contains("id=\"mapa\"", pagina);
    }

    [Fact]
    public async Task CA02_OperativoSinAsignacion_Disponible_YNoOperativo_NoDisponible()
    {
        var (appOperativo, operativo) = await SerenoAsync();
        await CambiarAsync(appOperativo, true);
        var (_, inactivo) = await SerenoAsync();
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync(Fragmento);

        Assert.Contains("data-disponible=\"true\"", Elemento(html, operativo.Id));
        var elementoInactivo = Elemento(html, inactivo.Id);
        Assert.Contains("data-disponible=\"false\"", elementoInactivo);
        Assert.Contains("No operativo", elementoInactivo);
        Assert.Contains("No disponible", elementoInactivo);
    }

    [Fact]
    public async Task CA03_HU18CA04_CambioDeEstadoYNuevaPosicion_AvisanALaCentralSinRecargar()
    {
        await using var central = await fabrica.OperadorEnCentralAsync();
        var (app, sereno) = await SerenoAsync();

        await CambiarAsync(app, true);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Count(id => id == sereno.Id) == 1),
            "No llegó el aviso del cambio de estado.");

        await PosicionAsync(app, -12.06, -77.04);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Count(id => id == sereno.Id) == 2),
            "No llegó el aviso de la nueva posición.");

        await CambiarAsync(app, false);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Count(id => id == sereno.Id) == 3),
            "No llegó el aviso de la desactivación.");
        // Lo que el panel vuelve a pedir al recibir el aviso ya refleja el cambio.
        Assert.Contains("data-disponible=\"false\"", Elemento(await central.Navegador.GetStringAsync(Fragmento), sereno.Id));
    }

    [Fact]
    public async Task CA03_RepetirElMismoEstadoOUnaPosicionReenviada_NoGeneraAvisos()
    {
        await using var central = await fabrica.OperadorEnCentralAsync();
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true);
        var posicion = new PosicionSerenoEnviada(-12.07, -77.05, 7, DateTimeOffset.UtcNow);
        await app.PostAsJsonAsync("/api/sereno/posiciones", posicion);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Count(id => id == sereno.Id) == 2));

        await CambiarAsync(app, true);                                  // mismo estado
        await app.PostAsJsonAsync("/api/sereno/posiciones", posicion);   // misma posición
        await Task.Delay(500);

        Assert.Equal(2, central.AvisosSerenos.Count(id => id == sereno.Id));
    }

    [Fact]
    public async Task CA04_SerenoSinPosiciones_ApareceConUbicacionNoDisponibleSinMarcador()
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true); // operativo, pero aún no envió ninguna posición
        var operador = await OperadorAsync();

        var elemento = Elemento(await operador.GetStringAsync(Fragmento), sereno.Id);

        Assert.Contains("Ubicación no disponible", elemento);
        Assert.Contains("data-lat=\"\"", elemento); // sin coordenadas: el panel no dibuja marcador
        Assert.DoesNotContain("data-ver-sereno", elemento);
    }

    [Fact]
    public async Task SerenoNoOperativo_NoExponeSuUltimaPosicion()
    {
        // La ubicación se comparte solo mientras está operativo (HU18): tras desactivarse, la central no la ve.
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true);
        await PosicionAsync(app, -12.0899, -77.0499);
        await CambiarAsync(app, false);
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync(Fragmento);

        Assert.Contains("data-lat=\"\"", Elemento(html, sereno.Id));
        Assert.DoesNotContain("-12.0899", html);
    }

    [Fact]
    public async Task AlReactivarse_NoSeMuestraLaPosicionDelTurnoAnterior()
    {
        // Defecto encontrado en la prueba de extremo a extremo (14/10/2026): al reactivarse, la central mostraba
        // como «última ubicación» la del turno anterior hasta que llegaba la primera posición nueva.
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true);
        await PosicionAsync(app, -12.0777, -77.0666);
        await CambiarAsync(app, false);
        await CambiarAsync(app, true); // nuevo turno, aún sin posiciones
        var operador = await OperadorAsync();

        var elemento = Elemento(await operador.GetStringAsync(Fragmento), sereno.Id);
        Assert.Contains("Ubicación no disponible", elemento);
        Assert.DoesNotContain("-12.0777", elemento);

        await PosicionAsync(app, -12.0888, -77.0555);
        Assert.Contains("data-lat=\"-12.0888\"", Elemento(await operador.GetStringAsync(Fragmento), sereno.Id));
    }

    [Theory]
    [InlineData(Roles.Administrador)]
    public async Task CA05_OtroPerfilDelPanel_NoConsultaElMonitoreo(string rol)
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, true);
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var respuesta = await navegador.GetAsync(Fragmento);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.DoesNotContain(sereno.Id.ToString(), await respuesta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Ciudadano)]
    public async Task CA05_AppDeSerenoOCiudadano_NoConsultaElMonitoreo(string rol)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.GetAsync(Fragmento);

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("data-sereno", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SiFallaElAvisoEnTiempoReal_ElCambioQuedaGuardado()
    {
        await using var servidor = fabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<Cad.Server.Compartido.INotificador, NotificadorQueFalla>()));
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = servidor.CreateClient();
        var token = await (await app.PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);

        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(app, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PosicionAsync(app, -12.03, -77.02)).StatusCode);
    }

    private sealed class NotificadorQueFalla : Cad.Server.Compartido.INotificador
    {
        public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) => throw new InvalidOperationException("Fallo simulado.");
        public Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct) => throw new InvalidOperationException("Fallo simulado.");

        public Task CasoActualizadoAsync(Guid casoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado.");
    }
}
