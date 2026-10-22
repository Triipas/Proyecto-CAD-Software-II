using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// Personal (22/10/2026): el sereno que entra con la contraseña provisional del administrador la cambia antes de operar.
public class CambioContrasenaTests
{
    private sealed class ServidorLocal : IConfiguracionServidor
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
    private readonly IngresoPendiente pendiente = new();

    private IngresoVistaModelo Ingreso() =>
        new(api, sesiones, new ServidorLocal(), navegacion, pendiente: pendiente) { Correo = "sereno@correo.test", Contrasena = "ClaveInicial2026" };

    private CambiarContrasenaVistaModelo Cambio() => new(api, sesiones, navegacion, pendiente);

    private static ResultadoApi<RespuestaToken> Provisional() =>
        new ResultadoApi<RespuestaToken>.Correcto(new RespuestaToken("token-provisional", DateTimeOffset.UtcNow.AddHours(8), "Sereno", DebeCambiarContrasena: true));

    [Fact]
    public async Task ConContrasenaProvisional_VaAlCambio_SinGuardarLaSesion()
    {
        api.RespuestaIngreso = Provisional();

        await Ingreso().Ingresar.EjecutarAsync();

        Assert.Equal([Rutas.CambiarContrasena], navegacion.Rutas);
        Assert.Null(sesiones.Sesion); // cerrar la app a mitad obliga a ingresar de nuevo
        Assert.Equal("token-provisional", pendiente.Token!.Token);
    }

    [Fact]
    public async Task SinContrasenaProvisional_EntraComoSiempre()
    {
        api.RespuestaIngreso = new ResultadoApi<RespuestaToken>.Correcto(new RespuestaToken("token", DateTimeOffset.UtcNow.AddHours(8), "Sereno"));

        await Ingreso().Ingresar.EjecutarAsync();

        Assert.Equal([Rutas.Sereno], navegacion.Rutas);
        Assert.Equal("token", sesiones.Sesion!.Token);
    }

    [Fact]
    public async Task AlCambiarla_GuardaElTokenNuevoYEntraAlModoSereno()
    {
        api.RespuestaIngreso = Provisional();
        await Ingreso().Ingresar.EjecutarAsync();
        var vm = Cambio();
        (vm.Actual, vm.Nueva, vm.Confirmacion) = ("ClaveInicial2026", "NuevaClave2026", "NuevaClave2026");

        await vm.Guardar.EjecutarAsync();

        var (token, solicitud) = Assert.Single(api.CambiosContrasena);
        Assert.Equal("token-provisional", token);
        Assert.Equal(new SolicitudCambioContrasena("ClaveInicial2026", "NuevaClave2026", "NuevaClave2026"), solicitud);
        Assert.Equal("token-nuevo", sesiones.Sesion!.Token);
        Assert.Equal(Rutas.Sereno, navegacion.Rutas[^1]);
        Assert.Null(pendiente.Token);
        Assert.Equal("", vm.Nueva); // las contraseñas no quedan en memoria
    }

    [Fact]
    public async Task LosMotivosDelServidor_SeMuestranEnSuCampo()
    {
        pendiente.Token = (Provisional() as ResultadoApi<RespuestaToken>.Correcto)!.Valor;
        api.RespuestaCambioContrasena = new ResultadoApi<RespuestaToken>.Rechazado(400, "No se cambió la contraseña.",
            new Dictionary<string, string[]> { ["actual"] = ["La contraseña actual no es correcta."], ["confirmacion"] = ["Las contraseñas no coinciden."] });
        var vm = Cambio();

        await vm.Guardar.EjecutarAsync();

        Assert.Equal("La contraseña actual no es correcta.", vm.ErrorActual);
        Assert.Equal("Las contraseñas no coinciden.", vm.ErrorConfirmacion);
        Assert.False(vm.HayErrorNueva);
        Assert.Null(sesiones.Sesion);
        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task SinIngresoPendiente_PideVolverAIngresar_YSalirVuelveALaBienvenida()
    {
        var vm = Cambio();

        await vm.Guardar.EjecutarAsync();
        Assert.Equal("Vuelve a ingresar con tu correo y tu contraseña.", vm.Mensaje);
        Assert.Empty(api.CambiosContrasena);

        await vm.Salir.EjecutarAsync();
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }
}
