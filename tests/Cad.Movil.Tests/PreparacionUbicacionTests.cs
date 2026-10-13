using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU06 — Preparar la geolocalización al abrir la aplicación.
public class PreparacionUbicacionTests
{
    private readonly UbicacionFalsa ubicacion = new();
    private static UbicacionSos Posicion() => new(-12.05, -77.04, 10, DateTimeOffset.UtcNow);

    [Fact]
    public async Task CA01_PermisoPendiente_PrimeroSeExplica_YLuegoSeSolicita()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        ubicacion.Posicion = Posicion;
        var preparacion = new PreparacionUbicacion(ubicacion);

        Assert.Equal(ResultadoPreparacion.NecesitaExplicacion, await preparacion.ComprobarAlAbrirAsync());
        Assert.Equal(0, ubicacion.Solicitudes); // aún no se ha pedido: primero la explicación

        Assert.Equal(ResultadoPreparacion.Disponible, await preparacion.SolicitarTrasExplicarAsync());
        Assert.Equal(1, ubicacion.Solicitudes);
    }

    [Fact]
    public async Task CA02_PermisoConcedido_CompruebaLaPosicionSinVolverAPedirElPermiso()
    {
        ubicacion.Posicion = Posicion;

        var resultado = await new PreparacionUbicacion(ubicacion).ComprobarAlAbrirAsync();

        Assert.Equal(ResultadoPreparacion.Disponible, resultado);
        Assert.Equal(0, ubicacion.Solicitudes);
        Assert.Single(ubicacion.Lecturas);
    }

    [Fact]
    public async Task CA03_PermisoDenegado_SeOrientaAlCiudadano()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Denegado;

        Assert.Equal(ResultadoPreparacion.Denegado, await new PreparacionUbicacion(ubicacion).ComprobarAlAbrirAsync());
        Assert.Empty(ubicacion.Lecturas);
    }

    [Fact]
    public async Task CA03_DenegadoTrasLaExplicacion_SeOrientaAlCiudadano()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        ubicacion.RespuestaAlSolicitar = EstadoPermisoUbicacion.Denegado;

        Assert.Equal(ResultadoPreparacion.Denegado, await new PreparacionUbicacion(ubicacion).SolicitarTrasExplicarAsync());
    }

    [Fact]
    public async Task CA03_FalloAlObtenerLaPosicion_SeOrientaAlCiudadano()
    {
        ubicacion.Posicion = () => null;
        Assert.Equal(ResultadoPreparacion.SinPosicion, await new PreparacionUbicacion(ubicacion).ComprobarAlAbrirAsync());

        ubicacion.Falla = true;
        Assert.Equal(ResultadoPreparacion.SinPosicion, await new PreparacionUbicacion(ubicacion).ComprobarAlAbrirAsync());
    }

    [Fact]
    public void CA04_LaPreparacionNoTieneAccesoALaApi_NoPuedeTransmitirCoordenadas()
    {
        // Garantía estructural: el único colaborador es el servicio de ubicación del dispositivo.
        var parametros = typeof(PreparacionUbicacion).GetConstructors().Single().GetParameters();
        Assert.Equal(typeof(IServicioUbicacion), Assert.Single(parametros).ParameterType);
    }

    [Fact]
    public async Task CA04_AbrirLaAppConPermiso_NoEnviaNadaAlServidor()
    {
        ubicacion.Posicion = Posicion;
        var api = new ApiFalsa();
        var vistaModelo = new InicioCiudadanoVistaModelo(new EmisorSos(api, ubicacion, new SesionesFalsas(), TimeProvider.System),
            new PreparacionUbicacion(ubicacion), new SesionesFalsas(), new NavegacionFalsa(), () => { });

        await vistaModelo.AlAparecerAsync();
        await vistaModelo.AlAparecerAsync(); // una sola comprobación por apertura

        Assert.Empty(api.SosEnviados);
        Assert.Single(ubicacion.Lecturas);
        Assert.Equal("exito", vistaModelo.TipoAvisoUbicacion);
    }
}
