using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Casos;

// Decisión del PO del 16/10/2026: el operador registra el inicio de atención (HU23-CA02) y cierra (HU24-CA05) un reporte en
// curso desde su detalle, como en un SOS. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AtencionReporteTests(FabricaCad fabrica)
{
    private static string Ruta(Guid id) => $"/panel/reportes/{id}";

    // Un reporte admitido, asignado a un sereno operativo y, si se pide, ya «En camino».
    private async Task<(Guid Id, HttpClient SerenoApp, CuentaDePrueba Sereno)> ReporteAsignadoAsync(bool enCamino)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var id = (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id,
            "Hecho ficticio en curso", DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio"))))
            .Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        Assert.IsType<ResultadoDecision.Registrado>(await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>()
            .EjecutarAsync(id, new DatosDecision(TipoDecisionReporte.Admision, "Motivo ficticio"), operador.Id, CancellationToken.None)));
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var serenoApp = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        Assert.IsType<ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(id, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
        if (enCamino)
            Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{id}/desplazamiento", null)).StatusCode);
        return (id, serenoApp, sereno);
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient navegador, Guid id, string manejador, Dictionary<string, string>? campos = null)
    {
        campos ??= [];
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, Ruta(id));
        return await navegador.PostAsync($"{Ruta(id)}?handler={manejador}", new FormUrlEncodedContent(campos));
    }

    private Task<Caso> CasoAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));

    [Fact]
    public async Task HU23_CA02_ElOperadorRegistraElInicioDeAtencionDeUnReporteEnCamino()
    {
        var (id, serenoApp, _) = await ReporteAsignadoAsync(enCamino: true);
        var (operador, cuenta) = await OperadorAsync();
        Assert.Contains("Registrar inicio de atención", await operador.GetStringAsync(Ruta(id)));

        // 24/10/2026: con el motivo por escrito de por qué lo registra el operador.
        var respuesta = await EnviarAsync(operador, id, "IniciarAtencion", new() { ["MotivoInicio"] = "El sereno avisó por teléfono que ya llegó." });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.EnAtencion, (await CasoAsync(id)).Estado);
        var evento = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == id && e.Tipo == TipoEventoCaso.AtencionIniciada).ToListAsync()));
        Assert.Equal(cuenta.Id, evento.ActorId);
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));
        Assert.Contains("Inicio de atención registrado.", html);
        Assert.Contains("Cerrar el reporte", html);
        // El sereno ve el cambio en su despacho.
        Assert.Equal(DespachoSereno.EstadoEnAtencion, (await serenoApp.GetFromJsonAsync<DespachoSereno>("/api/sereno/despacho"))!.Estado);
    }

    [Fact]
    public async Task HU23_CA02_ConElReporteAunAsignado_NoSeRegistraElInicio()
    {
        var (id, _, _) = await ReporteAsignadoAsync(enCamino: false);
        var (operador, _) = await OperadorAsync();
        Assert.DoesNotContain("Registrar inicio de atención", await operador.GetStringAsync(Ruta(id)));

        var respuesta = await EnviarAsync(operador, id, "IniciarAtencion");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(AvanzarCaso.MotivoNoEnCamino, System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task HU24_CA05_ElOperadorCierraUnReporteEnAtencionYQuedaComoResponsable()
    {
        var (id, serenoApp, sereno) = await ReporteAsignadoAsync(enCamino: true);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{id}/atencion", null)).StatusCode);
        var (operador, cuenta) = await OperadorAsync();

        var respuesta = await EnviarAsync(operador, id, "Cerrar", new()
        {
            ["Cierre.CategoriaId"] = CategoriaIncidencia.Iniciales[3].Id.ToString(),
            ["Cierre.ResultadoPublico"] = "Se retiró el obstáculo (ficticio)",
            ["Cierre.NotasInternas"] = "Nota interna ficticia",
        });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Cerrada, (await CasoAsync(id)).Estado);
        var cierre = await fabrica.ConBaseDatosAsync(db => db.CierresCaso.AsNoTracking().SingleAsync(c => c.CasoId == id));
        Assert.Equal((cuenta.Id, "Se retiró el obstáculo (ficticio)", "Nota interna ficticia"), (cierre.ResponsableId, cierre.ResultadoPublico, cierre.NotasInternas));
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.SerenoId == sereno.Id && a.Vigente))); // el sereno queda libre
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));
        Assert.Contains("Reporte cerrado.", html);
        Assert.Contains("data-notas-internas", html);
        Assert.Equal(HttpStatusCode.NoContent, (await serenoApp.GetAsync("/api/sereno/despacho")).StatusCode);
    }

    [Fact]
    public async Task HU24_CA02_CierreSinTipificacionNiResultado_SeRechazaYElReporteSigueEnAtencion()
    {
        var (id, serenoApp, _) = await ReporteAsignadoAsync(enCamino: true);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{id}/atencion", null)).StatusCode);
        var (operador, _) = await OperadorAsync();

        var respuesta = await EnviarAsync(operador, id, "Cerrar", new() { ["Cierre.ResultadoPublico"] = "  " });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
        Assert.Contains("Cierre_CategoriaId-error", html);
        Assert.Contains("Cierre_ResultadoPublico-error", html);
        Assert.Equal(EstadoCaso.EnAtencion, (await CasoAsync(id)).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.CierresCaso.AnyAsync(c => c.CasoId == id)));
    }

    [Fact]
    public async Task SoloOperador_UnAdministradorNoCierraNiRegistraLaAtencion()
    {
        var (id, serenoApp, _) = await ReporteAsignadoAsync(enCamino: true);
        var admin = fabrica.CrearNavegador();
        await admin.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(admin, "/panel/admin/cuentas");

        foreach (var manejador in new[] { "IniciarAtencion", "Cerrar" })
        {
            var respuesta = await admin.PostAsync($"{Ruta(id)}?handler={manejador}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Cierre.CategoriaId"] = CategoriaIncidencia.Iniciales[3].Id.ToString(),
                ["Cierre.ResultadoPublico"] = "Intento no autorizado",
            }));
            Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.OriginalString);
        }

        Assert.Equal(EstadoCaso.EnCamino, (await CasoAsync(id)).Estado);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.GetAsync("/api/sereno/despacho")).StatusCode);
    }
}
