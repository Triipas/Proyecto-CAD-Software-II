using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Central;

// HU11 — Recibir los SOS en la sección de atención urgente (S1-T17). Datos ficticios.
// El aviso sonoro y la ausencia de recarga se verifican además en un navegador real (ver trazabilidad).
[Collection(ColeccionServidor.Nombre)]
public class RecepcionSosTests(FabricaCad fabrica)
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Activacion = DateTimeOffset.UtcNow;

    private async Task<(HubConnection Conexion, HttpClient Navegador, ConcurrentQueue<Guid> Avisos)> OperadorConectadoAsync()
    {
        var central = await fabrica.OperadorEnCentralAsync();
        return (central.Conexion, central.Navegador, central.Avisos);
    }

    private async Task<(HttpClient App, CuentaDePrueba Ciudadano)> CiudadanoAsync()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(ciudadano), ciudadano);
    }

    private static Task<bool> EsperarAsync(Func<bool> condicion) => CentralDePrueba.EsperarAsync(condicion);

    [Fact]
    public async Task CA01_CA02_SosRegistrado_LlegaALaCentralSinRecargarYDespuesDeGuardarse()
    {
        var (conexion, _, _) = await OperadorConectadoAsync();
        var persistidoAlLlegar = new TaskCompletionSource<(Guid Id, bool Persistido)>();
        conexion.On<JsonElement>("SosRecibido", async aviso =>
        {
            var id = aviso.GetProperty("id").GetGuid();
            // Notificación posterior a la persistencia: al recibir el aviso, el SOS ya debe estar en la base de datos.
            var persistido = await fabrica.ConBaseDatosAsync(db => db.Casos.AnyAsync(c => c.Id == id));
            persistidoAlLlegar.TrySetResult((id, persistido));
        });
        var (app, _) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), Activacion, new UbicacionSos(-12.05, -77.04, 10, Activacion.AddSeconds(2))));
        var creado = (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!;

        var (idAviso, persistidoAntes) = await persistidoAlLlegar.Task.WaitAsync(Espera);
        Assert.Equal(creado.Id, idAviso);
        Assert.True(persistidoAntes, "El aviso llegó antes de que el SOS estuviera guardado.");
        await conexion.DisposeAsync();
    }

    [Fact]
    public async Task CA02_ReiteracionTecnica_NoGeneraUnSegundoAviso()
    {
        var (conexion, _, avisos) = await OperadorConectadoAsync();
        var (app, _) = await CiudadanoAsync();
        var solicitud = new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null);

        var id = (await (await app.PostAsJsonAsync("/api/sos", solicitud)).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync("/api/sos", solicitud)).StatusCode);

        Assert.True(await EsperarAsync(() => avisos.Contains(id)), "No llegó el aviso del SOS.");
        await Task.Delay(500); // margen para un posible aviso duplicado
        Assert.Equal(1, avisos.Count(a => a == id));
        await conexion.DisposeAsync();
    }

    [Fact]
    public async Task CA01_CA03_CA04_SeccionUrgente_MuestraOrigenEstadoYTambienLosSosSinUbicacion()
    {
        var (conexion, navegador, avisos) = await OperadorConectadoAsync();
        var (app, ciudadano) = await CiudadanoAsync();

        var conUbicacion = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), Activacion,
            new UbicacionSos(-12.0711, -77.0312, 15, Activacion.AddSeconds(2))))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var sinUbicacion = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        Assert.True(await EsperarAsync(() => avisos.Contains(conUbicacion) && avisos.Contains(sinUbicacion)), "Faltan avisos.");

        // El fragmento que el panel pide al recibir el aviso (resincronización desde la base de datos).
        var lista = await navegador.GetStringAsync("/panel/central?handler=Lista");
        var itemCon = Item(lista, conUbicacion);
        Assert.Contains("SOS", itemCon);
        Assert.Contains("Pendiente", itemCon);
        Assert.Contains($"Prueba {Roles.Ciudadano}", itemCon);
        Assert.Contains("data-lat=\"-12.0711\"", itemCon); // representable en el mapa (CA01)
        Assert.Contains("Ver en el mapa", itemCon);
        var itemSin = Item(lista, sinUbicacion);
        Assert.Contains("Ubicación no disponible", itemSin); // CA04
        Assert.Contains("data-lat=\"\"", itemSin);           // sin marcador en el mapa

        // La página completa incluye la sección urgente separada y el mapa operativo.
        var pagina = await navegador.GetStringAsync("/panel/central");
        Assert.Contains("Atención urgente · SOS", pagina);
        Assert.Contains("id=\"mapa\"", pagina);
        Assert.Contains(conUbicacion.ToString(), pagina);
        await conexion.DisposeAsync();
    }

    [Fact]
    public async Task HU03_SoloOperadores_PuedenConectarseAlCanalDeLaCentral()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var token = await (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(ciudadano.Correo, ciudadano.Clave)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();

        foreach (var configurar in new Action<Microsoft.AspNetCore.Http.Connections.Client.HttpConnectionOptions>[]
        {
            _ => { },                                                          // sin sesión
            o => o.AccessTokenProvider = () => Task.FromResult<string?>(token!.Token), // ciudadano con JWT
        })
        {
            var conexion = new HubConnectionBuilder()
                .WithUrl("https://localhost/hubs/central", o =>
                {
                    o.HttpMessageHandlerFactory = _ => fabrica.Server.CreateHandler();
                    o.Transports = HttpTransportType.LongPolling;
                    configurar(o);
                })
                .Build();

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => conexion.StartAsync());
            Assert.True(error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Se obtuvo {error.StatusCode}");
        }
    }

    [Fact]
    public async Task SiElAvisoFalla_ElSosQuedaRegistradoYConfirmado()
    {
        await using var conAvisoRoto = fabrica.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<INotificador, NotificadorQueFalla>()));
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var token = await (await conAvisoRoto.CreateClient().PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(ciudadano.Correo, ciudadano.Clave)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
        var app = conAvisoRoto.CreateClient();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);

        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var id = (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        Assert.True(await fabrica.ConBaseDatosAsync(db => db.Casos.AnyAsync(c => c.Id == id)));
    }

    private static string Item(string html, Guid id)
    {
        var inicio = html.IndexOf($"data-sos=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"El SOS {id} no aparece en la sección urgente.");
        inicio = html.LastIndexOf("<li", inicio, StringComparison.Ordinal);
        return html[inicio..html.IndexOf("</li>", inicio, StringComparison.Ordinal)];
    }

    private sealed class NotificadorQueFalla : INotificador
    {
        public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado del canal en tiempo real.");

        public Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado del canal en tiempo real.");

        public Task CasoActualizadoAsync(Guid casoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado.");

        public Task SosCanceladoAsync(Guid casoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado.");

        public Task DespachoAsignadoAsync(Guid serenoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado.");

        public Task DespachoActualizadoAsync(Guid serenoId, CancellationToken ct) =>
            throw new InvalidOperationException("Fallo simulado.");
    }
}
