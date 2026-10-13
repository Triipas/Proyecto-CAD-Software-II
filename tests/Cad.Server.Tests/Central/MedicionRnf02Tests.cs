using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Sos;
using Cad.PruebaCarga;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Central;

// RNF02 / HU11-CA05 (S1-T18): instrumentación de la medición y cálculo del informe.
// No mide nada por sí misma: comprueba que la medición registra lo que debe y que el cálculo es correcto.
[Collection(ColeccionServidor.Nombre)]
public class MedicionRnf02Tests(FabricaCad fabrica)
{
    private WebApplicationFactory<Program> ConMedicionActiva() =>
        fabrica.WithWebHostBuilder(b => b.UseSetting("Medicion:Rnf02:Habilitada", "true"));

    private async Task<Guid> EmitirSosAsync(WebApplicationFactory<Program> servidor)
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var token = await (await servidor.CreateClient().PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(ciudadano.Correo, ciudadano.Clave))).Content
            .ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
        var app = servidor.CreateClient();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<HttpClient> AdministradorAsync(WebApplicationFactory<Program> servidor)
    {
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        return navegador;
    }

    [Fact]
    public async Task Activada_AcuseDelPanel_RegistraAceptacionYPresentacionConElRelojDelServidor()
    {
        await using var servidor = ConMedicionActiva();
        await using var central = await fabrica.OperadorEnCentralAsync(servidor);
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);

        var id = await EmitirSosAsync(servidor);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.Avisos.Contains(id)), "No llegó el aviso.");
        await central.Conexion.InvokeAsync("ConfirmarPresentacion", id);
        await central.Conexion.InvokeAsync("ConfirmarPresentacion", id); // acuse repetido
        await central.Conexion.InvokeAsync("ConfirmarPresentacion", Guid.NewGuid()); // caso inexistente

        var admin = await AdministradorAsync(servidor);
        var filas = await admin.GetFromJsonAsync<List<PresentacionJson>>($"/api/mediciones/presentacion?desde={Uri.EscapeDataString(desde.ToString("O"))}");
        var fila = Assert.Single(filas!, f => f.CasoId == id);
        var registradoEn = await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == id).Select(c => c.RegistradoEn).SingleAsync());
        Assert.Equal(registradoEn, fila.AceptadoEn);           // aceptación = registro del servidor
        Assert.True(fila.PresentadoEn >= fila.AceptadoEn);
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.MedicionesPresentacion.CountAsync(m => m.CasoId == id)));

        // El panel solo envía acuses cuando la página indica que la medición está activa.
        Assert.Contains("data-medicion=\"true\"", await central.Navegador.GetStringAsync("/panel/central"));
    }

    [Fact]
    public async Task Desactivada_PorDefecto_NoRegistraMedicionesNiExponeResultados()
    {
        await using var central = await fabrica.OperadorEnCentralAsync();
        var id = await EmitirSosAsync(fabrica);
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.Avisos.Contains(id)), "No llegó el aviso.");

        await central.Conexion.InvokeAsync("ConfirmarPresentacion", id);

        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.MedicionesPresentacion.CountAsync(m => m.CasoId == id)));
        Assert.Contains("data-medicion=\"false\"", await central.Navegador.GetStringAsync("/panel/central"));
        var admin = await AdministradorAsync(fabrica);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/mediciones/presentacion?desde={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}")).StatusCode);
    }

    [Fact]
    public void Estadisticas_CumpleConExactamenteEl95PorCiento()
    {
        var latencias = Enumerable.Repeat<TimeSpan?>(TimeSpan.FromMilliseconds(400), 95)
            .Concat(Enumerable.Repeat<TimeSpan?>(TimeSpan.FromSeconds(4), 5)).ToList();

        var r = Estadisticas.Evaluar(latencias, sesionesConcurrentes: 20);

        Assert.Equal(95.0, r.PorcentajeDentroDelLimite);
        Assert.True(r.Cumple);
        Assert.Equal(400, r.P50Ms);
        Assert.Equal(400, r.P95Ms);
        Assert.Equal(4000, r.MaximoMs);
    }

    [Fact]
    public void Estadisticas_UnSosSinAcuseCuentaComoIncumplimiento()
    {
        var latencias = Enumerable.Repeat<TimeSpan?>(TimeSpan.FromMilliseconds(300), 94)
            .Concat(Enumerable.Repeat<TimeSpan?>(null, 6)).ToList(); // 6 SOS nunca presentados

        var r = Estadisticas.Evaluar(latencias, 20);

        Assert.Equal(94, r.SosConAcuse);
        Assert.Equal(94.0, r.PorcentajeDentroDelLimite);
        Assert.False(r.Cumple);
    }

    [Fact]
    public void Estadisticas_ExactamenteTresSegundosEstaDentroDelLimite()
    {
        var r = Estadisticas.Evaluar(Enumerable.Repeat<TimeSpan?>(TimeSpan.FromSeconds(3), 100).ToList(), 20);

        Assert.Equal(100.0, r.PorcentajeDentroDelLimite);
        Assert.True(r.Cumple);
    }

    [Theory]
    [InlineData(99, 20)]  // muestra menor que la exigida
    [InlineData(100, 19)] // menos sesiones concurrentes que las exigidas
    public void Estadisticas_SinLasCondicionesDelRequisito_NoCumpleAunqueTodoSeaRapido(int sos, int sesiones)
    {
        var r = Estadisticas.Evaluar(Enumerable.Repeat<TimeSpan?>(TimeSpan.FromMilliseconds(100), sos).ToList(), sesiones);

        Assert.Equal(100.0, r.PorcentajeDentroDelLimite);
        Assert.False(r.Cumple);
    }

    private sealed record PresentacionJson(Guid CasoId, DateTimeOffset AceptadoEn, DateTimeOffset PresentadoEn);
}
