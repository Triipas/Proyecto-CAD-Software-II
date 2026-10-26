using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Acompanamiento;

// 26/10/2026 — el ciudadano informa cómo va durante su acompañamiento, sin que la central lo pida: «Estoy bien», «Algo no está
// bien» (requiere atención, sin SOS), «Necesito ayuda» (SOS, como HU41) y «Llegué» (finaliza la sesión). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class EstadoCiudadanoTests(FabricaCad fabrica)
{
    private sealed record Ciudadano(HttpClient App, CuentaDePrueba Cuenta, Guid SesionId);

    private async Task<Ciudadano> AcompanamientoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321");
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var respuesta = await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return new(app, cuenta, (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!.Id);
    }

    private static SolicitudEmisionSos Sos()
    {
        var ahora = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 12, ahora.AddSeconds(1)));
    }

    private static Task<HttpResponseMessage> InformarAsync(Ciudadano c, string? estado, Guid? idSolicitud = null, SolicitudEmisionSos? sos = null) =>
        c.App.PostAsJsonAsync($"/api/acompanamiento/{c.SesionId}/estado", new SolicitudEstadoAcompanamiento(idSolicitud ?? Guid.NewGuid(), estado, sos));

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<string> TarjetaAsync(HttpClient operador, Guid sesionId) =>
        Regex.Match(System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/acompanamientos?handler=Lista")),
            $"<li class=\"vigilado[^\"]*\" data-acompanamiento=\"{sesionId}\".*?</li>", RegexOptions.Singleline).Value;

    private Task<int> SosDeAsync(Guid ciudadanoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync(c => c.CiudadanoId == ciudadanoId));

    [Fact]
    public async Task EstoyBien_SeRegistra_LaCentralRecibeElAviso_YLaTarjetaLoMuestraEnCalma()
    {
        var ciudadano = await AcompanamientoAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();
        var antes = central.AvisosAcompanamientos.Count;

        var respuesta = await InformarAsync(ciudadano, EstadosCiudadano.EstoyBien);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<EstadoInformadoVista>())!;
        Assert.Equal((EstadosCiudadano.EstoyBien, EstadosAcompanamiento.Activa, (Guid?)null), (vista.Estado, vista.EstadoSesion, vista.CasoId));
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosAcompanamientos.Count > antes), "aviso a la central");
        var tarjeta = await TarjetaAsync(await OperadorAsync(), ciudadano.SesionId);
        Assert.Contains("data-atencion=\"Calma\"", tarjeta);
        Assert.Contains("Estoy bien", tarjeta);
        Assert.Equal(0, await SosDeAsync(ciudadano.Cuenta.Id));
    }

    [Fact]
    public async Task AlgoNoEstaBien_RequiereAtencionSinSos_HastaQueDigaQueEstaBien()
    {
        var ciudadano = await AcompanamientoAsync();
        var operador = await OperadorAsync();

        Assert.Equal(HttpStatusCode.OK, (await InformarAsync(ciudadano, EstadosCiudadano.AlgoNoEstaBien)).StatusCode);

        var tarjeta = await TarjetaAsync(operador, ciudadano.SesionId);
        Assert.Contains("data-atencion=\"AlgoNoEstaBien\"", tarjeta);
        Assert.Contains("Avisó que algo no está bien", tarjeta);
        Assert.Contains("data-alerta-ciudadano=", tarjeta); // suena una vez en el panel
        Assert.Equal(0, await SosDeAsync(ciudadano.Cuenta.Id));
        var seguimiento = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/acompanamientos?handler=Sesion&id={ciudadano.SesionId}"));
        Assert.Contains("Avisó que algo no está bien", seguimiento);
        Assert.Contains("href=\"tel:987654321\"", seguimiento);
        Assert.Contains("data-algo-no-esta-bien", await operador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}"));

        Assert.Equal(HttpStatusCode.OK, (await InformarAsync(ciudadano, EstadosCiudadano.EstoyBien)).StatusCode);
        Assert.Contains("data-atencion=\"Calma\"", await TarjetaAsync(operador, ciudadano.SesionId));
    }

    [Fact]
    public async Task AlgoNoEstaBien_DejaDeRequerirAtencion_SiRespondeEstoyBienAUnaConfirmacionPosterior()
    {
        var ciudadano = await AcompanamientoAsync();
        Assert.Equal(HttpStatusCode.OK, (await InformarAsync(ciudadano, EstadosCiudadano.AlgoNoEstaBien)).StatusCode);
        var operadorId = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var confirmacion = Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<ConfirmarBienestar>().SolicitarAsync(ciudadano.SesionId, operadorId, Guid.NewGuid(), CancellationToken.None))).Confirmacion;
        var operador = await OperadorAsync();
        Assert.Contains("data-atencion=\"AlgoNoEstaBien\"", await TarjetaAsync(operador, ciudadano.SesionId)); // pedirla no basta

        Assert.Equal(HttpStatusCode.OK, (await ciudadano.App.PostAsJsonAsync($"/api/acompanamiento/confirmaciones/{confirmacion.Id}/respuesta",
            new SolicitudRespuestaBienestar(RespuestasBienestar.EstoyBien, null))).StatusCode);

        Assert.Contains("data-atencion=\"Calma\"", await TarjetaAsync(operador, ciudadano.SesionId));

        // Pedir otra confirmación después no reabre el aviso que ya despejó con su «Estoy bien».
        Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<ConfirmarBienestar>().SolicitarAsync(ciudadano.SesionId, operadorId, Guid.NewGuid(), CancellationToken.None)));
        Assert.Contains("data-atencion=\"Calma\"", await TarjetaAsync(operador, ciudadano.SesionId));
        Assert.DoesNotContain("data-algo-no-esta-bien", await operador.GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}"));
    }

    [Fact]
    public async Task LaMismaSolicitudEnOtraSesion_NoSeTomaComoReenvio()
    {
        var ciudadano = await AcompanamientoAsync();
        var idSolicitud = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await InformarAsync(ciudadano, EstadosCiudadano.NecesitoAyuda, idSolicitud, Sos())).StatusCode);
        await ciudadano.App.PostAsync($"/api/acompanamiento/{ciudadano.SesionId}/finalizacion", null);
        var nueva = (await (await ciudadano.App.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!.Id;

        var respuesta = await ciudadano.App.PostAsJsonAsync($"/api/acompanamiento/{nueva}/estado",
            new SolicitudEstadoAcompanamiento(idSolicitud, EstadosCiudadano.NecesitoAyuda, Sos()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EstadosInformados.CountAsync(e => e.SesionId == nueva)));
    }

    [Fact]
    public async Task NecesitoAyuda_EmiteUnSos_LaTarjetaPideAyuda_YUnReenvioNoCreaOtro()
    {
        var ciudadano = await AcompanamientoAsync();
        var idSolicitud = Guid.NewGuid();
        var sos = Sos();

        var primera = (await (await InformarAsync(ciudadano, EstadosCiudadano.NecesitoAyuda, idSolicitud, sos)).Content.ReadFromJsonAsync<EstadoInformadoVista>())!;
        var segunda = (await (await InformarAsync(ciudadano, EstadosCiudadano.NecesitoAyuda, idSolicitud, sos)).Content.ReadFromJsonAsync<EstadoInformadoVista>())!;

        Assert.NotNull(primera.CasoId);
        Assert.Equal((primera.Id, primera.CasoId), (segunda.Id, segunda.CasoId));
        var caso = await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.CiudadanoId == ciudadano.Cuenta.Id));
        Assert.Equal((caso.Id, OrigenCaso.Sos, EstadoCaso.Pendiente), (primera.CasoId!.Value, caso.Origen, caso.Estado));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.EstadosInformados.CountAsync(e => e.SesionId == ciudadano.SesionId)));
        Assert.Equal(EstadosAcompanamiento.Activa, primera.EstadoSesion); // pedir ayuda no finaliza la sesión

        var operador = await OperadorAsync();
        var tarjeta = await TarjetaAsync(operador, ciudadano.SesionId);
        Assert.Contains("data-atencion=\"PideAyuda\"", tarjeta);
        Assert.Contains($"/panel/central/sos/{caso.Id}", tarjeta);
        Assert.Contains("Pulsó «Necesito ayuda» desde su acompañamiento",
            System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/acompanamientos?handler=Sesion&id={ciudadano.SesionId}")));
    }

    [Fact]
    public async Task NecesitoAyuda_ConUnSosVigente_SeVincula_YSeAceptaAunqueLaSesionHayaTerminado()
    {
        var ciudadano = await AcompanamientoAsync();
        var existente = (await (await ciudadano.App.PostAsJsonAsync("/api/sos", Sos())).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        await ciudadano.App.PostAsync($"/api/acompanamiento/{ciudadano.SesionId}/finalizacion", null);

        var respuesta = await InformarAsync(ciudadano, EstadosCiudadano.NecesitoAyuda, sos: Sos());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<EstadoInformadoVista>())!;
        Assert.Equal((existente, EstadosAcompanamiento.Finalizada), (vista.CasoId!.Value, vista.EstadoSesion));
        Assert.Equal(1, await SosDeAsync(ciudadano.Cuenta.Id));
    }

    [Fact]
    public async Task Llegue_SeRegistra_YFinalizaLaSesion()
    {
        var ciudadano = await AcompanamientoAsync();

        var vista = (await (await InformarAsync(ciudadano, EstadosCiudadano.Llegue)).Content.ReadFromJsonAsync<EstadoInformadoVista>())!;

        Assert.Equal((EstadosCiudadano.Llegue, EstadosAcompanamiento.Finalizada), (vista.Estado, vista.EstadoSesion));
        Assert.Equal(HttpStatusCode.NoContent, (await ciudadano.App.GetAsync("/api/acompanamiento/actual")).StatusCode);
        Assert.Contains("Llegué a mi destino", System.Net.WebUtility.HtmlDecode(
            await (await OperadorAsync()).GetStringAsync($"/panel/acompanamientos/{ciudadano.SesionId}")));
    }

    [Theory]
    [InlineData(EstadosCiudadano.EstoyBien)]
    [InlineData(EstadosCiudadano.AlgoNoEstaBien)]
    [InlineData(EstadosCiudadano.Llegue)]
    public async Task ConLaSesionCerrada_SoloNecesitoAyudaSeAcepta(string estado)
    {
        var ciudadano = await AcompanamientoAsync();
        await ciudadano.App.PostAsync($"/api/acompanamiento/{ciudadano.SesionId}/finalizacion", null);

        Assert.Equal(HttpStatusCode.Conflict, (await InformarAsync(ciudadano, estado)).StatusCode);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EstadosInformados.CountAsync(e => e.SesionId == ciudadano.SesionId)));
    }

    [Fact]
    public async Task EstadoInvalido_SinSos_UOtraSesion_SeRechazan()
    {
        var ciudadano = await AcompanamientoAsync();
        var otro = await AcompanamientoAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await InformarAsync(ciudadano, "Cansado")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InformarAsync(ciudadano, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InformarAsync(ciudadano, EstadosCiudadano.NecesitoAyuda)).StatusCode); // sin el SOS
        Assert.Equal(HttpStatusCode.BadRequest, (await InformarAsync(ciudadano, EstadosCiudadano.EstoyBien, Guid.Empty)).StatusCode);
        // Una sesión ajena responde igual que una inexistente.
        Assert.Equal(HttpStatusCode.NotFound, (await otro.App.PostAsJsonAsync($"/api/acompanamiento/{ciudadano.SesionId}/estado",
            new SolicitudEstadoAcompanamiento(Guid.NewGuid(), EstadosCiudadano.EstoyBien, null))).StatusCode);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EstadosInformados.CountAsync(e => e.SesionId == ciudadano.SesionId)));
    }

    [Fact]
    public async Task LaMismaPulsacionSimultanea_SeRegistraUnaSolaVez()
    {
        var ciudadano = await AcompanamientoAsync();
        var idSolicitud = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => InformarAsync(ciudadano, EstadosCiudadano.AlgoNoEstaBien, idSolicitud)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.EstadosInformados.CountAsync(e => e.SesionId == ciudadano.SesionId)));
    }
}
