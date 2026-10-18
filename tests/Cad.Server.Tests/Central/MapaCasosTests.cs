using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Central;

// HU25 — Visualizar los casos en el mapa (S4-T08). Los pendientes de asignación ya se dibujan desde las listas de la central
// (HU11, HU15-CA02); esta consulta añade los casos en curso con ubicación. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class MapaCasosTests(FabricaCad fabrica)
{
    private const string Fragmento = "/panel/central?handler=CasosMapa";

    private async Task<HttpClient> CiudadanoAsync() => await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

    private static async Task<Guid> SosAsync(HttpClient app, bool conUbicacion = true)
    {
        var ahora = DateTimeOffset.UtcNow;
        var ubicacion = conUbicacion ? new UbicacionSos(-12.0611, -77.0377, 7, ahora.AddSeconds(1)) : null;
        return (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, ubicacion))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private static async Task<Guid> ReporteAsync(HttpClient app, bool conPunto)
    {
        var lugar = conPunto
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0533, -77.0444, null, null)
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio sin punto");
        return (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[3].Id,
            "Hecho ficticio", DateTimeOffset.UtcNow.AddHours(-1), lugar))).Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private async Task AsignarAsync(Guid casoId)
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true));
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static string Elemento(string html, Guid id)
    {
        var inicio = html.IndexOf($"data-caso-mapa=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"El caso {id} no tiene marcador.");
        return html[inicio..html.IndexOf("</li>", inicio, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task CA01_CA03_CasosEnCursoConUbicacion_TienenMarcadorConSuOrigenYEstado()
    {
        var app = await CiudadanoAsync();
        var sos = await SosAsync(app);
        await AsignarAsync(sos);
        var reporte = await ReporteAsync(app, conPunto: true);
        var operador = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Fragmento));

        var marcadorSos = Elemento(html, sos);
        Assert.Contains("data-origen=\"SOS\"", marcadorSos);
        Assert.Contains("data-estado=\"Asignada\"", marcadorSos);
        Assert.Contains("data-lat=\"-12.0611\"", marcadorSos);
        Assert.Contains($"data-detalle=\"/panel/central/sos/{sos}\"", marcadorSos);
        var marcadorReporte = Elemento(html, reporte);
        Assert.Contains("data-origen=\"Reporte\"", marcadorReporte);
        Assert.Contains("data-estado=\"Recibido\"", marcadorReporte);
        Assert.Contains("data-lat=\"-12.0533\"", marcadorReporte); // el lugar del hecho, no una ubicación del dispositivo
        Assert.Contains($"data-detalle=\"/panel/reportes/{reporte}\"", marcadorReporte);
        // La página de la central incluye los mismos datos al cargarse.
        Assert.Contains($"data-caso-mapa=\"{sos}\"", await operador.GetStringAsync("/panel/central"));
    }

    [Fact]
    public async Task CA04_CasosSinUbicacionOSoloConReferencia_NoTienenMarcadorNiCoordenadas()
    {
        var app = await CiudadanoAsync();
        var sosSinUbicacion = await SosAsync(app, conUbicacion: false);
        await AsignarAsync(sosSinUbicacion);
        var reporteConReferencia = await ReporteAsync(app, conPunto: false);
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync(Fragmento);

        Assert.DoesNotContain(sosSinUbicacion.ToString(), html);
        Assert.DoesNotContain(reporteConReferencia.ToString(), html); // DP10: la referencia no genera marcador
        // Siguen disponibles en el listado (HU26).
        var casos = await operador.GetStringAsync("/panel/casos");
        Assert.Contains(sosSinUbicacion.ToString(), casos);
        Assert.Contains(reporteConReferencia.ToString(), casos);
    }

    [Fact]
    public async Task CA02_UnCasoPendiente_SeEligeDesdeElMapaParaAsignarlo_Y_NoSeDuplicaComoEnCurso()
    {
        var app = await CiudadanoAsync();
        var pendiente = await SosAsync(app);
        var operador = await OperadorAsync();

        var central = await operador.GetStringAsync("/panel/central");

        // El pendiente está en la lista de la central (marcador relleno y «Elegir sereno para asignar», HU20) y no en los en curso.
        Assert.Contains($"data-sos=\"{pendiente}\"", central);
        Assert.Contains("data-seleccionar-sos", central);
        Assert.DoesNotContain(pendiente.ToString(), await operador.GetStringAsync(Fragmento));
    }

    [Fact]
    public async Task CA05_UnReporteNuevoYSuRevision_SeAvisanALaCentralSinRecargar()
    {
        await using var central = await fabrica.OperadorEnCentralAsync();
        var app = await CiudadanoAsync();

        var reporte = await ReporteAsync(app, conPunto: true);

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(reporte)), "La central no recibió el aviso del reporte nuevo.");
        Assert.DoesNotContain(reporte, central.Avisos); // no es un SOS: sin aviso sonoro de atención urgente
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var avisosAntes = central.AvisosCasos.Count(id => id == reporte);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Count(id => id == reporte) > avisosAntes), "La central no recibió el aviso de la revisión.");
        Assert.Contains("data-estado=\"En revisión\"", Elemento(System.Net.WebUtility.HtmlDecode(await central.Navegador.GetStringAsync(Fragmento)), reporte));
    }

    [Fact]
    public async Task CasosTerminados_NoOcupanElMapa()
    {
        var app = await CiudadanoAsync();
        var reporte = await ReporteAsync(app, conPunto: true);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>()
            .EjecutarAsync(reporte, new DatosDecision(TipoDecisionReporte.Descarte, "Duplicado ficticio"), operador.Id, CancellationToken.None));

        Assert.DoesNotContain(reporte.ToString(), await (await OperadorAsync()).GetStringAsync(Fragmento));
    }
}
