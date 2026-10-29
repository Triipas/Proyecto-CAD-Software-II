using System.Net;
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

// Cola de trabajo de la central (29/10/2026): los SOS y los reportes que ya tienen sereno siguen a la vista tras los
// pendientes, con su sereno y su estado. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CasosAsignadosCentralTests(FabricaCad fabrica)
{
    private const string Fragmento = "/panel/central?handler=Asignados";

    private async Task<Guid> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321"));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1))));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<Guid> ReporteAdmitidoAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(),
            CategoriaIncidencia.Iniciales[1].Id, "Hecho ficticio", DateTimeOffset.UtcNow.AddHours(-1),
            new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0589, -77.0461, null, "Frente a la plaza")));
        var id = (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id,
            new DatosDecision(TipoDecisionReporte.Admision, "Corresponde atenderlo", Prioridad: "Media", Heridos: "No"), operador.Id, CancellationToken.None));
        return id;
    }

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return (app, cuenta);
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task AsignarAsync(HttpClient operador, string ruta, Guid serenoId)
    {
        var respuesta = await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["Asignacion.Volver"] = "central",
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, "/panel/central"),
        }));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        // La central muestra el motivo si la asignación se rechazó: que no pase inadvertido.
        var central = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/central"));
        var rechazo = central.IndexOf("No se pudo asignar el caso", StringComparison.Ordinal);
        Assert.True(rechazo < 0, rechazo < 0 ? "" : central[rechazo..Math.Min(central.Length, rechazo + 400)]);
    }

    private static string Tarjeta(string html, Guid id)
    {
        var inicio = html.IndexOf($"data-asignado=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"No hay tarjeta del caso {id} en: {html}");
        var fin = html.IndexOf("</li>", inicio, StringComparison.Ordinal);
        return html[inicio..fin];
    }

    [Fact]
    public async Task UnSosPendiente_NoEstaEnLosAsignados_PeroAlAsignarseSigueEnLaCola_ConSuSerenoYSuEstado()
    {
        var sosId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var operador = await OperadorAsync();
        Assert.DoesNotContain($"data-asignado=\"{sosId}\"", await operador.GetStringAsync(Fragmento));

        await AsignarAsync(operador, $"/panel/central/sos/{sosId}", sereno.Id);

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Fragmento));
        var tarjeta = Tarjeta(html, sosId);
        Assert.Contains("SOS", tarjeta);
        Assert.Contains("Asignada", tarjeta);
        Assert.Contains($"data-sereno=\"{sereno.Id}\"", html);
        Assert.Contains($"Sereno: Prueba Sereno", tarjeta);
        Assert.Contains($"/panel/central/sos/{sosId}", tarjeta);
        Assert.Contains("data-seleccionar-asignado", tarjeta);   // la tarjeta entera es el botón que lo elige
        Assert.Contains("data-lat=\"-12.0464\"", html);          // con su punto, para llevar el mapa
    }

    [Fact]
    public async Task UnReporteAdmitido_AlAsignarse_SigueEnLaCola_ComoReporte_ConSuSerenoYSuPuntoDelHecho()
    {
        var reporteId = await ReporteAdmitidoAsync();
        var (_, sereno) = await SerenoAsync();
        var operador = await OperadorAsync();

        await AsignarAsync(operador, $"/panel/reportes/{reporteId}", sereno.Id);

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Fragmento));
        var tarjeta = Tarjeta(html, reporteId);
        Assert.Contains("etiqueta-reporte", tarjeta);
        Assert.Contains("data-origen=\"Reporte\"", html);
        Assert.Contains("sos--reporte", html);
        Assert.Contains($"Sereno: Prueba Sereno", tarjeta);
        Assert.Contains($"/panel/reportes/{reporteId}", tarjeta);
        Assert.Contains("data-lat=\"-12.0589\"", html);
    }

    [Fact]
    public async Task LaCentral_LlevaLaSeccionDeAsignados_ConSuContador()
    {
        var sosId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var operador = await OperadorAsync();
        await AsignarAsync(operador, $"/panel/central/sos/{sosId}", sereno.Id);

        var pagina = await operador.GetStringAsync("/panel/central");

        Assert.Contains("Con sereno asignado", pagina);
        Assert.Contains("data-region-asignados", pagina);
        Assert.Contains($"data-asignado=\"{sosId}\"", pagina);
        // Ya no está entre los pendientes de «Atención urgente».
        Assert.DoesNotContain($"data-sos=\"{sosId}\"", pagina);
    }
}
