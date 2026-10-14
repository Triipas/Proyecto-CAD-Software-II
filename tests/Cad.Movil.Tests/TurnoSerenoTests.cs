using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU18 — Activar mi estado operativo y compartir mi ubicación (lado de la app). Coordenadas ficticias.
public class TurnoSerenoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider reloj = new(Inicio);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", Inicio.AddHours(8)) };

    public TurnoSerenoTests() => ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 6, reloj.GetUtcNow());

    private ServicioTurnoSereno Turno() => new(api, ubicacion, sesiones, reloj);

    private static async Task Esperar(Func<bool> condicion, string descripcion)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException($"No se cumplió: {descripcion}");
            await Task.Delay(10);
        }
    }

    // Deja que el bucle procese un tick sin avanzar el reloj.
    private static Task Calma() => Task.Delay(150);

    [Fact]
    public async Task CA01_AlActivarse_EnviaUnaPosicionYLuegoUnaCadaIntervaloConSuMarcaDeTiempo()
    {
        var turno = Turno();

        Assert.Equal(ResultadoActivacion.Activado, await turno.ActivarAsync());
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "primera posición al activarse");
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Esperar(() => api.PosicionesEnviadas.Count == 2, "segunda posición a los 30 s");
        reloj.Advance(TimeSpan.FromSeconds(29));
        await Calma();
        Assert.Equal(2, api.PosicionesEnviadas.Count); // aún no se cumplió el intervalo

        var enviadas = api.PosicionesEnviadas.ToArray();
        Assert.Equal(Inicio, enviadas[0].ObtenidaEn);
        Assert.Equal(Inicio.AddSeconds(30), enviadas[1].ObtenidaEn);
        Assert.True(turno.Operativo);
        Assert.Equal(Inicio.AddSeconds(30), turno.UltimoEnvio);
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task DP15_UsaElIntervaloQueIndicaElServidor()
    {
        api.EstadoServidor = new RespuestaEstadoOperativo(false, null, 45);
        var turno = Turno();
        await turno.CargarAsync();

        await turno.ActivarAsync();
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "primera posición");
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Calma();
        Assert.Single(api.PosicionesEnviadas);
        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => api.PosicionesEnviadas.Count == 2, "segunda posición a los 45 s");
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task CA02_AlDesactivarse_SeDetieneElEnvioYSeInformaAlServidor()
    {
        var turno = Turno();
        await turno.ActivarAsync();
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "primera posición");

        await turno.DesactivarAsync();
        reloj.Advance(TimeSpan.FromMinutes(5));
        await Calma();

        Assert.Single(api.PosicionesEnviadas);
        Assert.False(turno.Operativo);
        Assert.False(turno.Transmitiendo);
        Assert.False(api.CambiosEstado.Last().Operativo);
    }

    [Fact]
    public async Task CA02_SiElServidorNoConfirmaLaDesactivacion_ElEnvioSigueDetenido()
    {
        var turno = Turno();
        await turno.ActivarAsync();
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "primera posición");
        api.RespuestaCambio = _ => new ResultadoApi<RespuestaEstadoOperativo>.SinRespuesta("sin red");

        await turno.DesactivarAsync();
        reloj.Advance(TimeSpan.FromMinutes(2));
        await Calma();

        Assert.Single(api.PosicionesEnviadas);
        Assert.False(turno.Transmitiendo);
        Assert.Contains("no se pudo confirmar la desactivación", turno.Error);
    }

    [Theory]
    [InlineData(EstadoPermisoUbicacion.Desconocido, ResultadoActivacion.NecesitaExplicarPermiso)]
    [InlineData(EstadoPermisoUbicacion.Denegado, ResultadoActivacion.PermisoDenegado)]
    [InlineData(EstadoPermisoUbicacion.Restringido, ResultadoActivacion.PermisoDenegado)]
    public async Task CA03_DP16a_SinPermiso_NoSeActivaNiSeLlamaAlServidor(EstadoPermisoUbicacion permiso, ResultadoActivacion esperado)
    {
        ubicacion.Permiso = permiso;
        var turno = Turno();

        Assert.Equal(esperado, await turno.ActivarAsync());
        await Calma();

        Assert.Empty(api.CambiosEstado);
        Assert.Empty(api.PosicionesEnviadas);
        Assert.Equal(0, ubicacion.Solicitudes); // la app no pide el permiso por su cuenta: primero lo explica
    }

    [Fact]
    public async Task CA03_ActivacionConPermiso_DeclaraLaUbicacionAutorizada()
    {
        var turno = Turno();

        await turno.ActivarAsync();

        Assert.Equal(new SolicitudCambioEstadoOperativo(true, true), api.CambiosEstado.Single());
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task SiElServidorRechazaLaActivacion_NoSeEnviaNada()
    {
        api.RespuestaCambio = _ => new ResultadoApi<RespuestaEstadoOperativo>.Rechazado(400, "Para activar tu estado operativo debes permitir el acceso a la ubicación.", new Dictionary<string, string[]>());
        var turno = Turno();

        Assert.Equal(ResultadoActivacion.Fallo, await turno.ActivarAsync());
        await Calma();

        Assert.False(turno.Operativo);
        Assert.Empty(api.PosicionesEnviadas);
        Assert.Contains("debes permitir", turno.Error);
    }

    [Fact]
    public async Task ElServidorIndicaQueNoEstaOperativo_SeDetieneElEnvio()
    {
        var turno = Turno();
        await turno.ActivarAsync();
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "primera posición");
        api.RespuestaPosicion = _ => new ResultadoApi<SinContenido>.Rechazado(409, "Tu estado no es operativo", new Dictionary<string, string[]>());

        reloj.Advance(TimeSpan.FromSeconds(30));
        await Esperar(() => !turno.Transmitiendo, "el envío se detiene");
        reloj.Advance(TimeSpan.FromMinutes(2));
        await Calma();

        Assert.Equal(2, api.PosicionesEnviadas.Count);
        Assert.False(turno.Operativo);
        Assert.Contains("no es operativo", turno.Error);
    }

    [Fact]
    public async Task SinPosicionOSinConexion_SeAvisaYSeSigueIntentando()
    {
        var turno = Turno();
        ubicacion.Posicion = () => null;
        await turno.ActivarAsync();
        await Esperar(() => turno.AvisoEnvio is not null, "aviso sin posición");
        Assert.Empty(api.PosicionesEnviadas);
        Assert.Contains("No se pudo obtener tu posición", turno.AvisoEnvio);

        ubicacion.Posicion = () => new UbicacionSos(-12.05, -77.03, 9, reloj.GetUtcNow());
        api.RespuestaPosicion = _ => new ResultadoApi<SinContenido>.SinRespuesta("sin red");
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "intento de envío sin red");
        await Esperar(() => turno.AvisoEnvio?.Contains("Sin conexión") == true, "aviso sin conexión");

        api.RespuestaPosicion = _ => new ResultadoApi<SinContenido>.Correcto(new SinContenido());
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Esperar(() => api.PosicionesEnviadas.Count == 2 && turno.AvisoEnvio is null, "se recupera");
        Assert.True(turno.Transmitiendo);
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task LecturaDeUbicacionQueNoResponde_NoBloqueaElEnvio()
    {
        // Lección de SPIKE01: la lectura puede no terminar nunca; se corta y se intenta en el siguiente intervalo.
        ubicacion.NoResponde = true;
        var turno = Turno();
        await turno.ActivarAsync();
        await Calma();

        reloj.Advance(ServicioTurnoSereno.EsperaUbicacionMaxima);
        await Esperar(() => turno.AvisoEnvio is not null, "se corta la lectura");

        ubicacion.NoResponde = false;
        reloj.Advance(TimeSpan.FromSeconds(20)); // completa el intervalo de 30 s
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "envío en el siguiente intervalo");
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task AlAbrir_SiYaEstabaOperativoEnElServidor_ReanudaElEnvio()
    {
        api.EstadoServidor = new RespuestaEstadoOperativo(true, Inicio.AddMinutes(-5), 30);
        var turno = Turno();

        await turno.CargarAsync();
        await Esperar(() => api.PosicionesEnviadas.Count == 1, "reanuda el envío");

        Assert.True(turno.Operativo);
        Assert.Empty(api.CambiosEstado); // no vuelve a activar: solo continúa
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task AlAbrir_OperativoSinPermiso_NoEnviaYAvisa()
    {
        api.EstadoServidor = new RespuestaEstadoOperativo(true, Inicio, 30);
        ubicacion.Permiso = EstadoPermisoUbicacion.Denegado;
        var turno = Turno();

        await turno.CargarAsync();
        await Calma();

        Assert.Empty(api.PosicionesEnviadas);
        Assert.False(turno.Transmitiendo);
        Assert.Contains("Sin permiso de ubicación", turno.AvisoEnvio);
    }

    [Fact]
    public async Task SesionVencida_NoActivaYPideVolverAIngresar()
    {
        sesiones.Sesion = new SesionApp("token", "Sereno", Inicio.AddMinutes(-1));
        var turno = Turno();

        Assert.Equal(ResultadoActivacion.SesionTerminada, await turno.ActivarAsync());

        Assert.True(turno.SesionTerminada);
        Assert.Empty(api.CambiosEstado);
    }

    [Fact]
    public async Task TrasVolverAIngresar_NoSeHeredaLaSesionTerminada()
    {
        // Defecto encontrado en el emulador (14/10/2026): el servicio vive toda la vida de la app.
        sesiones.Sesion = new SesionApp("token-viejo", "Sereno", Inicio.AddMinutes(-1));
        var turno = Turno();
        await turno.ActivarAsync();
        Assert.True(turno.SesionTerminada);

        await turno.ReiniciarAsync(); // salir
        sesiones.Sesion = new SesionApp("token-nuevo", "Sereno", Inicio.AddHours(8)); // volver a ingresar
        await turno.CargarAsync();

        Assert.False(turno.SesionTerminada);
        Assert.Null(turno.Error);
        Assert.Equal(ResultadoActivacion.Activado, await turno.ActivarAsync());
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task DobleToqueEnActivar_SoloUnCambioDeEstado()
    {
        var bloqueo = new TaskCompletionSource();
        var turno = Turno();
        api.RespuestaCambio = s =>
        {
            bloqueo.Task.Wait(TimeSpan.FromSeconds(2));
            return new ResultadoApi<RespuestaEstadoOperativo>.Correcto(new RespuestaEstadoOperativo(s.Operativo, Inicio, 30));
        };

        var primero = Task.Run(() => turno.ActivarAsync());
        await Esperar(() => api.CambiosEstado.Count == 1, "primer cambio en curso");
        var segundo = await turno.ActivarAsync();
        bloqueo.SetResult();
        await primero;

        Assert.Equal(ResultadoActivacion.Fallo, segundo); // ignorado mientras el primero está en curso
        Assert.Single(api.CambiosEstado);
        await turno.DetenerAsync();
    }
}

public class SerenoVistaModeloTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new() { Posicion = () => new UbicacionSos(-12.05, -77.04, 5, DateTimeOffset.UtcNow) };
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("t", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };
    private readonly NavegacionFalsa navegacion = new();
    private int ajustes;

    private InicioSerenoVistaModelo Crear(out ServicioTurnoSereno turno)
    {
        turno = new ServicioTurnoSereno(api, ubicacion, sesiones, new FakeTimeProvider(Inicio));
        return new InicioSerenoVistaModelo(turno, new ServicioDespacho(api, sesiones, new CanalFalso(), TimeProvider.System), sesiones, navegacion, () => ajustes++) { HiloPrincipal = a => { a(); return Task.CompletedTask; } };
    }

    [Fact]
    public async Task PermisoPendiente_PrimeroExplica_YAlPermitirSeActiva()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        var vm = Crear(out var turno);

        await vm.Alternar.EjecutarAsync();
        Assert.True(vm.MostrarExplicacion);
        Assert.False(vm.Operativo);

        await vm.PermitirUbicacion.EjecutarAsync();
        Assert.False(vm.MostrarExplicacion);
        Assert.True(vm.Operativo);
        Assert.Equal("Desactivar estado operativo", vm.TextoBoton);
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task PermisoDenegado_OrientaALosAjustesSinActivar()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        ubicacion.RespuestaAlSolicitar = EstadoPermisoUbicacion.Denegado;
        var vm = Crear(out _);

        await vm.Alternar.EjecutarAsync();
        await vm.PermitirUbicacion.EjecutarAsync();
        await vm.AbrirAjustes.EjecutarAsync();

        Assert.True(vm.MostrarAjustes);
        Assert.False(vm.Operativo);
        Assert.Equal(1, ajustes);
        Assert.Empty(api.CambiosEstado);
    }

    [Fact]
    public async Task SesionCaducada_SalirYVolverAIngresar_LaMismaPantallaVuelveAPermitirActivar()
    {
        // Shell reutiliza la página (y su modelo de vista) entre sesiones.
        sesiones.Sesion = new SesionApp("t", "Sereno", Inicio.AddMinutes(-1)); // caducada según el reloj simulado
        var vm = Crear(out var turno);
        await vm.Alternar.EjecutarAsync();
        Assert.True(vm.SesionTerminada);
        Assert.False(vm.Alternar.CanExecute(null));

        await vm.Salir.EjecutarAsync();
        sesiones.Sesion = new SesionApp("t2", "Sereno", Inicio.AddHours(8));
        await vm.AlAparecerAsync();

        Assert.False(vm.SesionTerminada);
        Assert.False(vm.HayError);
        Assert.True(vm.Alternar.CanExecute(null));
        await turno.DetenerAsync();
    }

    [Fact]
    public async Task MientrasEstaOperativo_NoSePuedeSalir_HastaDesactivar()
    {
        var vm = Crear(out _);

        await vm.Alternar.EjecutarAsync();
        Assert.True(vm.Operativo);
        Assert.False(vm.Salir.CanExecute(null));
        Assert.True(vm.HayAyudaSalir);

        await vm.Alternar.EjecutarAsync(); // desactivar
        Assert.False(vm.Operativo);
        Assert.True(vm.Salir.CanExecute(null));

        await vm.Salir.EjecutarAsync();
        Assert.Null(sesiones.Sesion);
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }
}
