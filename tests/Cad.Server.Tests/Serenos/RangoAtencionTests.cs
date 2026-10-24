using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Serenos;

// Rango de atención (decisión del 24/10/2026): el sereno inicia la atención dentro de 150 m del punto del caso (más la
// imprecisión de ambos); fuera, con un motivo, que queda registrado y lo ve la central. Puntos ficticios en Arequipa.
[Collection(ColeccionServidor.Nombre)]
public class RangoAtencionTests(FabricaCad fabrica)
{
    private const double LatCaso = -16.3988, LonCaso = -71.5369;

    private sealed record SerenoDePrueba(CuentaDePrueba Cuenta, HttpClient App);

    private async Task<SerenoDePrueba> SerenoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return new SerenoDePrueba(cuenta, app);
    }

    private static PosicionSerenoEnviada Posicion(double lat, double lon, TimeSpan? hace = null) =>
        new(lat, lon, 8, DateTimeOffset.UtcNow - (hace ?? TimeSpan.Zero));

    private static async Task EnviarPosicionAsync(SerenoDePrueba sereno, double lat, double lon) =>
        Assert.Equal(HttpStatusCode.Created, (await sereno.App.PostAsJsonAsync("/api/sereno/posiciones", Posicion(lat, lon))).StatusCode);

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    // Un SOS (con o sin ubicación) asignado al sereno y «En camino».
    private async Task<(Guid CasoId, HttpClient Operador)> EnCaminoAsync(SerenoDePrueba sereno, bool conUbicacion = true)
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            conUbicacion ? new UbicacionSos(LatCaso, LonCaso, 10, ahora) : null))).Content.ReadFromJsonAsync<RespuestaSos>();
        var operador = await OperadorAsync();
        var ruta = $"/panel/central/sos/{sos!.Id}";
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = sereno.Cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.App.PostAsync($"/api/sereno/casos/{sos.Id}/desplazamiento", null)).StatusCode);
        return (sos.Id, operador);
    }

    private static Task<HttpResponseMessage> IniciarAsync(SerenoDePrueba sereno, Guid casoId, SolicitudInicioAtencion? solicitud = null) =>
        solicitud is null
            ? sereno.App.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)
            : sereno.App.PostAsJsonAsync($"/api/sereno/casos/{casoId}/atencion", solicitud);

    private Task<InicioAtencion> InicioAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.IniciosAtencion.AsNoTracking().SingleAsync(i => i.CasoId == casoId));

    private Task<EstadoCaso> EstadoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.Id == casoId).Select(c => c.Estado).SingleAsync());

    [Fact]
    public async Task DentroDelRango_SeIniciaSinMotivo_YQuedaRegistradaLaDistancia()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);
        await EnviarPosicionAsync(sereno, LatCaso - 0.0005, LonCaso); // ~55 m al sur

        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId)).StatusCode); // sin cuerpo: usa la última posición

        var inicio = await InicioAsync(casoId);
        Assert.True(inicio.DentroDelRango);
        Assert.InRange(inicio.DistanciaMetros!.Value, 45, 65);
        Assert.Equal(150, inicio.RadioMetros);
        Assert.Null(inicio.Motivo);
        Assert.Equal(EstadoCaso.EnAtencion, await EstadoAsync(casoId));
    }

    [Fact]
    public async Task FueraDelRango_SinMotivo_NoSeInicia_YSeDevuelvenLaDistanciaYElRadio()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);

        var respuesta = await IniciarAsync(sereno, casoId, new SolicitudInicioAtencion(Posicion(LatCaso - 0.01, LonCaso))); // ~1,1 km

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        using var problema = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var rango = problema.RootElement.GetProperty("errors").GetProperty(RangoAtencion.ErrorFueraDeRango);
        Assert.InRange(double.Parse(rango[0].GetString()!, System.Globalization.CultureInfo.InvariantCulture), 1050, 1200);
        Assert.Equal("150", rango[1].GetString());
        Assert.Contains("Indica por qué", problema.RootElement.GetProperty("title").GetString());
        Assert.Equal(EstadoCaso.EnCamino, await EstadoAsync(casoId));
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.IniciosAtencion.AnyAsync(i => i.CasoId == casoId)));
    }

    [Fact]
    public async Task FueraDelRango_ConMotivo_SeInicia_YLaCentralLoVeEnElDetalleYElHistorial()
    {
        var sereno = await SerenoAsync();
        var (casoId, operador) = await EnCaminoAsync(sereno);

        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId,
            new SolicitudInicioAtencion(Posicion(LatCaso - 0.01, LonCaso), MotivosInicioFueraDeRango.CiudadanoSeMovio))).StatusCode);

        var inicio = await InicioAsync(casoId);
        Assert.False(inicio.DentroDelRango);
        Assert.Equal(MotivosInicioFueraDeRango.CiudadanoSeMovio, inicio.Motivo);
        var detalle = await operador.GetStringAsync($"/panel/central/sos/{casoId}");
        Assert.Contains("Fuera del rango: a 1,1 km del punto del caso", detalle);
        Assert.Contains("Motivo: El ciudadano se movió", detalle);
        Assert.Contains("Fuera del rango", await operador.GetStringAsync($"/panel/casos/{casoId}/historial"));
    }

    [Fact]
    public async Task MotivoOtroSinDetalle_OUnMotivoDesconocido_SeRechazan()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);
        var lejos = Posicion(LatCaso - 0.01, LonCaso);

        Assert.Equal(HttpStatusCode.BadRequest, (await IniciarAsync(sereno, casoId, new SolicitudInicioAtencion(lejos, MotivosInicioFueraDeRango.Otro))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await IniciarAsync(sereno, casoId, new SolicitudInicioAtencion(lejos, "Inventado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId,
            new SolicitudInicioAtencion(lejos, MotivosInicioFueraDeRango.Otro, "Me llamó desde la comisaría"))).StatusCode);
        Assert.Equal("Me llamó desde la comisaría", (await InicioAsync(casoId)).Detalle);
    }

    [Fact]
    public async Task SinPosicionReciente_CuentaComoFueraDelRango()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);

        // Una posición enviada hace 5 minutos no sirve (máximo 2) y el sereno no envió ninguna otra.
        var vieja = await IniciarAsync(sereno, casoId, new SolicitudInicioAtencion(Posicion(LatCaso, LonCaso, TimeSpan.FromMinutes(5))));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, vieja.StatusCode);
        Assert.Contains("No hay una posición reciente", await vieja.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId,
            new SolicitudInicioAtencion(null, MotivosInicioFueraDeRango.SinPosicion))).StatusCode);
        var inicio = await InicioAsync(casoId);
        Assert.Equal((false, (double?)null), (inicio.DentroDelRango!.Value, inicio.DistanciaMetros));
    }

    [Fact]
    public async Task UnaPosicionGuardadaConLaHoraAdelantada_NoCuentaComoReciente()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);
        // En el punto del caso, pero con la hora del teléfono 30 minutos adelantada.
        Assert.Equal(HttpStatusCode.Created, (await sereno.App.PostAsJsonAsync("/api/sereno/posiciones",
            Posicion(LatCaso, LonCaso, -TimeSpan.FromMinutes(30)))).StatusCode);

        var respuesta = await IniciarAsync(sereno, casoId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Contains("No hay una posición reciente", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LaPosicionEnviadaAlIniciar_MandaSobreUnaAnteriorLejana()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno);
        await EnviarPosicionAsync(sereno, LatCaso - 0.01, LonCaso); // la última recibida, lejos

        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId, new SolicitudInicioAtencion(Posicion(LatCaso, LonCaso + 0.0003)))).StatusCode);
        Assert.True((await InicioAsync(casoId)).DentroDelRango);
    }

    [Fact]
    public async Task UnCasoSinPunto_NoTieneRangoQueComprobar()
    {
        var sereno = await SerenoAsync();
        var (casoId, _) = await EnCaminoAsync(sereno, conUbicacion: false);

        Assert.Equal(HttpStatusCode.OK, (await IniciarAsync(sereno, casoId)).StatusCode);
        Assert.Null((await InicioAsync(casoId)).DentroDelRango);
    }

    [Fact]
    public async Task ElOperadorQueRegistraElInicio_NoPasaPorElRango_PeroIndicaElMotivoPorEscrito()
    {
        var sereno = await SerenoAsync();
        var (casoId, operador) = await EnCaminoAsync(sereno);
        var ruta = $"/panel/central/sos/{casoId}";
        async Task<HttpResponseMessage> RegistrarAsync(string motivo) =>
            await operador.PostAsync($"{ruta}?handler=IniciarAtencion", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["MotivoInicio"] = motivo,
                ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
            }));

        // Sin motivo no se registra y se explica por qué.
        var sinMotivo = await RegistrarAsync("   ");
        Assert.Equal(HttpStatusCode.OK, sinMotivo.StatusCode);
        Assert.Contains("Indica por qué registras tú el inicio de atención", await sinMotivo.Content.ReadAsStringAsync());
        Assert.Equal(EstadoCaso.EnCamino, await EstadoAsync(casoId));

        Assert.Equal(HttpStatusCode.Redirect, (await RegistrarAsync("El sereno avisó por radio: se quedó sin batería.")).StatusCode);

        Assert.Equal(EstadoCaso.EnAtencion, await EstadoAsync(casoId));
        var inicio = await InicioAsync(casoId);
        Assert.Null(inicio.DentroDelRango); // sin rango: el operador no tiene posición
        Assert.Equal(("El sereno avisó por radio: se quedó sin batería.", sereno.Cuenta.Id), (inicio.Detalle, inicio.SerenoId));
        Assert.NotNull(inicio.RegistradoPorOperadorId);
        Assert.Contains("Registrado por la central: El sereno avisó por radio", await operador.GetStringAsync(ruta));
        Assert.Contains("Registrado por la central: El sereno avisó por radio", await operador.GetStringAsync($"/panel/casos/{casoId}/historial"));
    }
}
