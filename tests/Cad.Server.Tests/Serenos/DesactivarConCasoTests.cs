using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Serenos;

// Sereno que se desactiva con un caso en curso (decisión del 24/10/2026, opción 1; DP16-b sigue vigente): el caso sigue
// asignado, pero la central lo ve marcado en «Serenos» y en el detalle del caso. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class DesactivarConCasoTests(FabricaCad fabrica)
{
    [Fact]
    public async Task AlDesactivarseConUnCaso_ElCasoSigueAsignado_YLaCentralLoVeMarcado()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var sereno = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        var operadorCuenta = await fabrica.CrearAsync(Roles.Operador);
        var operador = fabrica.CrearNavegador();
        await operador.IngresarAlPanelAsync(operadorCuenta.Correo, operadorCuenta.Clave);
        var ruta = $"/panel/central/sos/{sos!.Id}";
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);
        Assert.DoesNotContain("data-desactivado-con-caso", await operador.GetStringAsync(ruta));

        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(false, true))).StatusCode);

        // DP16-b: el caso sigue a su nombre.
        Assert.Equal(HttpStatusCode.OK, (await sereno.GetAsync("/api/sereno/despacho")).StatusCode);
        var lista = await operador.GetStringAsync("/panel/central?handler=Serenos");
        var elemento = lista[lista.LastIndexOf("<li", lista.IndexOf($"data-sereno=\"{cuenta.Id}\"", StringComparison.Ordinal), StringComparison.Ordinal)..];
        elemento = elemento[..elemento.IndexOf("</li>", StringComparison.Ordinal)];
        Assert.Contains("Se desactivó con el caso en curso", elemento);
        Assert.Contains("se desactivó con el caso en curso", await operador.GetStringAsync(ruta));
    }
}
