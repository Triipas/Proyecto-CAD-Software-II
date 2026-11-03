using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Reportes;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// Detalle del SOS y del reporte (29/10/2026): los datos del ciudadano van en la columna izquierda, fija al desplazarse y antes de
// los pasos del caso, para decidir sin bajar a buscarlos; ya no están en el expediente de la derecha. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class FichaCiudadanoSiempreVisibleTests(FabricaCad fabrica)
{
    private async Task<(HttpClient App, string Telefono, string Dni)> CiudadanoConPerfilAsync()
    {
        var dni = Random.Shared.Next(10_000_000, 99_999_999).ToString(); // el documento no se repite entre ciudadanos
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321");
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        await app.PutAsJsonAsync("/api/cuentas/yo/perfil", new SolicitudPerfilCiudadano("Rosa", "Ficticia Quispe", DatosCiudadano.Dni,
            dni, new DateOnly(1990, 5, 17), "Jr. Ficticio 123, Cercado de Lima", "Marta Ficticia", "Madre", "911222333"));
        return (app, "987654321", dni);
    }

    private async Task<HttpClient> PanelAsync()
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        return navegador;
    }

    private static int Posicion(string html, string texto)
    {
        var posicion = html.IndexOf(texto, StringComparison.Ordinal);
        Assert.True(posicion >= 0, $"No está «{texto}» en la página.");
        return posicion;
    }

    // La ficha, una sola vez, dentro de la columna izquierda: tras abrirla, antes de los pasos y antes del expediente.
    private static void AssertFichaEnLaColumnaIzquierda(string html, string dni)
    {
        var lateral = Posicion(html, "class=\"consola__lateral\"");
        var ficha = Posicion(html, "data-ficha-ciudadano");
        var pasos = Posicion(html, "class=\"consola__pasos\"");
        var trabajo = Posicion(html, "class=\"consola__trabajo\"");
        var expediente = Posicion(html, "consola__expediente");

        Assert.True(lateral < ficha && ficha < pasos && pasos < trabajo && trabajo < expediente,
            "El orden debe ser: columna izquierda (ficha y pasos), trabajo y expediente.");
        Assert.Equal(ficha, html.LastIndexOf("data-ficha-ciudadano", StringComparison.Ordinal));
        Assert.Contains("data-contacto-emergencia", html[ficha..pasos]);
        Assert.Contains("href=\"tel:911222333\"", html[ficha..pasos]);
        Assert.Contains($"DNI {dni}", html[ficha..pasos]);
    }

    [Fact]
    public async Task ElDetalleDelSos_LlevaLaFichaDelCiudadanoEnLaColumnaIzquierda()
    {
        var (app, telefono, dni) = await CiudadanoConPerfilAsync();
        var ahora = DateTimeOffset.UtcNow;
        var sos = await (await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1)))))
            .Content.ReadFromJsonAsync<RespuestaSos>();

        var html = System.Net.WebUtility.HtmlDecode(await (await PanelAsync()).GetStringAsync($"/panel/central/sos/{sos!.Id}"));

        AssertFichaEnLaColumnaIzquierda(html, dni);
        Assert.Contains($"href=\"tel:{telefono}\"", html[Posicion(html, "data-ficha-ciudadano")..Posicion(html, "class=\"consola__pasos\"")]);
    }

    [Fact]
    public async Task ElDetalleDelReporte_LlevaLaFichaDelCiudadanoEnLaColumnaIzquierda()
    {
        var (app, _, dni) = await CiudadanoConPerfilAsync();
        var id = (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(),
            CategoriaIncidencia.Iniciales[1].Id, "Hecho ficticio", DateTimeOffset.UtcNow.AddHours(-1),
            new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio")))).Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;

        var html = System.Net.WebUtility.HtmlDecode(await (await PanelAsync()).GetStringAsync($"/panel/reportes/{id}"));

        AssertFichaEnLaColumnaIzquierda(html, dni);
    }
}
