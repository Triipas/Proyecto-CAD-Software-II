using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Central;

// 06/11/2026 — Barra superior en dos niveles («carpeta»): fila de estado y lengüetas de las secciones; las páginas con vistas propias
// las cuelgan de su lengüeta en una hoja (sección «HojaBarra» del diseño base). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class BarraDosNivelesTests(FabricaCad fabrica)
{
    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    [Fact]
    public async Task LaCentral_TraeLaFilaDeEstadoYLasLenguetas_SinHoja_NiMenuDeHamburguesa()
    {
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync("/panel/central");

        Assert.Contains("class=\"barra__fila\"", html);
        Assert.Contains("class=\"barra__navegacion\"", html);
        Assert.Contains("aria-current=\"page\">Central</a>", html);
        Assert.DoesNotContain("barra__hoja", html);
        Assert.DoesNotContain("barra--con-hoja", html);
        Assert.DoesNotContain("data-menu", html);
    }

    [Fact]
    public async Task Casos_CuelgaSusVistasDeSuLenguetaEnLaHoja_YLaTarjetaDeFiltrosYaNoLasLleva()
    {
        var operador = await OperadorAsync();

        var html = await operador.GetStringAsync("/panel/casos");

        var cabecera = html[..html.IndexOf("</header>", StringComparison.Ordinal)];
        Assert.Contains("barra barra--con-hoja", cabecera);
        Assert.Contains("class=\"barra__hoja\"", cabecera);
        Assert.Contains("aria-label=\"Vistas de la lista\"", cabecera);
        Assert.Contains("aria-current=\"page\">Casos</a>", cabecera);
        Assert.Equal(1, html.Split("aria-label=\"Vistas de la lista\"").Length - 1); // solo en la hoja
    }
}
