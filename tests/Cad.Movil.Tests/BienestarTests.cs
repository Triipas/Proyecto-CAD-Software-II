using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU41 — Responder una confirmación de bienestar (lado de la app, S5-T05, S5-T06). Coordenadas ficticias.
public class BienestarTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider reloj = new(Inicio);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ciudadano", "Ciudadano", Inicio.AddHours(8)) };
    private readonly Guid sesionId = Guid.NewGuid();
    private readonly Guid confirmacionId = Guid.NewGuid();
    private readonly SegundoPlanoFalso segundoPlano = new();

    public BienestarTests()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, reloj.GetUtcNow());
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow(), id: sesionId));
    }

    private ConfirmacionBienestarVista Pendiente() => new(confirmacionId, Inicio.AddMinutes(1), null, null, null, null);

    private ServicioAcompanamiento Servicio() => new(api, ubicacion, sesiones, new AlmacenLocalFalso(), reloj, segundoPlano);

    private AcompanamientoVistaModelo VistaModelo(ServicioAcompanamiento servicio) =>
        new(servicio, ubicacion, new NavegacionFalsa(), () => { }, reloj) { HiloPrincipal = a => { a(); return Task.CompletedTask; } };

    private static async Task Esperar(Func<bool> condicion, string descripcion)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException($"No se cumplió: {descripcion}");
            await Task.Delay(10);
        }
    }

    // Una sesión activa con una confirmación pendiente ya recibida por la app (en el primer ciclo de envío).
    private async Task<(ServicioAcompanamiento Servicio, AcompanamientoVistaModelo Vm)> ConConfirmacionAsync()
    {
        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(Pendiente());
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.Confirmacion is not null, "la app recibe la confirmación");
        return (servicio, vm);
    }

    [Fact]
    public async Task CA01_LaAppRecibeLaConfirmacionDuranteElEnvio_LaPresentaConLasDosRespuestasYAvisaEnElTelefono()
    {
        var (servicio, vm) = await ConConfirmacionAsync();

        Assert.Contains(sesionId, api.ConsultasConfirmacion);
        Assert.True(vm.HayConfirmacion);
        Assert.Contains("La central te pide confirmar cómo estás", vm.TextoConfirmacion);
        Assert.True(vm.ResponderEstoyBien.CanExecute(null));
        Assert.True(vm.ResponderNecesitoAyuda.CanExecute(null));
        Assert.Equal(1, segundoPlano.AvisosConfirmacion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA01_LaPresentacionSeRegistraUnaVezAlMostrarlaEnPantalla()
    {
        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(Pendiente());
        // Al abrir la pantalla, el servidor confirma que la sesión sigue activa.
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(ApiFalsa.SesionActiva(Inicio, id: sesionId));
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.Confirmacion is not null, "la app recibe la confirmación");
        Assert.Empty(api.Presentaciones); // la pantalla aún no está visible

        await vm.AlAparecerAsync();
        await Esperar(() => !api.Presentaciones.IsEmpty, "se registra la presentación");
        await vm.AlAparecerAsync();
        reloj.Advance(TimeSpan.FromSeconds(15)); // otro ciclo: la misma confirmación sigue pendiente
        await Task.Delay(150);

        Assert.Equal([confirmacionId], api.Presentaciones);
        Assert.Equal(1, segundoPlano.AvisosConfirmacion); // la misma confirmación no se vuelve a avisar
        vm.AlDesaparecer();
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA01_ConLaPantallaFueraDeLaVista_NoSeRegistraLaPresentacion()
    {
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(ApiFalsa.SesionActiva(Inicio, id: sesionId));
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await vm.AlAparecerAsync();
        vm.AlDesaparecer(); // la app pasa a segundo plano

        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(Pendiente());
        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => servicio.Confirmacion is not null, "la app recibe la confirmación");
        await Task.Delay(100);
        Assert.Empty(api.Presentaciones);
        Assert.Equal(1, segundoPlano.AvisosConfirmacion); // el teléfono avisa

        await vm.AlAparecerAsync(); // el ciudadano vuelve a la pantalla
        await Esperar(() => !api.Presentaciones.IsEmpty, "se registra la presentación");
        vm.AlDesaparecer();
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA02_EstoyBien_SeEnviaYLaSesionSigueActiva()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.Correcto(null);

        await vm.ResponderEstoyBien.EjecutarAsync();

        var (id, respuesta) = Assert.Single(api.RespuestasBienestar);
        Assert.Equal((confirmacionId, RespuestasBienestar.EstoyBien, (SolicitudEmisionSos?)null), (id, respuesta.Respuesta, respuesta.Sos));
        Assert.False(vm.HayConfirmacion);
        Assert.Equal("Respondiste «Estoy bien». La central lo verá.", vm.ResultadoConfirmacion);
        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        Assert.Empty(api.SosEnviados);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA03_NecesitoAyuda_EnviaElSosConLaUbicacionActualYSoloLoConfirmaConLaRespuestaDelServidor()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        reloj.Advance(TimeSpan.FromSeconds(3));
        var pulsacion = reloj.GetUtcNow();

        await vm.ResponderNecesitoAyuda.EjecutarAsync();

        var (_, respuesta) = Assert.Single(api.RespuestasBienestar);
        Assert.Equal(RespuestasBienestar.NecesitoAyuda, respuesta.Respuesta);
        var sos = respuesta.Sos!;
        Assert.NotEqual(Guid.Empty, sos.IdSolicitud);
        Assert.Equal(pulsacion, sos.ActivadoEn);
        Assert.NotNull(sos.Ubicacion); // HU08: la posición actual, leída tras la pulsación
        Assert.True(sos.Ubicacion!.ObtenidaEn >= pulsacion);
        Assert.Equal("Pedido de ayuda registrado: la central recibió tu SOS.", vm.ResultadoConfirmacion);
        Assert.Equal("exito", vm.TipoResultadoConfirmacion);
        Assert.False(vm.HayConfirmacion);
        // El SOS va con la respuesta (el servidor lo emite o lo vincula), no por separado.
        Assert.Empty(api.SosEnviados);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA03_SinPermisoDeUbicacion_ElSosSeEnviaSinUbicacion()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        ubicacion.Permiso = EstadoPermisoUbicacion.Denegado;

        await vm.ResponderNecesitoAyuda.EjecutarAsync();

        Assert.Null(Assert.Single(api.RespuestasBienestar).Respuesta.Sos!.Ubicacion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA06_CA07_SinRespuestaDelServidor_NoSeConfirmaYElReintentoReenviaElMismoSos()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        api.RespuestaConfirmacion = (_, _) => new ResultadoApi<ConfirmacionBienestarVista>.SinRespuesta("No hay conexión con el servidor.");

        await vm.ResponderNecesitoAyuda.EjecutarAsync();

        Assert.Contains("NO está confirmado", vm.ResultadoConfirmacion);
        Assert.Equal("error", vm.TipoResultadoConfirmacion);
        Assert.True(vm.HayConfirmacion); // se puede reintentar

        api.RespuestaConfirmacion = null; // el servidor responde
        reloj.Advance(TimeSpan.FromSeconds(5));
        await vm.ResponderNecesitoAyuda.EjecutarAsync();

        var envios = api.RespuestasBienestar.Select(r => r.Respuesta.Sos!).ToList();
        Assert.Equal(2, envios.Count);
        Assert.Equal(envios[0], envios[1]); // mismo identificador, hora y ubicación: el servidor no duplica el SOS
        Assert.Equal("Pedido de ayuda registrado: la central recibió tu SOS.", vm.ResultadoConfirmacion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA07_YaRespondidaConLaOtraOpcion_SeMuestraElRechazo()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        api.RespuestaConfirmacion = (_, _) => new ResultadoApi<ConfirmacionBienestarVista>.Rechazado(409,
            "Ya respondiste esta confirmación con la otra opción.", new Dictionary<string, string[]>());

        await vm.ResponderEstoyBien.EjecutarAsync();

        Assert.Equal("Ya respondiste esta confirmación con la otra opción.", vm.ResultadoConfirmacion);
        Assert.False(vm.HayConfirmacion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CA05_SinResponder_LaAppNoEmiteNadaPorSuCuenta()
    {
        var (servicio, _) = await ConConfirmacionAsync();

        reloj.Advance(TimeSpan.FromMinutes(5)); // varios ciclos de envío sin respuesta
        await Task.Delay(200);

        Assert.Empty(api.RespuestasBienestar);
        Assert.Empty(api.SosEnviados);
        Assert.NotNull(servicio.Confirmacion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task AlTerminarElAcompanamiento_NoSeOfreceResponderDesdeEl()
    {
        var (servicio, vm) = await ConConfirmacionAsync();

        await servicio.FinalizarAsync();

        Assert.False(vm.HayConfirmacion);
    }

    [Fact]
    public async Task SinConexionAlConsultar_SeConservaLaConfirmacionConocida()
    {
        var (servicio, vm) = await ConConfirmacionAsync();
        api.RespuestaConsultaConfirmacion = _ => new ResultadoApi<ConfirmacionBienestarVista?>.SinRespuesta("Sin conexión");

        reloj.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(150);

        Assert.True(vm.HayConfirmacion);
        await servicio.DetenerAsync();
    }
}
