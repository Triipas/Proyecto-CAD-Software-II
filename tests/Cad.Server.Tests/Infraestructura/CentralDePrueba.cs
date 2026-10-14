using System.Collections.Concurrent;
using System.Text.Json;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cad.Server.Tests.Infraestructura;

// Operador del panel conectado al canal de la central con su cookie, como el navegador (HU11).
public sealed record OperadorEnCentral(HubConnection Conexion, HttpClient Navegador, ConcurrentQueue<Guid> Avisos) : IAsyncDisposable
{
    // HU19-CA03: identificadores de los serenos de los avisos «SerenoActualizado».
    public ConcurrentQueue<Guid> AvisosSerenos { get; init; } = new();

    public ValueTask DisposeAsync() => Conexion.DisposeAsync();
}

public static class CentralDePrueba
{
    // servidor: por defecto la fábrica compartida; puede ser una variante con otra configuración.
    public static async Task<OperadorEnCentral> OperadorEnCentralAsync(this FabricaCad fabrica, WebApplicationFactory<Program>? servidor = null)
    {
        servidor ??= fabrica;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var ingreso = await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        var cookie = ingreso.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("cad.panel=")).Split(';')[0];

        var avisos = new ConcurrentQueue<Guid>();
        var conexion = new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/central", opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => servidor.Server.CreateHandler();
                opciones.Transports = HttpTransportType.LongPolling;
                opciones.Headers["Cookie"] = cookie;
            })
            .Build();
        conexion.On<JsonElement>("SosRecibido", aviso => avisos.Enqueue(aviso.GetProperty("id").GetGuid()));
        var avisosSerenos = new ConcurrentQueue<Guid>();
        conexion.On<JsonElement>("SerenoActualizado", aviso => avisosSerenos.Enqueue(aviso.GetProperty("id").GetGuid()));
        await conexion.StartAsync();
        return new OperadorEnCentral(conexion, navegador, avisos) { AvisosSerenos = avisosSerenos };
    }

    public static async Task<bool> EsperarAsync(Func<bool> condicion, int segundos = 10)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (condicion()) return true;
            await Task.Delay(50);
        }
        return condicion();
    }
}
