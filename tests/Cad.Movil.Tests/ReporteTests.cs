using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU10 — Registrar un reporte de incidencia (lado de la app: S3-T01 y S3-T02). Datos y coordenadas ficticios.
public class ReporteTests
{
    // 14/10/2026 18:30 en Lima (UTC-5).
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 23, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = Guid.NewGuid();

    private readonly ApiFalsa api = new() { Categorias = [new(Robo, "Robo"), new(Guid.NewGuid(), "Otros")] };
    private readonly UbicacionFalsa ubicacion = new() { Posicion = () => new UbicacionSos(-12.07, -77.04, 9.5, Ahora) };
    private readonly SesionesFalsas sesiones = new();
    private readonly NavegacionFalsa navegacion = new();
    private readonly RelojFijo reloj = new(Ahora);

    private RegistradorReporte Registrador() => new(api, sesiones, reloj) { EsperasEntreIntentos = [TimeSpan.Zero, TimeSpan.Zero] };

    private ReporteVistaModelo VistaModelo(RegistradorReporte? registrador = null) =>
        new(api, registrador ?? Registrador(), ubicacion, sesiones, navegacion, reloj, Lima);

    private static DatosReporte Validos(UbicacionHecho? lugar = null) =>
        new(Robo, "Robo de una bicicleta", Ahora.AddHours(-1), lugar ?? new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Esquina del mercado"));

    private static ResultadoApi<RespuestaReporte> SinRespuesta() => new ResultadoApi<RespuestaReporte>.SinRespuesta("sin red");

    // Un formulario completo por referencia textual.
    private async Task<ReporteVistaModelo> FormularioCompletoAsync(ReporteVistaModelo? vm = null)
    {
        vm ??= VistaModelo();
        await vm.AlAparecerAsync();
        await vm.ElegirCategoria.EjecutarAsync(vm.Opciones[0]);
        vm.Descripcion = "Robo de una bicicleta";
        vm.Referencia = "Esquina del mercado"; // sin punto, el lugar es la referencia
        return vm;
    }

    // ----- Servicio de envío -----

    [Fact]
    public async Task CA01_UnReporteValido_SeEnviaYSeConfirmaConLaRespuestaDelServidor()
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());

