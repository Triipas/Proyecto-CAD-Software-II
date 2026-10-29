using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Consultas;

// HU29 — Consultar el mapa de concentración de incidencias (S5-T13) y HU17-CA04 (S5-T14). Cada prueba usa una zona propia
// (lejos del resto de datos de prueba) y un periodo que empieza al iniciarse. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class MapaConcentracionTests(FabricaCad fabrica)
{
    // Una zona distinta por prueba, alineada con la cuadrícula: los casos de la misma celda caen juntos.
    private static double ZonaNueva() => Math.Floor((-12.30 - Random.Shared.Next(0, 4000) * ConsultarConcentracion.TamanoCelda) / ConsultarConcentracion.TamanoCelda)
        * ConsultarConcentracion.TamanoCelda + ConsultarConcentracion.TamanoCelda / 2;

    private const double Longitud = -76.6475;

    private async Task<HttpClient> CiudadanoAsync() => await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

    private static async Task<Guid> SosAsync(HttpClient app, double? latitud)
    {
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            latitud is { } lat ? new UbicacionSos(lat, Longitud, 10, ahora.AddSeconds(1)) : null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private static async Task<Guid> ReporteAsync(HttpClient app, double? latitud)
    {
        var ubicacion = latitud is { } lat
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, lat, Longitud, null, null)
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio");
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id,
            "Hecho ficticio", DateTimeOffset.UtcNow.AddHours(-1), ubicacion));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private async Task DecidirAsync(Guid id, TipoDecisionReporte decision)
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        var datos = decision == TipoDecisionReporte.Derivacion
            ? new DatosDecision(decision, "Motivo ficticio", "Otro", "Se comunicó", "Comisaría ficticia")
            : new DatosDecision(decision, "Motivo ficticio", Prioridad: "Media", Heridos: "No", MotivoDescarte: "Duplicado");
        Assert.IsType<ResultadoDecision.Registrado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<DecidirReporte>().EjecutarAsync(id, datos, operador.Id, CancellationToken.None)));
    }

    private Task<MapaConcentracion> MapaAsync(DateTimeOffset desde, OrigenCaso? origen = null) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarConcentracion>()
            .EjecutarAsync(new FiltroConcentracion(desde, DateTimeOffset.UtcNow.AddMinutes(1), origen), CancellationToken.None));

    private static int En(MapaConcentracion mapa, double latitud, OrigenCaso origen) =>
        mapa.Celdas.Where(c => c.Origen == origen && c.Sur <= latitud && latitud < c.Norte && c.Oeste <= Longitud && Longitud < c.Este).Sum(c => c.Casos);

    [Fact]
    public async Task CA01_CA02_LaRepresentacionSeGeneraConElPeriodoYElOrigenSeleccionados()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var app = await CiudadanoAsync();
        await SosAsync(app, zona);
        await SosAsync(app, zona + 0.0001);
        await ReporteAsync(app, zona);
        // Un SOS de la misma zona registrado fuera del periodo.
        var antiguo = await SosAsync(app, zona);
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == antiguo)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.RegistradoEn, DateTimeOffset.UtcNow.AddDays(-40))));

        var todos = await MapaAsync(desde);
        var soloSos = await MapaAsync(desde, OrigenCaso.Sos);
        var soloReportes = await MapaAsync(desde, OrigenCaso.Reporte);

        Assert.Equal((2, 1), (En(todos, zona, OrigenCaso.Sos), En(todos, zona, OrigenCaso.Reporte)));
        Assert.Equal((2, 0), (En(soloSos, zona, OrigenCaso.Sos), En(soloSos, zona, OrigenCaso.Reporte)));
        Assert.Equal((0, 1), (En(soloReportes, zona, OrigenCaso.Sos), En(soloReportes, zona, OrigenCaso.Reporte)));
        Assert.Equal(3, En(await MapaAsync(DateTimeOffset.UtcNow.AddDays(-41)), zona, OrigenCaso.Sos));
    }

    [Fact]
    public async Task CA02_SoloCasosConUbicacionValida()
    {
        var zona = ZonaNueva();
        var app = await CiudadanoAsync();
        var ids = new[]
        {
            await SosAsync(app, zona),
            await SosAsync(app, null),       // «Ubicación no disponible»
            await ReporteAsync(app, null),   // referencia en texto, sin coordenadas
        };
        // Los totales cuentan todo el periodo: los casos se llevan a un instante propio para no contar los de otras pruebas.
        var instante = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Random.Shared.Next(0, 30_000_000));
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => ids.Contains(c.Id))
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.RegistradoEn, instante)));

        var mapa = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarConcentracion>()
            .EjecutarAsync(new FiltroConcentracion(instante, instante.AddSeconds(1), null), CancellationToken.None));

        Assert.Equal(1, En(mapa, zona, OrigenCaso.Sos));
        Assert.Equal(1, mapa.Sos);
        Assert.Equal(0, mapa.Reportes);
        Assert.Equal(2, mapa.SinUbicacion);
    }

    [Fact]
    public async Task CA03_S5T14_CasosCanceladosYReportesDescartados_NoSeIncluyen()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        // La prueba anterior pudo dejar un caso sin ubicación en el último segundo: se compara con lo que ya había.
        var sinUbicacionAntes = (await MapaAsync(desde)).SinUbicacion;
        var app = await CiudadanoAsync();
        var cancelado = await SosAsync(app, zona);
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/casos/mios/{cancelado}/cancelacion", new SolicitudCancelacionSos("Falsa alarma"))).StatusCode);
        var descartado = await ReporteAsync(app, zona);
        await DecidirAsync(descartado, TipoDecisionReporte.Descarte);
        var derivado = await ReporteAsync(app, zona);
        await DecidirAsync(derivado, TipoDecisionReporte.Derivacion);
        await SosAsync(app, zona);

        var mapa = await MapaAsync(desde);

        Assert.Equal(1, En(mapa, zona, OrigenCaso.Sos));
        // El derivado sí es una incidencia (CA03 solo excluye «Cancelada» y «Descartada»).
        Assert.Equal(1, En(mapa, zona, OrigenCaso.Reporte));
        Assert.Equal(sinUbicacionAntes, mapa.SinUbicacion);
    }

    [Fact]
    public async Task S5T14_HU17_CA04_ReporteDescartado_NoEsAsignableNiSeIncluyeEnElMapa()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var descartado = await ReporteAsync(await CiudadanoAsync(), zona);
        await DecidirAsync(descartado, TipoDecisionReporte.Descarte);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        Assert.Equal(HttpStatusCode.OK, (await (await fabrica.CrearClienteAppAsync(sereno)).PutAsJsonAsync("/api/sereno/estado",
            new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        var asignacion = await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(descartado, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.IsType<ResultadoAsignacion.Rechazado>(asignacion);
        Assert.Equal(0, En(await MapaAsync(desde), zona, OrigenCaso.Reporte));
    }

    [Fact]
    public async Task CA04_SosYReportesDeLaMismaZona_SeDiferencian()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var app = await CiudadanoAsync();
        await SosAsync(app, zona);
        await ReporteAsync(app, zona);
        await ReporteAsync(app, zona);

        var mapa = await MapaAsync(desde);

        var celdas = mapa.Celdas.Where(c => c.Sur <= zona && zona < c.Norte && c.Oeste <= Longitud && Longitud < c.Este).ToList();
        Assert.Equal(2, celdas.Count);
        Assert.Equal(1, celdas.Single(c => c.Origen == OrigenCaso.Sos).Casos);
        Assert.Equal(2, celdas.Single(c => c.Origen == OrigenCaso.Reporte).Casos);
        // Misma zona, mismos límites: la diferencia está solo en el origen.
        Assert.Equal(celdas[0] with { Origen = default, Casos = 0 }, celdas[1] with { Origen = default, Casos = 0 });
    }

    [Fact]
    public async Task CA05_PosicionesDeAcompanamientoYDeSerenos_NoSeIncluyen()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var ciudadano = await CiudadanoAsync();
        var inicio = await ciudadano.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true));
        var sesion = (await inicio.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        Assert.Equal(HttpStatusCode.Created, (await ciudadano.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
            new PosicionAcompanamientoEnviada(zona, Longitud, 8, DateTimeOffset.UtcNow))).StatusCode);
        var sereno = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Sereno));
        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        Assert.True((await sereno.PostAsJsonAsync("/api/sereno/posiciones", new PosicionSerenoEnviada(zona, Longitud, 8, DateTimeOffset.UtcNow))).IsSuccessStatusCode);

        var mapa = await MapaAsync(desde);

        Assert.Equal(0, En(mapa, zona, OrigenCaso.Sos));
        Assert.Equal(0, En(mapa, zona, OrigenCaso.Reporte));
    }

    // ----- Página del panel -----

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static JsonElement Celdas(string html)
    {
        var marca = "<script type=\"application/json\" id=\"celdas-concentracion\">";
        var inicio = html.IndexOf(marca, StringComparison.Ordinal) + marca.Length;
        return JsonDocument.Parse(html[inicio..html.IndexOf("</script>", inicio, StringComparison.Ordinal)]).RootElement;
    }

    [Fact]
    public async Task MapaDeCalor_JuntaLosCasosCercanosEnPuntosDeUnos110Metros_SinCancelados()
    {
        var zona = ZonaNueva();
        var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
        var app = await CiudadanoAsync();
        await SosAsync(app, zona);
        await ReporteAsync(app, zona);
        var cancelado = await SosAsync(app, zona);
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/casos/mios/{cancelado}/cancelacion", new SolicitudCancelacionSos("Falsa alarma"))).StatusCode);

        var mapa = await MapaAsync(desde);

        // El SOS y el reporte del mismo lugar caen en un solo punto de calor, en el centro de su celda fina; el cancelado no cuenta.
        var punto = Assert.Single(mapa.Calor, p => Math.Abs(p.Latitud - zona) < ConsultarConcentracion.TamanoCalor && Math.Abs(p.Longitud - Longitud) < ConsultarConcentracion.TamanoCalor);
        Assert.Equal(2, punto.Casos);
        Assert.Equal(mapa.Sos + mapa.Reportes, mapa.Calor.Sum(p => p.Casos));
    }

    [Fact]
    public async Task Pagina_TraeLaCapaDeCalorYLaDiferenciaConAnalitica()
    {
        var html = System.Net.WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Desde=2026-01-01&Hasta=2026-01-31"));

        Assert.Contains("id=\"calor-concentracion\"", html);
        Assert.Contains("data-diferencia", html);
        Assert.Contains("href=\"/panel/analitica/repite?periodo=rango&desde=2026-01-01&hasta=2026-01-31\"", html);
    }

    [Fact]
    public async Task CA01_Pagina_SinSeleccion_MuestraLosUltimos30DiasYAmbosOrigenes()
    {
        var html = await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion");
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).DateTime);

        Assert.Contains($"value=\"{hoy.AddDays(-29):yyyy-MM-dd}\"", html);
        Assert.Contains($"value=\"{hoy:yyyy-MM-dd}\"", html);
        Assert.Contains("Concentración de SOS y reportes", html);
        Assert.Contains("data-total-sos", html);
        Assert.Contains("data-total-reportes", html);
    }

    [Fact]
    public async Task CA04_Pagina_LasCeldasLlevanSuOrigenYNingunDatoDeCasos()
    {
        var zona = ZonaNueva();
        var app = await CiudadanoAsync();
        var sos = await SosAsync(app, zona);
        var reporte = await ReporteAsync(app, zona);

        var html = await (await OperadorAsync()).GetStringAsync($"/panel/mapa-concentracion?Desde={DateTimeOffset.UtcNow.AddDays(-1):yyyy-MM-dd}&Hasta={DateTimeOffset.UtcNow.AddDays(1):yyyy-MM-dd}");

        var celdas = Celdas(html).EnumerateArray().Where(c => c.GetProperty("sur").GetDouble() <= zona && zona < c.GetProperty("norte").GetDouble()
            && c.GetProperty("oeste").GetDouble() <= Longitud && Longitud < c.GetProperty("este").GetDouble()).ToList();
        Assert.Equal(["Reporte", "SOS"], celdas.Select(c => c.GetProperty("origen").GetString()).Order());
        Assert.DoesNotContain(sos.ToString(), html);
        Assert.DoesNotContain(reporte.ToString(), html);
        Assert.DoesNotContain("Prueba Ciudadano", html);
    }

    [Fact]
    public async Task CA01_Pagina_SoloReportes_NoIncluyeSos()
    {
        var html = await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Origen=Reporte");

        Assert.Contains("Concentración de reportes", html);
        Assert.DoesNotContain("data-total-sos", html);
        Assert.All(Celdas(html).EnumerateArray(), c => Assert.Equal("Reporte", c.GetProperty("origen").GetString()));
    }

    [Theory]
    [InlineData("Desde=2026-13-01&Hasta=2026-10-16", "Escribe una fecha válida.")]
    [InlineData("Desde=2026-10-16&Hasta=2026-10-01", "La fecha final es anterior a la inicial.")]
    [InlineData("Origen=Acompanamiento", "Elige un origen de la lista.")]
    public async Task CA01_Pagina_SeleccionInvalida_NoGeneraElMapa(string consulta, string error)
    {
        var html = System.Net.WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/mapa-concentracion?{consulta}"));

        Assert.Contains(error, html);
        Assert.DoesNotContain("id=\"mapa-concentracion\"", html);
    }
}
