using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cad.Server.Tests.Casos;

// HU15 — Admitir un reporte (S3-T06), HU16 — Derivarlo (S3-T07), HU17 — Descartarlo (S3-T09) y
// HU20-CA07 — asignar un reporte admitido (S3-T08). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class DecisionReporteTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private static string Ruta(Guid id) => $"/panel/reportes/{id}";

    // Un reporte ya «En revisión» (HU10 y HU14), con el lugar del hecho por punto o por referencia.
    private async Task<Guid> EnRevisionAsync(bool conPunto = true)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var lugar = conPunto
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0589, -77.0461, null, "Frente a la plaza")
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje Los Olivos");
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), Robo, "Hecho ficticio",
            DateTimeOffset.UtcNow.AddHours(-1), lugar));
        var id = (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoRevision.Registrado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None)));
        return id;
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> DecidirAsync(HttpClient navegador, Guid id, string tipo, string? motivo,
        string? destino = null, string? accion = null)
    {
        var campos = new Dictionary<string, string>
        {
            ["Decision.Tipo"] = tipo,
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, Ruta(id)),
        };
        if (motivo is not null) campos["Decision.Motivo"] = motivo;
        if (destino is not null) campos["Decision.Destino"] = destino;
        if (accion is not null) campos["Decision.Accion"] = accion;
        return await navegador.PostAsync($"{Ruta(id)}?handler=Decidir", new FormUrlEncodedContent(campos));
    }

    private Task<ResultadoDecision> DecidirDirectoAsync(Guid id, DatosDecision datos, Guid operadorId) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id, datos, operadorId, CancellationToken.None));

    private Task<Caso> CasoAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<List<DecisionReporte>> DecisionesAsync(Guid id) =>
        fabrica.ConBaseDatosAsync(db => db.DecisionesReporte.AsNoTracking().Where(d => d.CasoId == id).ToListAsync());

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> SerenoOperativoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return (app, cuenta);
    }

    private static async Task<HttpResponseMessage> AsignarAsync(HttpClient navegador, Guid id, Guid serenoId) =>
        await navegador.PostAsync($"{Ruta(id)}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(),
            ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["Asignacion.Volver"] = "central",
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/central"),
        }));

    // ----- HU15 -----

    [Fact]
    public async Task HU15_CA01_Admitir_PasaAPendienteConResponsableFechaHoraYMotivo()
    {
        var id = await EnRevisionAsync();
        var (operador, cuenta) = await OperadorAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await DecidirAsync(operador, id, "Admision", "  Hay riesgo para los vecinos  ");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(id)).Estado);
        var decision = Assert.Single(await DecisionesAsync(id));
        Assert.Equal((TipoDecisionReporte.Admision, "Hay riesgo para los vecinos", cuenta.Id), (decision.Tipo, decision.Motivo, decision.OperadorId));
        Assert.InRange(decision.DecididaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Null(decision.Destino);
        var evento = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == id && e.Tipo == TipoEventoCaso.ReporteAdmitido).ToListAsync()));
        Assert.Equal(VisibilidadEvento.Publica, evento.Visibilidad); // DP05: «Admitido» es un estado público
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));
        Assert.Contains("Admitido para atención", html);
        Assert.Contains("data-pendiente-asignacion", html);
    }

    [Fact]
    public async Task HU15_CA02_HU20_CA07_UnReporteAdmitido_SeMuestraEnLaCentralYSeAsignaComoUnSos()
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();
        await DecidirAsync(operador, id, "Admision", "Corresponde atenderlo");
        var (serenoApp, sereno) = await SerenoOperativoAsync();

        // En la central, en su propia sección (no en la de SOS), con el lugar del hecho para el mapa.
        var central = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/central"));
        Assert.Contains($"id=\"sos-{id}\"", central);
        Assert.Contains("data-origen=\"Reporte\"", central);
        Assert.Contains("data-lat=\"-12.0589\"", central);
        var sos = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarSosPendientes>().EjecutarAsync(CancellationToken.None));
        Assert.DoesNotContain(sos, p => p.Id == id);
        Assert.Contains($"id=\"sos-{id}\"", await operador.GetStringAsync("/panel/central?handler=Reportes"));

        var respuesta = await AsignarAsync(operador, id, sereno.Id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/panel/central", respuesta.Headers.Location!.OriginalString);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(id)).Estado);
        Assert.DoesNotContain($"id=\"sos-{id}\"", await operador.GetStringAsync("/panel/central?handler=Reportes"));
        // El sereno ve el lugar del hecho, no una ubicación del dispositivo (HU21-CA02, CA03) y sin datos del ciudadano (DP06).
        var json = await serenoApp.GetStringAsync("/api/sereno/despacho");
        var despacho = System.Text.Json.JsonSerializer.Deserialize<DespachoSereno>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal("REPORTE", despacho.Origen);
        Assert.Null(despacho.Ubicacion);
        Assert.Equal(new LugarHechoDespacho(-12.0589, -77.0461, null, "Frente a la plaza"), despacho.LugarHecho);
        Assert.DoesNotContain("Hecho ficticio", json);
        Assert.DoesNotContain("Prueba Ciudadano", json);
        // En la lista de serenos de la central, «Ver el caso» lleva al detalle del reporte.
        Assert.Contains($"href=\"/panel/reportes/{id}\"", await operador.GetStringAsync("/panel/central?handler=Serenos"));
    }

    [Fact]
    public async Task HU15_CA02_LaAdmision_AvisaALasCentralesAbiertasSinUnAvisoDeSos()
    {
        var id = await EnRevisionAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();
        var (operador, _) = await OperadorAsync();

        await DecidirAsync(operador, id, "Admision", "Corresponde atenderlo");

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(id)));
        Assert.DoesNotContain(id, central.Avisos); // no es un SOS: sin aviso sonoro de atención urgente
    }

    [Fact]
    public async Task HU20_CA07_UnReporteAdmitidoSinPunto_TambienSeAsigna_YElSerenoVeLaReferencia()
    {
        var id = await EnRevisionAsync(conPunto: false);
        var (operador, _) = await OperadorAsync();
        await DecidirAsync(operador, id, "Admision", "Corresponde atenderlo");
        var (serenoApp, sereno) = await SerenoOperativoAsync();

        Assert.Contains("Sin punto en el mapa", System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/central?handler=Reportes")));
        await AsignarAsync(operador, id, sereno.Id);

        var despacho = await serenoApp.GetFromJsonAsync<DespachoSereno>("/api/sereno/despacho");
        Assert.Equal(new LugarHechoDespacho(null, null, null, "Pasaje Los Olivos"), despacho!.LugarHecho);
    }

    [Fact]
    public async Task HU15_CA03_UnReporteQueNoEstaEnRevision_NoSeAdmite()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var recibido = (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), Robo, "Hecho",
            DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Esquina")))).Content
            .ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await DecidirDirectoAsync(recibido, new DatosDecision(TipoDecisionReporte.Admision, "Motivo"), operador.Id);

        Assert.Equal(DecidirReporte.MotivoNoEnRevision, Assert.IsType<ResultadoDecision.Rechazado>(resultado).Motivo);
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(recibido)).Estado);
        Assert.Empty(await DecisionesAsync(recibido));
    }

    [Theory]
    [InlineData("Admision")]
    [InlineData("Derivacion")]
    [InlineData("Descarte")]
    public async Task DP21_SinMotivo_NoSeDecideYElEstadoNoCambia(string tipo)
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();

        var respuesta = await DecidirAsync(operador, id, tipo, "   ", "Comisaría", "Llamada");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
        Assert.Empty(await DecisionesAsync(id));
    }

    [Fact]
    public async Task HU15_CA04_DosDecisionesSimultaneas_PrevaleceUnaYElEstadoEsConsistente()
    {
        var id = await EnRevisionAsync();
        var a = await fabrica.CrearAsync(Roles.Operador);
        var b = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await ConOtroOperadorAsync(
            otro => otro.EjecutarAsync(id, new DatosDecision(TipoDecisionReporte.Descarte, "No corresponde"), b.Id, CancellationToken.None),
            propio => propio.EjecutarAsync(id, new DatosDecision(TipoDecisionReporte.Admision, "Corresponde"), a.Id, CancellationToken.None));

        Assert.Contains("ya fue descartado por", Assert.IsType<ResultadoDecision.Rechazado>(resultado).Motivo);
        var decision = Assert.Single(await DecisionesAsync(id));
        Assert.Equal((TipoDecisionReporte.Descarte, b.Id), (decision.Tipo, decision.OperadorId));
        Assert.Equal(EstadoCaso.Descartada, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task HU15_HU16_HU17_DiezDecisionesALaVez_SoloUnaQueda()
    {
        var id = await EnRevisionAsync();
        var operadores = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fabrica.CrearAsync(Roles.Operador)));
        var tipos = new[] { TipoDecisionReporte.Admision, TipoDecisionReporte.Derivacion, TipoDecisionReporte.Descarte };

        var resultados = await Task.WhenAll(operadores.Select((o, i) => DecidirDirectoAsync(id,
            new DatosDecision(tipos[i % 3], "Motivo", "Comisaría", "Llamada"), o.Id)));

        Assert.Single(resultados, r => r is ResultadoDecision.Registrado);
        var decision = Assert.Single(await DecisionesAsync(id));
        var esperado = decision.Tipo switch
        {
            TipoDecisionReporte.Admision => EstadoCaso.Pendiente,
            TipoDecisionReporte.Derivacion => EstadoCaso.Derivada,
            _ => EstadoCaso.Descartada,
        };
        Assert.Equal(esperado, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task ElMismoOperadorQueReenviaLaDecision_NoRegistraOtra()
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();

        await DecidirAsync(operador, id, "Descarte", "Información insuficiente");
        var reenvio = await DecidirAsync(operador, id, "Descarte", "Información insuficiente");

        Assert.Equal(HttpStatusCode.Redirect, reenvio.StatusCode);
        Assert.Single(await DecisionesAsync(id));
    }

    // ----- HU16 -----

    [Fact]
    public async Task HU16_CA01_Derivar_PasaADerivadaConDestinoAccionYMotivo()
    {
        var id = await EnRevisionAsync();
        var (operador, cuenta) = await OperadorAsync();

        var respuesta = await DecidirAsync(operador, id, "Derivacion", "Corresponde a la policía", "Comisaría de Breña (ficticia)",
            "Se comunicó por teléfono al oficial de guardia");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Derivada, (await CasoAsync(id)).Estado);
        var decision = Assert.Single(await DecisionesAsync(id));
        Assert.Equal(("Comisaría de Breña (ficticia)", "Se comunicó por teléfono al oficial de guardia", "Corresponde a la policía", cuenta.Id),
            (decision.Destino, decision.Accion, decision.Motivo, decision.OperadorId));
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));
        Assert.Contains("el sistema no envía información a la entidad de destino", html);
    }

    [Theory]
    [InlineData(null, "Llamada", "destino")]
    [InlineData("Comisaría", null, "accion")]
    [InlineData("  ", "  ", "destino")]
    public async Task HU16_CA02_SinDestinoOSinAccion_NoSeDeriva(string? destino, string? accion, string campo)
    {
        var id = await EnRevisionAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await DecidirDirectoAsync(id, new DatosDecision(TipoDecisionReporte.Derivacion, "Motivo", destino, accion), operador.Id);

        Assert.Contains(campo, Assert.IsType<ResultadoDecision.Invalido>(resultado).Errores.Keys);
        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
        Assert.Empty(await DecisionesAsync(id));
    }

    [Fact]
    public async Task HU16_CA02_LaBaseDeDatos_ExigeDestinoYAccionEnUnaDerivacion()
    {
        var id = await EnRevisionAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);

        await Assert.ThrowsAsync<DbUpdateException>(() => fabrica.ConBaseDatosAsync(async db =>
        {
            var caso = await db.Casos.SingleAsync(c => c.Id == id);
            db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Derivacion, "Motivo", null, null, operador.Id, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task HU16_CA04_HU17_CA03_ElHistorial_IdentificaElResultado()
    {
        var derivado = await EnRevisionAsync();
        var descartado = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();
        await DecidirAsync(operador, derivado, "Derivacion", "Corresponde a la policía", "Comisaría (ficticia)", "Llamada al oficial");
        await DecidirAsync(operador, descartado, "Descarte", "Es un duplicado de otro reporte");

        var htmlDerivado = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos/{derivado}/historial"));
        var htmlDescartado = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos/{descartado}/historial"));

        Assert.Contains("data-resultado-final", htmlDerivado);
        Assert.Contains("Derivada el", htmlDerivado);
        Assert.Contains("Derivado a Comisaría (ficticia): Llamada al oficial", htmlDerivado);
        Assert.Contains("Reporte derivado a Comisaría (ficticia)", htmlDerivado);
        Assert.Contains("Descartada el", htmlDescartado);
        Assert.Contains("Motivo: Es un duplicado de otro reporte", htmlDescartado);
    }

    // ----- HU17 -----

    [Fact]
    public async Task HU17_CA01_Descartar_PasaADescartadaConResponsableYMotivo()
    {
        var id = await EnRevisionAsync();
        var (operador, cuenta) = await OperadorAsync();

        await DecidirAsync(operador, id, "Descarte", "Es un duplicado de otro reporte");

        Assert.Equal(EstadoCaso.Descartada, (await CasoAsync(id)).Estado);
        var decision = Assert.Single(await DecisionesAsync(id));
        Assert.Equal((TipoDecisionReporte.Descarte, "Es un duplicado de otro reporte", cuenta.Id), (decision.Tipo, decision.Motivo, decision.OperadorId));
    }

    [Fact]
    public async Task HU17_CA04_UnReporteDescartado_NoEsAsignableNiApareceEnLaCentral()
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();
        await DecidirAsync(operador, id, "Descarte", "No corresponde");
        var (_, sereno) = await SerenoOperativoAsync();

        Assert.DoesNotContain(id.ToString(), await operador.GetStringAsync("/panel/central"));
        await AsignarAsync(operador, id, sereno.Id);

        Assert.Equal(EstadoCaso.Descartada, (await CasoAsync(id)).Estado);
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AsNoTracking().Where(a => a.CasoId == id).ToListAsync()));
    }

    [Fact]
    public async Task UnaDecision_SaleDeLaBandejaDeReportes()
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();

        await DecidirAsync(operador, id, "Admision", "Corresponde");

        Assert.DoesNotContain($"data-reporte=\"{id}\"", await operador.GetStringAsync("/panel/reportes"));
    }

    // ----- Autorización y registros -----

    [Fact]
    public async Task HU03_UnAdministrador_NoDecide()
    {
        var id = await EnRevisionAsync();
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/admin/cuentas");

        var respuesta = await navegador.PostAsync($"{Ruta(id)}?handler=Decidir", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Decision.Tipo"] = "Descarte",
            ["Decision.Motivo"] = "Intento directo",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task RNF07_ElMotivoElDestinoYLaAccion_NoAparecenEnLosRegistros()
    {
        var id = await EnRevisionAsync();
        var (operador, _) = await OperadorAsync();

        await DecidirAsync(operador, id, "Derivacion", "Motivo ficticio único", "Destino ficticio único", "Acción ficticia única");

        foreach (var dato in new[] { "Motivo ficticio único", "Destino ficticio único", "Acción ficticia única" })
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato));
    }

    // Simula que otro operador decide entre la lectura y el guardado de este operador.
    private sealed class OtroOperador(string conexion, Func<DecidirReporte, Task> accion) : SaveChangesInterceptor
    {
        private bool hecho;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (!hecho)
            {
                hecho = true;
                await using var otro = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).Options);
                await accion(new DecidirReporte(otro, TimeProvider.System, new NotificadorNulo(), NullLogger<DecidirReporte>.Instance));
            }
            return resultado;
        }
    }

    private async Task<ResultadoDecision> ConOtroOperadorAsync(Func<DecidirReporte, Task> otro, Func<DecidirReporte, Task<ResultadoDecision>> propio)
    {
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion)
            .AddInterceptors(new OtroOperador(conexion, otro)).Options);
        return await propio(new DecidirReporte(db, TimeProvider.System, new NotificadorNulo(), NullLogger<DecidirReporte>.Instance));
    }

    private sealed class NotificadorNulo : INotificador
    {
        public Task SosRegistradoAsync(Guid casoId, CancellationToken ct) => Task.CompletedTask;
        public Task CasoActualizadoAsync(Guid casoId, CancellationToken ct) => Task.CompletedTask;
        public Task SerenoActualizadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
        public Task DespachoAsignadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
        public Task DespachoActualizadoAsync(Guid serenoId, CancellationToken ct) => Task.CompletedTask;
    }
}
