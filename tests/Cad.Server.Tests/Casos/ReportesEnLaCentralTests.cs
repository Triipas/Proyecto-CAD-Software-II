using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Casos;

// 06/11/2026 — «Reportes» deja de ser una sección del panel: sus reportes por revisar son la pestaña «Reportes» de la central, y
// «Nuevo reporte» y los criterios de triaje se abren en el panel del caso. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class ReportesEnLaCentralTests(FabricaCad fabrica)
{
    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    [Fact]
    public async Task LaAntiguaListaDeReportes_LlevaALaPestanaReportesDeLaCentral()
    {
        var operador = await OperadorAsync();

        var respuesta = await operador.GetAsync("/panel/reportes");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/panel/central?pestana=reportes", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task LaCentral_OfreceNuevoReporteYLosCriteriosEnLaPestanaReportes_YLaBarraYaNoTraeLaSeccion()
    {
        var operador = await OperadorAsync();

        var central = await operador.GetStringAsync("/panel/central");

        var pestana = central[central.IndexOf("data-grupo-pestana=\"reportes\"", StringComparison.Ordinal)..];
        Assert.Contains("<a class=\"boton boton--secundario\" href=\"/panel/reportes/nuevo\" data-abrir-panel>", pestana);
        Assert.Contains("href=\"/panel/criterios-triaje\" data-abrir-panel", pestana);
        Assert.DoesNotContain("href=\"/panel/reportes\"", central);
    }

    [Fact]
    public async Task NuevoReporte_EnModoPanel_VaSinLaBarra_YEnPaginaCompletaConsuVolverALaCentral()
    {
        var operador = await OperadorAsync();

        var panel = await operador.GetStringAsync("/panel/reportes/nuevo?vista=panel");
        var completa = await operador.GetStringAsync("/panel/reportes/nuevo");

        Assert.Contains("data-en-panel=\"true\"", panel);
        Assert.DoesNotContain("data-barra", panel);
        Assert.DoesNotContain("enlace-volver", panel);
        Assert.Contains("data-cerrar-panel-pagina", panel); // «Cancelar» cierra el panel
        Assert.Contains("data-barra", completa);
        Assert.Contains("href=\"/panel/central?pestana=reportes\">← Central", completa);
    }

    [Fact]
    public async Task LosCriteriosDeTriaje_SePuedenVerEnElPanel()
    {
        var operador = await OperadorAsync();

        var panel = await operador.GetStringAsync("/panel/criterios-triaje?vista=panel");

        Assert.Contains("data-en-panel=\"true\"", panel);
        Assert.Contains("data-criterios", panel);
    }

    [Fact]
    public async Task ElReporteDeUnaLlamada_AlRegistrarse_QuedaAdmitidoYSeAbreSuDetalleEnElPanel()
    {
        var operador = await OperadorAsync();
        var formulario = new Dictionary<string, string>
        {
            ["Reporte.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["Reporte.CategoriaId"] = CategoriaIncidencia.Iniciales[1].Id.ToString(),
            ["Reporte.Descripcion"] = "Llamada de un vecino por un robo (ficticio)",
            ["Reporte.OcurridoEn"] = DateTime.UtcNow.AddHours(-8).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture), // hora local ya pasada
            ["Reporte.Referencia"] = "Frente a la plaza (ficticio)",
            ["Reporte.Canal"] = Cad.Contratos.Casos.CanalesEntrada.DeLaCentral[0].Codigo,
            ["Reporte.Prioridad"] = Cad.Contratos.Casos.PrioridadesCaso.Todas[0].Codigo,
            ["Reporte.Heridos"] = Cad.Contratos.Casos.PersonasHeridas.Todas[0].Codigo,
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, "/panel/reportes/nuevo"),
        };

        var respuesta = await operador.PostAsync("/panel/reportes/nuevo", new FormUrlEncodedContent(formulario));

        Assert.True(respuesta.StatusCode == HttpStatusCode.Redirect, await respuesta.Content.ReadAsStringAsync());
        var detalle = respuesta.Headers.Location!.ToString();
        Assert.StartsWith("/panel/reportes/", detalle);
        Assert.Contains("Admitido", await operador.GetStringAsync($"{detalle}?vista=panel"));
        // Ya está en «Por asignar» de la central, con la hora del hecho en su tarjeta de «Por revisar» cuando corresponda.
        Assert.Contains(detalle.Split('/').Last(), await operador.GetStringAsync("/panel/central"));
    }
}
