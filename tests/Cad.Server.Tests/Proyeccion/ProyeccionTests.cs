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
        Assert.Contains("en esa franja en las últimas 4 semanas", primera.Explicacion);
        // Los SOS sembrados no tienen categoría: la explicación no trae el paréntesis.
        Assert.Matches(@"^Lunes 08–12 h: \d+ casos en esa franja en las últimas 4 semanas\.$", primera.Explicacion);
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

    // Un foco los lunes entre las 8 y las 12, más un fondo repartido por el distrito, con el reloj en lunes a las 10:00.
    private async Task SembrarFocoYFondoAsync()
    {
        reloj.Ahora = new DateTimeOffset(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var azar = new Random(46);
        await SembrarAsync(ciudadano, Enumerable.Range(1, 8).SelectMany(k => Enumerable.Range(0, 6)
            .Select(n => (reloj.Ahora.AddDays(-7 * k).AddMinutes(-60 + 20 * n), LatFoco + azar.NextDouble() * 0.002, LonFoco + azar.NextDouble() * 0.002))));
        await SembrarAsync(ciudadano, Enumerable.Range(0, 120).Select(_ => (reloj.Ahora.AddHours(-azar.Next(1, 56 * 24)),
            -16.4546 + azar.NextDouble() * 0.0435, -71.5471 + azar.NextDouble() * 0.0428)));
    }

    // Rediseño del 05/11/2026: la línea de tiempo trae los bloques de 4 h de la ventana y lo esperado en cada uno.
    [Fact]
    public async Task LaVentana_TraeSusBloquesDeCuatroHoras_ConLoEsperadoEnCadaUnoYSuExplicacion()
    {
        await SembrarFocoYFondoAsync();

        var proyeccion = await ProyectarAsync();

        // 10:00 a 16:00: la mitad del bloque de 8 a 12 (que está en curso) y el de 12 a 16 entero.
        Assert.Equal(["08–12 h", "12–16 h"], proyeccion.Bloques!.Select(b => b.Rotulo));
        Assert.Equal([0.5, 1.0], proyeccion.Bloques!.Select(b => b.Fraccion));
        Assert.Equal([true, false], proyeccion.Bloques!.Select(b => b.EsAhora));
        Assert.All(proyeccion.Bloques, b => Assert.Equal("Hoy", b.Dia));
        var foco = proyeccion.Zonas[0];
        Assert.Equal(2, foco.PorBloque!.Count);
        // La ventana suma lo esperado en cada bloque por la parte que cae dentro (cada cifra va redondeada a dos decimales).
        var suma = foco.PorBloque[0] * 0.5 + foco.PorBloque[1];
        Assert.InRange(foco.Esperados, suma - 0.02, suma + 0.02);
        Assert.Equal(foco.PorBloque.Select(v => ConsultarProyeccion.NivelDe(v, new OpcionesProyeccion())), foco.NivelesPorBloque);
        // El total de cada bloque es lo esperado en toda la ciudad, y el foco de los lunes por la mañana pesa más en el bloque de 8 a 12.
        // (cada zona va redondeada a dos decimales, así que la suma puede apartarse unas centésimas del total).
        Assert.InRange(proyeccion.Bloques![0].Total, proyeccion.Zonas.Sum(z => z.PorBloque![0]) - 0.5, proyeccion.Zonas.Sum(z => z.PorBloque![0]) + 0.5);
        Assert.True(foco.PorBloque[0] > foco.PorBloque[1]);
        Assert.Contains("Lunes 08–12 h: ", foco.ExplicacionesPorBloque![0]);
    }

    [Fact]
    public async Task LaPagina_EsUnMapaConPanel_ConLaLineaDeTiempoElAvisoSobreElMapaYLaVentanaElegida()
    {
        await SembrarFocoYFondoAsync();

        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Vista=proyeccion&Ventana=6h"));

        Assert.Contains("data-focos data-vista=\"proyeccion\"", html);
        Assert.Contains("class=\"aviso-mapa\" role=\"note\" data-aviso-proyeccion", html);
        Assert.Contains("data-linea-tiempo", html);
        Assert.Contains("data-bloque=\"-1\"", html);
        Assert.Contains("data-bloque=\"0\"", html);
        Assert.Contains("data-bloque=\"1\"", html);
        Assert.DoesNotContain("data-bloque=\"2\"", html);
        Assert.Contains("name=\"Ventana\" value=\"6h\" class=\"segmentos__opcion\" aria-pressed=\"true\"", html);
        Assert.Contains("name=\"Ventana\" value=\"noche\" class=\"segmentos__opcion\" aria-pressed=\"false\"", html);
        // El mapa recibe los bloques, los umbrales y los serenos (nunca un caso).
        Assert.Contains("id=\"contexto-proyeccion\"", html);
        Assert.Contains("\"umbralAlto\":0.3", html);
        Assert.Contains("\"rotulo\":\"08", html); // el guion largo del rótulo va escapado dentro del JSON
        Assert.Contains("id=\"zonas-proyectadas\" data-zonas-proyectadas", html);
    }

    // Nivel absoluto: sin focos que lleguen al umbral Medio, nada se marca Alto ni Medio y la pantalla lo dice.
    [Fact]
    public async Task UnaSemanaTranquila_NoMarcaNingunaZonaYLaPantallaLoDice()
    {
        reloj.Ahora = new DateTimeOffset(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var azar = new Random(7);
        await SembrarAsync(ciudadano, Enumerable.Range(0, 80).Select(_ => (reloj.Ahora.AddHours(-azar.Next(1, 56 * 24)),
            -16.4546 + azar.NextDouble() * 0.0435, -71.5471 + azar.NextDouble() * 0.0428)));

        var proyeccion = await ProyectarAsync();
        var html = WebUtility.HtmlDecode(await (await OperadorAsync()).GetStringAsync("/panel/mapa-concentracion?Vista=proyeccion&Ventana=6h"));

        Assert.True(proyeccion.Suficiente);
        Assert.True(proyeccion.Tranquila);
        Assert.All(proyeccion.Zonas, z => Assert.Equal(NivelFoco.Bajo, z.Nivel));
        Assert.Contains("data-tranquila", html);
        Assert.Contains("Semana tranquila: ninguna zona llega a 0.15 casos esperados", html);
        Assert.DoesNotContain("nivel-foco--alto", html);
    }

    [Fact]
    public async Task ElNivelEsAbsoluto_ElFocoDeLosLunesEsAltoYLasZonasDeFondoNo()
    {
        await SembrarFocoYFondoAsync();

        var proyeccion = await ProyectarAsync();

        Assert.False(proyeccion.Tranquila);
        var altas = proyeccion.Zonas.Where(z => z.Nivel == NivelFoco.Alto).ToList();
        Assert.NotEmpty(altas);
        // Alto exige 0,30 casos esperados por bloque: el promedio por bloque de cada zona alta lo alcanza.
        var cobertura = proyeccion.Bloques!.Sum(b => b.Fraccion);
        Assert.All(altas, z => Assert.True(z.Esperados / cobertura >= proyeccion.UmbralAlto - 0.005));
        // Y las de fondo, que esperan casi nada, quedan en Bajo en lugar de «Medio» por estar entre las más altas.
        Assert.Contains(proyeccion.Zonas, z => z.Nivel == NivelFoco.Bajo);
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
