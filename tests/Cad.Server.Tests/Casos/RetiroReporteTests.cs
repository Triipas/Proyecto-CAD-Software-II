using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Casos;

// Decisión del PO del 26/10/2026 — el ciudadano consulta en «Mi caso» lo que dio en su reporte (solo lectura, con sus fotos) y
// puede retirarlo, con motivo obligatorio, mientras la central no lo haya decidido («Recibido» o «En revisión»). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class RetiroReporteTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Otros = CategoriaIncidencia.Iniciales[3].Id;

    private async Task<(HttpClient App, Guid Id)> ReporteAsync(IReadOnlyList<byte[]>? fotos = null)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), Robo, "Robo de una bicicleta (ficticio)",
            DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0631, -77.0365, null, "Frente al mercado"), fotos));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (app, (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id);
    }

    private async Task<CuentaDePrueba> RevisarAsync(Guid id)
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        return operador;
    }

    private static Task<HttpResponseMessage> RetirarAsync(HttpClient app, Guid id, string? motivo = "Ya lo resolvieron los vecinos.") =>
        app.PostAsJsonAsync($"/api/casos/mios/{id}/cancelacion", new SolicitudCancelacionSos(motivo));

    private Task<Caso> CasoAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));

    [Fact]
    public async Task MiCaso_MuestraLoQueDioConLaCategoriaOriginalYSusFotos()
    {
        var (app, id) = await ReporteAsync([FotosReporteTests.Jpeg(sello: 0x61), FotosReporteTests.Jpeg(sello: 0x62)]);
        var operador = await RevisarAsync(id);
        // La corrección del operador es interna: el titular ve la categoría que eligió.
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().CorregirCategoriaAsync(id, Otros, operador.Id, CancellationToken.None));

        var detalle = (await app.GetFromJsonAsync<MiCasoDetalle>($"/api/casos/mios/{id}"))!;
        var foto = await app.GetAsync($"/api/casos/mios/{id}/fotos/2");

        var r = Assert.IsType<MiReporte>(detalle.Reporte);
        Assert.Equal(CategoriaIncidencia.Iniciales[1].Nombre, r.Categoria);
        Assert.Equal("Robo de una bicicleta (ficticio)", r.Descripcion);
        Assert.Equal(FuentesUbicacionHecho.Mapa, r.FuenteUbicacion);
        Assert.Equal(-12.0631, r.Latitud);
        Assert.Equal("Frente al mercado", r.Referencia);
        Assert.Equal(2, r.Fotos);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/jpeg", foto.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", foto.Headers.CacheControl!.ToString());
        Assert.Equal(FotosReporteTests.Jpeg(conMetadatos: false, sello: 0x62), await foto.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"/api/casos/mios/{id}/fotos/3")).StatusCode);
    }

    [Fact]
    public async Task LaFotoDeOtroCiudadano_NoSeObtiene()
    {
        var (_, id) = await ReporteAsync([FotosReporteTests.Jpeg()]);
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"/api/casos/mios/{id}/fotos/1")).StatusCode);
    }

    [Fact]
    public async Task UnSos_NoTraeDatosDeReporte()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var app = await fabrica.CrearClienteAppAsync(ciudadano);
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!;

        Assert.Null((await app.GetFromJsonAsync<MiCasoDetalle>($"/api/casos/mios/{sos.Id}"))!.Reporte);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinDecidir_ElTitularLoRetiraConMotivo(bool enRevision)
    {
        var (app, id) = await ReporteAsync();
        if (enRevision)
            await RevisarAsync(id);

        var respuesta = await RetirarAsync(app, id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var detalle = (await respuesta.Content.ReadFromJsonAsync<MiCasoDetalle>())!;
        Assert.Equal(EstadosPublicos.Cancelado, detalle.Estado);
        Assert.Equal("Cancelado por ti. Motivo: Ya lo resolvieron los vecinos.", detalle.Resultado);
        Assert.NotNull(detalle.Reporte); // lo que dio sigue disponible
        Assert.Equal(EstadoCaso.Cancelada, (await CasoAsync(id)).Estado);
        var evento = Assert.Single(await fabrica.ConBaseDatosAsync(db =>
            db.EventosCaso.AsNoTracking().Where(e => e.CasoId == id && e.Tipo == TipoEventoCaso.ReporteRetirado).ToListAsync()));
        Assert.Equal(VisibilidadEvento.Publica, evento.Visibilidad);
        // Un reenvío no cambia nada.
        Assert.Equal(HttpStatusCode.OK, (await RetirarAsync(app, id, "Otro motivo")).StatusCode);
        Assert.Equal("Ya lo resolvieron los vecinos.", (await fabrica.ConBaseDatosAsync(db => db.CancelacionesSos.SingleAsync(c => c.CasoId == id))).Motivo);
    }

    [Fact]
    public async Task SinMotivo_NoSeRetira()
    {
        var (app, id) = await ReporteAsync();

        var respuesta = await RetirarAsync(app, id, "  ");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task YaDecidido_NoSeRetira()
    {
        var (app, id) = await ReporteAsync();
        var operador = await RevisarAsync(id);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>()
            .EjecutarAsync(id, new DatosDecision(TipoDecisionReporte.Admision, "Corresponde (ficticio)", Prioridad: "Media", Heridos: "No"), operador.Id, CancellationToken.None));

        var respuesta = await RetirarAsync(app, id);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains(CancelarSos.MotivoReporteDecidido, await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task OtroCiudadano_NoLoRetira()
    {
        var (_, id) = await ReporteAsync();
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

        Assert.Equal(HttpStatusCode.NotFound, (await RetirarAsync(otro, id)).StatusCode);
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task ElPanel_MuestraElRetiroYLoSacaDeReportes()
    {
        var (app, id) = await ReporteAsync();
        await RetirarAsync(app, id, "Fue un error (ficticio).");
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var operador = fabrica.CrearNavegador();
        await operador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var detalle = await operador.GetStringAsync($"/panel/reportes/{id}");
        var bandeja = await operador.GetStringAsync("/panel/reportes");
        var historial = await operador.GetStringAsync($"/panel/casos/{id}/historial");

        Assert.Contains("data-retirado", detalle);
        Assert.Contains("Fue un error (ficticio).", detalle);
        Assert.DoesNotContain(id.ToString(), bandeja);
        Assert.Contains("Reporte retirado por el ciudadano", historial);
    }
}
