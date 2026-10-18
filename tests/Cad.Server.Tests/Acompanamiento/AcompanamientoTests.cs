using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Acompanamiento;

// HU34 — Activar el acompañamiento (S4-T01), HU35 — Compartir mi posición (S4-T03), HU36 — Finalizarlo (S4-T05, S4-T06) y
// HU37 — Cierre automático a los 30 minutos (S4-T07). Lado del servidor; la app se prueba en Cad.Movil.Tests. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AcompanamientoTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/acompanamiento";

    private async Task<(HttpClient App, CuentaDePrueba Cuenta)> CiudadanoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(cuenta), cuenta);
    }

    private static Task<HttpResponseMessage> IniciarAsync(HttpClient app, Guid? idSolicitud = null, bool finalidad = true, bool ubicacion = true) =>
        app.PostAsJsonAsync(Ruta, new SolicitudInicioAcompanamiento(idSolicitud ?? Guid.NewGuid(), finalidad, ubicacion));

    private static async Task<SesionAcompanamientoVista> SesionActivaAsync(HttpClient app)
    {
        var respuesta = await IniciarAsync(app);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
    }

    private static Task<HttpResponseMessage> EnviarPosicionAsync(HttpClient app, Guid sesionId, DateTimeOffset? obtenidaEn = null,
        double latitud = -12.0464, double longitud = -77.0428, double precision = 12) =>
        app.PostAsJsonAsync($"{Ruta}/{sesionId}/posiciones",
            new PosicionAcompanamientoEnviada(latitud, longitud, precision, obtenidaEn ?? DateTimeOffset.UtcNow));

    private static Task<HttpResponseMessage> FinalizarAsync(HttpClient app, Guid sesionId) => app.PostAsync($"{Ruta}/{sesionId}/finalizacion", null);

    private Task<List<SesionAcompanamiento>> SesionesAsync(Guid ciudadanoId) =>
        fabrica.ConBaseDatosAsync(db => db.SesionesAcompanamiento.AsNoTracking().Where(s => s.CiudadanoId == ciudadanoId).ToListAsync());

    private Task<List<PosicionAcompanamiento>> PosicionesAsync(Guid sesionId) =>
        fabrica.ConBaseDatosAsync(db => db.PosicionesAcompanamiento.AsNoTracking().Where(p => p.SesionId == sesionId).ToListAsync());

    private Task<int> CasosDeAsync(Guid ciudadanoId) => fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync(c => c.CiudadanoId == ciudadanoId));

    // Simula que pasaron los 30 minutos: la sesión vence un segundo antes de ahora (el inicio se mueve con ella).
    private Task CumplirTreintaMinutosAsync(Guid sesionId) => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE \"SesionesAcompanamiento\" SET \"VenceEn\" = now() - interval '1 second', \"IniciadaEn\" = now() - interval '1801 seconds' WHERE \"Id\" = {sesionId}"));

    // ----- HU34 -----

    [Fact]
    public async Task HU34_CA01_ActivacionValida_ElServidorRegistraInicioYVencimientoATreintaMinutos()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await IniciarAsync(app);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        Assert.Equal(EstadosAcompanamiento.Activa, vista.Estado);
        Assert.InRange(vista.IniciadaEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(TimeSpan.FromMinutes(30), vista.VenceEn - vista.IniciadaEn);
        Assert.Null(vista.CerradaEn);
        Assert.Equal(15, vista.IntervaloSegundos); // HU35-CA01
        Assert.InRange(vista.Ahora, vista.IniciadaEn, vista.VenceEn); // la app calcula el tiempo restante con la hora del servidor
        var sesion = Assert.Single(await SesionesAsync(cuenta.Id));
        Assert.Equal((vista.Id, EstadoSesion.Activa, vista.IniciadaEn, vista.VenceEn), (sesion.Id, sesion.Estado, sesion.IniciadaEn, sesion.VenceEn));
    }

    [Fact]
    public async Task HU34_CA02_FinalidadNoAceptada_NoSeCreaLaSesion()
    {
        var (app, cuenta) = await CiudadanoAsync();

        var respuesta = await IniciarAsync(app, finalidad: false);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("finalidadAceptada", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await SesionesAsync(cuenta.Id));
    }

    [Fact]
    public async Task HU34_CA03_SinPermisoOSinUbicacion_NoSeCreaLaSesionNiSeAceptanPosiciones()
    {
        var (app, cuenta) = await CiudadanoAsync();

        var respuesta = await IniciarAsync(app, ubicacion: false);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("ubicacionDisponible", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await SesionesAsync(cuenta.Id));
        // Sin sesión no hay dónde transmitir: una posición a una sesión inexistente no se registra.
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarPosicionAsync(app, Guid.NewGuid())).StatusCode);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.PosicionesAcompanamiento.CountAsync(p =>
            db.SesionesAcompanamiento.Any(s => s.Id == p.SesionId && s.CiudadanoId == cuenta.Id))));
    }

    [Fact]
    public async Task HU34_SinIdentificadorDeSolicitud_SeRechaza()
    {
        var (app, cuenta) = await CiudadanoAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await IniciarAsync(app, Guid.Empty)).StatusCode);
        Assert.Empty(await SesionesAsync(cuenta.Id));
    }

    [Fact]
    public async Task HU34_CA05_ElBotonDeAcompanamiento_NoEmiteUnSosNiRegistraUnReporte()
    {
        var (app, cuenta) = await CiudadanoAsync();

        await SesionActivaAsync(app);

        Assert.Equal(0, await CasosDeAsync(cuenta.Id));
    }

    [Fact]
    public async Task HU34_CA06_SegundaSesionConUnaActiva_SeRechazaYQuedaUnaSola()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var primera = await SesionActivaAsync(app);

        var segunda = await IniciarAsync(app);

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Contains("Ya tienes un acompañamiento activo", await segunda.Content.ReadAsStringAsync());
        Assert.Equal(primera.Id, Assert.Single(await SesionesAsync(cuenta.Id)).Id);
    }

    [Fact]
    public async Task HU34_CA06_SolicitudesSimultaneas_UnaSolaSesionActivaSinRegistrosParciales()
    {
        var (app, cuenta) = await CiudadanoAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => IniciarAsync(app)));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await SesionesAsync(cuenta.Id));
    }

    [Fact]
    public async Task HU34_ReintentoTecnicoDeLaMismaPulsacion_DevuelveLaMismaSesion()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var id = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => IniciarAsync(app, id)));
        var reenvio = await IniciarAsync(app, id);

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas.Append(reenvio).Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var sesion = Assert.Single(await SesionesAsync(cuenta.Id));
        Assert.Equal(sesion.Id, (await reenvio.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!.Id);
    }

    [Fact]
    public async Task HU34_CA08_EstadoPersistente_LaSesionActivaSeConsultaConSuVencimiento()
    {
        var (app, _) = await CiudadanoAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await app.GetAsync($"{Ruta}/actual")).StatusCode);
        var sesion = await SesionActivaAsync(app);

        var actual = await app.GetFromJsonAsync<SesionAcompanamientoVista>($"{Ruta}/actual");

        Assert.Equal((sesion.Id, EstadosAcompanamiento.Activa, sesion.VenceEn), (actual!.Id, actual.Estado, actual.VenceEn));
        Assert.True(actual.VenceEn - actual.Ahora is { TotalMinutes: > 29 and <= 30 }, "El tiempo restante debe ser de casi 30 minutos.");
    }

    [Fact]
    public async Task HU34_SoloElTitular_OtroCiudadanoNoVeNiUsaLaSesion()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var (otro, _) = await CiudadanoAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"{Ruta}/{sesion.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarPosicionAsync(otro, sesion.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await FinalizarAsync(otro, sesion.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await otro.GetAsync($"{Ruta}/actual")).StatusCode);
        Assert.Empty(await PosicionesAsync(sesion.Id));
        Assert.Equal(EstadoSesion.Activa, Assert.Single(await SesionesAsync(cuenta.Id)).Estado);
    }

    [Theory]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Sereno)]
    public async Task HU34_OtrosPerfiles_NoInicianNiTransmiten(string rol)
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var cuenta = await fabrica.CrearAsync(rol);
        var cliente = rol == Roles.Sereno ? await fabrica.CrearClienteAppAsync(cuenta) : fabrica.CrearNavegador();
        if (rol == Roles.Operador)
            await cliente.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        Assert.Equal(HttpStatusCode.Forbidden, (await IniciarAsync(cliente)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EnviarPosicionAsync(cliente, sesion.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FinalizarAsync(cliente, sesion.Id)).StatusCode);
        Assert.Empty(await SesionesAsync(cuenta.Id));
        Assert.Empty(await PosicionesAsync(sesion.Id));
    }

    // ----- HU35 -----

    [Fact]
    public async Task HU35_CA01_PosicionDuranteLaSesion_SeRegistraConSuHoraDeObtencionYPrecision()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var obtenida = DateTimeOffset.UtcNow.AddSeconds(-2);

        var respuesta = await EnviarPosicionAsync(app, sesion.Id, obtenida, -12.0501, -77.0412, 9.5);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var posicion = Assert.Single(await PosicionesAsync(sesion.Id));
        Assert.Equal((-12.0501, -77.0412, 9.5), (posicion.Latitud, posicion.Longitud, posicion.PrecisionMetros));
        Assert.Equal(obtenida.UtcTicks / 10, posicion.ObtenidaEn.UtcTicks / 10);
        Assert.InRange(posicion.RecibidaEn, obtenida, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task HU35_CA03_PosicionAntigua_ConservaSuHoraDeObtencionOriginal()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var antigua = DateTimeOffset.UtcNow.AddMinutes(-3);

        Assert.Equal(HttpStatusCode.Created, (await EnviarPosicionAsync(app, sesion.Id, antigua)).StatusCode);

        var posicion = Assert.Single(await PosicionesAsync(sesion.Id));
        Assert.Equal(antigua.UtcTicks / 10, posicion.ObtenidaEn.UtcTicks / 10);
        Assert.True(posicion.RecibidaEn - posicion.ObtenidaEn > TimeSpan.FromMinutes(2), "La posición antigua no debe presentarse como actual.");
    }

    [Fact]
    public async Task HU35_ReenvioDeLaMismaPosicion_NoSeDuplica()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var obtenida = DateTimeOffset.UtcNow;

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => EnviarPosicionAsync(app, sesion.Id, obtenida)));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await PosicionesAsync(sesion.Id));
    }

    [Theory]
    [InlineData(91, -77, 10, 0)]
    [InlineData(-12, -181, 10, 0)]
    [InlineData(-12, -77, 0, 0)]
    [InlineData(-12, -77, 10, 10)] // hora de obtención 10 minutos en el futuro
    public async Task HU35_PosicionInvalida_SeRechazaSinRegistrar(double latitud, double longitud, double precision, int minutosFuturo)
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);

        var respuesta = await EnviarPosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddMinutes(minutosFuturo), latitud, longitud, precision);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(await PosicionesAsync(sesion.Id));
    }

    [Fact]
    public async Task HU35_CA02_HU36_CA04_DespuesDeFinalizar_NoSeIncorporanPosiciones()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        Assert.Equal(HttpStatusCode.Created, (await EnviarPosicionAsync(app, sesion.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FinalizarAsync(app, sesion.Id)).StatusCode);

        var tardia = await EnviarPosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddSeconds(-30));

        Assert.Equal(HttpStatusCode.Conflict, tardia.StatusCode);
        Assert.Contains(EstadosAcompanamiento.Finalizada, await tardia.Content.ReadAsStringAsync());
        Assert.Single(await PosicionesAsync(sesion.Id));
    }

    [Fact]
    public async Task HU36_CA04_PosicionesSimultaneasConLaFinalizacion_NingunaQuedaDespuesDelCierre()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var inicio = DateTimeOffset.UtcNow;

        var envios = Enumerable.Range(0, 20).Select(i => EnviarPosicionAsync(app, sesion.Id, inicio.AddMilliseconds(i))).ToList();
        var cierre = FinalizarAsync(app, sesion.Id);
        await Task.WhenAll(envios.Append(cierre));

        var cerrada = await fabrica.ConBaseDatosAsync(db => db.SesionesAcompanamiento.AsNoTracking().SingleAsync(s => s.Id == sesion.Id));
        Assert.Equal(EstadoSesion.Finalizada, cerrada.Estado);
        Assert.All(await PosicionesAsync(sesion.Id), p => Assert.True(p.RecibidaEn <= cerrada.CerradaEn, "Se incorporó una posición recibida después del cierre."));
        Assert.All(envios.Select(e => e.Result.StatusCode), e => Assert.Contains(e, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
    }

    // ----- HU36 -----

    [Fact]
    public async Task HU36_CA02_FinalizacionConConexion_ElServidorRegistraElCierre()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await FinalizarAsync(app, sesion.Id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vista = (await respuesta.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        Assert.Equal(EstadosAcompanamiento.Finalizada, vista.Estado);
        var guardada = Assert.Single(await SesionesAsync(cuenta.Id));
        Assert.Equal(EstadoSesion.Finalizada, guardada.Estado);
        Assert.InRange(guardada.CerradaEn!.Value, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await app.GetAsync($"{Ruta}/actual")).StatusCode);
    }

    [Fact]
    public async Task HU36_CA07_FinalizacionRepetida_EstadoFinalConsistenteSinDuplicar()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => FinalizarAsync(app, sesion.Id)));
        var reenvio = await FinalizarAsync(app, sesion.Id);

        Assert.All(respuestas.Append(reenvio), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var cierres = await Task.WhenAll(respuestas.Append(reenvio).Select(async r => (await r.Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!.CerradaEn));
        Assert.Single(cierres.Distinct());
        var guardada = Assert.Single(await SesionesAsync(cuenta.Id));
        Assert.Equal((EstadoSesion.Finalizada, cierres[0]), (guardada.Estado, guardada.CerradaEn));
    }

    [Fact]
    public async Task HU36_CA05_CA06_HU37_CA05_ElCierreNoCancelaElSosNiSeInterpretaComoPeligro()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var ahora = DateTimeOffset.UtcNow;
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!;
        var voluntaria = await SesionActivaAsync(app);
        Assert.Equal(HttpStatusCode.OK, (await FinalizarAsync(app, voluntaria.Id)).StatusCode);
        var porVencer = await SesionActivaAsync(app);
        await CumplirTreintaMinutosAsync(porVencer.Id);
        Assert.Equal(EstadosAcompanamiento.Vencida, (await app.GetFromJsonAsync<SesionAcompanamientoVista>($"{Ruta}/{porVencer.Id}"))!.Estado);

        // El SOS sigue igual y ningún cierre crea un caso nuevo (ni SOS ni despacho).
        var caso = await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == sos.Id));
        Assert.Equal(EstadoCaso.Pendiente, caso.Estado);
        Assert.Equal(1, await CasosDeAsync(cuenta.Id));
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync(a => a.CasoId == sos.Id)));
    }

    // ----- HU37 -----

    [Fact]
    public async Task HU37_CA01_SesionQueCumpleTreintaMinutos_ElServidorLaCierraSinElDispositivo()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);

        // El dispositivo no vuelve a llamar: solo el temporizador del servidor puede cerrarla.
        await CumplirTreintaMinutosAsync(sesion.Id);
        SesionAcompanamiento guardada;
        var limite = DateTime.UtcNow.AddSeconds(20);
        do
        {
            await Task.Delay(250);
            guardada = Assert.Single(await SesionesAsync(cuenta.Id));
        }
        while (guardada.Estado == EstadoSesion.Activa && DateTime.UtcNow < limite);

        Assert.Equal(EstadoSesion.Vencida, guardada.Estado);
        Assert.Equal(guardada.VenceEn, guardada.CerradaEn); // la hora de cierre es la del vencimiento
    }

    [Fact]
    public async Task HU37_CA03_SesionVencida_NoSeRenuevaNiSeCreaOtra()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        await CumplirTreintaMinutosAsync(sesion.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await app.GetAsync($"{Ruta}/actual")).StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(6)); // al menos una vuelta del temporizador

        var sesiones = await SesionesAsync(cuenta.Id);
        Assert.Equal(EstadoSesion.Vencida, Assert.Single(sesiones).Estado);
        // El ciudadano debe iniciar una nueva (HU34), que sí se permite.
        Assert.NotEqual(sesion.Id, (await SesionActivaAsync(app)).Id);
    }

    [Fact]
    public async Task HU37_CA03_TrasVencer_PuedeIniciarUnaNuevaAunqueElTemporizadorNoLaHayaCerrado()
    {
        var (app, cuenta) = await CiudadanoAsync();
        var vencida = await SesionActivaAsync(app);
        await CumplirTreintaMinutosAsync(vencida.Id);

        // Sin consultar antes: el inicio cierra la sesión vencida (el temporizador pasa cada 5 s).
        var nueva = await IniciarAsync(app);

        Assert.Equal(HttpStatusCode.Created, nueva.StatusCode);
        var sesiones = await SesionesAsync(cuenta.Id);
        Assert.Equal(EstadoSesion.Vencida, sesiones.Single(s => s.Id == vencida.Id).Estado);
        Assert.Single(sesiones, s => s.Estado == EstadoSesion.Activa);
    }

    [Fact]
    public async Task HU37_CA04_PosicionesRecibidasDespuesDelVencimiento_NoSeIncorporan()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        await CumplirTreintaMinutosAsync(sesion.Id);

        // Aunque el temporizador aún no la haya cerrado, el servidor decide con su propio reloj.
        var tardia = await EnviarPosicionAsync(app, sesion.Id, DateTimeOffset.UtcNow.AddSeconds(-5));

        Assert.Equal(HttpStatusCode.Conflict, tardia.StatusCode);
        Assert.Empty(await PosicionesAsync(sesion.Id));
    }

    [Fact]
    public async Task HU37_CA06_DispositivoReconectadoTrasVencer_LaVeFinalizadaPorVencimiento()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        await CumplirTreintaMinutosAsync(sesion.Id);

        var consultada = await app.GetFromJsonAsync<SesionAcompanamientoVista>($"{Ruta}/{sesion.Id}");

        Assert.Equal(EstadosAcompanamiento.Vencida, consultada!.Estado);
        Assert.Equal(consultada.VenceEn, consultada.CerradaEn);
        // Una finalización que llega tarde (p. ej., guardada sin conexión, DP20) no la convierte en «Finalizada».
        var finalizacion = await (await FinalizarAsync(app, sesion.Id)).Content.ReadFromJsonAsync<SesionAcompanamientoVista>();
        Assert.Equal(EstadosAcompanamiento.Vencida, finalizacion!.Estado);
    }

    [Fact]
    public async Task RegistroDeLasPosiciones_SinCoordenadas()
    {
        var (app, _) = await CiudadanoAsync();
        var sesion = await SesionActivaAsync(app);
        var (lat, lon) = (-12.0 - Random.Shared.Next(100_000, 999_999) / 1e7, -77.0 - Random.Shared.Next(100_000, 999_999) / 1e7);

        Assert.Equal(HttpStatusCode.Created, (await EnviarPosicionAsync(app, sesion.Id, null, lat, lon)).StatusCode);
        await FinalizarAsync(app, sesion.Id);

        var textos = new[] { lat.ToString(CultureInfo.InvariantCulture), lon.ToString(CultureInfo.InvariantCulture),
            lat.ToString(new CultureInfo("es-PE")), lon.ToString(new CultureInfo("es-PE")) };
        Assert.DoesNotContain(fabrica.Registros.Entradas, e => textos.Any(t => e.Contains(t)));
        Assert.Contains(fabrica.Registros.Entradas, e => e.Contains(sesion.Id.ToString()) && e.Contains("finalizado por el ciudadano"));
    }
}
