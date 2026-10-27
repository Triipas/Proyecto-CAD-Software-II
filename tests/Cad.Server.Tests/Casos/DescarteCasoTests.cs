using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Casos;

// Descartar un caso falso o erróneo desde la central (23/10/2026): desde cualquier estado abierto de la atención, con motivo
// obligatorio; libera al sereno y retira el despacho; el ciudadano ve «Descartado». Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class DescarteCasoTests(FabricaCad fabrica)
{
    private const string Motivo = "Llamada de broma: el ciudadano confirmó que no pasaba nada.";

    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App);

    private async Task<SerenoDePrueba> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app);
    }

    private async Task<HttpClient> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private HttpClient? appCiudadano;

    private async Task<(Guid CasoId, Guid CiudadanoId)> SosAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var ciudadano = appCiudadano = await fabrica.CrearClienteAppAsync(cuenta);
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        return (sos!.Id, cuenta.Id);
    }

    private static async Task AsignarAsync(HttpClient operador, Guid casoId, Guid serenoId)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);
    }

    private static async Task<HttpResponseMessage> DescartarAsync(HttpClient operador, Guid casoId, string motivo = Motivo)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        return await operador.PostAsync($"{ruta}?handler=Descartar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Descarte.MotivoCodigo"] = "Duplicado", ["Descarte.Motivo"] = motivo,
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }));
    }

    private Task<Caso> LeerCasoAsync(Guid casoId) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    [Fact]
    public async Task SosPendiente_SeDescartaConMotivo_YElCiudadanoVeDescartado()
    {
        var (casoId, ciudadanoId) = await SosAsync();
        var operador = await PanelAsync();

        Assert.Equal(HttpStatusCode.Redirect, (await DescartarAsync(operador, casoId)).StatusCode);

        Assert.Equal(EstadoCaso.Descartada, (await LeerCasoAsync(casoId)).Estado);
        var descarte = await fabrica.ConBaseDatosAsync(db => db.DescartesCaso.AsNoTracking().SingleAsync(d => d.CasoId == casoId));
        Assert.Equal(Motivo, descarte.Motivo);
        Assert.Equal(EstadoCaso.Pendiente, descarte.EstadoPrevio);
        Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoDescartado).ToListAsync()));
        // El ciudadano recibe el cambio de estado, sin el motivo.
        var avisos = await fabrica.ConBaseDatosAsync(db => db.NotificacionesCiudadano.Where(n => n.CiudadanoId == ciudadanoId).Select(n => n.Texto).ToListAsync());
        Assert.Contains("Tu SOS pasó a «Descartado».", avisos);
        Assert.DoesNotContain(avisos, a => a.Contains("broma"));
        // «Mis casos»: el descarte cierra la línea de tiempo pública, con su hora y sin el motivo.
        var miCaso = await appCiudadano!.GetFromJsonAsync<Cad.Contratos.Casos.MiCasoDetalle>($"/api/casos/mios/{casoId}");
        Assert.Equal(("Descartado", "Descartado por la central."), (miCaso!.Estado, miCaso.Resultado));
        Assert.Equal(("Descartado", descarte.DescartadoEn), (miCaso.Avances[^1].Estado, miCaso.Avances[^1].En));
        Assert.DoesNotContain(miCaso.Avances, a => a.Detalle?.Contains("broma") == true);
        // El detalle y el historial lo muestran como resultado final, con el motivo para el personal.
        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains("Caso descartado", detalle);
        Assert.Contains(Motivo, detalle);
        Assert.DoesNotContain("Descartar el caso…", detalle);
        Assert.Contains("Caso descartado por la central", await operador.GetStringAsync($"/panel/casos/{casoId}/historial"));
    }

    [Theory]
    [InlineData(EstadoCaso.Asignada)]
    [InlineData(EstadoCaso.EnCamino)]
    [InlineData(EstadoCaso.EnAtencion)]
    public async Task CasoConSereno_AlDescartarlo_ElSerenoQuedaLibre_YSuDespachoDesaparece(EstadoCaso estado)
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await SosAsync();
        var operador = await PanelAsync();
        await AsignarAsync(operador, casoId, sereno.Cuenta.Id);
        if (estado >= EstadoCaso.EnCamino)
            Assert.Equal(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        if (estado >= EstadoCaso.EnAtencion)
            Assert.Equal(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
        Assert.Contains("Se retirará el despacho a", await operador.GetStringAsync($"/panel/central/sos/{casoId}"));

        Assert.Equal(HttpStatusCode.Redirect, (await DescartarAsync(operador, casoId)).StatusCode);

        Assert.Equal(EstadoCaso.Descartada, (await LeerCasoAsync(casoId)).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId && a.Vigente)));
        Assert.Equal(HttpStatusCode.NoContent, (await sereno.App.GetAsync("/api/sereno/despacho")).StatusCode);
        // El sereno ya no puede avanzar ni cerrar el caso descartado.
        Assert.NotEqual(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
    }

    [Fact]
    public async Task SinMotivo_NoSeDescarta_YSePideElMotivo()
    {
        var (casoId, _) = await SosAsync();
        var operador = await PanelAsync();

        var respuesta = await DescartarAsync(operador, casoId, motivo: "   ");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Indica por qué se descarta el caso.", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.Pendiente, (await LeerCasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task UnReenvio_NoCambiaElDescarte_YUnCasoInexistenteNoSeEncuentra()
    {
        var (casoId, _) = await SosAsync();
        var operador = await PanelAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await DescartarAsync(operador, casoId)).StatusCode);

        // Otro descarte del mismo caso (p. ej. un reenvío) no cambia nada.
        Assert.Equal(HttpStatusCode.Redirect, (await DescartarAsync(operador, casoId, "Otro motivo")).StatusCode);
        Assert.Equal(Motivo, (await fabrica.ConBaseDatosAsync(db => db.DescartesCaso.AsNoTracking().SingleAsync(d => d.CasoId == casoId))).Motivo);

        var resultado = await fabrica.ConServiciosAsync(sp => sp.GetRequiredService<DescartarCaso>()
            .EjecutarAsync(Guid.NewGuid(), "Duplicado", Motivo, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<ResultadoDescarte.NoEncontrado>(resultado);
    }

    [Fact]
    public async Task OtroPerfilDelPanel_NoDescarta()
    {
        var (casoId, _) = await SosAsync();
        var administrador = await PanelAsync(Roles.Administrador);
        var operador = await PanelAsync();
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, $"/panel/central/sos/{casoId}");

        var respuesta = await administrador.PostAsync($"/panel/central/sos/{casoId}?handler=Descartar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Descarte.MotivoCodigo"] = "Duplicado", ["Descarte.Motivo"] = Motivo, ["__RequestVerificationToken"] = token,
        }));

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Pendiente, (await LeerCasoAsync(casoId)).Estado);
    }

    [Theory]
    [InlineData(EstadoCaso.Recibido, false)]
    [InlineData(EstadoCaso.EnRevision, false)]
    [InlineData(EstadoCaso.Pendiente, true)]
    [InlineData(EstadoCaso.Asignada, true)]
    [InlineData(EstadoCaso.EnCamino, true)]
    [InlineData(EstadoCaso.EnAtencion, true)]
    [InlineData(EstadoCaso.Cerrada, false)]
    [InlineData(EstadoCaso.Cancelada, false)]
    [InlineData(EstadoCaso.Derivada, false)]
    [InlineData(EstadoCaso.Descartada, false)]
    public void SoloLosEstadosAbiertosDeLaAtencion_SeDescartan(EstadoCaso estado, bool descartable)
    {
        var caso = Caso.EmitirSos(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        typeof(Caso).GetProperty(nameof(Caso.Estado))!.SetValue(caso, estado);

        Assert.Equal(descartable, caso.PuedeDescartarse);
    }
}
