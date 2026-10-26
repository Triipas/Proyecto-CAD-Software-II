using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU34–HU37 — acompañamiento temporal, lado de la app (S4-T01–S4-T07). Coordenadas ficticias.
public class AcompanamientoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider reloj = new(Inicio);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly AlmacenLocalFalso almacen = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ciudadano", "Ciudadano", Inicio.AddHours(8)) };
    private readonly Guid sesionId = Guid.NewGuid();
    private readonly SegundoPlanoFalso segundoPlano = new();

    public AcompanamientoTests()
    {
        ubicacion.Posicion = () => new UbicacionSos(-12.0464, -77.0428, 8, reloj.GetUtcNow());
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow(), id: sesionId));
    }

    private ServicioAcompanamiento Servicio() => new(api, ubicacion, sesiones, almacen, reloj, segundoPlano);

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

    private static Task Calma() => Task.Delay(150);

    private static ResultadoApi<SesionAcompanamientoVista> Cerrada(Guid id, string estado, DateTimeOffset inicio) =>
        new ResultadoApi<SesionAcompanamientoVista>.Correcto(new SesionAcompanamientoVista(id, estado, inicio, inicio.AddMinutes(30),
            estado == EstadosAcompanamiento.Vencida ? inicio.AddMinutes(30) : inicio.AddMinutes(5), inicio.AddMinutes(30), 15));

    // ----- HU34 -----

    [Fact]
    public async Task HU34_CA01_InicioValido_MuestraEstadoActivoTiempoRestanteYControlDeFinalizacion()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        vm.FinalidadAceptada = true;

        await vm.Iniciar.EjecutarAsync();

        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        Assert.True(vm.MostrarActivo);
        Assert.False(vm.MostrarInicio);
        Assert.Equal("30:00", vm.TiempoRestante);
        Assert.True(vm.Finalizar.CanExecute(null));
        var inicio = Assert.Single(api.IniciosAcompanamiento);
        Assert.True(inicio.FinalidadAceptada && inicio.UbicacionDisponible);
        reloj.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal("20:00", vm.TiempoRestante);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA01_ElTiempoRestanteUsaLaHoraDelServidor_NoLaDelTelefono()
    {
        // El teléfono va 7 minutos adelantado respecto del servidor.
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow().AddMinutes(-7)));
        var servicio = Servicio();

        await servicio.IniciarAsync(true);

        Assert.Equal(TimeSpan.FromMinutes(30), servicio.Restante);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA02_FinalidadNoAceptada_NoLlamaAlServidorNiTransmite()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);

        await vm.Iniciar.EjecutarAsync();

        Assert.Empty(api.IniciosAcompanamiento);
        Assert.Contains("marca que aceptas compartir tu ubicación", vm.MensajeInicio);
        Assert.Equal(EstadoAcompanamiento.SinSesion, servicio.Estado);
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Calma();
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Empty(ubicacion.Lecturas); // ni siquiera se lee la posición
    }

    [Theory]
    [InlineData(EstadoPermisoUbicacion.Desconocido, ResultadoInicioAcompanamiento.NecesitaExplicarPermiso)]
    [InlineData(EstadoPermisoUbicacion.Denegado, ResultadoInicioAcompanamiento.PermisoDenegado)]
    [InlineData(EstadoPermisoUbicacion.Restringido, ResultadoInicioAcompanamiento.PermisoDenegado)]
    public async Task HU34_CA03_SinPermiso_NoSeCreaLaSesionNiSeTransmite(EstadoPermisoUbicacion permiso, ResultadoInicioAcompanamiento esperado)
    {
        ubicacion.Permiso = permiso;
        var servicio = Servicio();

        Assert.Equal(esperado, await servicio.IniciarAsync(true));

        Assert.Empty(api.IniciosAcompanamiento);
        Assert.Empty(ubicacion.Lecturas);
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Equal(EstadoAcompanamiento.SinSesion, servicio.Estado);
    }

    [Fact]
    public async Task HU34_CA03_SinUbicacionDisponible_NoSeCreaLaSesion()
    {
        ubicacion.Posicion = () => null;
        var servicio = Servicio();

        Assert.Equal(ResultadoInicioAcompanamiento.SinUbicacion, await servicio.IniciarAsync(true));

        Assert.Empty(api.IniciosAcompanamiento);
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Contains("No se pudo obtener tu ubicación", servicio.Error);
    }

    [Fact]
    public async Task HU34_CA03_PermisoPedidoTrasExplicar_SiSeConcedeInicia()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        vm.FinalidadAceptada = true;
        await vm.Iniciar.EjecutarAsync();
        Assert.True(vm.MostrarPermitir);

        await vm.PermitirUbicacion.EjecutarAsync();

        Assert.Equal(1, ubicacion.Solicitudes);
        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA04_ConPermisoPeroSinPulsar_NoHaySesionNiPosiciones()
    {
        var servicio = Servicio();

        await servicio.CargarAsync(); // abrir la pantalla consulta el estado, nada más
        reloj.Advance(TimeSpan.FromMinutes(2));
        await Calma();

        Assert.Empty(api.IniciosAcompanamiento);
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Empty(ubicacion.Lecturas);
        Assert.False(servicio.Transmitiendo);
    }

    [Fact]
    public async Task HU34_CA05_IniciarElAcompanamiento_NoEmiteSosNiRegistraReporte()
    {
        var servicio = Servicio();

        await servicio.IniciarAsync(true);

        Assert.Empty(api.SosEnviados);
        Assert.Empty(api.ReportesEnviados);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA06_ElServidorRechazaUnaSegundaSesion_SeMuestraLaQueYaEstabaActiva()
    {
        var activa = ApiFalsa.SesionActiva(Inicio.AddMinutes(-5), ahoraServidor: Inicio);
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Rechazado(409, "Ya tienes un acompañamiento activo.", new Dictionary<string, string[]>());
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(activa);
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        vm.FinalidadAceptada = true;

        await vm.Iniciar.EjecutarAsync();

        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        Assert.Equal(activa.Id, servicio.Sesion!.Id);
        Assert.Equal("25:00", vm.TiempoRestante);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA06_ElRechazoDeUnaSegundaSesion_NoSeMuestraComoActivaNiTransmite()
    {
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Rechazado(409, "Ya tienes un acompañamiento activo.", new Dictionary<string, string[]>());
        var servicio = Servicio();

        Assert.Equal(ResultadoInicioAcompanamiento.OtraSesionActiva, await servicio.IniciarAsync(true));
        reloj.Advance(TimeSpan.FromSeconds(30));
        await Calma();

        Assert.NotEqual(EstadoAcompanamiento.Activo, servicio.Estado);
        Assert.False(servicio.Transmitiendo);
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Contains("Ya tienes un acompañamiento activo", servicio.Error);
    }

    [Fact]
    public async Task HU34_CA07_SinConfirmacionDelServidor_NoSeMuestraActivaNiTransmite()
    {
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("sin red");
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        vm.FinalidadAceptada = true;

        await vm.Iniciar.EjecutarAsync();
        reloj.Advance(TimeSpan.FromSeconds(45));
        await Calma();

        Assert.Equal(EstadoAcompanamiento.NoConfirmado, servicio.Estado);
        Assert.False(vm.MostrarActivo);
        Assert.Empty(api.PosicionesAcompanamiento);
        Assert.Equal("Reintentar el inicio", vm.TextoBotonIniciar);
        Assert.Contains("no está activo", vm.TextoEstado);
    }

    [Fact]
    public async Task HU34_CA07_ReintentoTrasNoConfirmar_UsaElMismoIdentificador()
    {
        var respuestas = new Queue<ResultadoApi<SesionAcompanamientoVista>>([
            new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("sin red"),
            new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(Inicio))]);
        api.RespuestaInicioAcompanamiento = _ => respuestas.Dequeue();
        var servicio = Servicio();

        await servicio.IniciarAsync(true);
        await servicio.IniciarAsync(true);

        var ids = api.IniciosAcompanamiento.Select(i => i.IdSolicitud).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Single(ids.Distinct());
        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU34_CA08_AlVolverAAbrirLaApp_ConsultaLaSesionActivaYReanudaElEnvio()
    {
        var activa = ApiFalsa.SesionActiva(Inicio.AddMinutes(-12), ahoraServidor: Inicio);
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(activa);
        var servicio = Servicio(); // un servicio nuevo: la app se volvió a abrir

        await servicio.CargarAsync();

        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado);
        Assert.Equal(TimeSpan.FromMinutes(18), servicio.Restante);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "envío reanudado");
        await servicio.DetenerAsync();
    }

    // 26/10/2026: «Volver a ingresar» (la sesión de la app venció) ya no se bloquea con un acompañamiento en curso: sin sesión no
    // puede transmitir y, al volver a ingresar, se retoma.
    [Fact]
    public async Task HU34_CA08_ElInicioMuestraElAcompanamientoActivo_YVolverAIngresarSiguePermitido()
    {
        var servicio = Servicio();
        var inicio = new InicioCiudadanoVistaModelo(new EmisorSos(api, ubicacion, sesiones, reloj), new PreparacionUbicacion(ubicacion),
            sesiones, new NavegacionFalsa(), () => { }, null, servicio) { HiloPrincipal = a => { a(); return Task.CompletedTask; } };

        await servicio.IniciarAsync(true);

        Assert.True(inicio.AcompanamientoEnCurso);
        Assert.Contains("Quedan 30 min", inicio.TextoAcompanamiento);
        Assert.True(inicio.Salir.CanExecute(null));
        await servicio.FinalizarAsync();
        Assert.False(inicio.AcompanamientoEnCurso);
    }

    // ----- 26/10/2026: cerrar sesión con un acompañamiento en curso -----

    private PerfilVistaModelo Perfil(ServicioAcompanamiento servicio, NavegacionFalsa navegacion) =>
        new(api, sesiones, navegacion, new FlujoIngresoCelular(api, sesiones, new DispositivosFalsos(), navegacion, reloj), servicio);

    [Fact]
    public async Task CerrarSesionConAcompanamiento_PideConfirmarYCancelarNoCambiaNada()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        var navegacion = new NavegacionFalsa();
        var comun = 0;
        var perfil = Perfil(servicio, navegacion);
        perfil.ConfirmarCierre = () => { comun++; return Task.FromResult(true); };
        perfil.ConfirmarCierreConAcompanamiento = () => Task.FromResult(false);

        await perfil.CerrarSesion.EjecutarAsync();

        Assert.Equal(0, comun); // se usa el diálogo que avisa del acompañamiento
        Assert.True(servicio.Activo);
        Assert.Empty(api.Finalizaciones);
        Assert.NotNull(sesiones.Sesion);
        Assert.Empty(navegacion.Rutas);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task CerrarSesionConAcompanamiento_AlConfirmar_LoFinalizaYCierraLaSesion()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        var navegacion = new NavegacionFalsa();

        await Perfil(servicio, navegacion).CerrarSesion.EjecutarAsync();

        Assert.Contains(sesionId, api.Finalizaciones); // la central no lo ve «Sin conexión»
        Assert.False(servicio.Transmitiendo);
        Assert.False(almacen.Valores.ContainsKey(ServicioAcompanamiento.ClaveCierrePendiente));
        Assert.Null(sesiones.Sesion);
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }

    [Fact]
    public async Task CerrarSesionConAcompanamiento_SinConexion_CierraIgualYGuardaElCierre()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        api.RespuestaFinalizacion = _ => new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("Sin conexión.");
        var navegacion = new NavegacionFalsa();

        await Perfil(servicio, navegacion).CerrarSesion.EjecutarAsync();

        Assert.False(servicio.Transmitiendo);
        Assert.Equal(sesionId.ToString(), almacen.Valores[ServicioAcompanamiento.ClaveCierrePendiente]); // se envía al volver a ingresar
        Assert.Null(sesiones.Sesion);
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }

    // ----- 26/10/2026: rediseño de la pantalla activa -----

    [Fact]
    public async Task Activo_ElLatidoYElRecorridoSoloMuestranLoQueLaCentralConfirmo()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        var cambios = 0;
        vm.RecorridoCambio += () => cambios++;

        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.Recorrido.Count == 1, "la primera posición confirmada");

        Assert.True(vm.MostrarActivo);
        Assert.False(vm.MostrarFormulario);
        Assert.True(vm.LatidoAlDia);
        Assert.Contains("Tu ubicación llegó a la central", vm.TextoLatido);
        Assert.Equal(1, vm.ProgresoRestante, 2);
        Assert.StartsWith("Quedan 30 min · hasta las ", vm.TextoRestante);
        Assert.Equal("Iniciaste el acompañamiento.", Assert.Single(vm.Actividad).Texto);
        Assert.True(cambios > 0); // el mapa se vuelve a dibujar
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task Activo_SinConexion_ElLatidoDiceLaCondicionYElRecorridoNoCrece()
    {
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.SinRespuesta("Sin conexión.");
        var servicio = Servicio();
        var vm = VistaModelo(servicio);

        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count >= 1 && servicio.Condicion is not null, "el primer intento de envío");

        Assert.False(vm.LatidoAlDia);
        Assert.Contains("Sin conexión con el servidor", vm.TextoLatido);
        Assert.Empty(servicio.Recorrido);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task Activo_ConLaSesionDeLaAppVencida_DejaDeMostrarseAcompanadoYOfreceVolverAIngresar()
    {
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.Rechazado(401, "Sesión vencida.", new Dictionary<string, string[]>());
        var servicio = Servicio();
        var vm = VistaModelo(servicio);

        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.SesionTerminada, "el 401 del envío");

        Assert.False(vm.MostrarActivo); // ya no dice «La central te acompaña»
        Assert.True(vm.MostrarFormulario);
        Assert.True(vm.HayEstado);
        Assert.Contains("Vuelve a ingresar", vm.TextoEstado);
        Assert.True(vm.SesionTerminada);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task Activo_LaActividadMuestraLoUltimoConLaCentral_YUnaSesionNuevaEmpiezaDeCero()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.Recorrido.Count == 1, "la primera posición confirmada");

        await servicio.InformarEstadoAsync(EstadosCiudadano.AlgoNoEstaBien);
        await servicio.InformarEstadoAsync(EstadosCiudadano.EstoyBien);

        Assert.Equal(["Avisaste: «Estoy bien».", "Avisaste: «Algo no está bien».", "Iniciaste el acompañamiento."],
            vm.Actividad.Select(l => l.Texto));
        await servicio.FinalizarAsync();
        var otra = Guid.NewGuid();
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.Correcto(ApiFalsa.SesionActiva(reloj.GetUtcNow(), id: otra));
        await servicio.IniciarAsync(true);

        Assert.Equal("Iniciaste el acompañamiento.", Assert.Single(vm.Actividad).Texto);
        await servicio.DetenerAsync();
    }

    // ----- HU35 -----

    [Fact]
    public async Task HU35_CA01_DuranteLaSesion_EnviaLaPosicionCadaQuinceSegundosConHoraYPrecision()
    {
        var servicio = Servicio();

        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición al iniciar");
        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => api.PosicionesAcompanamiento.Count == 2, "segunda posición a los 15 s");
        reloj.Advance(TimeSpan.FromSeconds(14));
        await Calma();
        Assert.Equal(2, api.PosicionesAcompanamiento.Count);

        var enviadas = api.PosicionesAcompanamiento.ToArray();
        Assert.Equal(Inicio, enviadas[0].ObtenidaEn);
        Assert.Equal(Inicio.AddSeconds(15), enviadas[1].ObtenidaEn);
        Assert.All(enviadas, p => Assert.Equal(8, p.PrecisionMetros));
        Assert.Equal(Inicio.AddSeconds(15), servicio.UltimoEnvio);
        Assert.Null(servicio.Condicion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU35_CA02_DespuesDeFinalizar_NoHayTransmisionesPeriodicas()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");

        await servicio.FinalizarAsync();
        reloj.Advance(TimeSpan.FromMinutes(2));
        await Calma();

        Assert.Single(api.PosicionesAcompanamiento);
        Assert.False(servicio.Transmitiendo);
    }

    [Fact]
    public async Task HU35_CA03_SinPosicionNueva_ReenviaLaUltimaConSuHoraOriginalYLoIndica()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");
        ubicacion.Posicion = () => null; // el GPS deja de entregar lecturas

        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => api.PosicionesAcompanamiento.Count == 2, "segundo intento");

        Assert.Equal(Inicio, api.PosicionesAcompanamiento.Last().ObtenidaEn); // no se presenta como actual
        Assert.Contains("Sin posiciones nuevas", servicio.Condicion);
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU35_CA04_SinConexion_LaInterfazMuestraLaCondicionReal()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.SinRespuesta("sin red");

        await servicio.IniciarAsync(true);
        await Esperar(() => servicio.Condicion is not null, "condición de la transmisión");

        Assert.Contains("Sin conexión con el servidor", vm.TextoTransmision);
        Assert.Equal("advertencia", vm.TipoAvisoTransmision);
        Assert.Equal(EstadoAcompanamiento.Activo, servicio.Estado); // sigue activa: se reintenta en el siguiente intervalo
        await servicio.DetenerAsync();
    }

    [Fact]
    public async Task HU35_CA04_PermisoRetiradoDuranteLaSesion_NoSeLeeLaPosicionYSeIndica()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");
        var lecturas = ubicacion.Lecturas.Count;
        ubicacion.Permiso = EstadoPermisoUbicacion.Denegado;

        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => servicio.Condicion is not null, "condición");

        Assert.Contains("Sin permiso de ubicación", servicio.Condicion);
        Assert.Single(api.PosicionesAcompanamiento);
        Assert.Equal(lecturas, ubicacion.Lecturas.Count);
        await servicio.DetenerAsync();
    }

    [Fact]
    public void HU35_CA05_DP13_LaInterfazDiceQueSigueConLaAppCerradaYQueNecesitaConexion()
    {
        Assert.Contains("aunque cierres la aplicación o apagues la pantalla", AcompanamientoVistaModelo.TextoEntorno);
        Assert.Contains("Necesita conexión", AcompanamientoVistaModelo.TextoEntorno);
        Assert.Contains("mientras veas la notificación", AcompanamientoVistaModelo.TextoEntorno);
    }

    // ----- DP13: transmisión con la app fuera de la pantalla -----

    [Fact]
    public async Task DP13_AlIniciar_SeMantieneLaTransmisionEnSegundoPlanoHastaElVencimiento()
    {
        var servicio = Servicio();

        await servicio.IniciarAsync(true);

        Assert.True(segundoPlano.Activo);
        Assert.Equal(Inicio.AddMinutes(30), Assert.Single(segundoPlano.Inicios));
        await servicio.DetenerAsync();
        Assert.False(segundoPlano.Activo);
    }

    [Fact]
    public async Task DP13_SinIniciarLaSesion_NoSeActivaElSegundoPlano()
    {
        api.RespuestaInicioAcompanamiento = _ => new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("sin red");
        var servicio = Servicio();

        await servicio.IniciarAsync(false);
        await servicio.IniciarAsync(true); // sin confirmación del servidor (HU34-CA07)

        Assert.Empty(segundoPlano.Inicios);
    }

    [Fact]
    public async Task DP13_AlFinalizar_SeDetieneElSegundoPlanoAunqueNoHayaConexion()
    {
        api.RespuestaFinalizacion = _ => new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("sin red");
        var servicio = Servicio();
        await servicio.IniciarAsync(true);

        await servicio.FinalizarAsync();

        Assert.False(segundoPlano.Activo);
        Assert.Equal(EstadoAcompanamiento.CierrePendiente, servicio.Estado);
    }

    [Fact]
    public async Task DP13_CuandoElServidorLaCierra_SeDetieneElSegundoPlano()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.Rechazado(409, "Terminó.", new Dictionary<string, string[]>());
        api.RespuestaAcompanamiento = id => Cerrada(id, EstadosAcompanamiento.Vencida, Inicio);

        reloj.Advance(TimeSpan.FromSeconds(15));
        await Esperar(() => !segundoPlano.Activo, "segundo plano detenido");

        Assert.Equal(EstadoAcompanamiento.Vencido, servicio.Estado);
    }

    [Fact]
    public async Task DP13_AlSalirDeLaSesionDeLaApp_SeDetieneElSegundoPlano()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);

        await servicio.ReiniciarAsync();

        Assert.False(segundoPlano.Activo);
    }

    [Fact]
    public async Task DP13_AlVolverAAbrirLaAppConUnaSesionActiva_SeReanudaElSegundoPlano()
    {
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(ApiFalsa.SesionActiva(Inicio.AddMinutes(-3), ahoraServidor: Inicio));
        var servicio = Servicio();

        await servicio.CargarAsync();

        Assert.True(segundoPlano.Activo);
        await servicio.DetenerAsync();
    }

    // ----- HU36 -----

    [Fact]
    public async Task HU36_CA01_CA02_Finalizar_DetieneEnElActoYElServidorRegistraElCierre()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");

        await vm.Finalizar.EjecutarAsync();

        Assert.False(servicio.Transmitiendo);
        Assert.Equal(sesionId, Assert.Single(api.Finalizaciones));
        Assert.Equal(EstadoAcompanamiento.Finalizado, servicio.Estado);
        Assert.Contains("Tu ubicación ya no se comparte", vm.TextoEstado);
        Assert.Empty(almacen.Valores);
    }

    [Fact]
    public async Task HU36_CA01_CA03_SinConexion_SeDetieneIgualYElCierreSeEnviaAlVolverLaConexion()
    {
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");
        var conexion = false;
        api.RespuestaFinalizacion = id => conexion ? Cerrada(id, EstadosAcompanamiento.Finalizada, Inicio)
            : new ResultadoApi<SesionAcompanamientoVista>.SinRespuesta("sin red");

        await servicio.FinalizarAsync();

        Assert.False(servicio.Transmitiendo); // CA01: la captura y la transmisión local se detienen igual
        Assert.Equal(EstadoAcompanamiento.CierrePendiente, servicio.Estado);
        Assert.Contains("el cierre se enviará", vm.TextoEstado);
        Assert.Equal(sesionId.ToString(), almacen.Valores[ServicioAcompanamiento.ClaveCierrePendiente]);
        reloj.Advance(TimeSpan.FromMinutes(1));
        await Calma();
        Assert.Single(api.PosicionesAcompanamiento);

        conexion = true;
        reloj.Advance(ServicioAcompanamiento.ReintentoCierre);
        await Esperar(() => servicio.Estado == EstadoAcompanamiento.Finalizado, "cierre enviado al volver la conexión");
        Assert.Empty(almacen.Valores);
        Assert.All(api.Finalizaciones, id => Assert.Equal(sesionId, id)); // siempre la misma sesión
    }

    [Fact]
    public async Task HU36_CA03_CierrePendienteTrasCerrarLaApp_SeEnviaAlVolverAAbrirla()
    {
        almacen.Guardar(ServicioAcompanamiento.ClaveCierrePendiente, sesionId.ToString());
        api.RespuestaFinalizacion = id => Cerrada(id, EstadosAcompanamiento.Finalizada, Inicio);
        var servicio = Servicio();

        await servicio.CargarAsync();

        Assert.Equal(sesionId, Assert.Single(api.Finalizaciones));
        Assert.Empty(almacen.Valores);
        Assert.False(servicio.Transmitiendo);
    }

    [Fact]
    public async Task HU36_CA07_FinalizarDosVeces_NoDuplicaElCierre()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);

        await Task.WhenAll(servicio.FinalizarAsync(), servicio.FinalizarAsync());
        await servicio.FinalizarAsync();

        Assert.Single(api.Finalizaciones);
    }

    // ----- HU37 -----

    [Fact]
    public async Task HU37_CA02_AlCumplirseLos30Minutos_DejaDeTransmitirYNoLaMuestraActiva()
    {
        api.RespuestaAcompanamiento = id => Cerrada(id, EstadosAcompanamiento.Vencida, Inicio);
        var servicio = Servicio();
        var vm = VistaModelo(servicio);
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");

        for (var i = 0; i < 121 && servicio.Estado == EstadoAcompanamiento.Activo; i++)
        {
            reloj.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(5);
        }
        await Esperar(() => servicio.Estado == EstadoAcompanamiento.Vencido, "vencida a los 30 minutos");
        var enviadas = api.PosicionesAcompanamiento.Count;
        reloj.Advance(TimeSpan.FromMinutes(1));
        await Calma();

        Assert.False(servicio.Transmitiendo);
        Assert.Equal(enviadas, api.PosicionesAcompanamiento.Count);
        Assert.False(vm.MostrarActivo);
        Assert.Contains("terminó a los 30 minutos", vm.TextoEstado);
        Assert.All(api.PosicionesAcompanamiento, p => Assert.True(p.ObtenidaEn < Inicio.AddMinutes(30)));
    }

    [Fact]
    public async Task HU37_CA02_ElServidorYaLaCerro_LaAppDejaDeTransmitir()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        await Esperar(() => api.PosicionesAcompanamiento.Count == 1, "primera posición");
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.Rechazado(409, "El acompañamiento ya terminó.", new Dictionary<string, string[]>());
        api.RespuestaAcompanamiento = id => Cerrada(id, EstadosAcompanamiento.Vencida, Inicio);

        reloj.Advance(TimeSpan.FromSeconds(15));
        // El estado cambia un instante antes de que el bucle de envío se retire: se esperan las dos cosas.
        await Esperar(() => servicio.Estado == EstadoAcompanamiento.Vencido && !servicio.Transmitiendo, "cierre informado por el servidor");

        Assert.False(servicio.Transmitiendo);
    }

    [Fact]
    public async Task HU37_CA06_DispositivoReconectadoTrasVencer_LaMuestraFinalizada()
    {
        var servicio = Servicio();
        await servicio.IniciarAsync(true);
        api.RespuestaPosicionAcompanamiento = _ => new ResultadoApi<SinContenido>.SinRespuesta("sin red");
        // Al reconectar, el servidor ya no tiene sesión activa: la había vencido.
        api.RespuestaAcompanamientoActual = () => new ResultadoApi<SesionAcompanamientoVista?>.Correcto(null);
        api.RespuestaAcompanamiento = id => Cerrada(id, EstadosAcompanamiento.Vencida, Inicio);

        await servicio.CargarAsync();

        Assert.Equal(EstadoAcompanamiento.Vencido, servicio.Estado);
        Assert.False(servicio.Transmitiendo);
        Assert.Null(servicio.Restante);
    }

    [Fact]
    public async Task SesionDeLaAppVencida_NoInicia()
    {
        sesiones.Sesion = new SesionApp("token-ciudadano", "Ciudadano", Inicio.AddMinutes(-1));
        var servicio = Servicio();

        Assert.Equal(ResultadoInicioAcompanamiento.SesionTerminada, await servicio.IniciarAsync(true));

        Assert.Empty(api.IniciosAcompanamiento);
        Assert.True(servicio.SesionTerminada);
    }
}
