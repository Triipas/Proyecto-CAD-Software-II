using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

public class IngresoVistaModeloTests
{
    private sealed class ServidorConfigurable : IConfiguracionServidor
    {
        public Uri? Direccion { get; private set; } = new("http://10.0.2.2:5220/");
        public string? Guardar(string direccion)
        {
            var error = ValidacionDireccion.Validar(direccion, out var uri);
            if (error is null) Direccion = uri;
            return error;
        }
    }

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = null };
    private readonly NavegacionFalsa navegacion = new();

    private IngresoVistaModelo Crear() => new(api, sesiones, new ServidorConfigurable(), navegacion)
    {
        Correo = "sereno@ejemplo.test",
        Contrasena = "Clave-ficticia-1",
    };

    [Theory]
    [InlineData("Ciudadano", Rutas.Ciudadano)]
    [InlineData("Sereno", Rutas.Sereno)]
    public async Task HU02_CadaRolEntraEnSuModo_YLaSesionSeGuarda(string rol, string ruta)
    {
        api.RespuestaIngreso = new ResultadoApi<RespuestaToken>.Correcto(new RespuestaToken("t", DateTimeOffset.UtcNow.AddHours(8), rol));
        var vistaModelo = Crear();

        await vistaModelo.Ingresar.EjecutarAsync();

        Assert.Equal([ruta], navegacion.Rutas);
        Assert.Equal(rol, sesiones.Sesion!.Rol);
        Assert.Equal("", vistaModelo.Contrasena); // la contraseña no queda en memoria de la pantalla
    }

    [Fact]
    public async Task HU02_Rechazo_MuestraElMensajeDelServidorYNoNavega()
    {
        api.RespuestaIngreso = new ResultadoApi<RespuestaToken>.Rechazado(401, "Correo o contraseña incorrectos.", new Dictionary<string, string[]>());
        var vistaModelo = Crear();

        await vistaModelo.Ingresar.EjecutarAsync();

        Assert.Equal("Correo o contraseña incorrectos.", vistaModelo.Mensaje);
        Assert.Empty(navegacion.Rutas);
        Assert.Null(sesiones.Sesion);
    }

    [Fact]
    public async Task RNF09_DireccionSinHttps_NoSeLlamaALaApi()
    {
        var vistaModelo = Crear();
        vistaModelo.DireccionServidor = "http://cad.ejemplo.test";

        await vistaModelo.Ingresar.EjecutarAsync();

        Assert.Equal(0, api.Ingresos);
        Assert.StartsWith("La dirección debe empezar por https://", vistaModelo.Mensaje);
    }
}

public class DiagnosticoDesdeIngresoTests
{
    [Fact]
    public async Task SPIKE01_IrAlDiagnostico_ValidaYGuardaLaDireccionAntes()
    {
        var navegacion = new NavegacionFalsa();
        var vistaModelo = new IngresoVistaModelo(new ApiFalsa(), new SesionesFalsas(), new ServidorConfigurable(), navegacion)
        {
            DireccionServidor = "http://cad.ejemplo.test",
        };

        await vistaModelo.IrADiagnostico.EjecutarAsync();
        Assert.Empty(navegacion.Rutas);
        Assert.StartsWith("La dirección debe empezar por https://", vistaModelo.Mensaje);

        vistaModelo.DireccionServidor = "http://localhost:5220";
        await vistaModelo.IrADiagnostico.EjecutarAsync();
        Assert.Equal([Rutas.Diagnostico], navegacion.Rutas);
    }

    private sealed class ServidorConfigurable : IConfiguracionServidor
    {
        public Uri? Direccion { get; private set; }
        public string? Guardar(string direccion)
        {
            var error = ValidacionDireccion.Validar(direccion, out var uri);
            if (error is null) Direccion = uri;
            return error;
        }
    }
}

public class InicioCiudadanoVistaModeloTests
{
    private readonly UbicacionFalsa ubicacion = new();
    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new();
    private readonly NavegacionFalsa navegacion = new();
    private int ajustesAbiertos;

