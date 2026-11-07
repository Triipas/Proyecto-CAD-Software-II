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
    public async Task Perfil_MuestraNombreCelularVerificadoYQueFaltanDatos()
    {
        api.Perfil = api.Perfil with { NumeroDocumento = "12345678", TipoDocumento = "DNI" };
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo);

        await vistaModelo.CargarAsync();

        Assert.Equal(("RF", "Rosa Ficticia Quispe", "+51 987 654 321"), (vistaModelo.Iniciales, vistaModelo.Nombre, vistaModelo.Celular));
        Assert.True(vistaModelo.CelularVerificado);
        Assert.Equal("Faltan datos", vistaModelo.TextoDatos);
        Assert.True(vistaModelo.DatosIncompletos);
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
    public async Task MisDatos_CuentaAnteriorSinTodosLosDatos_CargaLoQueHayYPideCompletar()
    {
        api.Perfil = api.Perfil with { TipoDocumento = "CE", NumeroDocumento = "000123456", FechaNacimiento = new DateOnly(1990, 5, 17) };
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);

        await vistaModelo.CargarAsync();

        Assert.Equal(("Carné de extranjería", "000123456", "17/05/1990"), (vistaModelo.TipoDocumento, vistaModelo.NumeroDocumento, vistaModelo.FechaNacimiento));
        Assert.Equal(FormularioDatosCiudadano.SinElegir, vistaModelo.ContactoParentesco);
        Assert.Equal("+51 987 654 321", vistaModelo.Celular);
        Assert.Equal(("advertencia", MisDatosVistaModelo.AvisoIncompletos), (vistaModelo.TipoMensaje, vistaModelo.Mensaje));
    }

    [Fact]
    public async Task MisDatos_Guardar_EnviaLosCodigosYQuitaLosVacios()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);
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
    public async Task MisDatos_SinLosDatosObligatorios_NoSeEnvia()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);
        await vistaModelo.CargarAsync();
        vistaModelo.NumeroDocumento = "12345678";

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.Equal("Elige el tipo de documento.", vistaModelo.ErrorDocumento);
        Assert.True(vistaModelo.HayErrorFechaNacimiento);
        Assert.True(vistaModelo.HayErrorContacto);
        Assert.Empty(api.PerfilesEnviados);
    }

    [Fact]
    public async Task MisDatos_FechaMalEscrita_SeAvisaSinEnviar()
    {
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);
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
        api.Perfil = PerfilCompleto();
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);
        await vistaModelo.CargarAsync();

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.Equal("El DNI tiene 8 dígitos.", vistaModelo.ErrorDocumento);
        Assert.True(vistaModelo.HayErrorContacto);
        Assert.Equal("Revisa los datos marcados.", vistaModelo.Mensaje);
    }

    // El teclado numérico de Android no trae «/» (revisión del PR #137): la fecha se acepta solo con dígitos o con otro separador.
    [Theory]
    [InlineData("17051990")]
    [InlineData("17/05/1990")]
    [InlineData("17-05-1990")]
    [InlineData("17.05.1990")]
    [InlineData(" 17 5 1990 ")]
    public void LeerFecha_SinBarrasOConOtrosSeparadores_SeEntiende(string escrita) =>
        Assert.Equal(new DateOnly(1990, 5, 17), FormularioDatosCiudadano.LeerFecha(escrita));

    [Theory]
    [InlineData("1990-05-17")]
    [InlineData("1705199")]
    [InlineData("31021990")]
    public void LeerFecha_Ambigua_OImposible_NoSeAcepta(string escrita) =>
        Assert.Null(FormularioDatosCiudadano.LeerFecha(escrita));

    [Fact]
    public async Task MisDatos_FechaSoloConDigitos_SeEnvia()
    {
        api.Perfil = PerfilCompleto();
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);
        await vistaModelo.CargarAsync();
        vistaModelo.FechaNacimiento = "02031985";

        await vistaModelo.Guardar.EjecutarAsync();

        Assert.Equal(new DateOnly(1985, 3, 2), Assert.Single(api.PerfilesEnviados).FechaNacimiento);
    }

    private PerfilCiudadanoVista PerfilCompleto() => api.Perfil with
    {
        TipoDocumento = "DNI", NumeroDocumento = "12345678", FechaNacimiento = new DateOnly(1990, 5, 17),
        ContactoNombre = "Marta Ficticia", ContactoParentesco = "Madre", ContactoTelefono = "911222333",
    };

    [Fact]
    public async Task MisDatos_DatosCompletos_NoMuestraElAviso()
    {
        api.Perfil = PerfilCompleto();
        var vistaModelo = new MisDatosVistaModelo(api, sesiones, navegacion, reloj);

        await vistaModelo.CargarAsync();

        Assert.False(vistaModelo.HayMensaje);
    }

    // ----- Inicio rediseñado -----

    [Fact]
    public async Task Inicio_CuentaAnteriorSinTodosLosDatos_AvisaYLlevaAMisDatos()
    {
        var inicio = Inicio(new UbicacionFalsa());

        await inicio.AlAparecerAsync();
        Assert.True(inicio.DatosIncompletos);
        await inicio.CompletarDatos.EjecutarAsync();

        Assert.Equal([Rutas.MisDatos], navegacion.Rutas);
    }

    [Fact]
    public async Task Inicio_DatosCompletos_NoAvisa()
    {
        api.Perfil = PerfilCompleto();
        var inicio = Inicio(new UbicacionFalsa());

        await inicio.AlAparecerAsync();

        Assert.False(inicio.DatosIncompletos);
    }

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
    public async Task Perfil_CerrarSesion_DescartaElSosPendienteDeReintento()
    {
        // Un SOS sin respuesta queda pendiente de reintento; tras cerrar sesión no puede reenviarse con otra cuenta.
        api.RespuestasSos.Enqueue(_ => Task.FromResult<ResultadoApi<RespuestaSos>>(new ResultadoApi<RespuestaSos>.SinRespuesta("Sin conexión.")));
        var emisor = new EmisorSos(api, new UbicacionFalsa(), sesiones, reloj) { EsperasEntreIntentos = [] };
        await emisor.ActivarAsync();
        Assert.True(emisor.PuedeReintentar);
        var vistaModelo = new PerfilVistaModelo(api, sesiones, navegacion, flujo, emisor: emisor);

        await vistaModelo.CerrarSesion.EjecutarAsync();

        Assert.False(emisor.PuedeReintentar);
        Assert.IsType<EstadoSos.Listo>(emisor.Estado);
        await emisor.ReintentarAsync();
        Assert.Single(api.SosEnviados); // el reintento ya no envía nada
    }

    [Fact]
    public async Task Inicio_ElSaludoSeActualizaAlVolver()
    {
        var inicio = Inicio(new UbicacionFalsa());
        await inicio.AlAparecerAsync();
        Assert.Equal(("Hola, Rosa", "RF"), (inicio.Saludo, inicio.Iniciales));

        api.Perfil = api.Perfil with { Nombres = "Ana", Apellidos = "Quispe" }; // cambió en «Mis datos» u otra cuenta
        await inicio.AlAparecerAsync();

        Assert.Equal(("Hola, Ana", "AQ"), (inicio.Saludo, inicio.Iniciales));
    }

    [Fact]
    public async Task Inicio_CentrarFallaTrasUnaLecturaPrevia_NoReutilizaLaPosicionVieja()
    {
        var lecturas = new Queue<UbicacionSos?>([new UbicacionSos(-12.0453, -77.0365, 8, reloj.GetUtcNow()), null]);
        var inicio = Inicio(new UbicacionFalsa { Posicion = () => lecturas.Count > 0 ? lecturas.Dequeue() : null });
        var pintadas = new List<UbicacionSos>();
        inicio.PosicionParaMapa += pintadas.Add;
        await inicio.AlAparecerAsync();
        Assert.Single(pintadas);

        await inicio.Centrar.EjecutarAsync();

        Assert.Single(pintadas); // no se recentra en la posición anterior
        Assert.True(inicio.HayTextoCentrar);
    }

    [Fact]
    public async Task Inicio_CentrarSinPosicion_ExplicaQueHacer()
    {
        var inicio = Inicio(new UbicacionFalsa { Posicion = () => null });

        await inicio.Centrar.EjecutarAsync();

        Assert.True(inicio.HayTextoCentrar);
    }
}