        var enviado = Assert.Single(api.ReportesEnviados);
        Assert.NotEqual(Guid.Empty, enviado.IdSolicitud);
        Assert.Equal(Robo, enviado.CategoriaId);
        Assert.Equal("Esquina del mercado", enviado.Ubicacion!.Referencia);
        Assert.Equal("Recibido", Assert.IsType<EstadoReporte.Registrado>(registrador.Estado).Respuesta.Estado);
    }

    public static TheoryData<string, DatosReporte> Incompletos => new()
    {
        { "categoriaId", Validos() with { CategoriaId = null } },
        { "descripcion", Validos() with { Descripcion = "   " } },
        { "descripcion", Validos() with { Descripcion = new string('a', RegistradorReporte.MaximoDescripcion + 1) } },
        { "ocurridoAproximadamenteEn", Validos() with { OcurridoEn = null } },
        { "ocurridoAproximadamenteEn", Validos() with { OcurridoEn = Ahora.AddMinutes(1) } },
        { "ubicacion", Validos() with { Ubicacion = null } },
        { "ubicacion", Validos(new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, " ")) },
        { "ubicacion", Validos(new UbicacionHecho(FuentesUbicacionHecho.Mapa, null, null, null, null)) },
        { "ubicacion", Validos(new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12, -77, null, new string('r', RegistradorReporte.MaximoReferencia + 1))) },
    };

    [Theory]
    [MemberData(nameof(Incompletos))]
    public async Task CA02_UnCampoObligatorioIncompleto_NoSeEnviaYSeIndicaElCampo(string campo, DatosReporte datos)
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(datos);

        Assert.Empty(api.ReportesEnviados);
        var rechazo = Assert.IsType<EstadoReporte.Rechazado>(registrador.Estado);
        Assert.True(rechazo.Errores.ContainsKey(campo), $"Se esperaba un error en «{campo}»");
        Assert.True(registrador.PuedeEnviar); // corrige y vuelve a enviar
    }

    [Fact]
    public async Task CA08_SinRespuesta_LosReintentosAutomaticosUsanElMismoIdSolicitud()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult(SinRespuesta()));
        api.RespuestasReporte.Enqueue(_ => Task.FromResult(SinRespuesta()));
        api.RespuestasReporte.Enqueue(s => Task.FromResult(ApiFalsa.ReporteRegistrado(s)));
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());

        Assert.Equal(3, api.ReportesEnviados.Count);
        Assert.Single(api.ReportesEnviados.Select(s => s.IdSolicitud).Distinct());
        Assert.IsType<EstadoReporte.Registrado>(registrador.Estado);
    }

    [Fact]
    public async Task CA08_SinConfirmar_NoSeDaPorRegistrado_YElReintentoManualEnviaLaMismaSolicitud()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult(SinRespuesta()));
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());
        Assert.IsType<EstadoReporte.NoConfirmado>(registrador.Estado);
        Assert.False(registrador.PuedeEnviar); // no se admite otro envío con datos nuevos: podría estar ya registrado
        api.RespuestasReporte.Clear();
        await registrador.ReintentarAsync();

        Assert.IsType<EstadoReporte.Registrado>(registrador.Estado);
        Assert.Equal(4, api.ReportesEnviados.Count);
        Assert.Single(api.ReportesEnviados.Distinct());
    }

    [Fact]
    public async Task CA08_UnDobleToque_NoEnviaDosReportes()
    {
        var respuesta = new TaskCompletionSource<ResultadoApi<RespuestaReporte>>();
        api.RespuestasReporte.Enqueue(_ => respuesta.Task);
        var registrador = Registrador();

        var primero = registrador.EnviarAsync(Validos());
        var segundo = registrador.EnviarAsync(Validos());
        Assert.True(segundo.IsCompleted);
        respuesta.SetResult(ApiFalsa.ReporteRegistrado(api.ReportesEnviados.First()));
        await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(api.ReportesEnviados);
    }

    // En el teléfono, la sesión se lee de SecureStorage de forma asíncrona: un segundo toque llega mientras tanto.
    private sealed class SesionesLentas(TaskCompletionSource liberar) : IAlmacenSesion
    {
        public Task GuardarAsync(SesionApp sesion) => Task.CompletedTask;
        public async Task<SesionApp?> LeerAsync()
        {
            await liberar.Task;
            return new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8));
        }
        public void Cerrar() { }
    }

    [Fact]
    public async Task CA08_UnDobleToqueMientrasSeLeeLaSesion_NoEnviaDosReportes()
    {
        var liberar = new TaskCompletionSource();
        var registrador = new RegistradorReporte(api, new SesionesLentas(liberar), reloj) { EsperasEntreIntentos = [] };

        var primero = registrador.EnviarAsync(Validos());
        var segundo = registrador.EnviarAsync(Validos());
        liberar.SetResult();
        await Task.WhenAll(primero, segundo).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(api.ReportesEnviados);
    }

    [Fact]
    public async Task CA08_UnaVezRegistrado_NoSePuedeEnviarOtraVez()
    {
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());
        await registrador.EnviarAsync(Validos());

        Assert.Single(api.ReportesEnviados);
    }

    [Fact]
    public async Task CA02_UnRechazoDelServidor_NoSeReintentaYTraeElMotivoPorCampo()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaReporte>>(new ResultadoApi<RespuestaReporte>.Rechazado(400, "El reporte no es válido.",
            new Dictionary<string, string[]> { ["ocurridoAproximadamenteEn"] = ["La fecha y hora del hecho no pueden ser futuras."] })));
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());

        Assert.Single(api.ReportesEnviados);
        Assert.Contains("ocurridoAproximadamenteEn", Assert.IsType<EstadoReporte.Rechazado>(registrador.Estado).Errores.Keys);
        Assert.True(registrador.PuedeEnviar);
    }

    [Fact]
    public async Task SesionVencida_NoEnviaElReporte()
    {
        sesiones.Sesion = new("token-ficticio", "Ciudadano", Ahora.AddMinutes(-1));
        var registrador = Registrador();

        await registrador.EnviarAsync(Validos());

        Assert.Empty(api.ReportesEnviados);
        Assert.Equal(401, Assert.IsType<EstadoReporte.Rechazado>(registrador.Estado).Estado);
    }

    // ----- Formulario -----

    [Fact]
    public void CA05_ElFormulario_AdvierteQueAntePeligroInmediatoSeUsaSos()
    {
        var vm = VistaModelo();

        Assert.Contains("peligro inmediato", vm.TextoAdvertenciaSos);
        Assert.Contains("SOS", vm.TextoAdvertenciaSos);
    }

    [Fact]
    public async Task CA05_IrAlSos_VuelveALaPantallaDelSos()
    {
        var vm = VistaModelo();

        await vm.IrAlSos.EjecutarAsync();

        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task CA01_DP07_ElFormulario_CargaElCatalogoDeTipos()
    {
        var vm = VistaModelo();

        await vm.AlAparecerAsync();

        Assert.Equal(["Robo", "Otros"], vm.Categorias.Select(c => c.Nombre));
        Assert.Equal(1, api.ConsultasCategoriasReporte);
    }

    [Fact]
    public async Task DP07_TocarUnaCasilla_EligeElTipoYPasaAlFormularioConLaIndicacionDelTipo()
    {
        api.Categorias = [new(Robo, "Robo o hurto", "Me robaron, intento de robo", "robo"), new(Guid.NewGuid(), "Otros", "Solo si nada de lo anterior encaja", "otros")];
        var vm = VistaModelo();
        await vm.AlAparecerAsync();
        Assert.Null(vm.Categoria); // nada elegido por defecto
        Assert.True(vm.EligiendoTipo); // paso 1: el mosaico
        Assert.Equal("¿Qué quieres reportar?", vm.TituloPaso);

        await vm.ElegirCategoria.EjecutarAsync(vm.Opciones[0]);

        Assert.Equal(Robo, vm.Categoria!.Id);
        Assert.True(vm.EnFormulario); // paso 2, con el tipo a la vista
        Assert.Equal(("Robo o hurto", "robo"), (vm.NombreCategoria, vm.IconoCategoria));
        Assert.Equal([true, false], vm.Opciones.Select(o => o.Seleccionada));
        Assert.Equal("Robo o hurto, elegido", vm.Opciones[0].DescripcionAccesible);
        Assert.Equal("Cuéntanos qué pasó. Por ejemplo: me robaron, intento de robo…", vm.IndicacionDescripcion);

        await vm.CambiarTipo.EjecutarAsync();
        await vm.ElegirCategoria.EjecutarAsync(vm.Opciones[1]);

        Assert.Equal([false, true], vm.Opciones.Select(o => o.Seleccionada));
        Assert.Equal("Describe lo ocurrido", vm.IndicacionDescripcion); // los ejemplos de «Otros» no orientan
    }

    [Fact]
    public async Task VolverDesdeElFormulario_RegresaAlMosaicoSinPerderLoEscrito()
    {
        var vm = await FormularioCompletoAsync();
        Assert.True(vm.EnFormulario);

        Assert.True(vm.VolverAlMosaico()); // botón «atrás» del sistema
        Assert.True(vm.EligiendoTipo);
        Assert.Equal("Robo de una bicicleta", vm.Descripcion);
        Assert.Equal("Esquina del mercado", vm.Referencia);
        Assert.False(vm.VolverAlMosaico()); // en el mosaico, «atrás» sale de la página

        await vm.Volver.EjecutarAsync();
        Assert.Equal(Rutas.Ciudadano, Assert.Single(navegacion.Rutas));
    }

    [Fact]
    public async Task SinCategoriaDelServidor_SinIcono_UsaElGenerico()
    {
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        Assert.Equal("otros", vm.Opciones[1].Icono); // cliente anterior al catálogo ampliado: sin clave de icono
        Assert.Equal("otros", vm.IconoCategoria);
    }

    // ----- Dictado -----

    [Fact]
    public async Task Dictar_AnadeElTextoReconocidoALoEscrito()
    {
        var dictado = new DictadoFalso { Resultado = () => "  fue frente al mercado  " };
        var vm = new ReporteVistaModelo(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima, dictado);
        vm.Descripcion = "Me quitaron el celular.";

        await vm.Dictar.EjecutarAsync();

        Assert.True(vm.PuedeDictar);
        Assert.Equal("Me quitaron el celular. fue frente al mercado", vm.Descripcion);

        vm.Descripcion = "";
        dictado.Resultado = () => "Un grupo bebiendo en el parque";
        await vm.Dictar.EjecutarAsync();
        Assert.Equal("Un grupo bebiendo en el parque", vm.Descripcion);
    }

    [Fact]
    public async Task Dictar_RespetaElMaximoDeLaDescripcion()
    {
        var dictado = new DictadoFalso { Resultado = () => new string('b', 50) };
        var vm = new ReporteVistaModelo(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima, dictado);
        vm.Descripcion = new string('a', RegistradorReporte.MaximoDescripcion - 10);

        await vm.Dictar.EjecutarAsync();

        Assert.Equal(RegistradorReporte.MaximoDescripcion, vm.Descripcion.Length);
    }

    [Fact]
    public async Task Dictar_CanceladoOFallido_NoCambiaLoEscrito()
    {
        var dictado = new DictadoFalso { Resultado = () => null };
        var vm = new ReporteVistaModelo(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima, dictado);
        vm.Descripcion = "Texto previo";

        await vm.Dictar.EjecutarAsync(); // el ciudadano cerró el dictado
        Assert.Equal("Texto previo", vm.Descripcion);
        Assert.False(vm.HayMensajeDictado);

        dictado.Resultado = () => throw new InvalidOperationException("sin reconocedor");
        await vm.Dictar.EjecutarAsync();
        Assert.Equal("Texto previo", vm.Descripcion);
        Assert.Equal("No se pudo usar el dictado. Escribe lo ocurrido.", vm.MensajeDictado);
    }

    [Fact]
    public async Task SinReconocedorDeVoz_ElDictadoNoSeOfrece()
    {
        var dictado = new DictadoFalso { Disponible = false };
        var vm = new ReporteVistaModelo(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima, dictado);

        Assert.False(vm.PuedeDictar);
        Assert.False(vm.Dictar.CanExecute(null));
        await vm.Dictar.EjecutarAsync();
        Assert.Equal(0, dictado.Llamadas);
        Assert.False(VistaModelo().PuedeDictar); // sin servicio de dictado (pruebas, otras plataformas)
    }

    [Fact]
    public async Task DP07_RecargarElCatalogo_ConservaElTipoElegido()
    {
        var vm = VistaModelo();
        await vm.AlAparecerAsync();
        await vm.ElegirCategoria.EjecutarAsync(vm.Opciones[0]);

        api.Categorias = [new(Robo, "Robo o hurto"), new(Guid.NewGuid(), "Otros")];
        await vm.CargarCategorias.EjecutarAsync();

        Assert.Equal("Robo o hurto", vm.Categoria!.Nombre);
        Assert.True(vm.Opciones[0].Seleccionada);
    }

    [Fact]
    public async Task CA01_SinRespuestaAlCargarLosTipos_SeAvisaYSePuedeReintentar()
    {
        api.RespuestaCategoriasReporte = () => new ResultadoApi<List<CategoriaCatalogo>>.SinRespuesta("sin red");
        var vm = VistaModelo();

        await vm.AlAparecerAsync();
        Assert.True(vm.HayMensajeCategorias);
        api.RespuestaCategoriasReporte = null;
        await vm.CargarCategorias.EjecutarAsync();

        Assert.False(vm.HayMensajeCategorias);
        Assert.Equal(2, vm.Categorias.Count);
    }

    [Fact]
    public void DP08_LaFechaYHoraPorDefecto_SonLasActualesEnLaHoraLocal()
    {
        var vm = VistaModelo();

        Assert.Equal(new DateTime(2026, 10, 14), vm.Fecha);
        Assert.Equal(new TimeSpan(18, 30, 0), vm.Hora);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 18, 30, 0, TimeSpan.FromHours(-5)), vm.Datos().OcurridoEn);
    }

    [Fact]
    public async Task CA03_PorReferencia_SeEnviaSinCoordenadas()
    {
        var vm = await FormularioCompletoAsync();

        await vm.Enviar.EjecutarAsync();

        var lugar = Assert.Single(api.ReportesEnviados).Ubicacion!;
        Assert.Equal(FuentesUbicacionHecho.Texto, lugar.Fuente);
        Assert.Null(lugar.Latitud);
        Assert.Null(lugar.Longitud);
        Assert.Equal("Esquina del mercado", lugar.Referencia);
        Assert.True(vm.Registrado);
        Assert.False(vm.Editable);
    }

    // 24/10/2026: tras registrarlo, la app vuelve sola al inicio después de mostrar la confirmación.
    [Fact]
    public async Task TrasRegistrar_VuelveSolaAlInicio_UnaSolaVez()
    {
        var vm = await FormularioCompletoAsync(new(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima) { EsperaTrasRegistro = TimeSpan.FromMilliseconds(50) });

        await vm.Enviar.EjecutarAsync();
        Assert.Contains("Volviendo al inicio", vm.TextoEstado);
        Assert.Empty(navegacion.Rutas); // primero se ve la confirmación

        var limite = DateTime.UtcNow.AddSeconds(5);
        while (navegacion.Rutas.Count == 0 && DateTime.UtcNow < limite) await Task.Delay(10);
        await vm.IrAlSos.EjecutarAsync(); // tocar «Volver al inicio» a la vez no navega dos veces
        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task TrasRegistrar_ElBotonVuelveAlInstante_YLaEsperaNoNavegaOtraVez()
    {
        var vm = await FormularioCompletoAsync(new(api, Registrador(), ubicacion, sesiones, navegacion, reloj, Lima) { EsperaTrasRegistro = TimeSpan.FromMilliseconds(50) });
        await vm.Enviar.EjecutarAsync();

        await vm.IrAlSos.EjecutarAsync();
        await Task.Delay(200);

        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task CA03_PorMapa_SeEnviaElPuntoTocado()
    {
        var vm = await FormularioCompletoAsync();
        await vm.ElegirMapa.EjecutarAsync();
        vm.ElegirPuntoEnMapa(-12.1, -77.02);

        await vm.Enviar.EjecutarAsync();

        var lugar = Assert.Single(api.ReportesEnviados).Ubicacion!;
        Assert.Equal(FuentesUbicacionHecho.Mapa, lugar.Fuente);
        Assert.Equal(-12.1, lugar.Latitud);
        Assert.Equal(-77.02, lugar.Longitud);
        Assert.Null(lugar.PrecisionMetros);
    }

    [Fact]
    public async Task CA03_ElMapaSinPunto_SeEnviaComoReferencia_YSinReferenciaNoSeEnvia()
    {
        var vm = await FormularioCompletoAsync();
        await vm.ElegirMapa.EjecutarAsync();
        vm.Referencia = "";

        await vm.Enviar.EjecutarAsync();

        Assert.Empty(api.ReportesEnviados);
        Assert.Equal("Marca el lugar en el mapa o escribe una referencia.", vm.ErrorUbicacion);

        vm.Referencia = "Esquina del mercado";
        await vm.Enviar.EjecutarAsync();
        Assert.Equal(FuentesUbicacionHecho.Texto, Assert.Single(api.ReportesEnviados).Ubicacion!.Fuente);
    }

    [Fact]
    public async Task CA04_SinElegirLaPosicionActual_NuncaSeLeeLaPosicionDelDispositivo()
    {
        var vm = await FormularioCompletoAsync();
        await vm.ElegirMapa.EjecutarAsync();
        vm.ElegirPuntoEnMapa(-12.1, -77.02);
        await vm.QuitarPunto.EjecutarAsync();

        await vm.Enviar.EjecutarAsync();

        Assert.Empty(ubicacion.Lecturas);
        Assert.Equal(0, ubicacion.Solicitudes);
        Assert.Null(Assert.Single(api.ReportesEnviados).Ubicacion!.Latitud);
    }

    [Fact]
    public async Task CA04_AlElegirLaPosicionActual_SeUsaComoUbicacionDelHecho()
    {
        var vm = await FormularioCompletoAsync();
        (double, double)? punto = null;
        vm.PuntoCambiado += (lat, lon) => punto = (lat, lon);

        await vm.UsarPosicionActual.EjecutarAsync();
        await vm.Enviar.EjecutarAsync();

        Assert.Single(ubicacion.Lecturas);
        Assert.Equal((-12.07, -77.04), punto);
        var lugar = Assert.Single(api.ReportesEnviados).Ubicacion!;
        Assert.Equal(FuentesUbicacionHecho.PosicionActual, lugar.Fuente);
        Assert.Equal(-12.07, lugar.Latitud);
        Assert.Equal(9.5, lugar.PrecisionMetros);
    }

    [Fact]
    public async Task CA04_SiMueveElPuntoTrasUsarSuPosicion_SeEnviaComoPuntoDelMapa()
    {
        var vm = await FormularioCompletoAsync();
        await vm.UsarPosicionActual.EjecutarAsync();

        vm.ElegirPuntoEnMapa(-12.08, -77.05);
        await vm.Enviar.EjecutarAsync();

        var lugar = Assert.Single(api.ReportesEnviados).Ubicacion!;
        Assert.Equal(FuentesUbicacionHecho.Mapa, lugar.Fuente);
        Assert.Equal(-12.08, lugar.Latitud);
        Assert.Null(lugar.PrecisionMetros);
    }

    [Fact]
    public async Task CA04_SiPasaAReferenciaTrasUsarSuPosicion_LaPosicionNoSeEnvia()
    {
        var vm = await FormularioCompletoAsync();
        await vm.UsarPosicionActual.EjecutarAsync();

        await vm.QuitarPunto.EjecutarAsync();
        vm.Referencia = "Frente al parque";
        await vm.Enviar.EjecutarAsync();

        var lugar = Assert.Single(api.ReportesEnviados).Ubicacion!;
        Assert.Equal(FuentesUbicacionHecho.Texto, lugar.Fuente);
        Assert.Null(lugar.Latitud);
        Assert.Null(lugar.PrecisionMetros);
    }

    [Fact]
    public async Task CA04_SiPasaAlMapaTrasUsarSuPosicion_LaPosicionSeDescartaHastaQueToqueElMapa()
    {
        var vm = await FormularioCompletoAsync();
        await vm.UsarPosicionActual.EjecutarAsync();

        await vm.ElegirMapa.EjecutarAsync();

        Assert.Null(vm.Latitud);
        // Con el mapa abierto sin tocarlo no hay punto: el lugar es la referencia, sin la posición anterior.
        Assert.Equal((FuentesUbicacionHecho.Texto, (double?)null), (vm.Datos().Ubicacion!.Fuente, vm.Datos().Ubicacion!.Latitud));
    }

    [Fact]
    public async Task LaReferencia_EstaSiempreAMano_YEsObligatoriaSoloSinPunto()
    {
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        Assert.Equal(ModoUbicacionHecho.Texto, vm.Modo);
        Assert.False(vm.HayPunto);
        Assert.Equal("Referencia del lugar", vm.EtiquetaReferencia);

        await vm.ElegirMapa.EjecutarAsync();
        Assert.Equal("Cerrar el mapa", vm.TextoQuitarPunto);
        vm.ElegirPuntoEnMapa(-16.3988, -71.5369);
        Assert.Equal("Referencia (opcional)", vm.EtiquetaReferencia);
        Assert.Equal("Quitar el punto", vm.TextoQuitarPunto);

        await vm.QuitarPunto.EjecutarAsync();
        Assert.False(vm.HayPunto);
        Assert.False(vm.MostrarMapa);
        Assert.Equal("Referencia del lugar", vm.EtiquetaReferencia);
    }

    [Theory]
    [InlineData(EstadoPermisoUbicacion.Denegado)]
    [InlineData(EstadoPermisoUbicacion.Restringido)]
    public async Task CA04_SinPermiso_NoSeLeeLaPosicionYSeOfrecenLasOtrasFormas(EstadoPermisoUbicacion permiso)
    {
        ubicacion.Permiso = permiso;
        var vm = await FormularioCompletoAsync();

        await vm.UsarPosicionActual.EjecutarAsync();

        Assert.Empty(ubicacion.Lecturas);
        Assert.True(vm.HayMensajeUbicacion);
        Assert.Equal(ModoUbicacionHecho.Texto, vm.Modo); // se mantiene lo que había elegido
    }

    [Fact]
    public async Task CA04_ConElPermisoSinDecidir_SePideAlElegirLaPosicion()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        var vm = await FormularioCompletoAsync();

        await vm.UsarPosicionActual.EjecutarAsync();

        Assert.Equal(1, ubicacion.Solicitudes);
        Assert.Equal(ModoUbicacionHecho.PosicionActual, vm.Modo);
    }

    [Fact]
    public async Task CA04_SiNoSeObtieneLaPosicion_SeAvisaYNoSeUsaNinguna()
    {
        ubicacion.Posicion = () => null;
        var vm = await FormularioCompletoAsync();

        await vm.UsarPosicionActual.EjecutarAsync();

        Assert.True(vm.HayMensajeUbicacion);
        Assert.Null(vm.Latitud);
        Assert.Equal(ModoUbicacionHecho.Texto, vm.Modo);
    }

    [Fact]
    public async Task CA02_UnaFechaFutura_SeMarcaYNoSeEnvia()
    {
        var vm = await FormularioCompletoAsync();
        vm.Hora = new TimeSpan(19, 0, 0); // 30 minutos después de «ahora»

        await vm.Enviar.EjecutarAsync();

        Assert.Empty(api.ReportesEnviados);
        Assert.True(vm.HayErrorFecha);
        Assert.Contains("futuras", vm.ErrorFecha);
    }

    [Fact]
    public async Task CA02_SinTipoNiDescripcion_SeMarcanAmbosCampos()
    {
        var vm = VistaModelo();
        await vm.AlAparecerAsync();
        await vm.QuitarPunto.EjecutarAsync();
        vm.Referencia = "Esquina";

        await vm.Enviar.EjecutarAsync();

        Assert.Empty(api.ReportesEnviados);
        Assert.True(vm.HayErrorCategoria);
        Assert.True(vm.HayErrorDescripcion);
        Assert.False(vm.HayErrorUbicacion);
        Assert.True(vm.Editable);
    }

    [Fact]
    public async Task CA02_LosErroresDelServidor_SeMuestranEnSuCampo()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaReporte>>(new ResultadoApi<RespuestaReporte>.Rechazado(400, "El reporte no es válido.",
            new Dictionary<string, string[]> { ["ubicacion.referencia"] = ["La referencia admite hasta 500 caracteres."] })));
        var vm = await FormularioCompletoAsync();

        await vm.Enviar.EjecutarAsync();

        Assert.Equal("La referencia admite hasta 500 caracteres.", vm.ErrorUbicacion);
    }

    [Fact]
    public async Task CA08_SinRespuesta_SeOfreceReintentarElMismoReporteYNoEditarlo()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult(SinRespuesta()));
        var vm = await FormularioCompletoAsync();

        await vm.Enviar.EjecutarAsync();

        Assert.True(vm.PuedeReintentar);
        Assert.False(vm.Editable);
        Assert.False(vm.Registrado);
        Assert.Contains("no se duplicará", vm.TextoEstado);
    }

    [Fact]
    public async Task CA01_AlRegistrarse_SeMuestraLaConfirmacionConLaHoraLocal()
    {
        api.RespuestasReporte.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaReporte>>(
            new ResultadoApi<RespuestaReporte>.Correcto(new RespuestaReporte(Guid.NewGuid(), "Recibido", Ahora))));
        var vm = await FormularioCompletoAsync();

        await vm.Enviar.EjecutarAsync();

        Assert.Equal("exito", vm.TipoAviso);
        Assert.Contains("Reporte registrado a las 18:30 (estado: Recibido)", vm.TextoEstado);
    }

    [Fact]
    public async Task CA01_DesdeElInicio_SeAbreElFormularioDeReporte()
    {
        var inicio = new InicioCiudadanoVistaModelo(new EmisorSos(api, ubicacion, sesiones, reloj), new PreparacionUbicacion(ubicacion),
            sesiones, navegacion, () => { });

        await inicio.Reportar.EjecutarAsync();

        Assert.Equal([Rutas.Reporte], navegacion.Rutas);
        Assert.Empty(ubicacion.Lecturas); // abrir el formulario no lee la posición
    }
}
