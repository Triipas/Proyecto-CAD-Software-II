using Cad.Contratos.Casos;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// Bloque C de la analítica (27/10/2026): puntos críticos, zonas en alza, demora en reportar, efecto de las fotos y percepción
// de inseguridad. Cada prueba usa sus propias fechas de 2020, antes de cualquier otro dato de la base. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PatronesTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Violencia = CategoriaIncidencia.Iniciales[0].Id;
    private const double LatA = -12.0525, LonA = -77.0425; // centro de una celda
    private const double LatB = -12.0025, LonB = -77.0425; // otra celda, a unos 5,5 km

    private static DateTimeOffset Local(int mes, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(2020, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);
    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    private static Caso Sos(Guid ciudadano, DateTimeOffset en, double lat, double lon) =>
        Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(lat, lon, 5, en, en), en);

    [Fact]
    public async Task PuntosCriticos_AgrupanTresOMasIncidenciasCercanas_SinCanceladas()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        // P: tres incidencias a menos de 100 m (y una cancelada, que no cuenta). Q: solo dos. R: tres, a 1 km.
        var p = Enumerable.Range(0, 3).Select(i => Sos(ciudadano, Local(6, 1 + i, 21), LatA + 0.0002 * i, LonA)).ToList();
        foreach (var caso in p)
            caso.Clasificar(Robo, PrioridadesCaso.Media, PersonasHeridas.No, operador, caso.RegistradoEn);
        var cancelado = Sos(ciudadano, Local(6, 2, 22), LatA, LonA);
        cancelado.Cancelar();
        var q = Enumerable.Range(0, 2).Select(i => Sos(ciudadano, Local(6, 3, 10 + i), LatA + 0.005, LonA)).ToList();
        var r = Enumerable.Range(0, 3).Select(i => Sos(ciudadano, Local(6, 4, 10 + i), LatA - 0.009, LonA + 0.0003 * i)).ToList();
        await Guardar(db => db.Casos.AddRange([.. p, cancelado, .. q, .. r]));

        var puntos = await Servicio(s => s.GetRequiredService<ConsultarPuntosCriticos>()
            .EjecutarAsync(new PeriodoAnalitica(Local(6, 1, 0), Local(6, 6, 0), Lima), CancellationToken.None));

        Assert.Equal(2, puntos.Count);
        var enP = Assert.Single(puntos, x => Math.Abs(x.Latitud - (LatA + 0.0002)) < 1e-9);
        Assert.Equal((3, 3, 0, "Robo o hurto"), (enP.Casos, enP.Sos, enP.Reportes, enP.CategoriaFrecuente));
        Assert.Equal(Local(6, 3, 21), enP.UltimoCaso);
        Assert.Single(puntos, x => x.Casos == 3 && x.CategoriaFrecuente is null);
    }

    [Fact]
    public async Task ZonasEnAlza_ComparanLaSemanaConElPromedioDeLasCuatroAnteriores()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var hasta = Local(8, 3, 0); // la semana es del 27/07 al 02/08
        Caso EnA(DateTimeOffset en)
        {
            var caso = Sos(ciudadano, en, LatA, LonA);
            caso.Clasificar(Robo, PrioridadesCaso.Media, PersonasHeridas.No, operador, en);
            return caso;
        }
        // A: 1 por semana antes; 5 esta semana (robos). B: 2 por semana antes; 3 esta semana (sin clasificar).
        var casos = Enumerable.Range(1, 4).Select(s => EnA(hasta.AddDays(-7 * s - 3)))
            .Concat(Enumerable.Range(0, 5).Select(i => EnA(hasta.AddDays(-1 - i))))
            .Concat(Enumerable.Range(1, 8).Select(i => Sos(ciudadano, hasta.AddDays(-7 - 3.5 * i), LatB, LonB)))
            .Concat(Enumerable.Range(0, 3).Select(i => Sos(ciudadano, hasta.AddDays(-2 - i), LatB, LonB)))
            .ToList();
        await Guardar(db => db.Casos.AddRange(casos));

        var zonas = await Servicio(s => s.GetRequiredService<ConsultarZonasEnAlza>().EjecutarAsync(hasta, CancellationToken.None));

        Assert.Equal(4, zonas.Count);
        Assert.All(zonas.Take(2), z => Assert.True(z.Alerta));
        var robos = Assert.Single(zonas, z => z.Categoria == "Robo o hurto");
        Assert.Equal((5, 1.0, 4.0, true), (robos.SemanaActual, robos.PromedioAnterior, robos.Desviacion, robos.Alerta));
        var b = Assert.Single(zonas, z => z.Categoria is null && z.SemanaActual == 3);
        Assert.Equal(2, b.PromedioAnterior);
        Assert.False(b.Alerta); // sube, pero dentro de lo normal
        Assert.Contains(zonas, z => z.Categoria == "Sin clasificar");
    }

    [Fact]
    public async Task Reportes_DemoraPorCategoria_YDecisionesConYSinFotos()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var registro = Local(9, 10, 12);
        (Caso Caso, DetalleReporte Detalle) Reporte(Guid categoria, double horasDeDemora)
        {
            var caso = Caso.RegistrarReporte(ciudadano, Guid.NewGuid(), categoria, registro);
            return (caso, DetalleReporte.Registrar(caso, categoria, "Descripción ficticia", registro.AddHours(-horasDeDemora), "Texto",
                null, null, null, "Referencia ficticia"));
        }
        var reportes = new[] { Reporte(Robo, 0.5), Reporte(Robo, 2), Reporte(Robo, 10), Reporte(Violencia, 3) };
        DecisionReporte Decision((Caso Caso, DetalleReporte) r, TipoDecisionReporte tipo, string? motivo = null) =>
            DecisionReporte.Registrar(r.Caso, tipo, "Motivo ficticio", null, null, operador, registro.AddHours(1), null, motivo);
        await Guardar(db =>
        {
            db.Casos.AddRange(reportes.Select(r => r.Caso));
            db.DetallesReporte.AddRange(reportes.Select(r => r.Detalle));
            db.FotosReporte.Add(FotoReporte.Registrar(reportes[0].Caso, 1, [0xFF, 0xD8, 0xFF], registro));
            db.FotosReporte.Add(FotoReporte.Registrar(reportes[1].Caso, 1, [0xFF, 0xD8, 0xFF], registro));
            db.DecisionesReporte.AddRange(
                Decision(reportes[0], TipoDecisionReporte.Admision),
                Decision(reportes[1], TipoDecisionReporte.Descarte, MotivosDescarte.FalsoOMalintencionado),
                Decision(reportes[2], TipoDecisionReporte.Admision),
                Decision(reportes[3], TipoDecisionReporte.Admision));
        });

        var resultado = await Servicio(s => s.GetRequiredService<ConsultarReportesAnalitica>()
            .EjecutarAsync(new PeriodoAnalitica(Local(9, 1, 0), Local(10, 1, 0), Lima), CancellationToken.None));

        var robo = Assert.Single(resultado.DemoraPorCategoria, d => d.Categoria == "Robo o hurto");
        Assert.Equal(3, robo.Reportes);
        Assert.Equal(2, robo.HorasP50!.Value, 6);
        Assert.Equal(8.4, robo.HorasP90!.Value, 6);
        Assert.Equal(100.0 / 3, robo.PorcentajeEnLaPrimeraHora, 6);
        Assert.Equal((4, 2.5), (resultado.DemoraTotal.Reportes, Math.Round(resultado.DemoraTotal.HorasP50!.Value, 6)));
        Assert.Equal(new DecisionesSegunFotos(2, 1, 0, 1, 1), resultado.ConFotos);
        Assert.Equal(new DecisionesSegunFotos(2, 2, 0, 0, 0), resultado.SinFotos);
        Assert.Equal(100, resultado.SinFotos.PorcentajeAdmitidos);
    }

    [Fact]
    public async Task Percepcion_SeGuardaAntesDeEliminarLosRecorridos_YSeSumaALoQueAunNoSeElimina()
    {
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var inicio = Local(10, 5, 20); // 20:00 en Lima
        async Task<SesionAcompanamiento> SesionAsync(bool pasaPorB, bool conAviso = false)
        {
            var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
            var sesion = SesionAcompanamiento.Iniciar(ciudadano, Guid.NewGuid(), inicio);
            // «Algo no está bien» se informa durante la sesión activa.
            var aviso = conAviso ? EstadoInformado.Registrar(sesion, Guid.NewGuid(), EstadoCiudadano.AlgoNoEstaBien, null, inicio.AddMinutes(5)) : null;
            sesion.Finalizar(inicio.AddMinutes(25));
            await Guardar(db =>
            {
                db.SesionesAcompanamiento.Add(sesion);
                if (aviso is not null)
                    db.EstadosInformados.Add(aviso);
                for (var m = 0; m < 10; m++)
                    db.PosicionesAcompanamiento.Add(PosicionAcompanamiento.Crear(sesion.Id, LatA, LonA + 0.0001 * m, 5, inicio.AddMinutes(m), inicio.AddMinutes(m))!);
                if (pasaPorB)
                    db.PosicionesAcompanamiento.Add(PosicionAcompanamiento.Crear(sesion.Id, LatB, LonB, 5, inicio.AddMinutes(15), inicio.AddMinutes(15))!);
            });
            return sesion;
        }
        var conAviso = await SesionAsync(false, conAviso: true);
        await SesionAsync(true);
        var conservada = await SesionAsync(false);
        // La tercera conserva su recorrido (HU39-B): sus posiciones no se eliminan y se calcula al consultar.
        var caso = Sos((await fabrica.CrearAsync(Roles.Ciudadano)).Id, inicio, LatA, LonA);
        await Guardar(db =>
        {
            db.Casos.Add(caso);
            db.ConservacionesRecorrido.Add(ConservacionRecorrido.Registrar(conservada.Id, caso.Id, "Finalidad ficticia", operador, inicio));
        });

        await Servicio(s => s.GetRequiredService<ConservarRecorridos>().EliminarVencidasAsync(CancellationToken.None));
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.PosicionesAcompanamiento.AnyAsync(p => p.SesionId == conAviso.Id)));

        var percepcion = await Servicio(s => s.GetRequiredService<ConsultarPercepcion>()
            .EjecutarAsync(new PeriodoAnalitica(Local(10, 5, 0), Local(10, 6, 0), Lima), CancellationToken.None));

        var a = Assert.Single(percepcion.Zonas);
        Assert.Equal((3, 1), (a.Recorridos, a.Avisos));
        Assert.Equal(LatA - 0.0025, a.Sur, 6);
        Assert.Equal((1, 1, 0), (percepcion.ZonasAgrupadas, percepcion.RecorridosAgrupados, percepcion.AvisosAgrupados)); // B: un solo recorrido
        Assert.Equal(new HoraPercepcion(20, 4, 1), percepcion.PorHora[20]);
    }
}
