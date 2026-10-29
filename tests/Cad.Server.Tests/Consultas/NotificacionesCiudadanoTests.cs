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

namespace Cad.Server.Tests.Consultas;

// HU31 — Recibir notificaciones de avances de mis casos (S3-T14, S3-T15). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class NotificacionesCiudadanoTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/notificaciones";
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Otros = CategoriaIncidencia.Iniciales[3].Id;

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> CiudadanoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(cuenta), cuenta);
    }

    private static async Task<Guid> ReporteAsync(HttpClient app, string descripcion = "Descripción ficticia del hecho")
    {
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), Robo, descripcion,
            DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Referencia ficticia del lugar")));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private static async Task<Guid> SosAsync(HttpClient app)
    {
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> accion) => fabrica.ConServiciosAsync(accion);

    private static async Task<BandejaNotificaciones> BandejaAsync(HttpClient app) => (await app.GetFromJsonAsync<BandejaNotificaciones>(Ruta))!;

    private Task<List<NotificacionCiudadano>> DelCasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.NotificacionesCiudadano.AsNoTracking().Where(n => n.CasoId == casoId).OrderBy(n => n.CreadaEn).ToListAsync());

    private async Task<CuentaDePrueba> SerenoOperativoAsync()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return sereno;
    }

    private async Task Asignar(Guid casoId, Guid serenoId, Guid operadorId) =>
        Assert.IsType<ResultadoAsignacion.Asignado>(await Servicio(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, serenoId, operadorId, Guid.NewGuid(), CancellationToken.None)));

    private Task Avanzar(Guid casoId, Avance avance, Guid serenoId) =>
        Servicio(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(casoId, avance, serenoId, ActorAvance.Sereno, CancellationToken.None));

    private async Task Cerrar(Guid casoId, Guid serenoId) =>
        Assert.IsType<ResultadoCierre.Cerrado>(await Servicio(s => s.GetRequiredService<CerrarCaso>().EjecutarAsync(casoId,
            new DatosCierre(Robo, "ResueltoEnElLugar", "Resultado público ficticio", "Notas internas ficticias"), serenoId, ActorAvance.Sereno, CancellationToken.None)));

    // ----- CA01 y CA05 -----

    [Fact]
    public async Task CA01_CA05_CadaCambioDelEstadoPublico_GeneraUnaNotificacion_YLosInternosNo()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await SerenoOperativoAsync();
        Assert.Empty(await DelCasoAsync(reporte)); // registrar no notifica: lo hizo el propio ciudadano

        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));
        await Servicio(s => s.GetRequiredService<RevisarReporte>().CorregirCategoriaAsync(reporte, Otros, operador.Id, CancellationToken.None)); // interno
        await Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(reporte,
            new DatosDecision(TipoDecisionReporte.Admision, "Motivo ficticio", Prioridad: "Media", Heridos: "No"), operador.Id, CancellationToken.None));
        await Asignar(reporte, sereno.Id, operador.Id);
        await Avanzar(reporte, Avance.ConfirmarDesplazamiento, sereno.Id); // sigue «En atención»
        await Avanzar(reporte, Avance.IniciarAtencion, sereno.Id);         // sigue «En atención»
        await Cerrar(reporte, sereno.Id);

        var notificaciones = await DelCasoAsync(reporte);
        Assert.Equal([EstadosPublicos.EnRevision, EstadosPublicos.Admitido, EstadosPublicos.EnAtencion, EstadosPublicos.Cerrado],
            notificaciones.Select(n => n.EstadoPublico));
        Assert.All(notificaciones, n => Assert.Equal(ciudadano.Id, n.CiudadanoId));
        Assert.Equal("Tu reporte pasó a «En revisión».", notificaciones[0].Texto);
        var bandeja = await BandejaAsync(app);
        Assert.Equal(4, bandeja.NoLeidas);
        Assert.All(bandeja.Notificaciones, n => Assert.Equal(reporte, n.CasoId)); // vinculadas con el caso
        Assert.Equal("Tu reporte pasó a «Cerrado».", bandeja.Notificaciones[0].Texto); // la más reciente primero
    }

    [Fact]
    public async Task CA01_UnSos_NotificaLaAtencionYElCierre_PeroNoLaClasificacionNiElContacto()
    {
        var (app, _) = await CiudadanoAsync();
        var sos = await SosAsync(app);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await SerenoOperativoAsync();

        await Servicio(s => s.GetRequiredService<RegistrarIntentoContacto>().EjecutarAsync(sos, operador.Id,
            new DatosIntentoContacto(Guid.NewGuid(), "Contactado", "Información reservada", null), CancellationToken.None));
        await Servicio(s => s.GetRequiredService<ClasificarCaso>().EjecutarAsync(sos, Robo, "Media", "No", operador.Id, CancellationToken.None));
        Assert.Empty(await DelCasoAsync(sos));
        await Asignar(sos, sereno.Id, operador.Id);
        await Avanzar(sos, Avance.ConfirmarDesplazamiento, sereno.Id);
        await Avanzar(sos, Avance.IniciarAtencion, sereno.Id);
        await Cerrar(sos, sereno.Id);

        Assert.Equal(["Tu SOS pasó a «En atención».", "Tu SOS pasó a «Cerrado»."], (await DelCasoAsync(sos)).Select(n => n.Texto));
    }

    [Theory]
    [InlineData(TipoDecisionReporte.Derivacion, EstadosPublicos.Derivado)]
    [InlineData(TipoDecisionReporte.Descarte, EstadosPublicos.Descartado)]
    public async Task CA01_DerivarODescartar_GeneraSuNotificacion(TipoDecisionReporte tipo, string estado)
    {
        var (app, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));

        await Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(reporte,
            new DatosDecision(tipo, "Motivo reservado", "Otro", "Acción reservada", "Destino reservado", "Media", "No", "Duplicado"), operador.Id, CancellationToken.None));

        Assert.Equal([EstadosPublicos.EnRevision, estado], (await DelCasoAsync(reporte)).Select(n => n.EstadoPublico));
    }

    [Fact]
    public async Task CA01_ConRevisionesSimultaneas_SeGeneraUnaSolaNotificacion()
    {
        var (app, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operadores = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fabrica.CrearAsync(Roles.Operador)));

        await Task.WhenAll(operadores.Select(o =>
            Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, o.Id, CancellationToken.None))));

        Assert.Single(await DelCasoAsync(reporte));
    }

    // ----- CA02 -----

    [Fact]
    public async Task CA02_LoGeneradoSinConexion_SeConsultaAlVolverAIngresar_YSePuedeMarcarComoLeido()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var reporte = await ReporteAsync(await fabrica.CrearClienteAppAsync(ciudadano));
        var operador = await fabrica.CrearAsync(Roles.Operador);
        // El ciudadano no está conectado: la notificación queda guardada.
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));

        var app = await fabrica.CrearClienteAppAsync(ciudadano); // nuevo ingreso
        var bandeja = await BandejaAsync(app);
        var notificacion = Assert.Single(bandeja.Notificaciones);
        Assert.Equal(1, bandeja.NoLeidas);
        Assert.False(notificacion.Leida);

        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync($"{Ruta}/{notificacion.Id}/leida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync($"{Ruta}/{notificacion.Id}/leida", null)).StatusCode); // repetir no falla

        var despues = await BandejaAsync(app);
        Assert.Equal(0, despues.NoLeidas);
        Assert.True(Assert.Single(despues.Notificaciones).Leida); // sigue disponible
    }

    // ----- CA03 -----

    [Fact]
    public async Task CA03_LaVistaPrevia_NoIncluyeDetallesDelHecho()
    {
        var (app, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app, "Descripción reservada del hecho");
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));
        await Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(reporte,
            new DatosDecision(TipoDecisionReporte.Derivacion, "Motivo reservado", "Otro", "Acción reservada", "Destino reservado"), operador.Id, CancellationToken.None));

        var json = await app.GetStringAsync(Ruta);

        foreach (var reservado in new[] { "Descripción reservada", "Referencia ficticia", "Motivo reservado", "Destino reservado", "Acción reservada",
                     "Robo", "Prueba Operador", operador.Id.ToString() })
            Assert.DoesNotContain(reservado, json);
        Assert.Contains("Tu reporte pasó a «Derivado».", json);
    }

    // ----- CA04 -----

    [Fact]
    public async Task CA04_SoloElTitular_RecibeYMarcaSusNotificaciones()
    {
        var (app, _) = await CiudadanoAsync();
        var (otro, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(reporte, operador.Id, CancellationToken.None));
        var propia = Assert.Single((await BandejaAsync(app)).Notificaciones);

        Assert.Empty((await BandejaAsync(otro)).Notificaciones);
        Assert.Equal(HttpStatusCode.NotFound, (await otro.PostAsync($"{Ruta}/{propia.Id}/leida", null)).StatusCode);
        Assert.False(Assert.Single((await BandejaAsync(app)).Notificaciones).Leida);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Operador)]
    public async Task CA04_HU03_OtrosPerfiles_NoConsultanLaBandeja(string? rol)
    {
        HttpClient cliente;
        if (rol is null)
            cliente = fabrica.CreateClient();
        else if (rol == Roles.Sereno)
            cliente = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));
        else
        {
            var cuenta = await fabrica.CrearAsync(rol);
            cliente = fabrica.CrearNavegador();
            await cliente.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        }

        var respuesta = await cliente.GetAsync(Ruta);

        Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{(int)respuesta.StatusCode}");
    }
}
