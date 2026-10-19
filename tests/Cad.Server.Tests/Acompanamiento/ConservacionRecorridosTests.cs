using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Acompanamiento;

// HU39-B — conservación y eliminación de recorridos (S4-T14, S4-T15; RNF13). DP22, decisión del equipo de desarrollo delegada
// por el PO el 16/10/2026: 30 días tras el cierre, con conservación excepcional vinculada a un caso. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class ConservacionRecorridosTests(FabricaCad fabrica)
{
    private static string Ruta(Guid id) => $"/panel/acompanamientos/{id}";

    // Una sesión finalizada con dos posiciones; dias: cuántos días atrás se cerró (se mueven inicio, vencimiento y cierre).
    private async Task<(Guid SesionId, HttpClient App)> SesionTerminadaAsync(int dias)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sesion = (await (await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        foreach (var segundos in new[] { 20, 5 })
            await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
                new PosicionAcompanamientoEnviada(-12.0501, -77.0412, 9, DateTimeOffset.UtcNow.AddSeconds(-segundos)));
        await app.PostAsync($"/api/acompanamiento/{sesion.Id}/finalizacion", null);
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SesionesAcompanamiento\" SET \"IniciadaEn\" = \"IniciadaEn\" - make_interval(days => {dias}), \"VenceEn\" = \"VenceEn\" - make_interval(days => {dias}), \"CerradaEn\" = \"CerradaEn\" - make_interval(days => {dias}) WHERE \"Id\" = {sesion.Id}"));
        return (sesion.Id, app);
    }

    private Task<IReadOnlyList<Guid>> EliminarAsync() =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<ConservarRecorridos>().EliminarVencidasAsync(CancellationToken.None));

    private Task<int> PosicionesAsync(Guid sesionId) => fabrica.ConBaseDatosAsync(db => db.PosicionesAcompanamiento.CountAsync(p => p.SesionId == sesionId));

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> ConservarAsync(HttpClient navegador, Guid sesionId, string? casoId, string? finalidad)
    {
        var campos = new Dictionary<string, string> { ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, Ruta(sesionId)) };
        if (casoId is not null) campos["Conservacion.CasoId"] = casoId;
        if (finalidad is not null) campos["Conservacion.Finalidad"] = finalidad;
        return await navegador.PostAsync($"{Ruta(sesionId)}?handler=Conservar", new FormUrlEncodedContent(campos));
    }

    private async Task<Guid> SosAsync(HttpClient app) =>
        (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null))).Content.ReadFromJsonAsync<RespuestaSos>())!.Id;

    [Fact]
    public async Task HU39_CA04_PosicionesDeUnaSesionCerradaHaceMasDe30Dias_SeEliminanYLaSesionSeConserva()
    {
        var (sesionId, _) = await SesionTerminadaAsync(31);
        Assert.Equal(2, await PosicionesAsync(sesionId));

        var eliminadas = await EliminarAsync();

        Assert.Contains(sesionId, eliminadas);
        Assert.Equal(0, await PosicionesAsync(sesionId));
        var sesion = await fabrica.ConBaseDatosAsync(db => db.SesionesAcompanamiento.AsNoTracking().SingleAsync(s => s.Id == sesionId));
        Assert.NotNull(sesion.PosicionesEliminadasEn);
        Assert.NotNull(sesion.CerradaEn); // los datos de la sesión siguen para HU39-A
        var (operador, _) = await OperadorAsync();
        var detalle = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(sesionId)));
        Assert.Contains("Las posiciones se eliminaron", detalle);
        Assert.Contains("30 días", detalle);
        var historial = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/acompanamientos/historial?pagina=1"));
        var fila = historial[historial.IndexOf($"data-sesion=\"{sesionId}\"", StringComparison.Ordinal)..];
        Assert.Contains("Eliminadas por plazo", fila[..fila.IndexOf("</tr>", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task HU39_CA04_SesionCerradaHace29Dias_ConservaSusPosiciones()
    {
        var (sesionId, _) = await SesionTerminadaAsync(29);

        await EliminarAsync();

        Assert.Equal(2, await PosicionesAsync(sesionId));
    }

    [Fact]
    public async Task HU39_CA04_UnaSesionActiva_NuncaPierdeSusPosiciones()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sesion = (await (await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones", new PosicionAcompanamientoEnviada(-12.05, -77.04, 9, DateTimeOffset.UtcNow));

        await EliminarAsync();

        Assert.Equal(1, await PosicionesAsync(sesion.Id));
    }

    [Fact]
    public async Task HU39_CA05_ConservacionExcepcional_ConCasoYFinalidad_QuedaRegistradaYNoSeElimina()
    {
        var (sesionId, app) = await SesionTerminadaAsync(31);
        var casoId = await SosAsync(app);
        var (operador, cuenta) = await OperadorAsync();

        var respuesta = await ConservarAsync(operador, sesionId, casoId.ToString(), "  Investigación del SOS relacionado (ficticio)  ");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var conservacion = await fabrica.ConBaseDatosAsync(db => db.ConservacionesRecorrido.AsNoTracking().SingleAsync(c => c.SesionId == sesionId));
        Assert.Equal((casoId, "Investigación del SOS relacionado (ficticio)", cuenta.Id), (conservacion.CasoId, conservacion.Finalidad, conservacion.OperadorId));
        Assert.InRange(conservacion.RegistradaEn, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.DoesNotContain(sesionId, await EliminarAsync());
        Assert.Equal(2, await PosicionesAsync(sesionId)); // se trata por separado: no se elimina por plazo
        var detalle = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(sesionId)));
        Assert.Contains("data-conservacion-registrada", detalle);
        Assert.Contains("Investigación del SOS relacionado (ficticio)", detalle);
    }

    [Theory]
    [InlineData(null, "Finalidad ficticia", "casoId")]
    [InlineData("00000000-0000-0000-0000-000000000001", "Finalidad ficticia", "casoId")]
    [InlineData("CASO", "   ", "finalidad")]
    public async Task HU39_CA05_SinFinalidadOSinCasoExistente_NoSeConserva(string? caso, string finalidad, string campo)
    {
        var (sesionId, app) = await SesionTerminadaAsync(2);
        var casoId = caso == "CASO" ? (await SosAsync(app)).ToString() : caso;
        var (operador, _) = await OperadorAsync();

        var respuesta = await ConservarAsync(operador, sesionId, casoId, finalidad);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains($"Conservacion_{(campo == "casoId" ? "CasoId" : "Finalidad")}-error", await respuesta.Content.ReadAsStringAsync());
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.ConservacionesRecorrido.AnyAsync(c => c.SesionId == sesionId)));
    }

    [Fact]
    public async Task HU39_CA05_FinalidadEnBlanco_ElCasoDeUsoLaRechazaSinDependerDelFormulario()
    {
        var (sesionId, app) = await SesionTerminadaAsync(1);
        var casoId = await SosAsync(app);

        var resultado = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConservarRecorridos>()
            .ConservarAsync(sesionId, casoId, "   ", Guid.Empty, CancellationToken.None));

        Assert.Contains("finalidad", Assert.IsType<ResultadoConservacion.Invalida>(resultado).Errores.Keys);
    }

    [Fact]
    public async Task HU39_CA05_UnaSegundaConservacion_NoCambiaLaPrimera()
    {
        var (sesionId, app) = await SesionTerminadaAsync(1);
        var casoId = await SosAsync(app);
        var (operador, _) = await OperadorAsync();
        await ConservarAsync(operador, sesionId, casoId.ToString(), "Primera finalidad");

        var resultado = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConservarRecorridos>()
            .ConservarAsync(sesionId, casoId, "Otra finalidad", Guid.Empty, CancellationToken.None));

        Assert.IsType<ResultadoConservacion.YaRegistrada>(resultado);
        Assert.Equal("Primera finalidad", (await fabrica.ConBaseDatosAsync(db => db.ConservacionesRecorrido.AsNoTracking().SingleAsync(c => c.SesionId == sesionId))).Finalidad);
    }

    [Fact]
    public async Task HU39_CA05_ConPosicionesYaEliminadas_NoHayRecorridoQueConservar()
    {
        var (sesionId, app) = await SesionTerminadaAsync(31);
        var casoId = await SosAsync(app);
        await EliminarAsync();

        var resultado = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConservarRecorridos>()
            .ConservarAsync(sesionId, casoId, "Tarde", Guid.Empty, CancellationToken.None));

        Assert.IsType<ResultadoConservacion.SinPosiciones>(resultado);
    }

    [Fact]
    public async Task HU39_CA03_SoloOperador_UnAdministradorNoConserva()
    {
        var (sesionId, app) = await SesionTerminadaAsync(1);
        var casoId = await SosAsync(app);
        var admin = fabrica.CrearNavegador();
        await admin.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);

        var respuesta = await admin.PostAsync($"{Ruta(sesionId)}?handler=Conservar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(admin, "/panel/admin/cuentas"),
            ["Conservacion.CasoId"] = casoId.ToString(),
            ["Conservacion.Finalidad"] = "No autorizado",
        }));

        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.OriginalString);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.ConservacionesRecorrido.AnyAsync(c => c.SesionId == sesionId)));
    }
}
