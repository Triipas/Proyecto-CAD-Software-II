using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Calles;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// Despacho del sereno con mapa (diseño C, 22/10/2026): punto del caso, distancia, etapas y «Cómo llegar». Datos ficticios.
public class DespachoMapaTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 22, 3, 9, 0, TimeSpan.Zero);
    private readonly ApiFalsa api = new();
    private readonly UbicacionFalsa ubicacion = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };
    private readonly List<Uri> enlaces = [];
    private bool abreEnlaces = true;

    private static DespachoSereno Sos(UbicacionDespacho? ubicacion) =>
        new(Guid.NewGuid(), "SOS", DespachoSereno.EstadoAsignada, Inicio, Inicio.AddMinutes(1), ubicacion, []);

    private InicioSerenoVistaModelo VistaModelo(IServicioRutas? rutas = null) =>
        new(new ServicioTurnoSereno(api, ubicacion, sesiones, new FakeTimeProvider(Inicio)),
            new ServicioDespacho(api, sesiones, new CanalFalso(), TimeProvider.System),
            new ServicioNotificaciones(api, sesiones, new ServicioPushNoDisponible(), TimeProvider.System), sesiones, new NavegacionFalsa(), () => { },
            enlace => { enlaces.Add(enlace); return Task.FromResult(abreEnlaces); }, rutas)
        { HiloPrincipal = a => { a(); return Task.CompletedTask; } };

    private static async Task Esperar(Func<bool> condicion, string descripcion)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicion())
        {
            if (DateTime.UtcNow > limite) throw new TimeoutException($"No se cumplió: {descripcion}");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task UnSos_MarcaElTelefonoDelCiudadano_YLaDistanciaDesdeElSereno()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Sos(new UbicacionDespacho(-12.0464, -77.0428, 12, Inicio)));
        api.EstadoServidor = new RespuestaEstadoOperativo(true, Inicio, 15); // ya operativo: reanuda el envío de su posición
        ubicacion.Posicion = () => new UbicacionSos(-12.0500, -77.0428, 8, Inicio);
        var vm = VistaModelo();

        await vm.AlAparecerAsync();
        await Esperar(() => vm.HayDistancia, "la posición propia llega al mapa");

        Assert.Equal(new PuntoMapa(-12.0464, -77.0428, 12, PuntoMapa.Sos), vm.Destino);
        Assert.Equal("Ubicación del teléfono del ciudadano", vm.TituloUbicacion);
        Assert.StartsWith("± 12 m · obtenida a las ", vm.DetalleUbicacion);
        Assert.Equal("400 m", vm.TextoDistancia); // 0,0036° de latitud ≈ 400 m
        Assert.Equal(-12.0500, vm.PosicionPropia!.Latitud);
        Assert.True(vm.SinModoDespacho is false && vm.PuedeActuar);
    }

    [Fact]
    public async Task UnReporte_MarcaElLugarDelHecho_YSinPunto_NoInventaCoordenadas()
    {
        var conPunto = Sos(null) with { Origen = "Reporte", LugarHecho = new LugarHechoDespacho(-12.05, -77.03, 15, null) };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(conPunto);
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        Assert.Equal(PuntoMapa.Reporte, vm.Destino!.Tipo);
        Assert.Equal("Lugar del hecho (lo marcó el ciudadano)", vm.TituloUbicacion);
        Assert.Equal("± 15 m", vm.DetalleUbicacion);

        var soloReferencia = conPunto with { LugarHecho = new LugarHechoDespacho(null, null, null, "Esquina del mercado") };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(soloReferencia);
        var otro = VistaModelo();
        await otro.AlAparecerAsync();

        Assert.Null(otro.Destino);
        Assert.True(otro.SinPuntoEnMapa);
        Assert.Equal("Lugar del hecho: solo una referencia", otro.TituloUbicacion);
        Assert.Equal("Referencia: Esquina del mercado", otro.DetalleUbicacion);
        Assert.False(otro.ComoLlegar.CanExecute(null)); // sin punto no hay destino al que llevar
        Assert.False(otro.HayDistancia);
    }

    [Fact]
    public async Task UnSosSinUbicacion_LoDiceSinMostrarDistancia()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Sos(null));
        var vm = VistaModelo();

        await vm.AlAparecerAsync();

        Assert.True(vm.SinPuntoEnMapa);
        Assert.Equal("Ubicación no disponible", vm.TituloUbicacion);
        Assert.Contains("no compartió su ubicación", vm.DetalleUbicacion);
    }

    [Fact]
    public async Task ComoLlegar_AbreGoogleMapsConElPuntoDelCaso_YAvisaSiNoPuede()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Sos(new UbicacionDespacho(-12.0464, -77.0428, 12, Inicio)));
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        await vm.ComoLlegar.EjecutarAsync();

        Assert.Equal("https://www.google.com/maps/dir/?api=1&destination=-12.0464,-77.0428", Assert.Single(enlaces).ToString());
        Assert.False(vm.HayAvisoComoLlegar);

        abreEnlaces = false;
        await vm.ComoLlegar.EjecutarAsync();
        Assert.Equal("No se pudo abrir la app de mapas del teléfono.", vm.AvisoComoLlegar);
    }

    [Fact]
    public async Task LasEtapas_MuestranLasHechasYLaActual()
    {
        var enCamino = Sos(null) with { Estado = DespachoSereno.EstadoEnCamino, EnCaminoEn = Inicio.AddMinutes(3) };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enCamino);
        var vm = VistaModelo();

        await vm.AlAparecerAsync();

        Assert.Equal(["Asignada", "En camino", "En atención", "Cierre"], vm.Etapas.Select(e => e.Nombre));
        Assert.Equal([true, true, false, false], vm.Etapas.Select(e => e.Hecha));
        Assert.Equal([false, true, false, false], vm.Etapas.Select(e => e.Actual));
        Assert.True(vm.Etapas[1].HayHora);
        Assert.EndsWith("etapa actual", vm.Etapas[1].Descripcion);
        Assert.False(vm.Etapas[2].HayHora);
    }

    [Fact]
    public async Task LoQueRegistroLaCentral_ResumeLoUltimo_YSeDespliega()
    {
        var despacho = Sos(null) with
        {
            Informacion = [new InformacionDespacho(Inicio, "Discusión entre vecinos", null), new InformacionDespacho(Inicio.AddMinutes(1), "Uno de ellos tiene un cuchillo", null)],
        };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(despacho);
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        Assert.Equal("Uno de ellos tiene un cuchillo", vm.ResumenCentral);
        Assert.False(vm.MostrarInformacion);
        await vm.VerInformacion.EjecutarAsync();
        Assert.True(vm.MostrarInformacion);
        Assert.Equal("arriba", vm.IconoInformacion);
    }

    // ----- Ruta por calles calculada en la app -----

    private async Task<InicioSerenoVistaModelo> SosConPosicionAsync(IServicioRutas rutas)
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Sos(new UbicacionDespacho(-12.0464, -77.0428, 12, Inicio)));
        api.EstadoServidor = new RespuestaEstadoOperativo(true, Inicio, 15);
        ubicacion.Posicion = () => new UbicacionSos(-12.0500, -77.0428, 8, Inicio);
        var vm = VistaModelo(rutas);
        await vm.AlAparecerAsync();
        return vm;
    }

    [Fact]
    public async Task ConPosicionYDestino_LaRutaSeCalculaEnLaApp_YLaDistanciaEsPorCalles()
    {
        var rutas = new RutasFalsas();
        var vm = await SosConPosicionAsync(rutas);
        await Esperar(() => vm.HayRuta, "la ruta llega");

        var (origen, destino) = rutas.Pedidas[0];
        Assert.Equal(new PuntoRuta(-12.0500, -77.0428), origen);
        Assert.Equal(new PuntoRuta(-12.0464, -77.0428), destino);
        Assert.Equal(3, vm.RutaMapa!.Count);
        Assert.Equal("520 m", vm.TextoDistancia);
        Assert.Equal("por calles · ~1 min", vm.EtiquetaDistancia);
        Assert.False(vm.HayEstadoRuta);
        Assert.False(vm.MostrarAbrirEnMaps);
    }

    [Fact]
    public async Task SiLaRutaNoSePuedeCalcular_SeDiceElMotivo_YQuedaElRespaldoDeLaAppDeMapas()
    {
        var rutas = new RutasFalsas { Respuesta = (_, _) => new ResultadoRuta.NoDisponible("No se pudieron descargar las calles de la zona.") };
        var vm = await SosConPosicionAsync(rutas);
        await Esperar(() => vm.HayEstadoRuta && !vm.EstadoRuta.StartsWith("Calculando"), "el motivo llega");

        Assert.Equal("No se pudieron descargar las calles de la zona.", vm.EstadoRuta);
        Assert.True(vm.MostrarAbrirEnMaps);
        Assert.Equal("400 m", vm.TextoDistancia); // en línea recta mientras no hay ruta
        Assert.Equal("en línea recta", vm.EtiquetaDistancia);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(1000, 2)]
    [InlineData(5000, 12)]
    public void ElTiempoEnVehiculo_EsOrientativo(double metros, int minutos) =>
        Assert.Equal(minutos, InicioSerenoVistaModelo.MinutosEnVehiculo(metros));

    // ----- Panel desplegable y encabezado plegable -----

    [Fact]
    public async Task ElPanel_EmpiezaRecogido_YElCierreLoDespliega_YSePuedeRetomar()
    {
        var enAtencion = Sos(null) with { Estado = DespachoSereno.EstadoEnAtencion, EnCaminoEn = Inicio.AddMinutes(2), EnAtencionEn = Inicio.AddMinutes(9) };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enAtencion);
        var vm = VistaModelo();
        await vm.AlAparecerAsync();
        Assert.False(vm.PanelExpandido);
        Assert.Equal("arriba", vm.IconoPanel);

        var avisadas = new List<string?>();
        vm.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);
        await vm.AbrirCierre.EjecutarAsync();
        Assert.True(vm.PanelExpandido);
        Assert.True(vm.MostrarFormularioCierre);
        Assert.Equal("Recoger el panel", vm.TextoAsaPanel);
        Assert.Contains(nameof(vm.TextoAsaPanel), avisadas); // el lector de pantalla anuncia el estado real del asa

        await vm.RecogerPanel.EjecutarAsync(); // el sereno quiere ver el mapa sin perder lo escrito
        Assert.False(vm.MostrarFormularioCierre);
        Assert.True(vm.PuedeContinuarCierre);
        Assert.True(vm.MostrarCierre);

        await vm.ExpandirPanel.EjecutarAsync();
        Assert.True(vm.MostrarFormularioCierre);
        Assert.False(vm.PuedeContinuarCierre);
    }

    [Fact]
    public async Task LoQueRegistroLaCentral_AlDesplegarse_DespliegaElPanel()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Sos(null));
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        await vm.VerInformacion.EjecutarAsync();

        Assert.True(vm.PanelExpandido);
        Assert.True(vm.MostrarDetalleCentral);
        await vm.AlternarPanel.EjecutarAsync();
        Assert.False(vm.MostrarDetalleCentral); // recogido no se ve el detalle
    }

    [Fact]
    public async Task ElEncabezado_ResumeElEstadoEnUnaLinea_YLasEtapasSeOcultan()
    {
        var enCamino = Sos(null) with { Estado = DespachoSereno.EstadoEnCamino, EnCaminoEn = Inicio.AddMinutes(3) };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enCamino);
        var vm = VistaModelo();
        await vm.AlAparecerAsync();

        Assert.StartsWith("En camino desde las ", vm.TextoEstadoCabecera);
        Assert.True(vm.MostrarEtapas);
        await vm.AlternarEtapas.EjecutarAsync();
        Assert.False(vm.MostrarEtapas);
        Assert.Equal("abajo", vm.IconoEtapas);
    }

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(404, "400 m")]
    [InlineData(995, "1.0 km")] // ya no son «1000 m»
    [InlineData(1234, "1.2 km")]
    [InlineData(12_560, "12.6 km")]
    public void LaDistancia_SeExpresaEnMetrosOKilometros(double metros, string texto) =>
        Assert.Equal(texto, InicioSerenoVistaModelo.FormatoDistancia(metros));

    [Fact]
    public void LaDistancia_UsaElHaversine()
    {
        // Un grado de latitud ≈ 111,2 km.
        Assert.InRange(InicioSerenoVistaModelo.DistanciaMetros(0, 0, 1, 0), 111_100, 111_300);
        Assert.Equal(0, InicioSerenoVistaModelo.DistanciaMetros(-12.0464, -77.0428, -12.0464, -77.0428), 3);
    }
}
