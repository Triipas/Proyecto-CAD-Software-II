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
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Consultas;

// HU30 — Consultar mis casos (S3-T13). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class MisCasosTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/casos/mios";
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> CiudadanoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(cuenta), cuenta);
    }

    private static async Task<Guid> SosAsync(HttpClient app)
    {
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.05, -77.03, 8, ahora.AddSeconds(1))));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private static async Task<Guid> ReporteAsync(HttpClient app)
    {
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), Robo, "Hecho ficticio",
            DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Esquina ficticia")));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> accion) => fabrica.ConServiciosAsync(accion);

    private async Task<CuentaDePrueba> OperadorAsync() => await fabrica.CrearAsync(Roles.Operador);

    private async Task Revisar(Guid id, Guid operador) =>
        await Servicio(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador, CancellationToken.None));

    private async Task Decidir(Guid id, Guid operador, DatosDecision datos) =>
        await Servicio(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id, datos, operador, CancellationToken.None));

    // Asigna el caso a un sereno nuevo y lo lleva hasta el cierre (con notas internas, que el titular no debe ver).
    private async Task<CuentaDePrueba> AtenderYCerrar(Guid id, bool cerrar = true)
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var operador = await OperadorAsync();
        Assert.IsType<ResultadoAsignacion.Asignado>(await Servicio(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(id, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
        await Servicio(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(id, Avance.ConfirmarDesplazamiento, sereno.Id, ActorAvance.Sereno, CancellationToken.None));
        await Servicio(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(id, Avance.IniciarAtencion, sereno.Id, ActorAvance.Sereno, CancellationToken.None));
        if (cerrar)
            Assert.IsType<ResultadoCierre.Cerrado>(await Servicio(s => s.GetRequiredService<CerrarCaso>().EjecutarAsync(id,
                new DatosCierre(Robo, "Se recuperó el objeto y se orientó al ciudadano", "Nota interna ficticia reservada"), sereno.Id,
                ActorAvance.Sereno, CancellationToken.None)));
        return sereno;
    }

    // ----- CA01 -----

    [Fact]
    public async Task CA01_MisCasos_MuestraCadaSosYReportePropioConIdentificadorOrigenFechaYEstado()
    {
        var (app, _) = await CiudadanoAsync();
        var sos = await SosAsync(app);
        var reporte = await ReporteAsync(app);
        var (otro, _) = await CiudadanoAsync();
        var ajeno = await SosAsync(otro);

        var casos = (await app.GetFromJsonAsync<List<MiCasoResumen>>(Ruta))!;

        Assert.Equal([reporte, sos], casos.Select(c => c.Id)); // los más recientes primero, sin el caso ajeno
        Assert.Equal(("Reporte", EstadosPublicos.Recibido), (casos[0].Origen, casos[0].Estado));
        Assert.Equal(("SOS", EstadosPublicos.Recibido), (casos[1].Origen, casos[1].Estado));
        Assert.All(casos, c => Assert.InRange(c.RegistradoEn, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.DoesNotContain(casos, c => c.Id == ajeno);
    }

    [Fact]
    public async Task CA01_UnCiudadanoSinCasos_RecibeUnaListaVacia()
    {
        var (app, _) = await CiudadanoAsync();

        Assert.Empty((await app.GetFromJsonAsync<List<MiCasoResumen>>(Ruta))!);
    }

    // ----- CA02 y DP05 -----

    [Fact]
    public async Task CA02_DP05_ElEstadoActual_EsElEstadoPublico()
    {
        var (app, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operador = await OperadorAsync();
        async Task<string> Estado() => (await app.GetFromJsonAsync<MiCasoDetalle>($"{Ruta}/{reporte}"))!.Estado;

        Assert.Equal(EstadosPublicos.Recibido, await Estado());
        await Revisar(reporte, operador.Id);
        Assert.Equal(EstadosPublicos.EnRevision, await Estado());
        await Decidir(reporte, operador.Id, new DatosDecision(TipoDecisionReporte.Admision, "Motivo de admisión reservado"));
        Assert.Equal(EstadosPublicos.Admitido, await Estado());
        await AtenderYCerrar(reporte, cerrar: false); // asignado → en camino → en atención: un solo estado público
        Assert.Equal(EstadosPublicos.EnAtencion, await Estado());
    }

    [Theory]
    [InlineData(EstadoCaso.Pendiente, OrigenCaso.Sos, EstadosPublicos.Recibido)]
    [InlineData(EstadoCaso.Pendiente, OrigenCaso.Reporte, EstadosPublicos.Admitido)]
    [InlineData(EstadoCaso.Asignada, OrigenCaso.Sos, EstadosPublicos.EnAtencion)]
    [InlineData(EstadoCaso.EnCamino, OrigenCaso.Sos, EstadosPublicos.EnAtencion)]
    [InlineData(EstadoCaso.EnAtencion, OrigenCaso.Reporte, EstadosPublicos.EnAtencion)]
    [InlineData(EstadoCaso.Cerrada, OrigenCaso.Sos, EstadosPublicos.Cerrado)]
    [InlineData(EstadoCaso.Recibido, OrigenCaso.Reporte, EstadosPublicos.Recibido)]
    [InlineData(EstadoCaso.EnRevision, OrigenCaso.Reporte, EstadosPublicos.EnRevision)]
    [InlineData(EstadoCaso.Derivada, OrigenCaso.Reporte, EstadosPublicos.Derivado)]
    [InlineData(EstadoCaso.Descartada, OrigenCaso.Reporte, EstadosPublicos.Descartado)]
    public void DP05_CadaEstadoInterno_TieneSuEstadoPublico(EstadoCaso estado, OrigenCaso origen, string publico) =>
        Assert.Equal(publico, Modulos.Consultas.ConsultarMisCasos.EstadoPublico(origen, estado));

    // ----- CA03 -----

    [Fact]
    public async Task CA03_UnCasoCerrado_MuestraElResultadoPublicoYSusAvances()
    {
        var (app, _) = await CiudadanoAsync();
        var sos = await SosAsync(app);
        await AtenderYCerrar(sos);

        var detalle = (await app.GetFromJsonAsync<MiCasoDetalle>($"{Ruta}/{sos}"))!;

        Assert.Equal(EstadosPublicos.Cerrado, detalle.Estado);
        Assert.Equal("Se recuperó el objeto y se orientó al ciudadano", detalle.Resultado);
        Assert.Equal([EstadosPublicos.Recibido, EstadosPublicos.EnAtencion, EstadosPublicos.Cerrado], detalle.Avances.Select(a => a.Estado));
        Assert.Equal(detalle.Avances.OrderBy(a => a.En), detalle.Avances);
        var resumen = Assert.Single((await app.GetFromJsonAsync<List<MiCasoResumen>>(Ruta))!);
        Assert.Equal("Se recuperó el objeto y se orientó al ciudadano", resumen.Resultado);
    }

    [Fact]
    public async Task CA03_DP05_UnReporteDerivado_MuestraElDestino_YUnoDescartado_ElMotivo()
    {
        var (app, _) = await CiudadanoAsync();
        var derivado = await ReporteAsync(app);
        var descartado = await ReporteAsync(app);
        var operador = await OperadorAsync();
        await Revisar(derivado, operador.Id);
        await Decidir(derivado, operador.Id, new DatosDecision(TipoDecisionReporte.Derivacion, "Motivo de derivación reservado",
            "Comisaría ficticia", "Acción realizada reservada"));
        await Revisar(descartado, operador.Id);
        await Decidir(descartado, operador.Id, new DatosDecision(TipoDecisionReporte.Descarte, "Es un duplicado"));

        var d1 = (await app.GetFromJsonAsync<MiCasoDetalle>($"{Ruta}/{derivado}"))!;
        var d2 = (await app.GetFromJsonAsync<MiCasoDetalle>($"{Ruta}/{descartado}"))!;

        Assert.Equal((EstadosPublicos.Derivado, "Derivado a Comisaría ficticia"), (d1.Estado, d1.Resultado));
        Assert.Equal([EstadosPublicos.Recibido, EstadosPublicos.EnRevision, EstadosPublicos.Derivado], d1.Avances.Select(a => a.Estado));
        Assert.Equal("Derivado a Comisaría ficticia", d1.Avances[^1].Detalle);
        // DP05: el destino sí; el motivo de la derivación y la acción realizada, no.
        var json = await app.GetStringAsync($"{Ruta}/{derivado}") + await app.GetStringAsync(Ruta);
        Assert.DoesNotContain("Motivo de derivación reservado", json);
        Assert.DoesNotContain("Acción realizada reservada", json);
        Assert.Equal((EstadosPublicos.Descartado, "Descartado. Motivo: Es un duplicado"), (d2.Estado, d2.Resultado));
        Assert.Equal("Motivo: Es un duplicado", d2.Avances[^1].Detalle);
    }

    [Fact]
    public async Task CA03_UnCasoNoFinalizado_NoTieneResultado()
    {
        var (app, _) = await CiudadanoAsync();
        var sos = await SosAsync(app);

        Assert.Null((await app.GetFromJsonAsync<MiCasoDetalle>($"{Ruta}/{sos}"))!.Resultado);
    }

    // ----- CA04 -----

    [Fact]
    public async Task CA04_ElCasoDeOtroCiudadano_SeRechazaAunqueSeConozcaSuIdentificador()
    {
        var (app, _) = await CiudadanoAsync();
        var (otro, _) = await CiudadanoAsync();
        var ajeno = await SosAsync(otro);

        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"{Ruta}/{ajeno}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.GetAsync($"{Ruta}/{Guid.NewGuid()}")).StatusCode); // indistinguible
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Operador)]
    public async Task CA04_HU03_OtrosPerfiles_NoConsultanMisCasos(string? rol)
    {
        var (app, _) = await CiudadanoAsync();
        var sos = await SosAsync(app);
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

        foreach (var ruta in new[] { Ruta, $"{Ruta}/{sos}" })
        {
            var respuesta = await cliente.GetAsync(ruta);
            Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{ruta}: se esperaba 401/403 y se obtuvo {(int)respuesta.StatusCode}");
        }
    }

    // ----- CA05 -----

    [Fact]
    public async Task CA05_NoSeMuestranNotasInternasNiDatosDeTerceros()
    {
        var (app, _) = await CiudadanoAsync();
        var reporte = await ReporteAsync(app);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await Revisar(reporte, operador.Id);
        await Decidir(reporte, operador.Id, new DatosDecision(TipoDecisionReporte.Admision, "Motivo de admisión reservado"));
        var sereno = await AtenderYCerrar(reporte);
        var sos = await SosAsync(app);
        await Servicio(s => s.GetRequiredService<RegistrarIntentoContacto>().EjecutarAsync(sos, operador.Id,
            new DatosIntentoContacto(Guid.NewGuid(), "Contactado", "Información del hecho reservada", "Referencia reservada"), CancellationToken.None));

        var json = await app.GetStringAsync($"{Ruta}/{reporte}") + await app.GetStringAsync($"{Ruta}/{sos}") + await app.GetStringAsync(Ruta);

        foreach (var reservado in new[] { "Nota interna ficticia reservada", "Motivo de admisión reservado", "Información del hecho reservada",
                     "Referencia reservada", "Prueba Operador", "Prueba Sereno", operador.Id.ToString(), sereno.Id.ToString(), operador.Correo, sereno.Correo })
            Assert.DoesNotContain(reservado, json);
    }
}
