using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// Bloque D de la analítica (27/10/2026): motivos de la evaluación (HU32) y su análisis, y adopción y retención con la actividad
// diaria del ciudadano. Los cálculos usan fechas propias de 2020, antes de cualquier otro dato de la base. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CiudadanoTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private static DateTimeOffset Local(int mes, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(2020, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);
    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    // Un SOS del ciudadano atendido y cerrado; la atención empieza «minutos» después del registro.
    private static (Caso Caso, CierreCaso Cierre, EventoCaso Atencion) Cerrado(Guid ciudadano, Guid sereno, DateTimeOffset en, double minutos)
    {
        var caso = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, en);
        caso.Asignar();
        caso.ConfirmarDesplazamiento();
        caso.IniciarAtencion();
        caso.Cerrar();
        return (caso, CierreCaso.Registrar(caso, Robo, ResultadosAtencion.ResueltoEnElLugar, "Resultado ficticio", null, sereno, en.AddHours(1)),
            EventoCaso.Registrar(caso, TipoEventoCaso.AtencionIniciada, sereno, VisibilidadEvento.Interna, en.AddMinutes(minutos)));
    }

    [Fact]
    public async Task Evaluar_GuardaLosMotivosDeLaLista_YRechazaLosQueNoEstan()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var (caso, cierre, atencion) = Cerrado(cuenta.Id, sereno.Id, DateTimeOffset.UtcNow.AddHours(-2), 10);
        await Guardar(db => { db.Casos.Add(caso); db.CierresCaso.Add(cierre); db.EventosCaso.Add(atencion); });
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var ruta = $"/api/casos/mios/{caso.Id}/evaluacion";

        var invalida = await app.PostAsJsonAsync(ruta, new SolicitudEvaluacionCaso(Guid.NewGuid(), 2, null, ["Lentos"]));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Contains("motivos", await invalida.Content.ReadAsStringAsync());

        var valida = await app.PostAsJsonAsync(ruta, new SolicitudEvaluacionCaso(Guid.NewGuid(), 2, null, [MotivosEvaluacion.Tardaron, MotivosEvaluacion.NoMeInformaron]));
        Assert.True(valida.IsSuccessStatusCode);
        Assert.Equal([MotivosEvaluacion.Tardaron, MotivosEvaluacion.NoMeInformaron],
            await fabrica.ConBaseDatosAsync(db => db.EvaluacionesCaso.Where(e => e.CasoId == caso.Id).Select(e => e.Motivos).SingleAsync()));
        // «Mi caso» devuelve los motivos registrados.
        var detalle = await app.GetFromJsonAsync<MiCasoDetalle>($"/api/casos/mios/{caso.Id}");
        Assert.Equal([MotivosEvaluacion.Tardaron, MotivosEvaluacion.NoMeInformaron], detalle!.Evaluacion!.Motivos);
    }

    [Fact]
    public async Task Evaluaciones_PorPuntuacionMotivosYTiempoDeRespuesta()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var sereno = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var dia = Local(11, 23, 10);
        var casos = new[] { Cerrado(ciudadano, sereno, dia, 40), Cerrado(ciudadano, sereno, dia, 25), Cerrado(ciudadano, sereno, dia, 5),
            Cerrado(ciudadano, sereno, dia, 15), Cerrado(ciudadano, sereno, dia, 12) };
        (int Puntuacion, string[] Motivos)[] notas =
            [(2, [MotivosEvaluacion.Tardaron]), (3, [MotivosEvaluacion.Tardaron, MotivosEvaluacion.NoMeInformaron]), (5, [MotivosEvaluacion.LlegaronRapido]), (4, [])];
        await Guardar(db =>
        {
            foreach (var (caso, cierre, atencion) in casos)
            {
                db.Casos.Add(caso);
                db.CierresCaso.Add(cierre);
                db.EventosCaso.Add(atencion);
            }
            for (var i = 0; i < notas.Length; i++) // el quinto no se evaluó
                db.EvaluacionesCaso.Add(EvaluacionCaso.Registrar(casos[i].Caso, Guid.NewGuid(), notas[i].Puntuacion, null, dia.AddHours(2), notas[i].Motivos));
        });

        var r = await Servicio(s => s.GetRequiredService<ConsultarEvaluaciones>()
            .EjecutarAsync(new PeriodoAnalitica(Local(11, 23, 0), Local(11, 24, 0), Lima), CancellationToken.None));

        Assert.Equal((5, 4, 80.0), (r.Cerrados, r.Evaluados, r.PorcentajeEvaluados));
        Assert.Equal([0, 1, 1, 1, 1], r.Distribucion.Select(d => d.Evaluaciones));
        Assert.Equal([(MotivosEvaluacion.Tardaron, 2), (MotivosEvaluacion.NoMeInformaron, 1)], r.MotivosBajas.Select(m => (m.Codigo, m.Veces)));
        Assert.Equal("Llegaron rápido", Assert.Single(r.MotivosAltas).Texto);
        Assert.Equal(32.5, r.RespuestaP50Bajas!.Value, 6);
        Assert.Equal(10, r.RespuestaP50Altas!.Value, 6);
        Assert.Equal([(1, 0.0), (1, 0.0), (1, 100.0), (1, 100.0)], r.PorTiempoDeRespuesta.Select(t => (t.Evaluaciones, t.PorcentajeBajas)));
    }

    [Fact]
    public async Task Actividad_SeAnotaUnaVezPorDiaAlUsarLaApp()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/casos/mios")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/notificaciones")).StatusCode);

        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.ActividadesCiudadano.CountAsync(a => a.UsuarioId == cuenta.Id)));
    }

    [Fact]
    public async Task Adopcion_CohortesPorSemanaDeRegistro_ActividadSemanal_YReportesPorCanal()
    {
        var conVuelta = await fabrica.CrearAsync(Roles.Ciudadano);
        var unaVez = await fabrica.CrearAsync(Roles.Ciudadano);
        var nunca = await fabrica.CrearAsync(Roles.Ciudadano);
        var registro = Local(11, 2, 9); // lunes 2/11/2020
        var reporte = Caso.RegistrarReporte(conVuelta.Id, Guid.NewGuid(), Robo, Local(11, 12, 18));
        await fabrica.ConBaseDatosAsync(async db =>
        {
            await db.Users.Where(u => u.Id == conVuelta.Id || u.Id == unaVez.Id || u.Id == nunca.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CreadoEn, registro));
            await db.Database.ExecuteSqlAsync($"""INSERT INTO "ActividadesCiudadano" ("UsuarioId", "Dia") VALUES ({conVuelta.Id}, {new DateOnly(2020, 11, 3)})""");
            db.Casos.Add(Caso.EmitirSos(unaVez.Id, Guid.NewGuid(), null, Local(11, 4, 20)));
            db.Casos.Add(reporte);
            db.DetallesReporte.Add(DetalleReporte.Registrar(reporte, Robo, "Descripción ficticia", Local(11, 12, 17), "Texto", null, null, null, "Referencia"));
            return await db.SaveChangesAsync();
        });

        var r = await Servicio(s => s.GetRequiredService<ConsultarAdopcion>()
            .EjecutarAsync(new PeriodoAnalitica(Local(11, 2, 0), Local(11, 16, 0), Lima), CancellationToken.None));

        Assert.Equal(new CohorteSemanal(new DateOnly(2020, 11, 2), 3, 2, 1), Assert.Single(r.Cohortes));
        Assert.Equal([new ActividadSemanal(new DateOnly(2020, 11, 2), 2, 2), new ActividadSemanal(new DateOnly(2020, 11, 9), 1, 0)], r.Actividad);
        Assert.Equal(new ReportesPorCanal(CanalesEntrada.App, "App CAD Móvil", 1), Assert.Single(r.Canales));
    }
}
