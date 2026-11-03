using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// Bloque B de la analítica (27/10/2026): demanda frente a oferta por hora, zonas sin cobertura y tiempo de respuesta por zona.
// Cada prueba usa su propio día de 2020, antes de cualquier otro dato de la base de pruebas, para que los cálculos solo vean
// los datos que arma. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CoberturaYRecursosTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");

    // Centro de una celda de la cuadrícula (0,005°) y otra a unos 5,5 km.
    private const double LatA = -12.0525, LonA = -77.0425;
    private const double LatB = -12.0025, LonB = -77.0425;
    private const double LatC = -12.1025, LonC = -77.0925;

    // Hora de Lima, guardada en UTC (Npgsql solo admite timestamptz en UTC).
    private static DateTimeOffset Local(int anio, int mes, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(anio, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);

    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    private static Caso Sos(Guid ciudadano, DateTimeOffset en, double? lat = null, double? lon = null) =>
        Caso.EmitirSos(ciudadano, Guid.NewGuid(), lat is null ? null : Posicion.ComoActual(lat.Value, lon!.Value, 5, en, en), en);

    // Un sereno con su historial: operativo en cada tramo indicado.
    private static IEnumerable<CambioEstadoSereno> Turnos(Guid sereno, params (DateTimeOffset Desde, DateTimeOffset Hasta)[] tramos)
    {
        var estado = EstadoSereno.Nuevo(sereno, tramos[0].Desde.AddDays(-1));
        foreach (var (desde, hasta) in tramos)
        {
            estado.Cambiar(true, desde);
            yield return CambioEstadoSereno.Registrar(estado);
            estado.Cambiar(false, hasta);
            yield return CambioEstadoSereno.Registrar(estado);
        }
    }

    private static Asignacion Asignada(Caso caso, Guid sereno, Guid operador, DateTimeOffset desde, DateTimeOffset hasta)
    {
        var asignacion = Asignacion.Registrar(caso, sereno, operador, Guid.NewGuid(), desde);
        asignacion.Finalizar(hasta);
        return asignacion;
    }

    [Fact]
    public void Percentil_InterpolaEntreLosVecinos_YSinValoresEsNulo()
    {
        Assert.Null(Estadistica.Percentil([], 0.5));
        Assert.Equal(4, Estadistica.Percentil([6, 2, 4], 0.5));
        Assert.Equal(5.6, Estadistica.Percentil([2, 4, 6], 0.9)!.Value, 6);
        Assert.Equal(7, Estadistica.Percentil([7], 0.9));
    }

    [Fact]
    public async Task DemandaYOferta_CuentaCasosYSerenosOperativosYDisponiblesPorHoraLocal()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var casos = new[] { Sos(ciudadano.Id, Local(2020, 3, 2, 8, 15)), Sos(ciudadano.Id, Local(2020, 3, 2, 8, 45)), Sos(ciudadano.Id, Local(2020, 3, 2, 9, 30)) };
        await Guardar(db =>
        {
            db.Casos.AddRange(casos);
            db.CambiosEstadoSereno.AddRange(Turnos(sereno.Id, (Local(2020, 3, 2, 8), Local(2020, 3, 2, 12))));
            // De 9:00 a 10:00 atiende un caso: operativo, pero no disponible.
            db.Asignaciones.Add(Asignada(casos[2], sereno.Id, operador.Id, Local(2020, 3, 2, 9), Local(2020, 3, 2, 10)));
        });

        var resultado = await Servicio(s => s.GetRequiredService<ConsultarDemandaYOferta>()
            .EjecutarAsync(new PeriodoAnalitica(Local(2020, 3, 2, 0), Local(2020, 3, 3, 0), Lima), CancellationToken.None));

        Assert.Equal(3, resultado.Casos);
        Assert.Equal(1, resultado.Dias, 6);
        Assert.Equal(new FranjaDemandaOferta(8, 2, 1, 1), resultado.PorHora[8]);
        Assert.Equal(new FranjaDemandaOferta(9, 1, 1, 0), resultado.PorHora[9]);
        Assert.Equal(new FranjaDemandaOferta(11, 0, 1, 1), resultado.PorHora[11]);
        Assert.Equal(new FranjaDemandaOferta(12, 0, 0, 0), resultado.PorHora[12]);
        Assert.Equal(new CeldaDemandaOferta(DayOfWeek.Monday, 8, 2, 1, 1), resultado.PorDiaYHora.Single(c => c.Hora == 8));
    }

    [Fact]
    public async Task Cobertura_CuentaElTiempoConUnSerenoDisponibleCerca_YAgrupaLasZonasConPocosCasos()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var inicio = Local(2020, 4, 6, 0);
        var enA = Enumerable.Range(0, 3).Select(i => Sos(ciudadano.Id, inicio.AddMinutes(i), LatA + 0.001, LonA)).ToList();
        var enB = Enumerable.Range(0, 3).Select(i => Sos(ciudadano.Id, inicio.AddMinutes(i), LatB, LonB)).ToList();
        await Guardar(db =>
        {
            db.Casos.AddRange([.. enA, .. enB, Sos(ciudadano.Id, inicio, LatC, LonC)]);
            db.CambiosEstadoSereno.AddRange(Turnos(sereno.Id, (inicio, inicio.AddHours(1))));
            // Ocupado los primeros 10 minutos; después, disponible junto a la zona A.
            db.Asignaciones.Add(Asignada(enA[0], sereno.Id, operador.Id, inicio, inicio.AddMinutes(10)));
            // Envía su posición cada minuto hasta las 00:29; luego deja de enviarla.
            for (var m = 0; m < 30; m++)
                db.PosicionesSereno.Add(PosicionSereno.Crear(sereno.Id, LatA + 0.0005, LonA, 8, inicio.AddMinutes(m), inicio.AddMinutes(m))!);
        });

        var resultado = await Servicio(s => s.GetRequiredService<ConsultarCobertura>()
            .EjecutarAsync(new PeriodoAnalitica(inicio, inicio.AddHours(1), Lima), CancellationToken.None));

        // 12 muestras (cada 5 min). Cubierta de 00:10 a 00:35: libre y con una posición de 10 min o menos.
        Assert.Equal(12, resultado.Muestras);
        Assert.Equal(50, resultado.PorcentajeConAlguno, 6);
        var b = resultado.Zonas[0];
        Assert.Equal(LatB - 0.0025, b.Sur, 6);
        Assert.Equal((0.0, 3), (b.PorcentajeCubierto, b.Casos));
        Assert.Equal(1, b.HorasSinCobertura, 6);
        var a = resultado.Zonas[1];
        Assert.Equal((50.0, 3), (Math.Round(a.PorcentajeCubierto, 6), a.Casos));
        Assert.Equal(0.5, a.HorasSinCobertura, 6);
        Assert.Equal((1, 1), (resultado.ZonasAgrupadas, resultado.CasosAgrupados)); // la zona C tiene un solo caso
    }

    [Fact]
    public async Task RespuestaPorZona_DaP50YP90DesdeElRegistro_SinCanceladosNiReportes()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var inicio = Local(2020, 5, 4, 10);
        var atendidos = Enumerable.Range(0, 3).Select(i => Sos(ciudadano.Id, inicio.AddHours(i), LatA, LonA)).ToList();
        var cancelado = Sos(ciudadano.Id, inicio, LatA, LonA);
        cancelado.Cancelar();
        var reporte = Caso.RegistrarReporte(ciudadano.Id, Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id, inicio);
        await Guardar(db =>
        {
            db.Casos.AddRange([.. atendidos, cancelado, reporte, Sos(ciudadano.Id, inicio, LatC, LonC), Sos(ciudadano.Id, inicio)]);
            for (var i = 0; i < 3; i++)
            {
                var registro = atendidos[i].RegistradoEn;
                db.EventosCaso.Add(EventoCaso.Registrar(atendidos[i], TipoEventoCaso.CasoAsignado, operador.Id, VisibilidadEvento.Interna, registro.AddMinutes(2 * (i + 1))));
                db.EventosCaso.Add(EventoCaso.Registrar(atendidos[i], TipoEventoCaso.AtencionIniciada, sereno.Id, VisibilidadEvento.Interna, registro.AddMinutes(10 * (i + 1))));
            }
        });

        var resultado = await Servicio(s => s.GetRequiredService<ConsultarRespuestaPorZona>()
            .EjecutarAsync(new PeriodoAnalitica(inicio.AddHours(-1), inicio.AddHours(5), Lima), reportes: false, CancellationToken.None));

        var zona = Assert.Single(resultado.Zonas).Tiempos;
        Assert.Equal((3, 3, 3), (zona.Casos, zona.Despachados, zona.Atendidos));
        Assert.Equal(4, zona.DespachoP50!.Value, 6);
        Assert.Equal(5.6, zona.DespachoP90!.Value, 6);
        Assert.Equal(20, zona.RespuestaP50!.Value, 6);
        Assert.Equal(28, zona.RespuestaP90!.Value, 6);
        Assert.Equal((1, 1), (resultado.ZonasAgrupadas, resultado.Agrupadas!.Casos)); // la zona C, con un caso, no se muestra sola
        Assert.Equal((5, 1), (resultado.Total.Casos, resultado.SinUbicacion)); // tres atendidos, el de la zona C y uno sin ubicación
    }

    [Fact]
    public async Task DesactivarLaCuentaDeUnSereno_LoDejaNoOperativo_YQuedaEnSuHistorial()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var administrador = await fabrica.CrearAsync(Roles.Administrador);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        await System.Net.Http.Json.HttpClientJsonExtensions.PutAsJsonAsync(app, "/api/sereno/estado",
            new Cad.Contratos.Serenos.SolicitudCambioEstadoOperativo(true, true));

        await Servicio(s => s.GetRequiredService<Cad.Server.Modulos.Cuentas.Aplicacion.DesactivarCuentaInstitucional>()
            .EjecutarAsync(sereno.Id, administrador.Id, CancellationToken.None));

        Assert.False(await fabrica.ConBaseDatosAsync(db => db.EstadosSereno.Where(e => e.SerenoId == sereno.Id).Select(e => e.Operativo).SingleAsync()));
        Assert.Equal([true, false], await fabrica.ConBaseDatosAsync(db => db.CambiosEstadoSereno.Where(c => c.SerenoId == sereno.Id)
            .OrderBy(c => c.CambiadoEn).Select(c => c.Operativo).ToListAsync()));
    }
}
