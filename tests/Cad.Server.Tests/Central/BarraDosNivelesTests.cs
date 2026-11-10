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
        Assert.Matches("(?s)aria-current=\"page\"><svg.*?</svg>\\s*Central</a>", html); // con el icono de su sección
        var navegacion = html[html.IndexOf("class=\"barra__navegacion\"", StringComparison.Ordinal)..];
        var orden = new[] { "Analítica</a>", "Central</a>", "Mapa de concentración</a>", "Acompañamientos</a>", "Casos</a>" };
        var posiciones = orden.Select(x => navegacion.IndexOf(x, StringComparison.Ordinal)).ToArray();
        Assert.All(posiciones, posicion => Assert.True(posicion >= 0));
        Assert.True(posiciones.SequenceEqual(posiciones.Order()));
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
        Assert.Matches("(?s)aria-current=\"page\"><svg.*?</svg>\\s*Casos</a>", cabecera);
        Assert.Equal(1, html.Split("aria-label=\"Vistas de la lista\"").Length - 1); // solo en la hoja
    }
}
