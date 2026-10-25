using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Acompanamiento;

// HU38 — Consultar los acompañamientos activos y sus posiciones (S4-T09, S4-T10), con «Sin conexión» (DP20) y el umbral de
// 60 s (DP12); HU39-A — historial de sesiones (S4-T11). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class SupervisionAcompanamientoTests(FabricaCad fabrica)
{
    private const string Pagina = "/panel/acompanamientos";

    private async Task<(HttpClient App, SesionAcompanamientoVista Sesion, string Apellido)> SesionAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var apellido = $"Acomp{Guid.NewGuid():N}"[..16];
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AspNetUsers\" SET \"Apellidos\" = {apellido} WHERE \"Id\" = {cuenta.Id}"));
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var respuesta = await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true));
        return (app, (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!, apellido);
    }

    private static Task<HttpResponseMessage> PosicionAsync(HttpClient app, Guid sesionId, DateTimeOffset obtenidaEn, double precision = 9) =>
        app.PostAsJsonAsync($"/api/acompanamiento/{sesionId}/posiciones", new PosicionAcompanamientoEnviada(-12.0501, -77.0412, precision, obtenidaEn));

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<string> HtmlAsync(HttpClient navegador, string ruta) => System.Net.WebUtility.HtmlDecode(await navegador.GetStringAsync(ruta));

    // El bloque de una sesión en la lista de activos.
    private static string Bloque(string html, Guid id)
    {
        var inicio = html.IndexOf($"data-acompanamiento=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "La sesión no aparece en la lista de activos.");
        var fin = html.IndexOf("</li>", inicio, StringComparison.Ordinal);
        return html[inicio..fin];
    }

    private Task SinEnviosDesdeAsync(Guid sesionId, int segundos) => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE \"SesionesAcompanamiento\" SET \"UltimoContactoEn\" = now() - make_interval(secs => {segundos}) WHERE \"Id\" = {sesionId}"));

    // ----- HU38 -----

    [Fact]
    public async Task HU38_CA01_CadaPosicionConSuHoraDeObtencionYSuPrecision()
    {
        var (app, sesion, apellido) = await SesionAsync();
        var primera = DateTimeOffset.UtcNow.AddSeconds(-20);
        Assert.Equal(HttpStatusCode.Created, (await PosicionAsync(app, sesion.Id, primera, 14)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddSeconds(-5), 7)).StatusCode);
        var operador = await OperadorAsync();

        var bloque = Bloque(await HtmlAsync(operador, Pagina), sesion.Id);
        Assert.Contains(apellido, bloque);
        Assert.Contains("Posición actual", bloque);
        Assert.Contains("precisión ± 7 m", bloque);
        Assert.Contains("data-antiguedad", bloque);

        var detalle = await HtmlAsync(operador, $"{Pagina}/{sesion.Id}");
        Assert.Equal(2, CuentasDePrueba.Contar(detalle, "<tr data-posicion"));
        Assert.Contains("± 14 m", detalle);
        Assert.Contains("± 7 m", detalle);
        Assert.Contains("· actual", detalle);
    }

    [Fact]
    public async Task HU38_CA02_CA03_PosicionDeMasDeSesentaSegundos_SeAdvierteYNoSePresentaComoActual()
    {
        var (app, sesion, _) = await SesionAsync();
        Assert.Equal(HttpStatusCode.Created, (await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddSeconds(-125))).StatusCode);
        var operador = await OperadorAsync();

        var bloque = Bloque(await HtmlAsync(operador, Pagina), sesion.Id);

        Assert.Contains("Posición desactualizada", bloque);
        Assert.Contains("Última posición conocida", bloque);
        Assert.DoesNotContain("Posición actual", bloque);
        Assert.Contains("hace 2 min", bloque);
        Assert.Contains("data-desactualizada=\"true\"", bloque);
        var detalle = await HtmlAsync(operador, $"{Pagina}/{sesion.Id}");
        Assert.DoesNotContain("· actual", detalle);
        Assert.Contains("desactualizada", detalle);
    }

    [Fact]
    public async Task HU38_CA02_PosicionReciente_SinAdvertencia()
    {
        var (app, sesion, _) = await SesionAsync();
        Assert.Equal(HttpStatusCode.Created, (await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddSeconds(-10))).StatusCode);

        var bloque = Bloque(await HtmlAsync(await OperadorAsync(), Pagina), sesion.Id);

        Assert.DoesNotContain("Posición desactualizada", bloque);
        Assert.Contains("data-desactualizada=\"false\"", bloque);
        Assert.Matches("hace \\d+ s", bloque);
    }

    [Fact]
    public async Task HU38_CA04_LosAcompanamientosSeDistinguenDeLosSosYLosReportes()
    {
        var (app, sesion, apellido) = await SesionAsync();
        await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow);
        var ahora = DateTimeOffset.UtcNow;
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, null))).Content.ReadFromJsonAsync<RespuestaSos>())!;
        var operador = await OperadorAsync();

        var acompanamientos = await HtmlAsync(operador, Pagina);
        var central = await HtmlAsync(operador, "/panel/central?handler=Lista");

        Assert.Contains("vigilado", Bloque(acompanamientos, sesion.Id)); // en la vigilancia de acompañamientos, no en la central
        Assert.DoesNotContain(sos.Id.ToString(), acompanamientos);       // el SOS no está entre los acompañamientos
        Assert.DoesNotContain(sesion.Id.ToString(), central);            // la sesión no está entre los SOS
        Assert.Contains(sos.Id.ToString(), central);
        Assert.Contains("No son SOS ni reportes", acompanamientos);
        Assert.Contains(apellido, acompanamientos);
    }

    public static TheoryData<string?> PerfilesSinPermiso => new() { null, Roles.Ciudadano, Roles.Sereno, Roles.Administrador };

    [Theory]
    [MemberData(nameof(PerfilesSinPermiso))]
    public async Task HU38_CA05_HU39_CA03_UsuarioSinPermiso_NoConsultaPosicionesNiRecorridos(string? perfil)
    {
        var (app, sesion, apellido) = await SesionAsync();
        await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow);
        HttpClient cliente = fabrica.CrearNavegador();
        if (perfil is Roles.Administrador)
            await cliente.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        else if (perfil is not null)
            cliente = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(perfil));

        foreach (var ruta in new[] { Pagina, $"{Pagina}?handler=Lista", $"{Pagina}/{sesion.Id}", $"{Pagina}/historial" })
        {
            var respuesta = await cliente.GetAsync(ruta);
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            Assert.True(respuesta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"{ruta} respondió {(int)respuesta.StatusCode} para {perfil ?? "anónimo"}");
            Assert.DoesNotContain(apellido, cuerpo);
            Assert.DoesNotContain("-12.05010", cuerpo);
        }
    }

    [Fact]
    public async Task HU38_CA05_ElPropioCiudadano_TampocoVeElPanelDeSuRecorrido()
    {
        var (app, sesion, _) = await SesionAsync();

        var respuesta = await app.GetAsync($"{Pagina}/{sesion.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // ----- DP20 -----

    [Fact]
    public async Task DP20_SinEnviosDelDispositivoMasDeSesentaSegundos_LaCentralLaMuestraSinConexion()
    {
        var (app, sesion, _) = await SesionAsync();
        var obtenida = DateTimeOffset.UtcNow.AddSeconds(-5);
        await PosicionAsync(app, sesion.Id, obtenida);
        await SinEnviosDesdeAsync(sesion.Id, 90);
        var operador = await OperadorAsync();

        var bloque = Bloque(await HtmlAsync(operador, Pagina), sesion.Id);
        Assert.Contains("Sin conexión", bloque);
        Assert.Contains("data-sin-conexion=\"true\"", bloque);
        Assert.Contains("Última posición conocida", bloque);
        Assert.DoesNotContain("Posición actual", bloque);

        // Un nuevo envío, aunque repita la misma posición (200), cuenta como contacto: vuelve a «Activa».
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(app, sesion.Id, obtenida)).StatusCode);
        var despues = Bloque(await HtmlAsync(operador, Pagina), sesion.Id);
        Assert.DoesNotContain("Sin conexión", despues);
        Assert.Contains("data-sin-conexion=\"false\"", despues);
    }

    [Fact]
    public async Task DP20_SesionRecienIniciadaSinPosiciones_NoSeMuestraSinConexion()
    {
        var (_, sesion, _) = await SesionAsync();

        var bloque = Bloque(await HtmlAsync(await OperadorAsync(), Pagina), sesion.Id);

        Assert.Contains("data-sin-conexion=\"false\"", bloque);
        Assert.Contains("Aún no se recibió ninguna posición", bloque);
    }

    [Fact]
    public async Task UnaSesionTerminada_NoApareceEntreLasActivas()
    {
        var (app, sesion, _) = await SesionAsync();
        await app.PostAsync($"/api/acompanamiento/{sesion.Id}/finalizacion", null);

        Assert.DoesNotContain(sesion.Id.ToString(), await HtmlAsync(await OperadorAsync(), Pagina));
    }

    // ----- HU39-A -----

    [Fact]
    public async Task HU39_CA01_CA02_HistorialSeparado_ConInicioVencimientoYCierre()
    {
        var (app1, finalizada, apellido1) = await SesionAsync();
        await PosicionAsync(app1, finalizada.Id, DateTimeOffset.UtcNow);
        await app1.PostAsync($"/api/acompanamiento/{finalizada.Id}/finalizacion", null);
        var (app2, vencida, apellido2) = await SesionAsync();
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SesionesAcompanamiento\" SET \"VenceEn\" = now() - interval '1 second', \"IniciadaEn\" = now() - interval '1801 seconds' WHERE \"Id\" = {vencida.Id}"));
        var (_, activa, apellido3) = await SesionAsync();
        var operador = await OperadorAsync();

        var historial = await HtmlAsync(operador, $"{Pagina}/historial");

        Assert.Contains($"data-sesion=\"{finalizada.Id}\"", historial);
        Assert.Contains($"data-sesion=\"{vencida.Id}\"", historial);
        Assert.DoesNotContain($"data-sesion=\"{activa.Id}\"", historial); // solo las terminadas
        var fila1 = historial[historial.IndexOf($"data-sesion=\"{finalizada.Id}\"", StringComparison.Ordinal)..];
        Assert.Contains("Finalizada por el ciudadano", fila1[..fila1.IndexOf("</tr>", StringComparison.Ordinal)]);
        var fila2 = historial[historial.IndexOf($"data-sesion=\"{vencida.Id}\"", StringComparison.Ordinal)..];
        Assert.Contains("Vencida a los 30 minutos", fila2[..fila2.IndexOf("</tr>", StringComparison.Ordinal)]);
        Assert.Contains(apellido1, historial);
        Assert.Contains(apellido2, historial);
        Assert.DoesNotContain(apellido3, historial);

        var detalle = await HtmlAsync(operador, $"{Pagina}/{finalizada.Id}");
        Assert.Contains("data-inicio", detalle);
        Assert.Contains("data-vencimiento", detalle);
        Assert.Contains("Finalizada por el ciudadano", detalle);
        Assert.DoesNotContain("Sin cerrar", detalle);
        Assert.DoesNotContain("· actual", detalle); // una sesión terminada no tiene posición actual
        Assert.Contains("<tr data-posicion data-desactualizada=\"true\"", detalle); // sus posiciones son todas históricas
        Assert.DoesNotContain("<tr data-posicion data-desactualizada=\"false\"", detalle);
    }

    [Fact]
    public async Task HU39_CA01_ElHistorialDeCasosNoIncluyeSesiones()
    {
        var (app, sesion, _) = await SesionAsync();
        await PosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow);
        var ahora = DateTimeOffset.UtcNow;
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, null))).Content.ReadFromJsonAsync<RespuestaSos>())!;

        var historialCaso = await HtmlAsync(await OperadorAsync(), $"/panel/casos/{sos.Id}/historial");

        Assert.DoesNotContain("Sesión de acompañamiento", historialCaso);
        Assert.DoesNotContain("data-acompanamiento", historialCaso);
        Assert.DoesNotContain(sesion.Id.ToString(), historialCaso);
    }

    [Fact]
    public async Task DetalleDeUnaSesionInexistente_Responde404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await (await OperadorAsync()).GetAsync($"{Pagina}/{Guid.NewGuid()}")).StatusCode);
    }
}
