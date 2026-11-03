using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01). Datos ficticios.
// HU12-CA06 «puede asignarse (HU20)» se completa con la prueba conjunta S2-T07.
[Collection(ColeccionServidor.Nombre)]
public class VerificacionSosTests(FabricaCad fabrica)
{
    private const string Telefono = "987654321";

    private static string Pagina(Guid casoId) => $"/panel/central/sos/{casoId}";

    private async Task<Guid> SosAsync(bool conUbicacion = true, string? telefono = Telefono)
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano, telefono: telefono);
        var app = await fabrica.CrearClienteAppAsync(ciudadano);
        var ahora = DateTimeOffset.UtcNow;
        var ubicacion = conUbicacion ? new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1)) : null;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, ubicacion));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> RegistrarAsync(HttpClient navegador, Guid casoId, string? resultado,
        string? informacion = null, string? referencia = null, Guid? idSolicitud = null, string? paginaToken = null)
    {
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, paginaToken ?? Pagina(casoId));
        var campos = new Dictionary<string, string>
        {
            ["Formulario.IdSolicitud"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["__RequestVerificationToken"] = token,
        };
        if (resultado is not null) campos["Formulario.Resultado"] = resultado;
        if (informacion is not null) campos["Formulario.InformacionHecho"] = informacion;
        if (referencia is not null) campos["Formulario.ReferenciaUbicacion"] = referencia;
        return await navegador.PostAsync(Pagina(casoId), new FormUrlEncodedContent(campos));
    }

    private Task<List<IntentoContacto>> IntentosAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.IntentosContacto.AsNoTracking().Where(i => i.CasoId == casoId).ToListAsync());

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    [Fact]
    public async Task CA01_ElOperadorVeElTelefonoRegistradoDelCiudadano()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var html = await operador.GetStringAsync(Pagina(casoId));

        Assert.Contains($"href=\"tel:{Telefono}\"", html);
        Assert.Contains("Teléfono registrado", html);
        // La sección urgente enlaza al detalle de cada SOS.
        Assert.Contains($"href=\"{Pagina(casoId)}\"", await operador.GetStringAsync("/panel/central?handler=Lista"));
    }

    [Fact]
    public async Task CA01_SinTelefonoRegistrado_SeIndicaSinInventarlo()
    {
        var casoId = await SosAsync(telefono: null);
        var (operador, _) = await PanelAsync();

        var html = await operador.GetStringAsync(Pagina(casoId));

        Assert.Contains("No registrado", html);
        Assert.DoesNotContain("href=\"tel:", html);
    }

    [Theory]
    [InlineData("Contactado")]
    [InlineData("SinRespuesta")]
    [InlineData("NoFuePosibleComunicar")]
    public async Task CA02_RegistraOperadorFechaHoraYResultado(string resultado)
    {
        var casoId = await SosAsync();
        var (operador, cuenta) = await PanelAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await RegistrarAsync(operador, casoId, resultado);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode); // PRG tras registrar
        var intento = Assert.Single(await IntentosAsync(casoId));
        Assert.Equal(Enum.Parse<ResultadoContacto>(resultado), intento.Resultado);
        Assert.Equal(cuenta.Id, intento.OperadorId);
        Assert.InRange(intento.RegistradoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Contains("Intento de contacto registrado.", await operador.GetStringAsync(Pagina(casoId)));
    }

    // Consola de incidente (27/10/2026): otro operador con el mismo SOS abierto se entera del intento por el canal de la central.
    [Fact]
    public async Task UnIntentoRegistrado_AvisaALaCentral()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();

        await RegistrarAsync(operador, casoId, "SinRespuesta");

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)), "No llegó el aviso del caso.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Cancelado")]
    [InlineData("contactado")]
    [InlineData("1")]
    public async Task CA03_ResultadoAusenteOFueraDeLosTresDefinidos_NoSeRegistra(string? resultado)
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var respuesta = await RegistrarAsync(operador, casoId, resultado, informacion: "Texto que no debe guardarse");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("No se pudo registrar el intento", html);
        Assert.Contains("Elige el resultado del intento", html);
        Assert.Empty(await IntentosAsync(casoId));
    }

    [Fact]
    public async Task CA04_InformacionDelHechoYReferencias_QuedanAsociadasAlSos()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        await RegistrarAsync(operador, casoId, "Contactado", "Ruidos de pelea en el segundo piso", "Frente al mercado central");

        var intento = Assert.Single(await IntentosAsync(casoId));
        Assert.Equal("Ruidos de pelea en el segundo piso", intento.InformacionHecho);
        Assert.Equal("Frente al mercado central", intento.ReferenciaUbicacion);
        var html = await operador.GetStringAsync(Pagina(casoId));
        Assert.Contains("Ruidos de pelea en el segundo piso", html);
        Assert.Contains("Frente al mercado central", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CA05_LaReferenciaSeDistingueDeLasCoordenadasYNoLasSustituye(bool conUbicacion)
    {
        var casoId = await SosAsync(conUbicacion);
        var antes = await CasoAsync(casoId);
        var (operador, _) = await PanelAsync();

        await RegistrarAsync(operador, casoId, "Contactado", referencia: "Esquina de Av. Lima con Jr. Cusco");

        var despues = await CasoAsync(casoId);
        Assert.Equal(antes.Latitud, despues.Latitud);
        Assert.Equal(antes.Longitud, despues.Longitud);
        Assert.Equal(antes.PrecisionMetros, despues.PrecisionMetros);
        var html = await operador.GetStringAsync(Pagina(casoId));
        Assert.Contains("Referencia indicada por el ciudadano:", html);
        Assert.Contains("Ubicación del dispositivo", html);
        Assert.Contains(conUbicacion ? "-12.04640, -77.04280" : "Ubicación no disponible", html);
        // DP10: la referencia no genera marcador; la lista de la central conserva solo las coordenadas del dispositivo.
        var lista = await operador.GetStringAsync("/panel/central?handler=Lista");
        Assert.DoesNotContain("Esquina de Av. Lima", lista);
    }

    [Theory]
    [InlineData("SinRespuesta")]
    [InlineData("NoFuePosibleComunicar")]
    public async Task CA06_SinRespuesta_ElSosNoSeCancelaYSigueEnLaSeccionUrgente(string resultado)
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        await RegistrarAsync(operador, casoId, resultado);

        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.Contains($"data-sos=\"{casoId}\"", await operador.GetStringAsync("/panel/central?handler=Lista"));
    }

    [Fact]
    public async Task CA07_VariosIntentos_SeConservanTodosSinSobrescribirse_DelMasReciente()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();
        var (otroOperador, _) = await PanelAsync();

        await RegistrarAsync(operador, casoId, "SinRespuesta", informacion: "Primer intento");
        await RegistrarAsync(otroOperador, casoId, "NoFuePosibleComunicar", informacion: "Segundo intento");
        await RegistrarAsync(operador, casoId, "Contactado", informacion: "Tercer intento");

        var intentos = await IntentosAsync(casoId);
        Assert.Equal(3, intentos.Count);
        Assert.Equal(2, intentos.Select(i => i.OperadorId).Distinct().Count());
        var html = await operador.GetStringAsync(Pagina(casoId));
        Assert.Contains("Intentos registrados · 3", html);
        Assert.True(html.IndexOf("Tercer intento", StringComparison.Ordinal) < html.IndexOf("Primer intento", StringComparison.Ordinal),
            "El historial debe mostrar primero el intento más reciente.");
    }

    [Fact]
    public async Task CA08_UnAdministradorConTokenValido_NoPuedeRegistrarIntentos()
    {
        var casoId = await SosAsync();
        var (administrador, _) = await PanelAsync(Roles.Administrador);

        // Token antifalsificación válido obtenido en una página a la que sí tiene acceso: la solicitud
        // llega bien formada y la rechaza la autorización del servidor, no la interfaz.
        var respuesta = await RegistrarAsync(administrador, casoId, "Contactado", paginaToken: "/panel/admin/cuentas/nueva");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.Empty(await IntentosAsync(casoId));
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA08_CiudadanoOSerenoConSuToken_NoPuedenConsultarNiRegistrar(string rol)
    {
        var casoId = await SosAsync();
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var consulta = await app.GetAsync(Pagina(casoId));
        var registro = await app.PostAsync(Pagina(casoId), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Formulario.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["Formulario.Resultado"] = "Contactado",
        }));

        Assert.DoesNotContain(Telefono, await consulta.Content.ReadAsStringAsync());
        Assert.NotEqual(HttpStatusCode.OK, consulta.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, registro.StatusCode);
        Assert.Empty(await IntentosAsync(casoId));
    }

    [Fact]
    public async Task RNF08_ElMismoEnvioRepetido_NoDuplicaElIntento()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();
        var idSolicitud = Guid.NewGuid();

        await RegistrarAsync(operador, casoId, "Contactado", idSolicitud: idSolicitud);
        await RegistrarAsync(operador, casoId, "Contactado", idSolicitud: idSolicitud);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RegistrarAsync(operador, casoId, "Contactado", idSolicitud: idSolicitud)));

        Assert.Single(await IntentosAsync(casoId));
    }

    [Fact]
    public async Task CadaIntento_QuedaEnElHistorialDelCasoComoEventoInterno()
    {
        var casoId = await SosAsync();
        var (operador, cuenta) = await PanelAsync();

        await RegistrarAsync(operador, casoId, "SinRespuesta");

        var evento = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .SingleAsync(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.IntentoContactoRegistrado));
        Assert.Equal(cuenta.Id, evento.ActorId);
        Assert.Equal(VisibilidadEvento.Interna, evento.Visibilidad); // no visible para el ciudadano (RNF12)
    }

    [Fact]
    public async Task SosInexistente_Responde404()
    {
        var (operador, _) = await PanelAsync();

        var respuesta = await operador.GetAsync(Pagina(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
