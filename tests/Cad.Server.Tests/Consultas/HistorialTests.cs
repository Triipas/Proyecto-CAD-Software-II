using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Consultas;

// HU27 — Consultar el historial de un caso (S3-T11), para los SOS y el registro de los reportes (HU10). Las demás
// actuaciones de los reportes se añaden con HU14–HU17. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public partial class HistorialTests(FabricaCad fabrica)
{
    private static string Ruta(Guid casoId) => $"/panel/casos/{casoId}/historial";

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private async Task<Guid> SosAsync()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        return (await (await ciudadano.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.05, -77.03, 8, ahora.AddSeconds(1))))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private static async Task EnviarAsync(HttpClient operador, Guid casoId, string manejador, Dictionary<string, string> campos)
    {
        var ruta = $"/panel/central/sos/{casoId}";
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta);
        var respuesta = await operador.PostAsync(manejador is "" ? ruta : $"{ruta}?handler={manejador}", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }

    // Recorre el ciclo completo de un SOS (Sprint 2) y devuelve el caso y el sereno.
    private async Task<(Guid CasoId, HttpClient Operador)> CicloCompletoAsync(string notas)
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();
        var serenoCuenta = await fabrica.CrearAsync(Roles.Sereno);
        var sereno = await fabrica.CrearClienteAppAsync(serenoCuenta);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        await EnviarAsync(operador, casoId, "", new()
        {
            ["Formulario.IdSolicitud"] = Guid.NewGuid().ToString(), ["Formulario.Resultado"] = "Contactado",
            ["Formulario.InformacionHecho"] = "Discusión entre vecinos", ["Formulario.ReferenciaUbicacion"] = "Frente al parque",
        });
        await EnviarAsync(operador, casoId, "Clasificar", new() { ["Clasificacion.CategoriaId"] = CategoriaIncidencia.Iniciales[0].Id.ToString() });
        await EnviarAsync(operador, casoId, "Asignar", new()
        {
            ["Asignacion.SerenoId"] = serenoCuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsync($"/api/sereno/casos/{casoId}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsync($"/api/sereno/casos/{casoId}/atencion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsJsonAsync($"/api/sereno/casos/{casoId}/cierre",
            new SolicitudCierreCaso(CategoriaIncidencia.Iniciales[1].Id, "Se resolvió en el lugar", notas))).StatusCode);
        return (casoId, operador);
    }

    // Actuaciones del historial en el orden en que aparecen en la página.
    private static List<string> Actuaciones(string html) =>
        ActuacionRegex().Matches(html).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    [GeneratedRegex("<li class=\"historial__elemento\" data-actuacion>\\s*<p class=\"historial__cabecera\">\\s*<span class=\"estado[^\"]*\">([^<]+)</span>")]
    private static partial Regex ActuacionRegex();

    [Fact]
    public async Task CA01_CA03_LaSecuenciaCompleta_EnOrdenConResponsablesYMarcasDeTiempo()
    {
        var (casoId, operador) = await CicloCompletoAsync("Nota interna ficticia");

        var html = await operador.GetStringAsync(Ruta(casoId));

        Assert.Equal(
        [
            "SOS emitido", "Intento de contacto: Contactado", "Clasificado como «Violencia doméstica»", "Asignado al sereno Prueba Sereno",
            "En camino", "Inicio de atención", "Asignación finalizada", "Caso cerrado · tipificación «Robo»", "Notas internas del cierre",
        ], Actuaciones(html));
        Assert.Contains("· Prueba Ciudadano", html);
        Assert.Contains("· Prueba Operador", html);
        Assert.Contains("· Prueba Sereno", html);
        Assert.Contains("Hecho: Discusión entre vecinos · Referencia indicada por el ciudadano: Frente al parque", html);
        Assert.Equal(9, Regex.Matches(html, "<time datetime=").Count);
    }

    [Fact]
    public async Task CA01_UnReporte_MuestraSuRegistroConLaCategoriaDelCiudadanoYLaUbicacionDelHecho()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ocurrido = new DateTimeOffset(2026, 10, 14, 18, 40, 0, TimeSpan.FromHours(-5));
        var reporte = (await (await ciudadano.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(),
            new Guid("0199e2b0-0000-7000-8000-000000000002"), "Descripción ficticia", ocurrido,
            new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Esquina del mercado")))).Content.ReadFromJsonAsync<RespuestaReporte>())!;
        var (operador, _) = await PanelAsync();

        var html = await operador.GetStringAsync(Ruta(reporte.Id));

        // La categoría del ciudadano figura en el registro, no como una clasificación aparte.
        Assert.Equal(["Reporte registrado como «Robo»"], Actuaciones(html));
        Assert.Contains("Operador · Reporte Recibido", html);
        Assert.Contains("Ubicación del hecho: referencia textual · Referencia: Esquina del mercado", html);
        Assert.Contains("Hecho ocurrido aproximadamente el 14/10/2026 18:40", html);
        Assert.Contains("· Prueba Ciudadano", html);
        Assert.Contains($"href=\"/panel/reportes/{reporte.Id}\">← Volver al caso", html);
    }

    [Fact]
    public async Task CA02_UnCasoCerrado_QuedaIdentificadoConSuResultado()
    {
        var (casoId, operador) = await CicloCompletoAsync("Nota");

        var html = await operador.GetStringAsync(Ruta(casoId));

        Assert.Contains("data-resultado-final", html);
        Assert.Contains("Robo: Se resolvió en el lugar", html);
        Assert.Contains("Operador · SOS Cerrada", html);
    }

    [Fact]
    public async Task CA04_LasNotasInternas_AparecenSoloRotuladasParaElPersonalAutorizado()
    {
        var (casoId, operador) = await CicloCompletoAsync("Antecedente ficticio de la zona");

        var html = await operador.GetStringAsync(Ruta(casoId));

        Assert.Contains("<strong>Solo personal autorizado:</strong>", html);
        Assert.Contains("Antecedente ficticio de la zona", html);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA04_CA06_OtrosRolesConSuToken_NoObtienenElHistorial(string rol)
    {
        var (casoId, _) = await CicloCompletoAsync("Nota interna que no debe salir");
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.GetAsync(Ruta(casoId));

        Assert.Contains(respuesta.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.DoesNotContain("Nota interna que no debe salir", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CA06_UnAdministrador_NoConsultaElHistorial()
    {
        var (casoId, _) = await CicloCompletoAsync("Nota");
        var (administrador, _) = await PanelAsync(Roles.Administrador);

        var respuesta = await administrador.GetAsync(Ruta(casoId));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UnSosRecienEmitido_TieneSoloSuRegistro_YSinResultadoFinal()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var html = await operador.GetStringAsync(Ruta(casoId));

        Assert.Equal(["SOS emitido"], Actuaciones(html));
        Assert.DoesNotContain("data-resultado-final", html);
        Assert.Contains("Con ubicación del dispositivo", html);
    }

    [Fact]
    public async Task UnCasoInexistente_Responde404()
    {
        var (operador, _) = await PanelAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await operador.GetAsync(Ruta(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task ElDetalleDelSos_EnlazaAlHistorial()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        Assert.Contains($"href=\"/panel/casos/{casoId}/historial\"", await operador.GetStringAsync($"/panel/central/sos/{casoId}"));
    }
}
