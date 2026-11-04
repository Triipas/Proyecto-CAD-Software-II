using System.Net;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Proyeccion;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Proyeccion;

// Reloj de la prueba: el servidor «vive» en el momento que diga la prueba, para proyectar sobre casos sembrados en un periodo propio.
public sealed class RelojAjustable(DateTimeOffset ahora) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = ahora;
    public override DateTimeOffset GetUtcNow() => Ahora.ToUniversalTime();
}

// HU46 (S6-T19 a S6-T21; plan, 8.7) — la proyección de focos contra PostgreSQL, con casos sembrados en 2003 (un periodo que ninguna otra
// prueba usa) y un reloj fijo. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public sealed class ProyeccionTests(FabricaCad fabrica) : IAsyncLifetime
{
    private readonly RelojAjustable reloj = new(new DateTimeOffset(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5)));
    private WebApplicationFactory<Program> servidor = null!;

    // Mercado de productores: el foco de la prueba. Otra zona con casos cancelados o descartados, que no deben contar (CA02).
    private const double LatFoco = -16.4234, LonFoco = -71.5365, LatExcluidos = -16.4400, LonExcluidos = -71.5100;

    public Task InitializeAsync()
    {
        servidor = fabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(reloj)));
        _ = servidor.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await servidor.DisposeAsync();

    private async Task<List<Guid>> SembrarAsync(Guid ciudadano, IEnumerable<(DateTimeOffset En, double Lat, double Lon)> puntos, Action<Caso>? terminar = null) =>
        await fabrica.ConBaseDatosAsync(async db =>
        {
            var casos = puntos.Select(p =>
            {
                var en = p.En.ToUniversalTime();
                var caso = Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(p.Lat, p.Lon, 10, en, en), en);
                terminar?.Invoke(caso);
                return caso;
            }).ToList();
            db.Casos.AddRange(casos);
            await db.SaveChangesAsync();
            return casos.Select(c => c.Id).ToList();
        });

    private Task<ResultadoProyeccion> ProyectarAsync(VentanaProyeccion ventana = VentanaProyeccion.ProximasSeisHoras) =>
        servidor.Services.CreateScope().ServiceProvider.GetRequiredService<ConsultarProyeccion>().EjecutarAsync(ventana, CancellationToken.None);

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = servidor.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    [Fact]
    public async Task CA05_ConPocosCasos_NoHayDatosSuficientesParaProyectar()
    {
        reloj.Ahora = new DateTimeOffset(2004, 1, 5, 10, 0, 0, TimeSpan.FromHours(-5));
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        await SembrarAsync(ciudadano, Enumerable.Range(1, 10).Select(d => (reloj.Ahora.AddDays(-d), LatFoco, LonFoco)));

        var proyeccion = await ProyectarAsync();

        Assert.False(proyeccion.Suficiente);
        Assert.Equal(10, proyeccion.CasosRecientes);
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Vista=proyeccion&Ventana=6h"));
        Assert.Contains("No hay datos suficientes para proyectar", html);
    }

    [Fact]
    public async Task CA01_CA02_CA03_CA04_CA08_CA10_LaZonaConElFocoEsAlta_SinCasosExcluidosNiDatosDeCasos()
    {
        reloj.Ahora = new DateTimeOffset(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5)); // lunes, 10:00
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var azar = new Random(46);
        // El foco: 6 casos cada lunes entre las 8 y las 12 de las últimas 8 semanas.
        var foco = await SembrarAsync(ciudadano, Enumerable.Range(1, 8).SelectMany(k => Enumerable.Range(0, 6)
            .Select(n => (reloj.Ahora.AddDays(-7 * k).AddMinutes(-60 + 20 * n), LatFoco + azar.NextDouble() * 0.002, LonFoco + azar.NextDouble() * 0.002))));
        // Fondo repartido por el distrito, en cualquier momento de las últimas 8 semanas.
        await SembrarAsync(ciudadano, Enumerable.Range(0, 120).Select(_ => (reloj.Ahora.AddHours(-azar.Next(1, 56 * 24)),
            -16.4546 + azar.NextDouble() * 0.0435, -71.5471 + azar.NextDouble() * 0.0428)));
        // Más casos que el foco en otra zona, pero cancelados (CA02): no cuentan.
        await SembrarAsync(ciudadano, Enumerable.Range(1, 8).SelectMany(k => Enumerable.Range(0, 10)
            .Select(n => (reloj.Ahora.AddDays(-7 * k).AddMinutes(-30 + n), LatExcluidos, LonExcluidos))), c => c.Cancelar());

        var proyeccion = await ProyectarAsync();

        Assert.True(proyeccion.Suficiente);
        Assert.True(proyeccion.ConModelo); // el modelo incrustado se cargó
        var primera = proyeccion.Principales(10)[0];
        Assert.Equal(Cad.Proyeccion.Celda.De(LatFoco, LonFoco).Clave, primera.Clave);
        Assert.Equal(NivelFoco.Alto, primera.Nivel);
        Assert.Contains("Lunes 08–12 h: ", primera.Explicacion);
        Assert.Contains("en las últimas 4 semanas", primera.Explicacion);
        var excluida = Cad.Proyeccion.Celda.De(LatExcluidos, LonExcluidos).Clave;
        Assert.DoesNotContain(proyeccion.Principales(3), z => z.Clave == excluida);
        // CA04: sin sereno operativo a menos de 1,5 km, «Sin cobertura cercana».
        Assert.All(proyeccion.Zonas.Where(z => z.Nivel == NivelFoco.Alto),
            z => Assert.Equal(z.MetrosSerenoCercano is null or > 1500, z.SinCobertura));

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Vista=proyeccion&Ventana=6h"));
        Assert.Contains("Proyección calculada sobre datos simulados", html);                                        // CA08
        Assert.Contains($"data-zona=\"{primera.Clave}\" data-nivel=\"Alto\"", html);
        Assert.Contains("nivel-foco--alto\">Alto</span>", html);                                                     // el nivel en texto
        Assert.Contains("data-parte-origen=\"plantilla\"", html);                                                    // CA10: sin IA, plantilla
        Assert.DoesNotContain(foco[0].ToString(), html);                                                             // CA03
        Assert.DoesNotContain(LatFoco.ToString(System.Globalization.CultureInfo.InvariantCulture), html);
    }

    [Fact]
    public async Task LasOtrasVentanas_EstaNocheYManana_TambienSeProyectan()
    {
        reloj.Ahora = new DateTimeOffset(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var azar = new Random(7);
        await SembrarAsync(ciudadano, Enumerable.Range(0, 80).Select(_ => (reloj.Ahora.AddHours(-azar.Next(1, 56 * 24)),
            -16.4546 + azar.NextDouble() * 0.0435, -71.5471 + azar.NextDouble() * 0.0428)));

        var noche = await ProyectarAsync(VentanaProyeccion.EstaNoche);
        var manana = await ProyectarAsync(VentanaProyeccion.Manana);

        Assert.True(noche.Suficiente && manana.Suficiente);
        Assert.Equal(6, (noche.Hasta - noche.Desde).TotalHours);
        Assert.True(manana.Zonas.Sum(z => z.Esperados) > noche.Zonas.Sum(z => z.Esperados)); // 24 h esperan más casos que 6
    }

    [Fact]
    public void ElModeloIncrustado_SeCargaConSuPesoYSeMideSuMemoria()
    {
        var modelo = fabrica.Services.GetRequiredService<ModeloProyeccion>().Modelo;

        Assert.NotNull(modelo);
        Assert.Equal("A", modelo.Metadatos.Mundo);
        Assert.InRange(modelo.Metadatos.PesoModelo, 0, 1);
        Assert.Equal(Cad.Proyeccion.FilaRasgos.Nombres, modelo.Metadatos.Rasgos);
        Assert.True(ModeloProyeccion.MemoriaBytes > 0);
    }
}
