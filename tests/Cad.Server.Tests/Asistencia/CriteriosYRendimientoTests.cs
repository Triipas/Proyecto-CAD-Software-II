using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Asistencia;

// S6-T01 — una sola fuente de criterios: la misma que recibe el asistente y la que leen los operadores.
public class CriteriosTriajeTests
{
    [Fact]
    public void ElDocumentoEstaIncrustadoConSuVersion()
    {
        Assert.Equal("2026-11", CriteriosTriaje.Version);
        Assert.Contains("## 3. Derivar", CriteriosTriaje.Texto);
    }

    [Fact]
    public void ElPromptLlevaLosCriteriosLiterales() =>
        Assert.Contains(CriteriosTriaje.Texto, PromptAsistente.Sistema(DatosAsistencia.Catalogo));
}

[Collection(ColeccionServidor.Nombre)]
public sealed class CriteriosYRendimientoTests(FabricaCad fabrica) : IAsyncLifetime
{
    private readonly AsistenteFalso falso = new();
    private WebApplicationFactory<Program> servidor = null!;

    public Task InitializeAsync()
    {
        servidor = fabrica.WithWebHostBuilder(b =>
        {
            b.UseSetting("Ia:Habilitada", "true");
            b.ConfigureTestServices(s => s.AddScoped<IAsistenteReportes>(_ => falso));
        });
        _ = servidor.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await servidor.DisposeAsync();

    [Fact]
    public async Task LaPaginaDeCriterios_MuestraElMismoTextoQueRecibeElAsistente()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/criterios-triaje"));

        Assert.Contains("data-version=\"2026-11\"", html);
        Assert.Contains("<h2 class=\"criterios__titulo\">3. Derivar</h2>", html);
        Assert.Contains("Incendio o humo: Bomberos (116).", html);
    }

    // S6-T17: con un modelo propio de la prueba, la categoría y la decisión aceptadas se deducen de lo que registró el operador.
    [Fact]
    public async Task Rendimiento_CuentaLoQueAceptaronLosOperadoresPorModelo()
    {
        var modelo = "modelo-rendimiento-" + Guid.NewGuid().ToString("N")[..6];
        falso.Responder = (_, _) => Task.FromResult<ResultadoAsistente?>(new(DatosAsistencia.Analisis(CategoriaIncidencia.RoboOHurto.Id), modelo,
            TokensEntrada: 1000, TokensSalida: 100));
        var app = await ReportesDePrueba.CiudadanoAsync(fabrica, servidor);
        var aceptado = await ReportesDePrueba.RegistrarAsync(app, "Me arrebataron el celular en el paradero", CategoriaIncidencia.Otros.Id);
        var rechazado = await ReportesDePrueba.RegistrarAsync(app, "Me arrebataron la mochila en el parque", CategoriaIncidencia.Otros.Id);
        var pendiente = await ReportesDePrueba.RegistrarAsync(app, "Me arrebataron la cartera en la esquina", CategoriaIncidencia.Otros.Id);
        foreach (var id in new[] { aceptado, rechazado, pendiente })
            Assert.True(await CentralDePrueba.EsperarAsync(() => fabrica.ConBaseDatosAsync(db => db.SugerenciasReporte.AnyAsync(s => s.CasoId == id)).Result));

        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        await fabrica.ConServiciosAsync(async s =>
        {
            var revisar = s.GetRequiredService<RevisarReporte>();
            var decidir = s.GetRequiredService<DecidirReporte>();
            await revisar.IniciarAsync(aceptado, operador, CancellationToken.None);
            await revisar.CorregirCategoriaAsync(aceptado, CategoriaIncidencia.RoboOHurto.Id, operador, CancellationToken.None);
            await decidir.EjecutarAsync(aceptado, new DatosDecision(TipoDecisionReporte.Admision, "Robo con ubicación.",
                Prioridad: PrioridadesCaso.Media, Heridos: PersonasHeridas.No), operador, CancellationToken.None);
            await revisar.IniciarAsync(rechazado, operador, CancellationToken.None);
            return await decidir.EjecutarAsync(rechazado, new DatosDecision(TipoDecisionReporte.Descarte, "No era un robo.",
                MotivoDescarte: MotivosDescarte.NoVerificable), operador, CancellationToken.None);
        });

        var rendimiento = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarAsistencia>()
            .RendimientoAsync(DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None));

        var fila = Assert.Single(rendimiento, r => r.Modelo == modelo);
        Assert.Equal((3, 2, 1, 2, 1), (fila.Sugerencias, fila.Decididas, fila.CategoriaAceptada, fila.DecisionComparable, fila.DecisionAceptada));
        Assert.Equal((50, 50), (fila.PorcentajeCategoria, fila.PorcentajeDecision));
        Assert.Equal((3000L, 300L), (fila.TokensEntrada, fila.TokensSalida));
    }
}