    private InicioCiudadanoVistaModelo Crear() =>
        new(new EmisorSos(api, ubicacion, sesiones, TimeProvider.System) { EsperasEntreIntentos = [TimeSpan.Zero, TimeSpan.Zero] },
            new PreparacionUbicacion(ubicacion), sesiones, navegacion, () => ajustesAbiertos++);

    [Fact]
    public async Task HU07_MientrasSeEnvia_ElBotonQuedaDeshabilitado_YSoloConfirmaConLaRespuesta()
    {
        var respuesta = new TaskCompletionSource<ResultadoApi<RespuestaSos>>();
        api.RespuestasSos.Enqueue(_ => respuesta.Task);
        var vistaModelo = Crear();

        var envio = vistaModelo.EmitirSos.EjecutarAsync();
        Assert.False(vistaModelo.EmitirSos.CanExecute(null));
        Assert.True(vistaModelo.SosEnCurso);
        Assert.Equal("Enviando…", vistaModelo.TextoBotonSos);
        Assert.NotEqual("exito", vistaModelo.TipoAvisoSos);

        respuesta.SetResult(ApiFalsa.Registrado(api.SosEnviados.Single()));
        await envio.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vistaModelo.EmitirSos.CanExecute(null));
        Assert.Equal("exito", vistaModelo.TipoAvisoSos);
        Assert.StartsWith("SOS registrado", vistaModelo.TextoSos);
    }

    [Fact]
    public async Task HU07_CA04_SinRespuesta_OfreceReintentarElMismoSos()
    {
        api.RespuestasSos.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaSos>>(new ResultadoApi<RespuestaSos>.SinRespuesta("sin red")));
        var vistaModelo = Crear();

        await vistaModelo.EmitirSos.EjecutarAsync();

        Assert.Equal("error", vistaModelo.TipoAvisoSos);
        Assert.True(vistaModelo.PuedeReintentar);
        Assert.True(vistaModelo.Reintentar.CanExecute(null));
        Assert.DoesNotContain("registrado", vistaModelo.TextoSos);
    }

    [Fact]
    public async Task SesionVencida_OfreceVolverAIngresar()
    {
        sesiones.Sesion = new SesionApp("t", "Ciudadano", DateTimeOffset.UtcNow.AddMinutes(-1));
        var vistaModelo = Crear();

        await vistaModelo.EmitirSos.EjecutarAsync();
        Assert.True(vistaModelo.SesionTerminada);

        await vistaModelo.Salir.EjecutarAsync();
        Assert.Null(sesiones.Sesion);
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }

    [Fact]
    public async Task HU06_CA01_PermisoPendiente_MuestraLaExplicacionAntesDePedirlo()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Desconocido;
        ubicacion.Posicion = () => new UbicacionSos(-12.05, -77.04, 10, DateTimeOffset.UtcNow);
        var vistaModelo = Crear();

        await vistaModelo.AlAparecerAsync();
        Assert.True(vistaModelo.MostrarExplicacion);
        Assert.Equal(0, ubicacion.Solicitudes);

        await vistaModelo.PermitirUbicacion.EjecutarAsync();
        Assert.False(vistaModelo.MostrarExplicacion);
        Assert.Equal("exito", vistaModelo.TipoAvisoUbicacion);
    }

    [Fact]
    public async Task HU06_CA03_PermisoDenegado_OrientaALosAjustes()
    {
        ubicacion.Permiso = EstadoPermisoUbicacion.Denegado;
        var vistaModelo = Crear();

        await vistaModelo.AlAparecerAsync();
        Assert.True(vistaModelo.MostrarAjustes);
        Assert.Equal("advertencia", vistaModelo.TipoAvisoUbicacion);

        await vistaModelo.AbrirAjustes.EjecutarAsync();
        Assert.Equal(1, ajustesAbiertos);
    }
}
