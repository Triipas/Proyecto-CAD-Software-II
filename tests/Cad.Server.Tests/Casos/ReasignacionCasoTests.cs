using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// Reasignar un caso desde la central (decisión del 24/10/2026): solo «Asignada» o «En camino», a un sereno disponible y con
// motivo obligatorio; el sereno anterior queda libre y el nuevo recibe el despacho. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class ReasignacionCasoTests(FabricaCad fabrica)
{
    private const string Motivo = "El sereno se desactivó con el caso en curso.";

    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App);

    private async Task<SerenoDePrueba> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app);
    }

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private async Task<Guid> SosAsignadoAsync(HttpClient operador, SerenoDePrueba sereno)
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var ruta = $"/panel/central/sos/{sos!.Id}";
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);
        return sos.Id;
    }

    private static async Task<HttpResponseMessage> ReasignarAsync(HttpClient operador, Guid casoId, Guid serenoId, string motivo = Motivo, Guid? idSolicitud = null)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        return await operador.PostAsync($"{ruta}?handler=Reasignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Reasignacion.SerenoId"] = serenoId.ToString(), ["Reasignacion.Motivo"] = motivo,
            ["Reasignacion.IdSolicitud"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }));
    }

    private Task<EstadoCaso> EstadoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.Id == casoId).Select(c => c.Estado).SingleAsync());

    [Fact]
    public async Task EnCamino_SeReasignaConMotivo_ElAnteriorQuedaLibre_YElNuevoContinua()
    {
        var operador = await OperadorAsync();
        var anterior = await SerenoAsync();
        var nuevo = await SerenoAsync();
        var casoId = await SosAsignadoAsync(operador, anterior);
        Assert.Equal(HttpStatusCode.OK, (await anterior.App.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Contains("Reasignar…", await operador.GetStringAsync($"/panel/central/sos/{casoId}"));

        Assert.Equal(HttpStatusCode.Redirect, (await ReasignarAsync(operador, casoId, nuevo.Cuenta.Id)).StatusCode);

        Assert.Equal(EstadoCaso.Asignada, await EstadoAsync(casoId)); // el nuevo debe confirmar que va en camino
        Assert.Equal(HttpStatusCode.NoContent, (await anterior.App.GetAsync("/api/sereno/despacho")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.App.GetAsync("/api/sereno/despacho")).StatusCode);
        var asignaciones = await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AsNoTracking().Where(a => a.CasoId == casoId).ToListAsync());
        Assert.Equal(nuevo.Cuenta.Id, Assert.Single(asignaciones, a => a.Vigente).SerenoId);
        Assert.True(asignaciones.Single(a => a.SerenoId == anterior.Cuenta.Id).Cancelada);
        // El nuevo sereno avanza aunque el anterior ya hubiera confirmado el desplazamiento.
        Assert.Equal(HttpStatusCode.OK, (await nuevo.App.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await nuevo.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
        Assert.Equal(EstadoCaso.EnAtencion, await EstadoAsync(casoId));
        // El historial dice de quién a quién y por qué.
        var historial = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos/{casoId}/historial"));
        Assert.Contains($"Reasignado de Prueba {Roles.Sereno}", historial);
        Assert.Contains($"Motivo: {Motivo}", historial);
    }

    [Fact]
    public async Task SinMotivo_OAlMismoSereno_NoSeReasigna()
    {
        var operador = await OperadorAsync();
        var anterior = await SerenoAsync();
        var nuevo = await SerenoAsync();
        var casoId = await SosAsignadoAsync(operador, anterior);

        var sinMotivo = await ReasignarAsync(operador, casoId, nuevo.Cuenta.Id, motivo: "  ");
        Assert.Equal(HttpStatusCode.OK, sinMotivo.StatusCode);
        Assert.Contains("Indica por qué se reasigna el caso.", System.Net.WebUtility.HtmlDecode(await sinMotivo.Content.ReadAsStringAsync()));
        var alMismo = await ReasignarAsync(operador, casoId, anterior.Cuenta.Id);
        Assert.Contains(ReasignarCaso.MotivoMismoSereno, System.Net.WebUtility.HtmlDecode(await alMismo.Content.ReadAsStringAsync()));
        Assert.Equal(anterior.Cuenta.Id, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.Where(a => a.CasoId == casoId && a.Vigente).Select(a => a.SerenoId).SingleAsync()));
    }

    [Fact]
    public async Task EnAtencion_NoSeReasigna()
    {
        var operador = await OperadorAsync();
        var anterior = await SerenoAsync();
        var nuevo = await SerenoAsync();
        var casoId = await SosAsignadoAsync(operador, anterior);
        Assert.Equal(HttpStatusCode.OK, (await anterior.App.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anterior.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
        Assert.DoesNotContain("Reasignar…", await operador.GetStringAsync($"/panel/central/sos/{casoId}"));

        var respuesta = await ReasignarAsync(operador, casoId, nuevo.Cuenta.Id);

        Assert.Contains(ReasignarCaso.MotivoNoReasignable, System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal(EstadoCaso.EnAtencion, await EstadoAsync(casoId));
    }

    [Fact]
    public async Task AUnSerenoNoDisponible_NoSeReasigna_YUnReenvioNoDuplica()
    {
        var operador = await OperadorAsync();
        var anterior = await SerenoAsync();
        var ocupado = await SerenoAsync();
        var nuevo = await SerenoAsync();
        var casoId = await SosAsignadoAsync(operador, anterior);
        await SosAsignadoAsync(operador, ocupado); // ya tiene una asignación vigente

        var respuesta = await ReasignarAsync(operador, casoId, ocupado.Cuenta.Id);
        Assert.Contains(AsignarCaso.MotivoSerenoNoDisponible, System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));

        var idSolicitud = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Redirect, (await ReasignarAsync(operador, casoId, nuevo.Cuenta.Id, idSolicitud: idSolicitud)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ReasignarAsync(operador, casoId, nuevo.Cuenta.Id, idSolicitud: idSolicitud)).StatusCode);
        Assert.Single(await fabrica.ConBaseDatosAsync(db => db.Reasignaciones.Where(r => r.CasoId == casoId).ToListAsync()));
    }
}
