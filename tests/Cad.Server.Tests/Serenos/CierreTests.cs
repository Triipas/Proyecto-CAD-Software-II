using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Serenos;

// HU24 — Cerrar un caso con su tipificación y resultado (S2-T12, S2-T13). Tipificación del catálogo de HU13 (DP07);
// resultado público y notas internas separados (DP05). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CierreTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private const string Resultado = "Se disuadió a los involucrados y se retiraron sin heridos.";
    private const string Notas = "Nota interna ficticia: reincidencia en la zona.";

    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App);

    private async Task<SerenoDePrueba> SerenoAsync(bool operativo = true)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app);
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    // Un SOS asignado al sereno y llevado hasta el estado indicado. Devuelve también la app del ciudadano y su solicitud.
    private async Task<(Guid CasoId, HttpClient Ciudadano, SolicitudEmisionSos Solicitud)> CasoAsync(SerenoDePrueba sereno, HttpClient operador,
        EstadoCaso hasta = EstadoCaso.EnAtencion)
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var solicitud = new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", solicitud)).Content.ReadFromJsonAsync<RespuestaSos>();
        var ruta = $"/panel/central/sos/{sos!.Id}";
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = token,
        }))).StatusCode);
        if (hasta >= EstadoCaso.EnCamino)
            Assert.Equal(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{sos.Id}/desplazamiento", null)).StatusCode);
        if (hasta >= EstadoCaso.EnAtencion)
            Assert.Equal(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{sos.Id}/atencion", null)).StatusCode);
        return (sos.Id, ciudadano, solicitud);
    }

    private static Task<HttpResponseMessage> CerrarAsync(SerenoDePrueba sereno, Guid casoId, Guid? categoria = null, string? resultado = Resultado, string? notas = Notas) =>
        sereno.App.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre", new SolicitudCierreCaso(categoria ?? Robo, resultado, notas));

    private static async Task<HttpResponseMessage> CerrarDesdeElPanelAsync(HttpClient operador, Guid casoId, string? categoria = null,
        string resultado = Resultado, string notas = Notas, string? token = null)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        token ??= await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        return await operador.PostAsync($"{ruta}?handler=Cerrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Cierre.CategoriaId"] = categoria ?? Robo.ToString(), ["Cierre.ResultadoPublico"] = resultado,
            ["Cierre.NotasInternas"] = notas, ["__RequestVerificationToken"] = token,
        }));
    }

    private Task<Caso> LeerCasoAsync(Guid casoId) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    private Task<List<CierreCaso>> CierresAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.CierresCaso.AsNoTracking().Where(c => c.CasoId == casoId).ToListAsync());

    private Task<bool> AsignacionVigenteAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId && a.Vigente));

    private static string ElementoSereno(string lista, Guid serenoId)
    {
        var inicio = lista.IndexOf($"data-sereno=\"{serenoId}\"", StringComparison.Ordinal);
        return lista[inicio..lista.IndexOf("</li>", inicio, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task CA01_CA07_ElSerenoCierra_QuedaCerradaConTodosLosDatos_YVuelveAEstarDisponible()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);
        await using var central = await fabrica.OperadorEnCentralAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await CerrarAsync(sereno, casoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("Cerrada", (await respuesta.Content.ReadFromJsonAsync<RespuestaCierreCaso>())!.Estado);
        Assert.Equal(EstadoCaso.Cerrada, (await LeerCasoAsync(casoId)).Estado);
        var cierre = Assert.Single(await CierresAsync(casoId));
        Assert.Equal(sereno.Cuenta.Id, cierre.ResponsableId);
        Assert.Equal(Robo, cierre.CategoriaId);
        Assert.Equal(Resultado, cierre.ResultadoPublico);
        Assert.Equal(Notas, cierre.NotasInternas);
        Assert.InRange(cierre.CerradoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoCerrado).ToListAsync()));

        // CA07: la asignación deja de estar vigente y el sereno, que sigue operativo, vuelve a estar disponible.
        Assert.False(await AsignacionVigenteAsync(casoId));
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Contains(sereno.Cuenta.Id)));
        Assert.Contains("data-disponible=\"true\"", ElementoSereno(await operador.GetStringAsync("/panel/central?handler=Serenos"), sereno.Cuenta.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await sereno.App.GetAsync("/api/sereno/despacho")).StatusCode);
        // Puede recibir otro caso.
        var (otroCaso, _, _) = await CasoAsync(sereno, operador, EstadoCaso.Asignada);
        Assert.Equal(EstadoCaso.Asignada, (await LeerCasoAsync(otroCaso)).Estado);
    }

    [Fact]
    public async Task CA07_SiYaNoEstaOperativo_SigueSinEstarDisponible()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);
        await sereno.App.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(false, true)); // DP16-b

        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(sereno, casoId)).StatusCode);

        Assert.Contains("data-disponible=\"false\"", ElementoSereno(await operador.GetStringAsync("/panel/central?handler=Serenos"), sereno.Cuenta.Id));
    }

    [Theory]
    [InlineData(false, true, "categoriaId")]
    [InlineData(true, false, "resultadoPublico")]
    public async Task CA02_SinTipificacionOSinResultado_SeRechaza_YSigueEnAtencion(bool conCategoria, bool conResultado, string campo)
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var respuesta = await sereno.App.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre",
            new SolicitudCierreCaso(conCategoria ? Robo : null, conResultado ? Resultado : "   ", null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains(campo, await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.EnAtencion, (await LeerCasoAsync(casoId)).Estado);
        Assert.Empty(await CierresAsync(casoId));
        Assert.True(await AsignacionVigenteAsync(casoId));
    }

    [Fact]
    public async Task CA02_DP07_TipificacionFueraDelCatalogo_SeRechaza()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var respuesta = await CerrarAsync(sereno, casoId, categoria: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.EnAtencion, (await LeerCasoAsync(casoId)).Estado);
    }

    [Theory]
    [InlineData(EstadoCaso.Asignada)]
    [InlineData(EstadoCaso.EnCamino)]
    public async Task CA03_UnCasoQueNoEstaEnAtencion_SeRechaza(EstadoCaso estado)
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador, estado);

        var desdeApp = await CerrarAsync(sereno, casoId);
        var desdePanel = await CerrarDesdeElPanelAsync(operador, casoId);

        Assert.Equal(HttpStatusCode.Conflict, desdeApp.StatusCode);
        Assert.Contains("No se pudo cerrar el caso", await desdePanel.Content.ReadAsStringAsync());
        Assert.Equal(estado, (await LeerCasoAsync(casoId)).Estado);
        Assert.Empty(await CierresAsync(casoId));
    }

    [Fact]
    public async Task CA04_ResultadoPublicoYNotasInternasDiferenciados_YElCiudadanoNoAccedeALasNotas()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, ciudadano, solicitud) = await CasoAsync(sereno, operador);
        await CerrarAsync(sereno, casoId);

        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains($"data-resultado-publico>{Resultado}</dd>", detalle);
        Assert.Contains($"data-notas-internas>{Notas}</dd>", detalle);
        Assert.Contains("Notas internas (solo personal autorizado)", detalle);

        // DP05: en el Sprint 2 ninguna ruta del ciudadano expone el caso. Su única respuesta sobre el SOS (el
        // reenvío de la misma emisión) no incluye ni el resultado ni las notas.
        var reenvio = await ciudadano.PostAsJsonAsync("/api/sos", solicitud);
        var cuerpo = await reenvio.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.DoesNotContain("reincidencia", cuerpo);
        Assert.DoesNotContain("disuadió", cuerpo);
        Assert.NotEqual(HttpStatusCode.OK, (await ciudadano.GetAsync($"/panel/central/sos/{casoId}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await ciudadano.GetAsync($"/api/sereno/casos/{casoId}")).StatusCode);
    }

    [Fact]
    public async Task CA05_UnOperadorCierraDesdeElPanel_QuedaComoResponsable()
    {
        var sereno = await SerenoAsync();
        var (operador, cuentaOperador) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var respuesta = await CerrarDesdeElPanelAsync(operador, casoId);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(cuentaOperador.Id, Assert.Single(await CierresAsync(casoId)).ResponsableId);
        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains("Caso cerrado.", detalle);
        Assert.Contains("<dd>Prueba Operador</dd>", detalle);
        Assert.False(await AsignacionVigenteAsync(casoId));
    }

    [Fact]
    public async Task CA02_DesdeElPanelSinResultado_MuestraElErrorEnElCampo()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var respuesta = await CerrarDesdeElPanelAsync(operador, casoId, categoria: "", resultado: "");
        var html = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Elige la tipificación final del catálogo.", html);
        Assert.Contains("Describe el resultado de la atención.", html);
        Assert.Equal(EstadoCaso.EnAtencion, (await LeerCasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task CA06_OtroSereno_SeRechazaSinCambios()
    {
        var asignado = await SerenoAsync();
        var otro = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(asignado, operador);

        Assert.Equal(HttpStatusCode.NotFound, (await CerrarAsync(otro, casoId)).StatusCode);
        Assert.Equal(EstadoCaso.EnAtencion, (await LeerCasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task CA08_SerenoYOperadorCierranALaVez_UnSoloCierreConsistente()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, $"/panel/central/sos/{casoId}");

        var respuestas = await Task.WhenAll(
            CerrarAsync(sereno, casoId, resultado: "Resultado del sereno."),
            CerrarDesdeElPanelAsync(operador, casoId, resultado: "Resultado del operador.", token: token));

        var cierre = Assert.Single(await CierresAsync(casoId));
        Assert.Equal(EstadoCaso.Cerrada, (await LeerCasoAsync(casoId)).Estado);
        Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoCerrado).ToListAsync()));
        // El cierre conservado es coherente con quien lo registró.
        var ganoSereno = respuestas[0].StatusCode == HttpStatusCode.OK;
        Assert.Equal(ganoSereno ? "Resultado del sereno." : "Resultado del operador.", cierre.ResultadoPublico);
        Assert.Equal(ganoSereno, respuestas[1].StatusCode != HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task CA08_LaMismaSolicitudReenviada_ConservaElPrimerCierre()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CerrarAsync(sereno, casoId)));
        var tarde = await CerrarAsync(sereno, casoId, resultado: "Otro texto");

        Assert.All(respuestas.Append(tarde), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(Resultado, Assert.Single(await CierresAsync(casoId)).ResultadoPublico);
    }

    [Fact]
    public async Task UnAdministradorConTokenValido_NoCierra()
    {
        var sereno = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);
        var (administrador, _) = await PanelAsync(Roles.Administrador);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(administrador, "/panel/admin/cuentas/nueva");

        var respuesta = await CerrarDesdeElPanelAsync(administrador, casoId, token: token);

        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.Empty(await CierresAsync(casoId));
    }

    [Fact]
    public async Task ElSerenoObtieneElCatalogoParaLaTipificacion()
    {
        var sereno = await SerenoAsync();

        var categorias = await sereno.App.GetFromJsonAsync<List<CategoriaCatalogo>>("/api/sereno/categorias");

        Assert.Equal(CategoriaIncidencia.Iniciales.OrderBy(c => c.Orden).Select(c => c.Nombre), categorias!.Select(c => c.Nombre));
    }

    [Fact]
    public void Dominio_SoloUnCasoEnAtencionSeCierra_YLaAsignacionSoloFinalizaUnaVez()
    {
        var caso = Caso.EmitirSos(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        caso.Asignar();
        caso.ConfirmarDesplazamiento();
        Assert.Throws<InvalidOperationException>(caso.Cerrar);
        caso.IniciarAtencion();
        caso.Cerrar();
        Assert.Throws<InvalidOperationException>(caso.Cerrar);

        var asignacion = Asignacion.Registrar(caso, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        asignacion.Finalizar(DateTimeOffset.UtcNow);
        Assert.False(asignacion.Vigente);
        Assert.Throws<InvalidOperationException>(() => asignacion.Finalizar(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Persistencia_UnCierreSinResultado_LoRechazaLaBaseDeDatos()
    {
        var sereno = await SerenoAsync();
        var (operador, cuentaOperador) = await PanelAsync();
        var (casoId, _, _) = await CasoAsync(sereno, operador);

        var error = await Record.ExceptionAsync(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"CierresCaso\" (\"CasoId\", \"CategoriaId\", \"ResultadoPublico\", \"ResponsableId\", \"CerradoEn\") VALUES ({casoId}, {Robo}, {"  "}, {cuentaOperador.Id}, now())")));

        Assert.NotNull(error);
        Assert.Empty(await CierresAsync(casoId));
    }
}
