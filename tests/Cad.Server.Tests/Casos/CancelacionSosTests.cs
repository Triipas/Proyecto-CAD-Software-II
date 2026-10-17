using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Notificaciones;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cad.Server.Tests.Casos;

// HU09 — Cancelar un SOS pendiente (S5-T01–S5-T03). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CancelacionSosTests(FabricaCad fabrica)
{
    private static string Ruta(Guid casoId) => $"/api/casos/mios/{casoId}/cancelacion";

    private async Task<(HttpClient App, CuentaDePrueba Cuenta, Guid CasoId)> SosAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321");
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1))));
        return (app, cuenta, (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id);
    }

    private static Task<HttpResponseMessage> CancelarAsync(HttpClient app, Guid casoId, string? motivo) =>
        app.PostAsJsonAsync(Ruta(casoId), new SolicitudCancelacionSos(motivo));

    private async Task<Guid> SerenoOperativoAsync()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return sereno.Id;
    }

    private Task<ResultadoAsignacion> AsignarAsync(Guid casoId, Guid serenoId, Guid operadorId) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, serenoId, operadorId, Guid.NewGuid(), CancellationToken.None));

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    private Task<List<CancelacionSos>> CancelacionesAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.CancelacionesSos.AsNoTracking().Where(c => c.CasoId == casoId).ToListAsync());

    private Task<List<EventoCaso>> EventosCancelacionAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.SosCancelado).ToListAsync());

    private Task<int> AsignacionesAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync(a => a.CasoId == casoId));

    [Fact]
    public async Task CA01_SosPropioPendienteConMotivo_PasaACanceladaYConservaMotivoFechaYHora()
    {
        var (app, cuenta, casoId) = await SosAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await CancelarAsync(app, casoId, "  Ya estoy a salvo, fue una falsa alarma.  ");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var detalle = (await respuesta.Content.ReadFromJsonAsync<MiCasoDetalle>())!;
        Assert.Equal(EstadosPublicos.Cancelado, detalle.Estado);
        Assert.Equal("Cancelado por ti. Motivo: Ya estoy a salvo, fue una falsa alarma.", detalle.Resultado);
        Assert.Equal(EstadoCaso.Cancelada, (await CasoAsync(casoId)).Estado);
        var cancelacion = Assert.Single(await CancelacionesAsync(casoId));
        Assert.Equal("Ya estoy a salvo, fue una falsa alarma.", cancelacion.Motivo);
        Assert.Equal(cuenta.Id, cancelacion.CiudadanoId);
        Assert.InRange(cancelacion.CanceladaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var evento = Assert.Single(await EventosCancelacionAsync(casoId));
        Assert.Equal(VisibilidadEvento.Publica, evento.Visibilidad);
        Assert.Equal(cuenta.Id, evento.ActorId);
        // El propio ciudadano lo canceló: no recibe una notificación de su propio cambio (como al registrarlo, HU31).
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.NotificacionesCiudadano.AnyAsync(n => n.CasoId == casoId)));

        // HU30: «Mis casos» lo muestra cancelado, con el motivo como avance público.
        var lista = (await app.GetFromJsonAsync<List<MiCasoResumen>>("/api/casos/mios"))!;
        Assert.Equal(EstadosPublicos.Cancelado, lista.Single(c => c.Id == casoId).Estado);
        Assert.Contains(detalle.Avances, a => a.Estado == EstadosPublicos.Cancelado && a.Detalle == "Motivo: Ya estoy a salvo, fue una falsa alarma.");
    }

    [Fact]
    public async Task CA02_CancelacionValida_NotificaALaCentral_QueLoRetiraDeLaSeccionUrgente()
    {
        var (app, _, casoId) = await SosAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();
        Assert.Contains($"data-sos=\"{casoId}\"", await central.Navegador.GetStringAsync("/panel/central?handler=Lista"));

        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Me equivoqué al pulsar.")).StatusCode);

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCancelaciones.Contains(casoId)), "No llegó el aviso de la cancelación.");
        Assert.DoesNotContain($"data-sos=\"{casoId}\"", await central.Navegador.GetStringAsync("/panel/central?handler=Lista"));
        // HU11-CA02 (vínculo): la central tiene el aviso visible que muestra central.js sin recargar.
        Assert.Contains("data-aviso-cancelacion", await central.Navegador.GetStringAsync("/panel/central"));
    }

    [Fact]
    public async Task CA02_SiFallaElAvisoEnTiempoReal_LaCancelacionSeConservaYLaCentralLaVeAlResincronizar()
    {
        var (_, cuenta, casoId) = await SosAsync();
        using var alcance = fabrica.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<CadDbContext>();

        var resultado = await new CancelarSos(db, TimeProvider.System, new NotificadorQueFalla(), NullLogger<CancelarSos>.Instance)
            .EjecutarAsync(cuenta.Id, casoId, "Ya llegó la ayuda de un vecino.", CancellationToken.None);

        Assert.IsType<ResultadoCancelacion.Cancelado>(resultado);
        Assert.Equal(EstadoCaso.Cancelada, (await CasoAsync(casoId)).Estado);
        await using var central = await fabrica.OperadorEnCentralAsync();
        Assert.DoesNotContain($"data-sos=\"{casoId}\"", await central.Navegador.GetStringAsync("/panel/central?handler=Lista"));
    }

    [Theory]
    [InlineData(EstadoCaso.Asignada)]
    [InlineData(EstadoCaso.EnCamino)]
    [InlineData(EstadoCaso.EnAtencion)]
    public async Task CA03_SosAsignadoOEnUnEstadoPosterior_SeRechazaYElEstadoNoCambia(EstadoCaso estado)
    {
        var (app, _, casoId) = await SosAsync();
        var serenoId = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAsignacion.Asignado>(await AsignarAsync(casoId, serenoId, operador.Id));
        var avances = estado switch
        {
            EstadoCaso.EnCamino => new[] { Avance.ConfirmarDesplazamiento },
            EstadoCaso.EnAtencion => [Avance.ConfirmarDesplazamiento, Avance.IniciarAtencion],
            _ => [],
        };
        foreach (var avance in avances)
            await fabrica.ConServiciosAsync(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(casoId, avance, serenoId, ActorAvance.Sereno, CancellationToken.None));
        Assert.Equal(estado, (await CasoAsync(casoId)).Estado);

        var respuesta = await CancelarAsync(app, casoId, "Ya no lo necesito.");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("ya no está pendiente", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(estado, (await CasoAsync(casoId)).Estado);
        Assert.Empty(await CancelacionesAsync(casoId));
        Assert.Empty(await EventosCancelacionAsync(casoId));
    }

    [Fact]
    public async Task CA03_SosCerrado_SeRechazaYElEstadoNoCambia()
    {
        var (app, _, casoId) = await SosAsync();
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == casoId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.Estado, EstadoCaso.Cerrada)));

        Assert.Equal(HttpStatusCode.Conflict, (await CancelarAsync(app, casoId, "Ya no lo necesito.")).StatusCode);
        Assert.Equal(EstadoCaso.Cerrada, (await CasoAsync(casoId)).Estado);
        Assert.Empty(await CancelacionesAsync(casoId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CA04_SinMotivo_NoSeCancela(string? motivo)
    {
        var (app, _, casoId) = await SosAsync();

        var respuesta = await CancelarAsync(app, casoId, motivo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("motivo", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.Empty(await CancelacionesAsync(casoId));
        Assert.Empty(await EventosCancelacionAsync(casoId));
    }

    [Fact]
    public async Task CA04_MotivoDemasiadoLargo_NoSeCancela()
    {
        var (app, _, casoId) = await SosAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await CancelarAsync(app, casoId, new string('a', CancelacionSos.MaximoMotivo + 1))).StatusCode);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);

        // El límite exacto se admite.
        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, new string('a', CancelacionSos.MaximoMotivo))).StatusCode);
    }

    [Fact]
    public async Task CA04_LaBaseDeDatosTampocoAdmiteUnaCancelacionSinMotivo()
    {
        var (_, cuenta, casoId) = await SosAsync();

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"CancelacionesSos\" (\"CasoId\", \"CiudadanoId\", \"Motivo\", \"CanceladaEn\") VALUES ({casoId}, {cuenta.Id}, {"  "}, now())")));
        Assert.Equal("CK_CancelacionesSos_Motivo", error.ConstraintName);
    }

    [Fact]
    public async Task CA05_SosAjeno_SeRechazaSinRevelarQueExiste()
    {
        var (_, _, casoId) = await SosAsync();
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

        var ajeno = await CancelarAsync(otro, casoId, "Quiero cancelarlo.");
        var inexistente = await CancelarAsync(otro, Guid.NewGuid(), "Quiero cancelarlo.");

        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.Empty(await CancelacionesAsync(casoId));
    }

    // El resto de perfiles (operador, administrador, sin sesión) está en MatrizAutorizacionTests.
    [Fact]
    public async Task CA05_UnSereno_NoPuedeCancelar()
    {
        var (_, _, casoId) = await SosAsync();
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Sereno));

        Assert.Equal(HttpStatusCode.Forbidden, (await CancelarAsync(app, casoId, "Quiero cancelarlo.")).StatusCode);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
    }

    // Ejecuta una acción, una sola vez, justo antes de que el contexto guarde: la otra operación ya leyó el caso pendiente.
    private sealed class AntesDeGuardar(Func<Task> accion) : SaveChangesInterceptor
    {
        private int pendiente = 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref pendiente, 0) == 1)
                await accion();
            return resultado;
        }
    }

    private async Task<CadDbContext> ContextoAsync(Func<Task> otra)
    {
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        return new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).AddInterceptors(new AntesDeGuardar(otra)).Options);
    }

    [Fact]
    public async Task CA06_S5T02_AsignacionConfirmadaMientrasSeCancela_PrevaleceLaAsignacionYLaCancelacionSeRechaza()
    {
        var (_, cuenta, casoId) = await SosAsync();
        var serenoId = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await using var db = await ContextoAsync(async () =>
            Assert.IsType<ResultadoAsignacion.Asignado>(await AsignarAsync(casoId, serenoId, operador.Id)));
        using var alcance = fabrica.Services.CreateScope();

        var resultado = await new CancelarSos(db, TimeProvider.System, alcance.ServiceProvider.GetRequiredService<INotificador>(),
            NullLogger<CancelarSos>.Instance).EjecutarAsync(cuenta.Id, casoId, "Ya no lo necesito.", CancellationToken.None);

        var rechazo = Assert.IsType<ResultadoCancelacion.Rechazado>(resultado);
        Assert.Equal(CancelarSos.MotivoNoPendiente, rechazo.Motivo);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
        Assert.Equal(1, await AsignacionesAsync(casoId));
        // Sin registros parciales de la cancelación.
        Assert.Empty(await CancelacionesAsync(casoId));
        Assert.Empty(await EventosCancelacionAsync(casoId));
    }

    [Fact]
    public async Task CA06_S5T02_CancelacionConfirmadaMientrasSeAsigna_PrevaleceLaCancelacionYLaAsignacionSeRechaza()
    {
        var (app, _, casoId) = await SosAsync();
        var serenoId = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await using var db = await ContextoAsync(async () =>
            Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Ya estoy a salvo.")).StatusCode));
        using var alcance = fabrica.Services.CreateScope();
        var servicios = alcance.ServiceProvider;

        var resultado = await new AsignarCaso(db, TimeProvider.System, servicios.GetRequiredService<INotificador>(),
                servicios.GetRequiredService<ColaAvisosDespacho>(), NullLogger<AsignarCaso>.Instance)
            .EjecutarAsync(casoId, serenoId, operador.Id, Guid.NewGuid(), CancellationToken.None);

        var rechazo = Assert.IsType<ResultadoAsignacion.Rechazado>(resultado);
        Assert.Equal(AsignarCaso.MotivoCancelado, rechazo.Motivo);
        Assert.Equal(EstadoCaso.Cancelada, (await CasoAsync(casoId)).Estado);
        Assert.Equal(0, await AsignacionesAsync(casoId));
        Assert.Single(await CancelacionesAsync(casoId));
        // El sereno sigue disponible: no quedó una asignación vigente a medias.
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.SerenoId == serenoId && a.Vigente)));
    }

    [Fact]
    public async Task CA06_S5T02_CancelacionYAsignacionALaVez_PrevaleceSoloUnaYElEstadoEsConsistente()
    {
        for (var vuelta = 0; vuelta < 10; vuelta++)
        {
            var (app, _, casoId) = await SosAsync();
            var serenoId = await SerenoOperativoAsync();
            var operador = await fabrica.CrearAsync(Roles.Operador);

            var cancelacion = CancelarAsync(app, casoId, "Ya no lo necesito.");
            var asignacion = AsignarAsync(casoId, serenoId, operador.Id);
            await Task.WhenAll(cancelacion, asignacion);

            var caso = await CasoAsync(casoId);
            var cancelaciones = (await CancelacionesAsync(casoId)).Count;
            var asignaciones = await AsignacionesAsync(casoId);
            if (caso.Estado == EstadoCaso.Cancelada)
            {
                Assert.Equal((1, 0), (cancelaciones, asignaciones));
                Assert.Equal(HttpStatusCode.OK, (await cancelacion).StatusCode);
                Assert.IsType<ResultadoAsignacion.Rechazado>(await asignacion);
            }
            else
            {
                Assert.Equal(EstadoCaso.Asignada, caso.Estado);
                Assert.Equal((0, 1), (cancelaciones, asignaciones));
                Assert.Equal(HttpStatusCode.Conflict, (await cancelacion).StatusCode);
                Assert.IsType<ResultadoAsignacion.Asignado>(await asignacion);
            }
        }
    }

    [Fact]
    public async Task CA07_AcompanamientoActivoDelMismoCiudadano_SigueActivoAlCancelarElSos()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsJsonAsync("/api/acompanamiento",
            new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true))).StatusCode);
        var ahora = DateTimeOffset.UtcNow;
        var sos = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, null));
        var casoId = (await sos.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;

        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Fue una falsa alarma.")).StatusCode);

        var sesion = (await app.GetFromJsonAsync<SesionAcompanamientoVista>("/api/acompanamiento/actual"))!;
        Assert.Equal(EstadosAcompanamiento.Activa, sesion.Estado);
        Assert.Null(sesion.CerradaEn);
    }

    [Fact]
    public async Task CA08_CancelacionReenviada_NoCambiaElMotivoNiLaFechaYHoraRegistrados()
    {
        var (app, _, casoId) = await SosAsync();
        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Primer motivo.")).StatusCode);
        var original = Assert.Single(await CancelacionesAsync(casoId));

        var reenvios = await Task.WhenAll(
            CancelarAsync(app, casoId, "Primer motivo."), CancelarAsync(app, casoId, "Otro motivo distinto."), CancelarAsync(app, casoId, null));

        Assert.All(reenvios, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var registrada = Assert.Single(await CancelacionesAsync(casoId));
        Assert.Equal(original.Motivo, registrada.Motivo);
        Assert.Equal(original.CanceladaEn, registrada.CanceladaEn);
        Assert.Single(await EventosCancelacionAsync(casoId));
        Assert.Equal("Cancelado por ti. Motivo: Primer motivo.", (await reenvios[1].Content.ReadFromJsonAsync<MiCasoDetalle>())!.Resultado);
    }

    [Fact]
    public async Task CA08_CancelacionesSimultaneasDelMismoSos_RegistranUnaSola()
    {
        var (app, _, casoId) = await SosAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => CancelarAsync(app, casoId, $"Motivo {i}.")));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await CancelacionesAsync(casoId));
        Assert.Single(await EventosCancelacionAsync(casoId));
    }

    // HU20 (vínculo con HU20-CA06): un SOS pendiente con intentos «Sin respuesta» se puede asignar; si su titular lo
    // cancela, deja de ser asignable y la central lo indica.
    [Fact]
    public async Task S5T03_HU20_SosCanceladoConIntentosSinRespuesta_NoSeAsigna()
    {
        var (app, _, casoId) = await SosAsync();
        var serenoId = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RegistrarIntentoContacto>()
            .EjecutarAsync(casoId, operador.Id, new DatosIntentoContacto(Guid.NewGuid(), "SinRespuesta", null, null), CancellationToken.None));
        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Ya me ayudaron.")).StatusCode);

        var rechazo = Assert.IsType<ResultadoAsignacion.Rechazado>(await AsignarAsync(casoId, serenoId, operador.Id));

        Assert.Equal(AsignarCaso.MotivoCancelado, rechazo.Motivo);
        Assert.Equal(EstadoCaso.Cancelada, (await CasoAsync(casoId)).Estado);
        Assert.Equal(0, await AsignacionesAsync(casoId));
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        Assert.Contains("El ciudadano canceló este SOS", await navegador.GetStringAsync($"/panel/central/sos/{casoId}"));
    }

    // HU27-CA02: un caso «Cancelada» permanece identificado con su resultado en el historial, con su responsable y momento.
    [Fact]
    public async Task S5T03_HU27_CA02_SosCancelado_ElHistorialLoIdentificaConSuResultado()
    {
        var (app, _, casoId) = await SosAsync();
        Assert.Equal(HttpStatusCode.OK, (await CancelarAsync(app, casoId, "Fue una falsa alarma.")).StatusCode);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);

        var historial = await fabrica.ConServiciosAsync(s => s.GetRequiredService<Cad.Server.Modulos.Consultas.ConsultarHistorialCaso>().EjecutarAsync(casoId, CancellationToken.None));
        var html = await navegador.GetStringAsync($"/panel/casos/{casoId}/historial");

        Assert.NotNull(historial);
        Assert.Equal("Cancelada", historial.Estado);
        Assert.Equal("Cancelada", historial.Resultado!.Estado);
        Assert.Equal("Cancelado por el ciudadano. Motivo: Fue una falsa alarma.", historial.Resultado.Descripcion);
        var entrada = Assert.Single(historial.Entradas, e => e.Actuacion == "SOS cancelado por el ciudadano");
        Assert.Equal("Prueba Ciudadano", entrada.Responsable);
        Assert.Equal("Motivo: Fue una falsa alarma.", entrada.Detalle);
        Assert.Contains("Cancelado por el ciudadano. Motivo: Fue una falsa alarma.", html);
        // HU26: la búsqueda de casos filtra por «Cancelada».
        Assert.Contains(casoId.ToString(), await navegador.GetStringAsync("/panel/casos?Estado=Cancelada"));
    }

    private sealed class NotificadorQueFalla : INotificador
    {
        public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) => Task.CompletedTask;
        public Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
        public Task CasoActualizadoAsync(Guid casoId, CancellationToken ct) => Task.CompletedTask;
        public Task SosCanceladoAsync(Guid casoId, CancellationToken ct) => throw new InvalidOperationException("Fallo simulado.");
        public Task DespachoAsignadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
        public Task DespachoActualizadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
    }
}
