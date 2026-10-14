using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// HU20 — Asignar un caso pendiente a un sereno disponible (S2-T05, S2-T06, S2-T07). HU20 unificada por el PO:
// CA07 (reporte admitido) se verifica en el Sprint 3, cuando existan reportes. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AsignacionTests(FabricaCad fabrica)
{
    private static string Detalle(Guid casoId) => $"/panel/central/sos/{casoId}";

    private async Task<Guid> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321"));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1))));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> SerenoAsync(bool operativo = true)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        if (operativo)
            Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return (app, cuenta);
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> AsignarAsync(HttpClient navegador, Guid casoId, Guid serenoId,
        Guid? idSolicitud = null, string? volver = null, string? paginaToken = null)
    {
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, paginaToken ?? Detalle(casoId));
        var campos = new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(),
            ["Asignacion.IdSolicitud"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = token,
        };
        if (volver is not null) campos["Asignacion.Volver"] = volver;
        return await navegador.PostAsync($"{Detalle(casoId)}?handler=Asignar", new FormUrlEncodedContent(campos));
    }

    private Task<List<Asignacion>> AsignacionesDelCasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Asignaciones.AsNoTracking().Where(a => a.CasoId == casoId).ToListAsync());

    private Task<List<Asignacion>> AsignacionesDelSerenoAsync(Guid serenoId) =>
        fabrica.ConBaseDatosAsync(db => db.Asignaciones.AsNoTracking().Where(a => a.SerenoId == serenoId).ToListAsync());

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    private Task<int> EventosAsignacionAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoAsignado));

    [Fact]
    public async Task CA01_AsignacionValida_PasaAAsignadaConSerenoOperadorYHora_YActualizaLaCentral()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, cuentaOperador) = await PanelAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await AsignarAsync(operador, casoId, sereno.Id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
        var asignacion = Assert.Single(await AsignacionesDelCasoAsync(casoId));
        Assert.True(asignacion.Vigente);
        Assert.Equal(sereno.Id, asignacion.SerenoId);
        Assert.Equal(cuentaOperador.Id, asignacion.OperadorId);
        Assert.InRange(asignacion.AsignadaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(1, await EventosAsignacionAsync(casoId));

        // La central se actualiza sin recargar: el caso deja «Pendientes» y el sereno deja de estar disponible.
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)), "No llegó el aviso del caso.");
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosSerenos.Contains(sereno.Id)), "No llegó el aviso del sereno.");
        Assert.DoesNotContain($"data-sos=\"{casoId}\"", await operador.GetStringAsync("/panel/central?handler=Lista"));
        var serenos = await operador.GetStringAsync("/panel/central?handler=Serenos");
        var inicio = serenos.IndexOf($"data-sereno=\"{sereno.Id}\"", StringComparison.Ordinal);
        Assert.Contains("data-disponible=\"false\"", serenos[inicio..serenos.IndexOf("</li>", inicio, StringComparison.Ordinal)]);
        Assert.Contains("Asignado a Prueba Sereno", await operador.GetStringAsync(Detalle(casoId)));
    }

    [Fact]
    public async Task CA02_CasoQueYaNoEstaPendiente_SeRechazaSinModificarDatos()
    {
        var casoId = await SosAsync();
        var (_, primero) = await SerenoAsync();
        var (_, segundo) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        await AsignarAsync(operador, casoId, primero.Id);

        var respuesta = await AsignarAsync(operador, casoId, segundo.Id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("El caso ya no está pendiente", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(primero.Id, Assert.Single(await AsignacionesDelCasoAsync(casoId)).SerenoId);
        Assert.Empty(await AsignacionesDelSerenoAsync(segundo.Id));
        Assert.Equal(1, await EventosAsignacionAsync(casoId));
    }

    [Fact]
    public async Task CA03_SerenoNoOperativo_SeRechaza()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync(operativo: false);
        var (operador, _) = await PanelAsync();

        var respuesta = await AsignarAsync(operador, casoId, sereno.Id);

        Assert.Contains("El sereno no está disponible", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await AsignacionesDelCasoAsync(casoId));
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.Equal(0, await EventosAsignacionAsync(casoId));
    }

    [Fact]
    public async Task CA03_SerenoConAsignacionVigente_SeRechaza()
    {
        var primerCaso = await SosAsync();
        var segundoCaso = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        await AsignarAsync(operador, primerCaso, sereno.Id);

        var respuesta = await AsignarAsync(operador, segundoCaso, sereno.Id);

        Assert.Contains("El sereno no está disponible", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await AsignacionesDelCasoAsync(segundoCaso));
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(segundoCaso)).Estado);
    }

    [Fact]
    public async Task CA03_UnaCuentaQueNoEsSereno_NoRecibeCasos()
    {
        var casoId = await SosAsync();
        var otroOperador = await fabrica.CrearAsync(Roles.Operador);
        var (operador, _) = await PanelAsync();

        var respuesta = await AsignarAsync(operador, casoId, otroOperador.Id);

        Assert.Contains("El sereno no está disponible", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await AsignacionesDelCasoAsync(casoId));
    }

    [Fact]
    public async Task CA04_AsignacionesSimultaneasDelMismoCaso_QuedaUnaSolaVigenteSinRegistrosParciales()
    {
        var casoId = await SosAsync();
        var serenos = new List<CuentaDePrueba>();
        var operadores = new List<HttpClient>();
        for (var i = 0; i < 5; i++)
        {
            serenos.Add((await SerenoAsync()).Cuenta);
            operadores.Add((await PanelAsync()).Navegador);
        }
        // Tokens antes de lanzar las peticiones, para que lleguen lo más juntas posible.
        var tokens = await Task.WhenAll(operadores.Select(o => CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(o, Detalle(casoId))));

        var respuestas = await Task.WhenAll(operadores.Select((o, i) => o.PostAsync($"{Detalle(casoId)}?handler=Asignar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Asignacion.SerenoId"] = serenos[i].Id.ToString(),
                ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
                ["__RequestVerificationToken"] = tokens[i],
            }))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Redirect));
        var asignacion = Assert.Single(await AsignacionesDelCasoAsync(casoId));
        Assert.True(asignacion.Vigente);
        Assert.Equal(1, await EventosAsignacionAsync(casoId));
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
        foreach (var sereno in serenos.Where(s => s.Id != asignacion.SerenoId))
            Assert.Empty(await AsignacionesDelSerenoAsync(sereno.Id));
    }

    [Fact]
    public async Task CA05_MismoSerenoEnCasosDistintosALaVez_SoloUnaSeConfirma()
    {
        var (_, sereno) = await SerenoAsync();
        var casos = new List<Guid>();
        var operadores = new List<HttpClient>();
        for (var i = 0; i < 5; i++)
        {
            casos.Add(await SosAsync());
            operadores.Add((await PanelAsync()).Navegador);
        }
        var tokens = await Task.WhenAll(operadores.Select((o, i) => CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(o, Detalle(casos[i]))));

        var respuestas = await Task.WhenAll(operadores.Select((o, i) => o.PostAsync($"{Detalle(casos[i])}?handler=Asignar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Asignacion.SerenoId"] = sereno.Id.ToString(),
                ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
                ["__RequestVerificationToken"] = tokens[i],
            }))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Redirect));
        var asignada = Assert.Single(await AsignacionesDelSerenoAsync(sereno.Id));
        foreach (var caso in casos.Where(c => c != asignada.CasoId))
        {
            Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(caso)).Estado); // sin registros parciales
            Assert.Equal(0, await EventosAsignacionAsync(caso));
        }
    }

    [Fact]
    public async Task CA06_S2T07_SosConIntentosSinRespuesta_SePuedeAsignar()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        foreach (var resultado in new[] { "SinRespuesta", "NoFuePosibleComunicar" })
        {
            var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, Detalle(casoId));
            await operador.PostAsync(Detalle(casoId), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Formulario.IdSolicitud"] = Guid.NewGuid().ToString(), ["Formulario.Resultado"] = resultado,
                ["__RequestVerificationToken"] = token,
            }));
        }

        var respuesta = await AsignarAsync(operador, casoId, sereno.Id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task CA08_LaMismaSolicitudReenviada_NoCreaUnaSegundaAsignacion()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var idSolicitud = Guid.NewGuid();

        var primera = await AsignarAsync(operador, casoId, sereno.Id, idSolicitud);
        var repetidas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AsignarAsync(operador, casoId, sereno.Id, idSolicitud)));

        Assert.Equal(HttpStatusCode.Redirect, primera.StatusCode);
        Assert.All(repetidas, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode)); // misma respuesta: ya asignado
        Assert.Single(await AsignacionesDelCasoAsync(casoId));
        Assert.Equal(1, await EventosAsignacionAsync(casoId));
    }

    [Fact]
    public async Task CA08_ReenvioSimultaneoDeLaPrimeraSolicitud_UnaSolaAsignacion()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        var idSolicitud = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => AsignarAsync(operador, casoId, sereno.Id, idSolicitud)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        Assert.Single(await AsignacionesDelCasoAsync(casoId));
    }

    [Fact]
    public async Task CA09_UnAdministradorConTokenValido_NoPuedeAsignar()
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (administrador, _) = await PanelAsync(Roles.Administrador);

        var respuesta = await AsignarAsync(administrador, casoId, sereno.Id, paginaToken: "/panel/admin/cuentas/nueva");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.Empty(await AsignacionesDelCasoAsync(casoId));
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
    }

    [Theory]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Ciudadano)]
    public async Task CA09_SerenoOCiudadanoConSuToken_NoPuedenAsignar(string rol)
    {
        var casoId = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.PostAsync($"{Detalle(casoId)}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
        }));

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty(await AsignacionesDelCasoAsync(casoId));
    }

    [Fact]
    public async Task HU18CA06_DP16b_SerenoConAsignacionQueSeDesactiva_LaAsignacionSigueVigente()
    {
        var casoId = await SosAsync();
        var (app, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();
        await AsignarAsync(operador, casoId, sereno.Id);

        var desactivacion = await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(false, true));

        Assert.Equal(HttpStatusCode.OK, desactivacion.StatusCode);
        Assert.True(Assert.Single(await AsignacionesDelCasoAsync(casoId)).Vigente);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task DesdeLaCentral_VuelveALaCentralConElResultado()
    {
        var casoId = await SosAsync();
        var otroCaso = await SosAsync();
        var (_, sereno) = await SerenoAsync();
        var (operador, _) = await PanelAsync();

        var asignada = await AsignarAsync(operador, casoId, sereno.Id, volver: "central", paginaToken: "/panel/central");
        Assert.Equal("/panel/central", asignada.Headers.Location?.ToString());
        Assert.Contains("SOS asignado.", await operador.GetStringAsync("/panel/central"));

        var rechazada = await AsignarAsync(operador, otroCaso, sereno.Id, volver: "central", paginaToken: "/panel/central");
        Assert.Equal("/panel/central", rechazada.Headers.Location?.ToString());
        Assert.Contains("El sereno no está disponible", await operador.GetStringAsync("/panel/central"));
    }

    [Fact]
    public async Task ElDetalle_OfreceSoloSerenosDisponibles()
    {
        var casoId = await SosAsync();
        var (_, disponible) = await SerenoAsync();
        var (_, inactivo) = await SerenoAsync(operativo: false);
        var (operador, _) = await PanelAsync();

        var html = await operador.GetStringAsync(Detalle(casoId));

        Assert.Contains($"value=\"{disponible.Id}\"", html);
        Assert.DoesNotContain($"value=\"{inactivo.Id}\"", html);
    }
}
