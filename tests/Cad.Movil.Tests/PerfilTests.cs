using Cad.Contratos.Casos;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// ADR-08, fase D — pestaña «Perfil», «Mis datos», cambio de PIN y el inicio rediseñado (franja del caso en curso y mapa).
// Datos ficticios.
public class PerfilTests
{
    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new();
    private readonly DispositivosFalsos dispositivos = new();
    private readonly NavegacionFalsa navegacion = new();
    private readonly FakeTimeProvider reloj = new(new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero));
    private readonly FlujoIngresoCelular flujo;

    public PerfilTests() => flujo = new FlujoIngresoCelular(api, sesiones, dispositivos, navegacion, reloj);

    // ----- Perfil -----

    [Fact]
    public async Task Perfil_MuestraNombreCelularVerificadoYCuantosDatosFaltan()
    {
        api.Perfil = api.Perfil with { NumeroDocumento = "12345678", TipoDocumento = "DNI" };
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo);

        await vistaModelo.CargarAsync();

        Assert.Equal(("RF", "Rosa Ficticia Quispe", "+51 987 654 321"), (vistaModelo.Iniciales, vistaModelo.Nombre, vistaModelo.Celular));
        Assert.True(vistaModelo.CelularVerificado);
        Assert.Equal("Completa tus datos (1 de 4)", vistaModelo.TextoDatos);
        Assert.True(vistaModelo.TienePin);
    }

    [Fact]
    public async Task Perfil_CerrarSesion_PideConfirmacionYConservaElTelefonoVinculado()
    {
        dispositivos.Dispositivo = new DispositivoVinculado("987654321", "llave");
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo) { ConfirmarCierre = () => Task.FromResult(false) };

        await vistaModelo.CerrarSesion.EjecutarAsync();
        Assert.NotNull(sesiones.Sesion); // canceló

        vistaModelo.ConfirmarCierre = () => Task.FromResult(true);
        await vistaModelo.CerrarSesion.EjecutarAsync();

        Assert.Null(sesiones.Sesion);
        Assert.NotNull(dispositivos.Dispositivo); // al volver basta el PIN
        Assert.Equal([Rutas.Ingreso], navegacion.Rutas);
    }

    [Fact]
    public async Task Perfil_CambiarPin_AbreElPinEnModoCambiar()
    {
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo);
        await vistaModelo.CargarAsync();

        await vistaModelo.CambiarPin.EjecutarAsync();

        Assert.Equal(ModoPin.Cambiar, flujo.ModoPin);
        Assert.Equal([Rutas.Pin], navegacion.Rutas);
    }

    [Fact]
    public async Task Perfil_AvisoPendiente_SeMuestraComoExito()
    {
        flujo.AvisoPendiente = "Cambiaste tu PIN.";
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo);

        await vistaModelo.CargarAsync();

        Assert.Equal(("exito", "Cambiaste tu PIN."), (vistaModelo.TipoMensaje, vistaModelo.Mensaje));
    }

    // ----- Cambiar PIN -----

    private async Task EscribirAsync(PinVistaModelo pin, string digitos)
    {
        foreach (var d in digitos)
            await pin.Tecla.EjecutarAsync(d.ToString());
    }

    private PinVistaModelo PantallaCambio()
    {
        flujo.Reiniciar("987654321");
        flujo.ModoPin = ModoPin.Cambiar;
        return new PinVistaModelo(flujo, api, navegacion);
    }

    [Fact]
    public async Task CambiarPin_PideActualNuevoYConfirmacion_YRenuevaLaSesion()
    {
        var vistaModelo = PantallaCambio();
        Assert.Equal("Ingresa tu PIN actual", vistaModelo.Titulo);

        await EscribirAsync(vistaModelo, "482915");
        Assert.Equal("Crea tu PIN nuevo", vistaModelo.Titulo);
        await EscribirAsync(vistaModelo, "730591");
        Assert.Equal("Confirma tu PIN nuevo", vistaModelo.Titulo);
        await EscribirAsync(vistaModelo, "730591");

        Assert.Equal(new SolicitudCambioPin("482915", "730591"), Assert.Single(api.CambiosPin));
        Assert.Equal("token-ciudadano", sesiones.Sesion!.Token);
        Assert.Equal("Cambiaste tu PIN.", flujo.AvisoPendiente);
        Assert.Equal([".."], navegacion.Rutas);
    }

    [Fact]
    public async Task CambiarPin_NuevoIgualAlActual_SeRechazaSinEnviar()
    {
        var vistaModelo = PantallaCambio();

        await EscribirAsync(vistaModelo, "482915");
        await EscribirAsync(vistaModelo, "482915");

        Assert.Equal("El PIN nuevo debe ser distinto del actual.", vistaModelo.Mensaje);
        Assert.Empty(api.CambiosPin);
    }

    [Fact]
    public async Task CambiarPin_ActualIncorrecto_VuelveAPedirElActual()
    {
        api.RespuestasIngresoCelular.Enqueue(new ResultadoApi<RespuestaIngresoCelular>.Rechazado(400,
            "PIN actual incorrecto. Te quedan 4 intentos.", new Dictionary<string, string[]>()));
        var vistaModelo = PantallaCambio();

        await EscribirAsync(vistaModelo, "000999");
        await EscribirAsync(vistaModelo, "730591");
        await EscribirAsync(vistaModelo, "730591");

        Assert.Equal("PIN actual incorrecto. Te quedan 4 intentos.", vistaModelo.Mensaje);
        Assert.Equal("Ingresa tu PIN actual", vistaModelo.Titulo);
        Assert.Empty(navegacion.Rutas);
    }

    // ----- Mis datos -----

    [Fact]
    public async Task MisDatos_CargaLosDatosYLosOpcionalesVacios()
    {
        api.Perfil = api.Perfil with { TipoDocumento = "CE", NumeroDocumento = "000123456", FechaNacimiento = new DateOnly(1990, 5, 17) };
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion);

        await vistaModelo.CargarAsync();

        Assert.Equal(("Carné de extranjería", "000123456", "17/05/1990"), (vistaModelo.TipoDocumento, vistaModelo.NumeroDocumento, vistaModelo.FechaNacimiento));
        Assert.Equal(MisDatosVistaModelo.SinContacto, vistaModelo.ContactoParentesco);
        Assert.Equal("+51 987 654 321", vistaModelo.Celular);
    }

    [Fact]
    public async Task MisDatos_Guardar_EnviaLosCodigosYQuitaLosVacios()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion);
        await vistaModelo.CargarAsync();
        vistaModelo.TipoDocumento = "DNI";
        vistaModelo.NumeroDocumento = " 12345678 ";
        vistaModelo.FechaNacimiento = "17/05/1990";
        vistaModelo.Direccion = "  ";
        vistaModelo.ContactoNombre = "Marta Ficticia";
        vistaModelo.ContactoParentesco = "Madre";
        vistaModelo.ContactoTelefono = "911222333";

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.Equal(new SolicitudPerfilCiudadano("Rosa", "Ficticia Quispe", "DNI", "12345678", new DateOnly(1990, 5, 17), null,
            "Marta Ficticia", "Madre", "911222333"), Assert.Single(api.PerfilesEnviados));
        Assert.Equal(("exito", "Tus datos se guardaron."), (vistaModelo.TipoMensaje, vistaModelo.Mensaje));
    }

    [Fact]
    public async Task MisDatos_SinDocumento_NoEnviaElNumero()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion);
        await vistaModelo.CargarAsync();
        vistaModelo.NumeroDocumento = "12345678";

        await vistaModelo.Guardar.EjecutarAsync();

        var enviado = Assert.Single(api.PerfilesEnviados);
        Assert.Null(enviado.TipoDocumento);
        Assert.Null(enviado.NumeroDocumento);
    }

    [Fact]
    public async Task MisDatos_FechaMalEscrita_SeAvisaSinEnviar()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion);
        await vistaModelo.CargarAsync();
        vistaModelo.FechaNacimiento = "1990-05-17";

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.True(vistaModelo.HayErrorFechaNacimiento);
        Assert.Empty(api.PerfilesEnviados);
    }

    [Fact]
    public async Task MisDatos_ErroresDelServidor_SeMuestranPorCampo()
    {
        api.RespuestaActualizarPerfil = new ResultadoApi<PerfilCiudadanoVista>.Rechazado(400, "Revisa los datos marcados.",
            new Dictionary<string, string[]> { ["numeroDocumento"] = ["El DNI tiene 8 dígitos."], ["contactoTelefono"] = ["Escribe un celular peruano de 9 dígitos que empiece por 9."] });
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion);
        await vistaModelo.CargarAsync();

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.Equal("El DNI tiene 8 dígitos.", vistaModelo.ErrorDocumento);
        Assert.True(vistaModelo.HayErrorContacto);
        Assert.Equal("Revisa los datos marcados.", vistaModelo.Mensaje);
    }

    // ----- Inicio rediseñado -----

    private InicioCiudadanoVistaModelo Inicio(UbicacionFalsa ubicacion) =>
        new(new EmisorSos(api, ubicacion, sesiones, reloj), new PreparacionUbicacion(ubicacion), sesiones, navegacion, () => { }, api: api);

    [Fact]
    public async Task Inicio_ConUnCasoAbierto_MuestraSuEstadoYLlevaAlDetalle()
    {
        var abierto = new MiCasoResumen(Guid.NewGuid(), "SOS", reloj.GetUtcNow(), EstadosPublicos.EnAtencion, null);
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(
            [new MiCasoResumen(Guid.NewGuid(), "Reporte", reloj.GetUtcNow().AddDays(-1), EstadosPublicos.Cerrado, "Atendido"), abierto]);
        var inicio = Inicio(new UbicacionFalsa());

        await inicio.AlAparecerAsync();
        await inicio.VerCasoActivo.EjecutarAsync();

        Assert.True(inicio.HayCasoActivo);
        Assert.Equal("SOS · En atención", inicio.TextoCasoActivo);
        Assert.Equal([$"{Rutas.MiCaso}?id={abierto.Id}"], navegacion.Rutas);
    }

    [Fact]
    public async Task Inicio_SoloCasosTerminados_NoMuestraLaFranja()
    {
        api.RespuestaMisCasos = () => new ResultadoApi<List<MiCasoResumen>>.Correcto(
            [new MiCasoResumen(Guid.NewGuid(), "SOS", reloj.GetUtcNow(), EstadosPublicos.Cancelado, null)]);
        var inicio = Inicio(new UbicacionFalsa());

        await inicio.AlAparecerAsync();

        Assert.False(inicio.HayCasoActivo);
    }

    [Fact]
    public async Task Inicio_ConUbicacionDisponible_PintaLaPosicionEnElMapaSinEnviarNada()
    {
        var posicion = new UbicacionSos(-12.0453, -77.0365, 8, reloj.GetUtcNow());
        var inicio = Inicio(new UbicacionFalsa { Posicion = () => posicion });
        UbicacionSos? pintada = null;
        inicio.PosicionParaMapa += p => pintada = p;

        await inicio.AlAparecerAsync();

        Assert.Equal(posicion, pintada);
        Assert.False(inicio.MostrarAvisoUbicacion); // el mapa ya lo muestra
        Assert.Empty(api.SosEnviados);
    }

    [Fact]
    public async Task Inicio_CentrarSinPosicion_ExplicaQueHacer()
    {
        var inicio = Inicio(new UbicacionFalsa { Posicion = () => null });

        await inicio.Centrar.EjecutarAsync();

        Assert.True(inicio.HayTextoCentrar);
    }
}
