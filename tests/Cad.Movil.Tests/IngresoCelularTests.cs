using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// ADR-08 — ingreso del ciudadano con celular verificado y PIN: bienvenida, código, crea tu cuenta y PIN. Datos ficticios.
public class IngresoCelularTests
{
    private const string Celular = "987654321";

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = null };
    private readonly DispositivosFalsos dispositivos = new();
    private readonly NavegacionFalsa navegacion = new();
    private readonly FakeTimeProvider reloj = new(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private readonly FlujoIngresoCelular flujo;

    public IngresoCelularTests() => flujo = new FlujoIngresoCelular(api, sesiones, dispositivos, navegacion, reloj);

    private async Task EscribirPinAsync(PinVistaModelo pin, string digitos)
    {
        foreach (var d in digitos)
            await pin.Tecla.EjecutarAsync(d.ToString());
    }

    // ----- Bienvenida -----

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("812345678")]
    public async Task Bienvenida_NumeroInvalido_AvisaSinPedirCodigo(string celular)
    {
        var vistaModelo = new BienvenidaVistaModelo(flujo, dispositivos, navegacion) { Celular = celular };

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.True(vistaModelo.HayMensaje);
        Assert.Empty(api.CodigosPedidos);
        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task Bienvenida_NumeroNuevo_NormalizaPideElCodigoYVaAlCodigo()
    {
        var vistaModelo = new BienvenidaVistaModelo(flujo, dispositivos, navegacion) { Celular = "+51 987 654 321" };

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.Equal(Celular, Assert.Single(api.CodigosPedidos).Celular);
        Assert.Equal([Rutas.Codigo], navegacion.Rutas);
        Assert.Equal("135790", flujo.CodigoDePrueba);
    }

    [Fact]
    public async Task Bienvenida_TelefonoYaVinculadoAEseNumero_PideSoloElPin()
    {
        dispositivos.Dispositivo = new DispositivoVinculado(Celular, "llave-guardada");
        var vistaModelo = new BienvenidaVistaModelo(flujo, dispositivos, navegacion);
        await vistaModelo.PrepararAsync();
        Assert.Equal("987 654 321", vistaModelo.Celular); // número prellenado

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.Empty(api.CodigosPedidos);
        Assert.Equal([Rutas.Pin], navegacion.Rutas);
        Assert.Equal(ModoPin.Ingresar, flujo.ModoPin);
        Assert.Equal("llave-guardada", flujo.Llave);
    }

    [Fact]
    public async Task Bienvenida_OtroNumeroDelVinculado_PideCodigo()
    {
        dispositivos.Dispositivo = new DispositivoVinculado("912345678", "llave-guardada");
        var vistaModelo = new BienvenidaVistaModelo(flujo, dispositivos, navegacion) { Celular = Celular };

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.Single(api.CodigosPedidos);
        Assert.Equal([Rutas.Codigo], navegacion.Rutas);
    }

    // ----- Código -----

    private async Task<CodigoVistaModelo> PantallaCodigoAsync()
    {
        flujo.Reiniciar(Celular);
        await flujo.EnviarCodigoAsync();
        return new CodigoVistaModelo(flujo, api, navegacion, reloj);
    }

    [Fact]
    public async Task Codigo_ConElProveedorSimulado_SeMuestraEnCualquierCompilacion()
    {
        // El servidor solo envía el código con Codigos:MostrarCodigoSimulado (también un servidor de demostración sin SMS).
        var vistaModelo = await PantallaCodigoAsync();

        Assert.True(vistaModelo.HayCodigoDePrueba);
        Assert.Equal("Código de prueba (simulado, sin SMS): 135790", vistaModelo.CodigoDePrueba);
    }

    [Fact]
    public async Task Codigo_ConSmsReal_NoMuestraNingunCodigo()
    {
        api.RespuestaCodigo = new ResultadoApi<RespuestaCodigoCelular>.Correcto(new RespuestaCodigoCelular(30, 600, null));

        var vistaModelo = await PantallaCodigoAsync();

        Assert.False(vistaModelo.HayCodigoDePrueba);
    }

    [Fact]
    public async Task Codigo_AlEscribirElSextoDigito_SeVerificaSolo_YUnNumeroNuevoVaACrearCuenta()
    {
        var vistaModelo = await PantallaCodigoAsync();

        vistaModelo.Codigo = "13579";
        Assert.Empty(api.Verificaciones);
        vistaModelo.Codigo = "135790";
        await Task.Yield();

        Assert.Equal(new SolicitudVerificacionCelular(Celular, "135790"), Assert.Single(api.Verificaciones));
        Assert.Equal(["../" + Rutas.CrearCuenta], navegacion.Rutas);
        Assert.Equal("token-verificacion", flujo.TokenVerificacion);
    }

    [Fact]
    public async Task Codigo_NumeroConCuenta_VaAlPinParaIngresar()
    {
        api.RespuestaVerificacion = new ResultadoApi<RespuestaVerificacionCelular>.Correcto(new("token-verificacion", true, false));
        var vistaModelo = await PantallaCodigoAsync();

        vistaModelo.Codigo = "135790";
        await Task.Yield();

        Assert.Equal(["../" + Rutas.Pin], navegacion.Rutas);
        Assert.Equal(ModoPin.Ingresar, flujo.ModoPin);
    }

    [Fact]
    public async Task Codigo_Incorrecto_MuestraElMensajeYVaciaElCampo()
    {
        api.RespuestaVerificacion = new ResultadoApi<RespuestaVerificacionCelular>.Rechazado(400,
            "Código incorrecto. Te quedan 4 intentos.", new Dictionary<string, string[]>());
        var vistaModelo = await PantallaCodigoAsync();

        vistaModelo.Codigo = "000000";
        await Task.Yield();

        Assert.Equal("Código incorrecto. Te quedan 4 intentos.", vistaModelo.Mensaje);
        Assert.Equal("", vistaModelo.Codigo);
        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task Codigo_ReenviarSoloTrasLaCuentaAtras()
    {
        var vistaModelo = await PantallaCodigoAsync();
        Assert.False(vistaModelo.PuedeReenviar);
        Assert.Equal("Reenviar en 0:30", vistaModelo.TextoReenviar);

        reloj.Advance(TimeSpan.FromSeconds(30));
        vistaModelo.ActualizarCuentaAtras();

        Assert.True(vistaModelo.PuedeReenviar);
        Assert.Equal("Reenviar código", vistaModelo.TextoReenviar);
        await vistaModelo.Reenviar.EjecutarAsync();
        Assert.Equal(2, api.CodigosPedidos.Count);
        Assert.False(vistaModelo.PuedeReenviar);
    }

    // ----- Crea tu cuenta -----

    [Fact]
    public async Task CrearCuenta_SinNombreNiTerminos_NoAvanza()
    {
        flujo.Reiniciar(Celular);
        var vistaModelo = new CrearCuentaVistaModelo(flujo, navegacion) { Nombres = " ", Apellidos = "Ficticia" };

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.True(vistaModelo.HayErrorNombres);
        Assert.True(vistaModelo.HayErrorTerminos);
        Assert.False(vistaModelo.HayErrorApellidos);
        Assert.Empty(navegacion.Rutas);
    }

    [Fact]
    public async Task CrearCuenta_ConDatos_GuardaElNombreYVaACrearElPin()
    {
        flujo.Reiniciar(Celular);
        var vistaModelo = new CrearCuentaVistaModelo(flujo, navegacion) { Nombres = " Rosa ", Apellidos = "Ficticia Quispe", AceptaTerminos = true };
        Assert.Equal("987 654 321", vistaModelo.Celular);

        await vistaModelo.Continuar.EjecutarAsync();

        Assert.Equal([Rutas.Pin], navegacion.Rutas);
        Assert.Equal(("Rosa", "Ficticia Quispe", ModoPin.Crear), (flujo.Nombres, flujo.Apellidos, flujo.ModoPin));
    }

    // ----- PIN -----

    private PinVistaModelo PantallaPin(ModoPin modo, string? token = "token-verificacion", string? llave = null)
    {
        flujo.Reiniciar(Celular);
        flujo.ModoPin = modo;
        flujo.TokenVerificacion = token;
        flujo.Llave = llave;
        flujo.Nombres = "Rosa";
        flujo.Apellidos = "Ficticia";
        return new PinVistaModelo(flujo, api, navegacion);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("777777")]
    public async Task PinCrear_Obvio_SeRechazaAntesDeConfirmar(string pin)
    {
        var vistaModelo = PantallaPin(ModoPin.Crear);

        await EscribirPinAsync(vistaModelo, pin);

        Assert.Equal("Ese PIN es fácil de adivinar: evita números repetidos o seguidos.", vistaModelo.Mensaje);
        Assert.Equal("Crea tu PIN de 6 dígitos", vistaModelo.Titulo);
        Assert.All(vistaModelo.Puntos, p => Assert.False(p));
    }

    [Fact]
    public async Task PinCrear_ConfirmacionDistinta_VuelveAlPrimerPaso()
    {
        var vistaModelo = PantallaPin(ModoPin.Crear);

        await EscribirPinAsync(vistaModelo, "482915");
        Assert.Equal("Confirma tu PIN", vistaModelo.Titulo);
        await EscribirPinAsync(vistaModelo, "482916");

        Assert.Equal("Los PIN no coinciden. Vuelve a crearlo.", vistaModelo.Mensaje);
        Assert.Equal("Crea tu PIN de 6 dígitos", vistaModelo.Titulo);
        Assert.Empty(api.RegistrosCelular);
    }

    [Fact]
    public async Task PinCrear_Confirmado_RegistraGuardaSesionYTelefonoYEntraAlInicio()
    {
        var vistaModelo = PantallaPin(ModoPin.Crear);

        await EscribirPinAsync(vistaModelo, "482915");
        await EscribirPinAsync(vistaModelo, "482915");

        Assert.Equal(new SolicitudRegistroCelular("token-verificacion", "Rosa", "Ficticia", "482915", true), Assert.Single(api.RegistrosCelular));
        Assert.Equal("token-ciudadano", sesiones.Sesion!.Token);
        Assert.Equal(new DispositivoVinculado(Celular, "llave-ficticia"), dispositivos.Dispositivo);
        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task PinIngresar_ConLlave_EnviaLaLlaveSinToken_YConservaElTelefonoVinculado()
    {
        dispositivos.Dispositivo = new DispositivoVinculado(Celular, "llave-guardada");
        api.RespuestasIngresoCelular.Enqueue(ApiFalsa.SesionCiudadano(llave: null));
        var vistaModelo = PantallaPin(ModoPin.Ingresar, token: null, llave: "llave-guardada");
        Assert.Equal("Hola de nuevo · 987 654 321", vistaModelo.Subtitulo);

        await EscribirPinAsync(vistaModelo, "482915");

        Assert.Equal(new SolicitudIngresoCelular(Celular, "482915", "llave-guardada", null), Assert.Single(api.IngresosCelular));
        Assert.Equal("llave-guardada", dispositivos.Dispositivo!.Llave);
        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task PinIngresar_Incorrecto_MuestraLosIntentosYNoNavega()
    {
        api.RespuestasIngresoCelular.Enqueue(new ResultadoApi<RespuestaIngresoCelular>.Rechazado(401,
            "PIN incorrecto. Te quedan 4 intentos.", new Dictionary<string, string[]>()));
        var vistaModelo = PantallaPin(ModoPin.Ingresar, token: null, llave: "llave");

        await EscribirPinAsync(vistaModelo, "000999");

        Assert.Equal("PIN incorrecto. Te quedan 4 intentos.", vistaModelo.Mensaje);
        Assert.Empty(navegacion.Rutas);
        Assert.Null(sesiones.Sesion);
    }

    [Fact]
    public async Task PinIngresar_Bloqueado_DesactivaElTecladoYOfreceCrearUnoNuevo()
    {
        api.RespuestasIngresoCelular.Enqueue(new ResultadoApi<RespuestaIngresoCelular>.Rechazado(423,
            "Tu PIN se bloqueó tras varios intentos.", new Dictionary<string, string[]>()));
        var vistaModelo = PantallaPin(ModoPin.Ingresar, token: null, llave: "llave");

        await EscribirPinAsync(vistaModelo, "000999");
        await EscribirPinAsync(vistaModelo, "4");

        Assert.True(vistaModelo.Bloqueado);
        Assert.All(vistaModelo.Puntos, p => Assert.False(p)); // el teclado ya no escribe
        await vistaModelo.OlvideMiPin.EjecutarAsync();
        Assert.Equal(PropositoCodigo.RestablecerPin, flujo.Proposito);
        Assert.Equal(["../" + Rutas.Codigo], navegacion.Rutas);
    }

    [Fact]
    public async Task PinIngresar_TelefonoYaNoVinculado_OlvidaLaLlaveYPideUnCodigo()
    {
        dispositivos.Dispositivo = new DispositivoVinculado(Celular, "llave-vieja");
        api.RespuestasIngresoCelular.Enqueue(new ResultadoApi<RespuestaIngresoCelular>.Rechazado(428,
            "Este teléfono no está vinculado a tu cuenta.", new Dictionary<string, string[]>()));
        var vistaModelo = PantallaPin(ModoPin.Ingresar, token: null, llave: "llave-vieja");

        await EscribirPinAsync(vistaModelo, "482915");

        Assert.Null(dispositivos.Dispositivo);
        Assert.Single(api.CodigosPedidos);
        Assert.Equal(["../" + Rutas.Codigo], navegacion.Rutas);
        Assert.Equal("Este teléfono no está vinculado a tu cuenta.", flujo.AvisoPendiente);
    }

    [Fact]
    public async Task PinRestablecer_Confirmado_EnviaElTokenYElPinNuevo()
    {
        var vistaModelo = PantallaPin(ModoPin.Restablecer);
        Assert.Equal("Crea un PIN nuevo", vistaModelo.Titulo);

        await EscribirPinAsync(vistaModelo, "305718");
        await EscribirPinAsync(vistaModelo, "305718");

        Assert.Equal(new SolicitudRestablecerPin("token-verificacion", "305718"), Assert.Single(api.PinesRestablecidos));
        Assert.Equal([Rutas.Ciudadano], navegacion.Rutas);
    }

    [Fact]
    public async Task Pin_Borrar_QuitaElUltimoDigito()
    {
        var vistaModelo = PantallaPin(ModoPin.Crear);

        await EscribirPinAsync(vistaModelo, "48");
        await vistaModelo.Borrar.EjecutarAsync();

        Assert.Equal(new[] { true, false, false, false, false, false }, vistaModelo.Puntos);
    }

    [Theory]
    [InlineData("482915", null)]
    [InlineData("123456", "Ese PIN es fácil de adivinar: evita números repetidos o seguidos.")]
    [InlineData("987654", "Ese PIN es fácil de adivinar: evita números repetidos o seguidos.")]
    [InlineData("000000", "Ese PIN es fácil de adivinar: evita números repetidos o seguidos.")]
    [InlineData("12345", "El PIN debe tener exactamente 6 dígitos.")]
    public void ReglasPin_IgualesQueElServidor(string pin, string? error) => Assert.Equal(error, ReglasPin.Validar(pin));
}
