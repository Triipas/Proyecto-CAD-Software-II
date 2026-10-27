using System.Net;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// La página «Preguntas del analista» (27/10/2026): cada pregunta responde con sus estados (sin datos, pocos datos, con datos), el
// periodo se valida y el informe se descarga. Los datos usan fechas propias de 2017, antes de cualquier otro dato. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PaginaAnaliticaTests(FabricaCad fabrica)
{
    private static DateTimeOffset Local(int mes, int dia, int hora) => new DateTimeOffset(2017, mes, dia, hora, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
    private static string Rango(int mes, int desde, int hasta) => $"periodo=rango&desde=2017-{mes:00}-{desde:00}&hasta=2017-{mes:00}-{hasta:00}";

    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    // SOS atendidos: la atención empieza a los minutos indicados desde el registro.
    private async Task SosAtendidosAsync(int mes, params double[] minutos)
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        await Guardar(db =>
        {
            for (var i = 0; i < minutos.Length; i++)
            {
                var sos = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, Local(mes, 2 + i, 10));
                db.Casos.Add(sos);
                db.EventosCaso.Add(EventoCaso.Registrar(sos, TipoEventoCaso.AtencionIniciada, sereno, VisibilidadEvento.Interna, sos.RegistradoEn.AddMinutes(minutos[i])));
            }
        });
    }

    [Theory]
    [InlineData("tiempo")]
    [InlineData("falsos")]
    [InlineData("cobertura")]
    [InlineData("repite")]
    [InlineData("sistema")]
    public async Task SinDatosEnElPeriodo_LaPreguntaLoDiceSinInventarCeros(string pregunta)
    {
        var html = await (await OperadorAsync()).GetStringAsync($"/panel/analitica/{pregunta}?{Rango(1, 1, 7)}");

        Assert.Contains($"data-analitica=\"{pregunta}\"", html);
        Assert.Contains("data-estado=\"vacio\"", html);
        Assert.DoesNotContain("data-respuesta=", html);
    }

    [Fact]
    public async Task Ciudadano_SinEvaluaciones_LoDiceYMantieneLaAdopcion()
    {
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/analitica/ciudadano?{Rango(1, 1, 7)}"));

        Assert.Contains("data-evaluados=\"0\"", html);
        Assert.Contains("Nadie evaluó la atención", html);
        Assert.Contains("¿Vuelven a usar la app?", html);
    }

    [Fact]
    public async Task Ahora_MuestraLosCarrilesYElIndicadorEnVivo()
    {
        var html = await (await OperadorAsync()).GetStringAsync("/panel/analitica/ahora");

        Assert.Contains("data-region-ahora", html);
        Assert.Contains("data-vivo", html);
        Assert.Contains("data-etapa=\"Pendiente\"", html);
        Assert.DoesNotContain("data-filtros", html); // «Ahora» no tiene periodo
    }

    [Fact]
    public async Task Tiempo_ConPocosCasos_MuestraCadaSosSinMedianas()
    {
        await SosAtendidosAsync(3, 8, 22);

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/analitica/tiempo?{Rango(3, 1, 28)}"));

        Assert.Contains("data-casos=\"2\"", html);
        Assert.Contains("Pocos casos para dar porcentajes", html);
        Assert.Contains("1 se atendió dentro de los 15 min", html);
        Assert.DoesNotContain("La mitad de los SOS", html);
    }

    [Fact]
    public async Task Tiempo_ConCasosSuficientes_RespondeConLaMedianaYElP90()
    {
        await SosAtendidosAsync(4, 6, 8, 10, 12, 30);

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/analitica/tiempo?{Rango(4, 1, 28)}"));

        Assert.Contains("data-casos=\"5\"", html);
        Assert.Contains("La mitad de los SOS se atendió en <em>10 min</em>", html);
        Assert.Contains("class=\"regla\"", html);
    }

    [Fact]
    public async Task Tiempo_UnSosDescartadoTrasLaAtencion_NoEntraEnLaMuestra()
    {
        await SosAtendidosAsync(6, 9, 11);
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var descartado = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, Local(6, 10, 10));
        descartado.Asignar(); descartado.ConfirmarDesplazamiento(); descartado.IniciarAtencion(); descartado.Descartar();
        await Guardar(db =>
        {
            db.Casos.Add(descartado);
            db.EventosCaso.Add(EventoCaso.Registrar(descartado, TipoEventoCaso.AtencionIniciada, sereno, VisibilidadEvento.Interna, descartado.RegistradoEn.AddMinutes(40)));
        });

        var html = await (await OperadorAsync()).GetStringAsync($"/panel/analitica/tiempo?{Rango(6, 1, 28)}");

        Assert.Contains("data-casos=\"2\"", html);
    }

    [Fact]
    public async Task ColumnaDePreguntas_MarcaLaActualYEnlazaLasSiete()
    {
        var html = await (await OperadorAsync()).GetStringAsync($"/panel/analitica/falsos?{Rango(1, 1, 7)}");

        Assert.Equal(7, html.Split("class=\"pregunta\"").Length - 1);
        Assert.Contains("aria-current=\"page\" data-pregunta=\"falsos\"", html);
        Assert.Contains("data-informe", html);
    }

    [Fact]
    public async Task PreguntaDesconocida_Responde404()
    {
        var respuesta = await (await OperadorAsync()).GetAsync("/panel/analitica/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task FechasInvertidas_AvisaYMuestraLosUltimos7Dias()
    {
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/analitica/tiempo?periodo=rango&desde=2017-02-10&hasta=2017-02-01"));

        Assert.Contains("La fecha final es anterior a la inicial", html);
        Assert.Contains("aria-current=\"true\">7 días", html);
    }

    [Fact]
    public async Task Informe_SeDescargaEnCsvConElPeriodo()
    {
        var respuesta = await (await OperadorAsync()).GetAsync($"/panel/analitica?handler=Informe&{Rango(1, 1, 7)}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("text/csv", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal("informe-analitica-2017-01-01.csv", respuesta.Content.Headers.ContentDisposition!.FileNameStar ?? respuesta.Content.Headers.ContentDisposition.FileName);
    }

    [Fact]
    public async Task Repite_EnlazaAlMapaDeConcentracionConElMismoPeriodo()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        await Guardar(db =>
        {
            for (var i = 0; i < 3; i++)
                db.Casos.Add(Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(-16.40, -71.54, 10, Local(5, 3 + i, 9), Local(5, 3 + i, 9)), Local(5, 3 + i, 9)));
        });

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync($"/panel/analitica/repite?{Rango(5, 1, 20)}"));

        Assert.Contains("data-respuesta=\"repite\"", html);
        Assert.Contains("/panel/mapa-concentracion?Desde=2017-05-01&Hasta=2017-05-20", html);
    }
}
