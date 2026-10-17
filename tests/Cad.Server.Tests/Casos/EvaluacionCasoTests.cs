using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Casos;

// HU32 — Evaluar la atención de un caso cerrado (S5-T11) y HU33 — Consultar las evaluaciones (S5-T12). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class EvaluacionCasoTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static string Ruta(Guid casoId) => $"/api/casos/mios/{casoId}/evaluacion";

    private async Task<(HttpClient App, CuentaDePrueba Cuenta, Guid CasoId)> SosAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        return (app, cuenta, (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id);
    }

    // Lleva el caso hasta el estado indicado con los casos de uso (asignado, en camino, en atención o cerrado).
    private async Task LlevarAAsync(Guid casoId, EstadoCaso estado)
    {
        if (estado == EstadoCaso.Pendiente)
            return;
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));
        if (estado == EstadoCaso.Asignada)
            return;
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(casoId, Avance.ConfirmarDesplazamiento, sereno.Id, ActorAvance.Sereno, CancellationToken.None));
        if (estado == EstadoCaso.EnCamino)
            return;
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(casoId, Avance.IniciarAtencion, sereno.Id, ActorAvance.Sereno, CancellationToken.None));
        if (estado == EstadoCaso.EnAtencion)
            return;
        Assert.IsType<ResultadoCierre.Cerrado>(await fabrica.ConServiciosAsync(s => s.GetRequiredService<CerrarCaso>().EjecutarAsync(casoId,
            new DatosCierre(Robo, "Se orientó al ciudadano", null), sereno.Id, ActorAvance.Sereno, CancellationToken.None)));
    }

    private async Task<(HttpClient App, CuentaDePrueba Cuenta, Guid CasoId)> CasoCerradoAsync()
    {
        var sos = await SosAsync();
        await LlevarAAsync(sos.CasoId, EstadoCaso.Cerrada);
        return sos;
    }

    private static Task<HttpResponseMessage> EvaluarAsync(HttpClient app, Guid casoId, int? puntuacion, string? comentario = null, Guid? idSolicitud = null) =>
        app.PostAsJsonAsync(Ruta(casoId), new SolicitudEvaluacionCaso(idSolicitud ?? Guid.NewGuid(), puntuacion, comentario));

    private Task<List<EvaluacionCaso>> EvaluacionesAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.EvaluacionesCaso.AsNoTracking().Where(e => e.CasoId == casoId).ToListAsync());

    private async Task<HttpClient> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    // ----- HU32 -----

    [Theory]
    [InlineData(1, "  La atención fue rápida y amable.  ", "La atención fue rápida y amable.")]
    [InlineData(5, null, null)]
    [InlineData(3, "   ", null)]
    public async Task HU32_CA01_CasoPropioCerrado_LaEvaluacionQuedaVinculadaConElCaso(int puntuacion, string? comentario, string? guardado)
    {
        var (app, cuenta, casoId) = await CasoCerradoAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await EvaluarAsync(app, casoId, puntuacion, comentario);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<MiEvaluacion>())!;
        Assert.Equal(puntuacion, vista.Puntuacion);
        Assert.Equal(guardado, vista.Comentario);
        var evaluacion = Assert.Single(await EvaluacionesAsync(casoId));
        Assert.Equal((puntuacion, guardado, cuenta.Id), (evaluacion.Puntuacion, evaluacion.Comentario, evaluacion.CiudadanoId));
        Assert.InRange(evaluacion.EvaluadaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        // HU30: el titular la ve en el detalle de su caso.
        var detalle = (await app.GetFromJsonAsync<MiCasoDetalle>($"/api/casos/mios/{casoId}"))!;
        Assert.Equal(puntuacion, detalle.Evaluacion!.Puntuacion);
        Assert.Equal(guardado, detalle.Evaluacion.Comentario);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task HU32_CA02_PuntuacionAusenteOFueraDeRango_NoSeRegistra(int? puntuacion)
    {
        var (app, _, casoId) = await CasoCerradoAsync();

        var respuesta = await EvaluarAsync(app, casoId, puntuacion, "Comentario");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("puntuacion", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await EvaluacionesAsync(casoId));
    }

    [Fact]
    public async Task HU32_CA02_LaBaseDeDatosTampocoAdmiteUnaPuntuacionFueraDeRango()
    {
        var (_, cuenta, casoId) = await CasoCerradoAsync();

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"EvaluacionesCaso\" (\"CasoId\", \"CiudadanoId\", \"IdSolicitud\", \"Puntuacion\", \"EvaluadaEn\") VALUES ({casoId}, {cuenta.Id}, {Guid.NewGuid()}, {6}, now())")));
        Assert.Equal("CK_EvaluacionesCaso_Puntuacion", error.ConstraintName);
    }

    [Fact]
    public async Task HU32_CA02_ComentarioDemasiadoLargo_NoSeRegistra()
    {
        var (app, _, casoId) = await CasoCerradoAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await EvaluarAsync(app, casoId, 4, new string('a', EvaluacionCaso.MaximoComentario + 1))).StatusCode);
        Assert.Empty(await EvaluacionesAsync(casoId));
        Assert.Equal(HttpStatusCode.Created, (await EvaluarAsync(app, casoId, 4, new string('a', EvaluacionCaso.MaximoComentario))).StatusCode);
    }

    [Fact]
    public async Task HU32_CA03_CasoYaEvaluado_OtroIntentoSeRechazaYQuedaUnaSola()
    {
        var (app, _, casoId) = await CasoCerradoAsync();
        Assert.Equal(HttpStatusCode.Created, (await EvaluarAsync(app, casoId, 2, "Primera")).StatusCode);

        var otra = await EvaluarAsync(app, casoId, 5, "Cambio de opinión");

        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Contains("Ya evaluaste este caso", await otra.Content.ReadAsStringAsync());
        var evaluacion = Assert.Single(await EvaluacionesAsync(casoId));
        Assert.Equal((2, "Primera"), (evaluacion.Puntuacion, evaluacion.Comentario));
    }

    [Fact]
    public async Task HU32_CA03_DosSolicitudesSimultaneas_QuedaUnaSolaEvaluacion()
    {
        var (app, _, casoId) = await CasoCerradoAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(1, 5).Select(i => EvaluarAsync(app, casoId, i, $"Intento {i}")));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await EvaluacionesAsync(casoId));
    }

    [Fact]
    public async Task HU32_CA03_ReenvioTecnicoDeLaMismaSolicitud_DevuelveLaMismaEvaluacion()
    {
        var (app, _, casoId) = await CasoCerradoAsync();
        var idSolicitud = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => EvaluarAsync(app, casoId, 4, "Bien", idSolicitud)));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Single(await EvaluacionesAsync(casoId));
    }

    [Theory]
    [InlineData(EstadoCaso.Pendiente)]
    [InlineData(EstadoCaso.Asignada)]
    [InlineData(EstadoCaso.EnCamino)]
    [InlineData(EstadoCaso.EnAtencion)]
    public async Task HU32_CA04_CasoNoCerrado_SeRechaza(EstadoCaso estado)
    {
        var (app, _, casoId) = await SosAsync();
        await LlevarAAsync(casoId, estado);

        var respuesta = await EvaluarAsync(app, casoId, 5);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("Solo se puede evaluar un caso cerrado", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await EvaluacionesAsync(casoId));
    }

    [Fact]
    public async Task HU32_CA04_SosCancelado_SeRechaza()
    {
        var (app, _, casoId) = await SosAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/casos/mios/{casoId}/cancelacion", new SolicitudCancelacionSos("Falsa alarma"))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await EvaluarAsync(app, casoId, 5)).StatusCode);
        Assert.Empty(await EvaluacionesAsync(casoId));
    }

    [Fact]
    public async Task HU32_CA05_NoTitular_SeRechazaSinRevelarElCaso()
    {
        var (_, _, casoId) = await CasoCerradoAsync();
        var otro = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sereno = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Sereno));

        Assert.Equal(HttpStatusCode.NotFound, (await EvaluarAsync(otro, casoId, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EvaluarAsync(otro, Guid.NewGuid(), 5)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EvaluarAsync(sereno, casoId, 5)).StatusCode);
        Assert.Empty(await EvaluacionesAsync(casoId));
    }

    [Fact]
    public async Task HU32_CA06_CasoCerradoSinEvaluar_NoRestringeNingunaFuncion()
    {
        var (app, _, casoId) = await CasoCerradoAsync();

        // Con el caso sin evaluar, el ciudadano consulta, emite otro SOS, cancela, reporta y abre sus notificaciones.
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/casos/mios")).StatusCode);
        Assert.Null((await app.GetFromJsonAsync<MiCasoDetalle>($"/api/casos/mios/{casoId}"))!.Evaluacion);
        var otro = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);
        var otroId = (await otro.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/casos/mios/{otroId}/cancelacion", new SolicitudCancelacionSos("Prueba"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/reportes/categorias")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/notificaciones")).StatusCode);
        Assert.True((await app.GetAsync("/api/acompanamiento/actual")).IsSuccessStatusCode);
        Assert.Empty(await EvaluacionesAsync(casoId));
    }

    // ----- HU33 -----

    [Fact]
    public async Task HU33_CA01_OperadorAutorizado_VeLaPuntuacionYElComentario()
    {
        var (app, _, casoId) = await CasoCerradoAsync();
        Assert.Equal(HttpStatusCode.Created, (await EvaluarAsync(app, casoId, 4, "El sereno llegó pronto.")).StatusCode);
        var operador = await PanelAsync();

        var html = await operador.GetStringAsync($"/panel/casos/{casoId}/historial");

        Assert.Contains("data-puntuacion=\"4\"", html);
        Assert.Contains("4 de 5", html);
        Assert.Contains("Comentario: El sereno llegó pronto.", html);
        Assert.DoesNotContain("data-sin-evaluacion", html);
    }

    [Fact]
    public async Task HU33_CA01_EvaluacionSinComentario_LoIndica()
    {
        var (app, _, casoId) = await CasoCerradoAsync();
        Assert.Equal(HttpStatusCode.Created, (await EvaluarAsync(app, casoId, 5)).StatusCode);

        var html = await (await PanelAsync()).GetStringAsync($"/panel/casos/{casoId}/historial");

        Assert.Contains("5 de 5", html);
        Assert.Contains("Sin comentario.", html);
    }

    [Fact]
    public async Task HU33_CA02_CasoCerradoSinEvaluacion_NoMuestraNingunaPuntuacion()
    {
        var (_, _, casoId) = await CasoCerradoAsync();

        var html = await (await PanelAsync()).GetStringAsync($"/panel/casos/{casoId}/historial");

        Assert.Contains("data-sin-evaluacion", html);
        Assert.DoesNotContain("data-puntuacion", html);
        Assert.DoesNotContain(" de 5", html);
    }

    [Theory]
    [InlineData(Roles.Administrador)]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Ciudadano)]
    public async Task HU33_CA03_UsuarioSinPermiso_NoVeLaEvaluacionNiConSolicitudDirecta(string rol)
    {
        var (app, _, casoId) = await CasoCerradoAsync();
        Assert.Equal(HttpStatusCode.Created, (await EvaluarAsync(app, casoId, 3, "Comentario reservado del ciudadano")).StatusCode);
        var navegador = rol == Roles.Administrador ? await PanelAsync(rol) : await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await navegador.GetAsync($"/panel/casos/{casoId}/historial");

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("Comentario reservado del ciudadano", await respuesta.Content.ReadAsStringAsync());
        // Tampoco por la API del ciudadano: solo el titular ve su evaluación.
        if (rol == Roles.Ciudadano)
            Assert.Equal(HttpStatusCode.NotFound, (await navegador.GetAsync($"/api/casos/mios/{casoId}")).StatusCode);
    }
}
