using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Casos;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Cad.Server.Tests.Casos;

// Datos de registro para la analítica (27/10/2026): lo que antes era texto libre se elige de un catálogo y se guarda como
// código (resultado del cierre, prioridad y heridos, motivos de descarte, cancelación y reasignación, destino de una derivación,
// canal de entrada), y el estado operativo del sereno deja historial. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class DatosAnaliticaTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);
    private Task<T> Base<T>(Func<Cad.Server.Persistencia.CadDbContext, Task<T>> consulta) => fabrica.ConBaseDatosAsync(consulta);

    private async Task<(Guid Id, HttpClient App)> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();
        return (sos!.Id, app);
    }

    private async Task<(Guid Id, HttpClient App)> ReporteAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", Solicitud());
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return ((await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id, app);
    }

    private static SolicitudRegistroReporte Solicitud() => new(Guid.NewGuid(), Robo, "Descripción ficticia", DateTimeOffset.UtcNow.AddHours(-1),
        new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Frente al parque ficticio"));

    private async Task<(Guid Id, Guid OperadorId)> ReporteEnRevisionAsync()
    {
        var (id, _) = await ReporteAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        return (id, operador.Id);
    }

    private async Task<(CuentaDePrueba Cuenta, HttpClient App)> SerenoOperativoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return (cuenta, app);
    }

    private Task<ResultadoAsignacion> AsignarAsync(Guid casoId, Guid serenoId, Guid operadorId) =>
        Servicio(s => s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, serenoId, operadorId, Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task Cierre_ExigeComoTerminoLaAtencion_YLoGuarda()
    {
        var (casoId, _) = await SosAsync();
        var (sereno, app) = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAsignacion.Asignado>(await AsignarAsync(casoId, sereno.Id, operador.Id));
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);

        // Una app anterior al catálogo no lo envía: no se cierra y el error lo nombra.
        var sinResultado = await app.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre", new SolicitudCierreCaso(Robo, "Resultado ficticio", null));
        Assert.Equal(HttpStatusCode.BadRequest, sinResultado.StatusCode);
        Assert.Contains("resultadoAtencion", await sinResultado.Content.ReadAsStringAsync());

        var cierre = await app.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre",
            new SolicitudCierreCaso(Robo, "Resultado ficticio", null, ResultadosAtencion.SinHallazgo));
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(ResultadosAtencion.SinHallazgo, await Base(db => db.CierresCaso.Where(c => c.CasoId == casoId).Select(c => c.ResultadoAtencion).SingleAsync()));
    }

    [Fact]
    public async Task Clasificar_ExigePrioridadYHeridos_YLosGuarda()
    {
        var (casoId, _) = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Task<ResultadoClasificacion> Clasificar(string? prioridad, string? heridos) =>
            Servicio(s => s.GetRequiredService<ClasificarCaso>().EjecutarAsync(casoId, Robo, prioridad, heridos, operador.Id, CancellationToken.None));

        Assert.Equal("prioridad", Assert.IsType<ResultadoClasificacion.Invalido>(await Clasificar(null, PersonasHeridas.No)).Campo);
        Assert.Equal("heridos", Assert.IsType<ResultadoClasificacion.Invalido>(await Clasificar(PrioridadesCaso.Alta, "Quizas")).Campo);
        Assert.Null(await Base(db => db.Casos.Where(c => c.Id == casoId).Select(c => c.CategoriaId).SingleAsync()));

        Assert.IsType<ResultadoClasificacion.Clasificado>(await Clasificar(PrioridadesCaso.Alta, PersonasHeridas.Si));
        var caso = await Base(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));
        Assert.Equal((PrioridadesCaso.Alta, PersonasHeridas.Si), (caso.Prioridad, caso.Heridos));
    }

    [Fact]
    public async Task Descartar_ExigeElMotivoDelCatalogo_YLoGuardaConSuDetalle()
    {
        var (casoId, _) = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Task<ResultadoDescarte> Descartar(string? codigo) =>
            Servicio(s => s.GetRequiredService<DescartarCaso>().EjecutarAsync(casoId, codigo, "Llamada de broma (ficticio)", operador.Id, CancellationToken.None));

        Assert.True(Assert.IsType<ResultadoDescarte.Invalido>(await Descartar(null)).Errores.ContainsKey("motivoCodigo"));
        Assert.IsType<ResultadoDescarte.Descartado>(await Descartar(MotivosDescarte.FalsoOMalintencionado));
        var descarte = await Base(db => db.DescartesCaso.AsNoTracking().SingleAsync(d => d.CasoId == casoId));
        Assert.Equal((MotivosDescarte.FalsoOMalintencionado, "Llamada de broma (ficticio)"), (descarte.MotivoCodigo, descarte.Motivo));
    }

    [Fact]
    public async Task Cancelacion_ElMotivoSeEligeDelCatalogo_YLaAppAnteriorQuedaComoOtroMotivo()
    {
        Task<HttpResponseMessage> Cancelar(HttpClient app, Guid id, object cuerpo) => app.PostAsJsonAsync($"/api/casos/mios/{id}/cancelacion", cuerpo);
        Task<CancelacionSos> CancelacionAsync(Guid id) => Base(db => db.CancelacionesSos.AsNoTracking().SingleAsync(c => c.CasoId == id));

        // Con el motivo del catálogo, escribirlo es opcional: se guarda su texto.
        var (porError, app1) = await SosAsync();
        Assert.Equal(HttpStatusCode.OK, (await Cancelar(app1, porError, new SolicitudCancelacionSos(null, MotivosCancelacion.PorError))).StatusCode);
        var c1 = await CancelacionAsync(porError);
        Assert.Equal((MotivosCancelacion.PorError, "Lo envié por error"), (c1.MotivoCodigo, c1.Motivo));

        // «Otro motivo» sin escribirlo no se acepta.
        var (otro, app2) = await SosAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Cancelar(app2, otro, new SolicitudCancelacionSos("  ", MotivosCancelacion.Otro))).StatusCode);

        // Una app anterior envía solo el texto: se registra como «Otro motivo» con lo que escribió.
        Assert.Equal(HttpStatusCode.OK, (await Cancelar(app2, otro, new { motivo = "Ya llegó un vecino (ficticio)." })).StatusCode);
        var c2 = await CancelacionAsync(otro);
        Assert.Equal((MotivosCancelacion.Otro, "Ya llegó un vecino (ficticio)."), (c2.MotivoCodigo, c2.Motivo));
    }

    [Fact]
    public async Task Reasignar_GuardaElMotivoDelCatalogo_ConSuTextoSiNoSeEscribioDetalle()
    {
        var (casoId, _) = await SosAsync();
        var (anterior, _) = await SerenoOperativoAsync();
        var (nuevo, _) = await SerenoOperativoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAsignacion.Asignado>(await AsignarAsync(casoId, anterior.Id, operador.Id));

        var resultado = await Servicio(s => s.GetRequiredService<ReasignarCaso>()
            .EjecutarAsync(casoId, nuevo.Id, MotivosReasignacion.OtroMasCercano, null, operador.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.IsType<ResultadoReasignacion.Reasignado>(resultado);
        var reasignacion = await Base(db => db.Reasignaciones.AsNoTracking().SingleAsync(r => r.CasoId == casoId));
        Assert.Equal((MotivosReasignacion.OtroMasCercano, "Hay otro sereno más cerca"), (reasignacion.MotivoCodigo, reasignacion.Motivo));
    }

    [Fact]
    public async Task Admitir_ExigeLaPrioridad_YSeGuardaEnElCaso()
    {
        var (id, operador) = await ReporteEnRevisionAsync();
        Task<ResultadoDecision> Admitir(string? prioridad) => Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id,
            new DatosDecision(TipoDecisionReporte.Admision, "Corresponde (ficticio)", Prioridad: prioridad, Heridos: PersonasHeridas.NoSeSabe),
            operador, CancellationToken.None));

        Assert.True(Assert.IsType<ResultadoDecision.Invalido>(await Admitir(null)).Errores.ContainsKey("prioridad"));
        Assert.IsType<ResultadoDecision.Registrado>(await Admitir(PrioridadesCaso.Baja));
        var caso = await Base(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal((PrioridadesCaso.Baja, PersonasHeridas.NoSeSabe), (caso.Prioridad, caso.Heridos));
    }

    [Fact]
    public async Task Derivar_GuardaElDestinoDelCatalogo_YConOtraEntidadExigeEscribirla()
    {
        var (id, operador) = await ReporteEnRevisionAsync();
        Task<ResultadoDecision> Derivar(string destino, string? detalle) => Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id,
            new DatosDecision(TipoDecisionReporte.Derivacion, "No es de serenazgo (ficticio)", destino, "Se llamó a la central (ficticio)", detalle),
            operador, CancellationToken.None));

        Assert.True(Assert.IsType<ResultadoDecision.Invalido>(await Derivar(DestinosDerivacion.Otro, null)).Errores.ContainsKey("detalleDestino"));
        Assert.True(Assert.IsType<ResultadoDecision.Invalido>(await Derivar("Comisaría", null)).Errores.ContainsKey("destino"));
        Assert.IsType<ResultadoDecision.Registrado>(await Derivar(DestinosDerivacion.Samu, null));
        var decision = await Base(db => db.DecisionesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id));
        Assert.Equal((DestinosDerivacion.Samu, "SAMU (106)"), (decision.DestinoCodigo, decision.Destino));
    }

    [Fact]
    public async Task DescartarUnReporte_GuardaElMotivoDelCatalogo()
    {
        var (id, operador) = await ReporteEnRevisionAsync();
        Task<ResultadoDecision> Descartar(string? motivo) => Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id,
            new DatosDecision(TipoDecisionReporte.Descarte, "Ya lo reportó otro vecino (ficticio)", MotivoDescarte: motivo), operador, CancellationToken.None));

        Assert.True(Assert.IsType<ResultadoDecision.Invalido>(await Descartar(null)).Errores.ContainsKey("motivoDescarte"));
        Assert.IsType<ResultadoDecision.Registrado>(await Descartar(MotivosDescarte.Duplicado));
        Assert.Equal(MotivosDescarte.Duplicado, await Base(db => db.DecisionesReporte.Where(d => d.CasoId == id).Select(d => d.MotivoCodigo).SingleAsync()));
    }

    [Fact]
    public async Task Canal_ElReporteDeLaAppEsApp_YElDeLaCentralExigeCanalYPrioridad()
    {
        var (deLaApp, _) = await ReporteAsync();
        Assert.Equal(CanalesEntrada.App, await Base(db => db.DetallesReporte.Where(d => d.CasoId == deLaApp).Select(d => d.Canal).SingleAsync()));

        var operador = await fabrica.CrearAsync(Roles.Operador);
        Task<ResultadoRegistroReporte> Registrar(string? canal) => Servicio(s => s.GetRequiredService<RegistrarReporte>().RegistrarDeLaCentralAsync(
            operador.Id, Solicitud(), new DatosReporteCentral(canal, PrioridadesCaso.Media, PersonasHeridas.No, null, null), CancellationToken.None));

        var sinCanal = Assert.IsType<ResultadoRegistroReporte.Invalido>(await Registrar(null));
        Assert.True(sinCanal.Errores.ContainsKey("canal"));
        Assert.True(Assert.IsType<ResultadoRegistroReporte.Invalido>(await Registrar(CanalesEntrada.App)).Errores.ContainsKey("canal")); // la app no es un canal de la central
        var caso = Assert.IsType<ResultadoRegistroReporte.Registrado>(await Registrar(CanalesEntrada.Radio)).Caso;
        Assert.Equal(CanalesEntrada.Radio, await Base(db => db.DetallesReporte.Where(d => d.CasoId == caso.Id).Select(d => d.Canal).SingleAsync()));
        Assert.Equal(PrioridadesCaso.Media, await Base(db => db.Casos.Where(c => c.Id == caso.Id).Select(c => c.Prioridad).SingleAsync()));
    }

    [Fact]
    public async Task EstadoDelSereno_CadaCambioQuedaEnSuHistorial_YRepetirElMismoNoSuma()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        foreach (var operativo in new[] { true, true, false })
            Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(operativo, true))).StatusCode);

        var historial = await Base(db => db.CambiosEstadoSereno.AsNoTracking().Where(c => c.SerenoId == cuenta.Id)
            .OrderBy(c => c.CambiadoEn).Select(c => c.Operativo).ToListAsync());
        Assert.Equal([true, false], historial);
    }

    [Fact]
    public async Task LaBaseDeDatos_NoAdmiteUnCodigoFueraDelCatalogo()
    {
        var (casoId, _) = await SosAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => Base(db =>
            db.Database.ExecuteSqlAsync($"UPDATE \"Casos\" SET \"Prioridad\" = 'Urgentisima' WHERE \"Id\" = {casoId}")));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
