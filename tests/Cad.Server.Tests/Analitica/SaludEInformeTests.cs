using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Notificaciones;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// Bloques E y F de la analítica (27/10/2026): salud del sistema, casos atascados, demanda esperada e informe del periodo. Los
// cálculos por periodo usan fechas propias (diciembre de 2020 y enero de 2021), antes de cualquier otro dato. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class SaludEInformeTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private static DateTimeOffset Local(int anio, int mes, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(anio, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);
    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    private static Caso Sos(Guid ciudadano, DateTimeOffset en, double? precision = 8) =>
        Caso.EmitirSos(ciudadano, Guid.NewGuid(), precision is { } p ? Posicion.ComoActual(-12.05, -77.04, p, en, en) : null, en);

    [Fact]
    public async Task AbrirElDetalle_AnotaLaPrimeraAperturaUnaSolaVez()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = (await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var panel = fabrica.CrearNavegador();
        await panel.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        Task<DateTimeOffset?> Apertura() => fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == sos.Id).Select(c => c.AbiertoEnCentralEn).SingleAsync());

        Assert.Null(await Apertura());
        Assert.Equal(HttpStatusCode.OK, (await panel.GetAsync($"/panel/central/sos/{sos.Id}")).StatusCode);
        var primera = await Apertura();
        Assert.NotNull(primera);
        await panel.GetAsync($"/panel/central/sos/{sos.Id}");
        Assert.Equal(primera, await Apertura());
    }

    [Fact]
    public async Task CadaAvisoPushAlSereno_QuedaConSuResultado()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        Task Avisar() => Servicio(async s => { await s.GetRequiredService<AvisarDespacho>().EjecutarAsync(sereno.Id, CancellationToken.None); return 0; });

        await Avisar(); // sin dispositivo registrado
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PutAsJsonAsync("/api/sereno/dispositivo", new SolicitudRegistroDispositivo($"token-{Guid.NewGuid():N}"))).StatusCode);
        await Avisar();

        Assert.Equal([EnvioPush.SinDispositivo, "Enviada"], await fabrica.ConBaseDatosAsync(db =>
            db.EnviosPush.Where(e => e.SerenoId == sereno.Id).OrderBy(e => e.EnviadoEn).Select(e => e.Resultado).ToListAsync()));
    }

    [Fact]
    public async Task Salud_UbicacionPresentacionAperturaPushEIniciosDeAtencion()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var en = Local(2020, 12, 2, 10);
        var casos = new[] { Sos(ciudadano, en, 8), Sos(ciudadano, en, 150), Sos(ciudadano, en, null) };
        await Guardar(db =>
        {
            db.Casos.AddRange(casos);
            db.EnviosPush.AddRange(EnvioPush.Registrar(sereno, "Enviada", en), EnvioPush.Registrar(sereno, "Enviada", en), EnvioPush.Registrar(sereno, "NoEnviada", en));
            db.IniciosAtencion.Add(InicioAtencion.RegistrarPorCentral(casos[0], sereno, operador, "Sin señal (ficticio)", en.AddMinutes(10)));
            db.IniciosAtencion.Add(InicioAtencion.Registrar(casos[1], sereno, null, 900, 150, false, "SinPosicion", null, en.AddMinutes(12)));
        });
        await fabrica.ConBaseDatosAsync(async db =>
        {
            await db.Casos.Where(c => c.Id == casos[0].Id).ExecuteUpdateAsync(c => c
                .SetProperty(x => x.PresentadoEnCentralEn, en.AddSeconds(2)).SetProperty(x => x.AbiertoEnCentralEn, en.AddSeconds(30)));
            return await db.Casos.Where(c => c.Id == casos[1].Id).ExecuteUpdateAsync(c => c
                .SetProperty(x => x.PresentadoEnCentralEn, en.AddSeconds(4)).SetProperty(x => x.AbiertoEnCentralEn, en.AddSeconds(90)));
        });

        var s = await Servicio(sp => sp.GetRequiredService<ConsultarSaludSistema>()
            .EjecutarAsync(new PeriodoAnalitica(Local(2020, 12, 2, 0), Local(2020, 12, 3, 0), Lima), CancellationToken.None));

        Assert.Equal(3, s.Sos);
        Assert.Equal(100.0 / 3, s.PorcentajeSinUbicacion, 6);
        Assert.Equal((79.0, 50.0), (s.PrecisionP50Metros, s.PorcentajePrecisionBaja));
        Assert.Equal((2, 3.0), (s.Presentados, s.PresentacionP50Segundos!.Value));
        Assert.Equal((2, 60.0), (s.Abiertos, s.AperturaP50Segundos!.Value));
        Assert.Equal(200.0 / 3, s.PorcentajePushEnviadas!.Value, 6);
        Assert.Equal((2, 50.0, 50.0), (s.IniciosAtencion, s.PorcentajeRegistradosPorCentral, s.PorcentajeFueraDeRango));
    }

    [Fact]
    public async Task CasosAtascados_MideDesdeQueEntraronASuEtapa()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var ahora = DateTimeOffset.UtcNow;
        var atascado = Sos(ciudadano, ahora.AddMinutes(-10)); // pendiente hace 10 min (umbral: 3)
        var reciente = Sos(ciudadano, ahora.AddMinutes(-1));
        var asignado = Sos(ciudadano, ahora.AddHours(-1)); // registrado hace una hora, pero asignado hace 2 min (umbral: 5)
        asignado.Asignar();
        await Guardar(db =>
        {
            db.Casos.AddRange(atascado, reciente, asignado);
            db.EventosCaso.Add(EventoCaso.Registrar(asignado, TipoEventoCaso.CasoAsignado, operador, VisibilidadEvento.Interna, ahora.AddMinutes(-2)));
            db.Asignaciones.Add(Asignacion.Registrar(asignado, sereno, operador, Guid.NewGuid(), ahora.AddMinutes(-2)));
        });

        var r = await Servicio(s => s.GetRequiredService<ConsultarCasosAtascados>().EjecutarAsync(CancellationToken.None));

        var ids = r.Atascados.Select(a => a.CasoId).ToHashSet();
        Assert.Contains(atascado.Id, ids);
        Assert.DoesNotContain(reciente.Id, ids);
        Assert.DoesNotContain(asignado.Id, ids);
        var fila = r.Atascados.Single(a => a.CasoId == atascado.Id);
        Assert.Equal((EstadoCaso.Pendiente, 3), (fila.Estado, fila.UmbralMinutos));
        Assert.InRange(fila.Minutos, 9.5, 12);
        Assert.Contains(r.PorEtapa, e => e.Estado == EstadoCaso.Asignada && !e.Reporte && e.Casos >= 1);
    }

    [Fact]
    public async Task DemandaEsperada_PromedioMinimoYMaximoDeLasCuatroSemanasAnteriores()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        // Lunes 28/12/2020: los cuatro lunes anteriores, a las 9, tuvieron 1, 2, 0 y 3 casos.
        int[] porSemana = [1, 2, 0, 3];
        await Guardar(db =>
        {
            for (var s = 0; s < porSemana.Length; s++)
                for (var n = 0; n < porSemana[s]; n++)
                    db.Casos.Add(Sos(ciudadano, Local(2020, 12, 21, 9, 10 + n).AddDays(-7 * s)));
        });

        var demanda = await Servicio(s => s.GetRequiredService<ConsultarDemandaEsperada>()
            .EjecutarAsync(new DateOnly(2020, 12, 28), Lima, CancellationToken.None));

        Assert.Equal(24, demanda.Count);
        Assert.Equal(new DemandaHora(9, 1.5, 0, 3), demanda[9]);
        Assert.Equal(new DemandaHora(10, 0, 0, 0), demanda[10]);
    }

    [Fact]
    public async Task Informe_ComparaConElPeriodoAnterior_YSeExportaACsv()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var inicio = Local(2021, 1, 4, 0);
        var semana = Enumerable.Range(0, 3).Select(i => Sos(ciudadano, inicio.AddDays(i + 1))).ToList();
        semana[2].Cancelar();
        var anterior = Sos(ciudadano, inicio.AddDays(-3));
        // Dos cierres en la semana: uno resuelto y otro sin hallazgo. Un reporte del ciudadano, descartado por falso.
        Caso Cerrado(string resultado, out CierreCaso cierre)
        {
            var caso = Sos(ciudadano, inicio.AddDays(2));
            caso.Asignar(); caso.ConfirmarDesplazamiento(); caso.IniciarAtencion(); caso.Cerrar();
            cierre = CierreCaso.Registrar(caso, Robo, resultado, "Resultado ficticio", null, sereno, inicio.AddDays(2).AddHours(1));
            return caso;
        }
        var resuelto = Cerrado(ResultadosAtencion.ResueltoEnElLugar, out var c1);
        var sinHallazgo = Cerrado(ResultadosAtencion.SinHallazgo, out var c2);
        var reporte = Caso.RegistrarReporte(ciudadano, Guid.NewGuid(), Robo, inicio.AddDays(3));
        await Guardar(db =>
        {
            db.Casos.AddRange([.. semana, anterior, resuelto, sinHallazgo, reporte]);
            db.CierresCaso.AddRange(c1, c2);
            db.DetallesReporte.Add(DetalleReporte.Registrar(reporte, Robo, "Descripción ficticia", inicio.AddDays(3), "Texto", null, null, null, "Referencia"));
            db.DecisionesReporte.Add(DecisionReporte.Registrar(reporte, TipoDecisionReporte.Descarte, "Broma (ficticio)", null, null, operador,
                inicio.AddDays(3).AddHours(1), null, MotivosDescarte.FalsoOMalintencionado));
        });

        var informe = await Servicio(s => s.GetRequiredService<GenerarInformePeriodo>()
            .EjecutarAsync(new PeriodoAnalitica(inicio, inicio.AddDays(7), Lima), CancellationToken.None));

        IndicadorInforme Indicador(string nombre) => informe.Indicadores.Single(i => i.Nombre == nombre);
        Assert.Equal(inicio.AddDays(-7), informe.Anterior.Desde);
        Assert.Equal((5.0, 1.0, 4.0, 400.0), (Indicador("SOS").Actual!.Value, Indicador("SOS").Anterior!.Value, Indicador("SOS").Variacion!.Value,
            Indicador("SOS").VariacionPorcentual!.Value));
        Assert.Equal(50, Indicador("Efectividad de la atención").Actual);
        Assert.Equal(100, Indicador("Índice de reportes falsos").Actual);
        Assert.Null(Indicador("Índice de reportes falsos").Anterior); // sin decisiones antes: no hay índice, no un 0 inventado
        Assert.Equal(20, Indicador("SOS cancelados por el ciudadano").Actual);

        var csv = informe.ACsv();
        Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
        var texto = Encoding.UTF8.GetString(csv[3..]);
        Assert.Contains("Grupo;Indicador;Unidad;Periodo;Periodo anterior;Variación;Variación %", texto);
        Assert.Contains("Demanda;SOS;casos;5;1;4;400", texto);
        Assert.Contains("MVP académico · datos simulados", texto);
    }
}
