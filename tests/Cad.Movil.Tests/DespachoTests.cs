using Cad.Contratos.Serenos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Time.Testing;

namespace Cad.Movil.Tests;

// HU21 — Recibir y consultar un despacho (lado de la app). Datos ficticios.
public class DespachoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private readonly ApiFalsa api = new();
    private readonly CanalFalso canal = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-sereno", "Sereno", DateTimeOffset.UtcNow.AddHours(8)) };

    private static DespachoSereno Despacho(UbicacionDespacho? ubicacion = null, params InformacionDespacho[] informacion) =>
        new(Guid.NewGuid(), "SOS", "Asignada", Inicio, Inicio.AddMinutes(2), ubicacion, informacion);

    private ServicioDespacho Servicio() => new(api, sesiones, canal, TimeProvider.System);

    private InicioSerenoVistaModelo VistaModelo(ServicioDespacho despacho) =>
        new(new ServicioTurnoSereno(api, new UbicacionFalsa(), sesiones, new FakeTimeProvider(Inicio)), despacho, sesiones, new NavegacionFalsa(), () => { })
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
    public async Task AlAbrir_SeConectaAlCanalYSeConsultaElDespacho()
    {
        var esperado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(esperado);
        var despacho = Servicio();

        await despacho.IniciarAsync();

        Assert.Equal(1, canal.Conexiones);
        Assert.Equal(esperado, despacho.Actual);
        Assert.False(despacho.AvisoNuevo);
    }

    [Fact]
    public async Task CA01_AlRecibirElAviso_VuelveAConsultarYMarcaUnDespachoNuevo()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        Assert.Null(despacho.Actual);
        Assert.True(despacho.Consultado);
        var nuevo = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(nuevo);

        canal.Avisar();

        await Esperar(() => despacho.Actual == nuevo, "el despacho nuevo se consulta");
        Assert.True(despacho.AvisoNuevo);
    }

    [Fact]
    public async Task ADR07_SinCanal_ElDespachoSeConsultaIgualmente()
    {
        canal.Conecta = false;
        var esperado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(esperado);
        var despacho = Servicio();

        await despacho.IniciarAsync();

        Assert.False(despacho.CanalConectado);
        Assert.Equal(esperado, despacho.Actual);
    }

    [Fact]
    public async Task AlReconectar_VuelveAConsultar_PorSiSePerdioUnAviso()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        var consultas = api.ConsultasDespacho;

        canal.Reconectar();

        await Esperar(() => api.ConsultasDespacho > consultas, "se consulta tras reconectar");
    }

    [Fact]
    public async Task SinConexion_ConservaElUltimoDespachoYAvisa()
    {
        var conocido = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(conocido);
        var despacho = Servicio();
        await despacho.IniciarAsync();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.SinRespuesta("No hay conexión con el servidor.");

        await despacho.ActualizarAsync();

        Assert.Equal(conocido, despacho.Actual);
        Assert.Contains("No se pudo consultar tu despacho", despacho.Error);
    }

    [Fact]
    public async Task AlSalir_NoSeHeredaElDespachoNiSeSigueEscuchando()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Despacho());
        var despacho = Servicio();
        await despacho.IniciarAsync();

        await despacho.ReiniciarAsync();
        var consultas = api.ConsultasDespacho;
        canal.Avisar();
        await Task.Delay(100);

        Assert.Null(despacho.Actual);
        Assert.False(despacho.Consultado);
        Assert.Equal(consultas, api.ConsultasDespacho);
    }

    [Fact]
    public async Task CA02_LaPantallaMuestraOrigenEstadoIdentificadorUbicacionEInformacion()
    {
        var d = Despacho(new UbicacionDespacho(-12.0464, -77.0428, 10, Inicio.AddSeconds(1)),
            new InformacionDespacho(Inicio.AddMinutes(1), "Dos personas discuten", "Frente al parque ficticio"));
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(d);
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.True(vm.HayDespacho);
        Assert.Equal("SOS · Asignada", vm.TextoDespacho);
        Assert.Contains(d.CasoId.ToString(), vm.TextoIdentificador);
        Assert.True(vm.HayUbicacionDespacho);
        Assert.StartsWith("-12.04640, -77.04280 · ± 10 m", vm.TextoUbicacionDespacho);
        var informacion = Assert.Single(vm.InformacionDespacho);
        Assert.Equal("Dos personas discuten", informacion.InformacionHecho);
        Assert.Equal("Frente al parque ficticio", informacion.ReferenciaUbicacion);
    }

    [Fact]
    public async Task CA03_SinUbicacion_NoHayCoordenadas_YLaReferenciaVaAparte()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(
            Despacho(null, new InformacionDespacho(Inicio, null, "Esquina ficticia")));
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.False(vm.HayUbicacionDespacho);
        Assert.True(vm.SinUbicacionDespacho);
        Assert.Equal("", vm.TextoUbicacionDespacho);
        var informacion = Assert.Single(vm.InformacionDespacho);
        Assert.True(informacion.HayReferencia);
        Assert.False(informacion.HayHecho);
    }

    // ---------------- HU22 y HU23 ----------------

    [Fact]
    public async Task HU22_VoyEnCamino_SoloConElCasoAsignada_YActualizaElDespacho()
    {
        var asignado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(asignado);
        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.Correcto(asignado with { Estado = DespachoSereno.EstadoEnCamino, EnCaminoEn = Inicio.AddMinutes(3) });
        var despacho = Servicio();
        var vm = VistaModelo(despacho);
        await vm.AlAparecerAsync();
        Assert.True(vm.PuedeConfirmarDesplazamiento);
        Assert.False(vm.PuedeIniciarAtencion);

        await vm.ConfirmarDesplazamiento.EjecutarAsync();

        Assert.Equal(["desplazamiento"], api.Avances);
        Assert.Equal(DespachoSereno.EstadoEnCamino, despacho.Actual!.Estado);
        Assert.False(vm.PuedeConfirmarDesplazamiento);
        Assert.True(vm.PuedeIniciarAtencion);
        Assert.StartsWith("En camino desde las ", vm.TextoAvances);
    }

    [Fact]
    public async Task HU23_IniciarAtencion_ConElCasoEnCamino()
    {
        var enCamino = Despacho() with { Estado = DespachoSereno.EstadoEnCamino };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enCamino);
        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.Correcto(enCamino with { Estado = DespachoSereno.EstadoEnAtencion });
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();

        await vm.IniciarAtencion.EjecutarAsync();

        Assert.Equal(["atencion"], api.Avances);
        Assert.False(vm.PuedeIniciarAtencion);
        Assert.False(vm.PuedeConfirmarDesplazamiento);
    }

    [Fact]
    public async Task HU23_SiElEstadoYaCambio_InformaYVuelveAConsultar()
    {
        var enCamino = Despacho() with { Estado = DespachoSereno.EstadoEnCamino };
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enCamino);
        var despacho = Servicio();
        await despacho.IniciarAsync();
        // Un operador registró el inicio antes: el servidor rechaza y el despacho ya está «En atención».
        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.Rechazado(409, "El inicio de atención ya fue registrado por otro responsable.", new Dictionary<string, string[]>());
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(enCamino with { Estado = DespachoSereno.EstadoEnAtencion });

        Assert.False(await despacho.IniciarAtencionAsync());

        Assert.Equal(DespachoSereno.EstadoEnAtencion, despacho.Actual!.Estado);
        Assert.Equal("El inicio de atención ya fue registrado por otro responsable.", despacho.Error);
    }

    [Fact]
    public async Task HU22_SinConexion_ConservaElEstadoYPideReintentar()
    {
        var asignado = Despacho();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(asignado);
        api.RespuestaAvance = _ => new ResultadoApi<DespachoSereno?>.SinRespuesta("No hay conexión con el servidor.");
        var despacho = Servicio();
        await despacho.IniciarAsync();

        Assert.False(await despacho.ConfirmarDesplazamientoAsync());

        Assert.Equal(asignado, despacho.Actual);
        Assert.EndsWith("Reinténtalo.", despacho.Error);
    }

    [Fact]
    public async Task HU23_CA02_SiUnOperadorCambiaElEstado_LaAppVuelveAConsultar()
    {
        var despacho = Servicio();
        await despacho.IniciarAsync();
        var consultas = api.ConsultasDespacho;

        canal.Actualizar();

        await Esperar(() => api.ConsultasDespacho > consultas, "se consulta tras el aviso de actualización");
        Assert.False(despacho.AvisoNuevo); // no es un despacho nuevo
    }

    // ---------------- HU24 ----------------

    private DespachoSereno EnAtencion() => Despacho() with { Estado = DespachoSereno.EstadoEnAtencion };

    [Fact]
    public async Task HU24_CerrarElCaso_EnviaTipificacionResultadoYNotas_YDejaDeMostrarElDespacho()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(EnAtencion());
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();
        Assert.True(vm.PuedeAbrirCierre);

        await vm.AbrirCierre.EjecutarAsync();
        Assert.True(vm.MostrarCierre);
        Assert.Equal(api.Categorias, vm.CategoriasCierre);
        vm.CategoriaCierre = vm.CategoriasCierre[0];
        vm.ResultadoCierre = "Resultado ficticio";
        vm.NotasCierre = "Nota interna ficticia";
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(null);
        await vm.ConfirmarCierre.EjecutarAsync();

        var enviado = Assert.Single(api.Cierres);
        Assert.Equal(api.Categorias[0].Id, enviado.CategoriaId);
        Assert.Equal("Resultado ficticio", enviado.ResultadoPublico);
        Assert.Equal("Nota interna ficticia", enviado.NotasInternas);
        Assert.False(vm.HayDespacho);
        Assert.False(vm.MostrarCierre);
        Assert.True(vm.HayCierreReciente);
        Assert.StartsWith("Caso cerrado a las ", vm.CierreReciente);
    }

    [Fact]
    public async Task HU24_CA02_SinTipificacionOResultado_MuestraLosErroresYSigueAbierto()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(EnAtencion());
        api.RespuestaCierre = _ => new ResultadoApi<RespuestaCierreCaso>.Rechazado(400, "No se pudo cerrar el caso.", new Dictionary<string, string[]>
        {
            ["categoriaId"] = ["Elige la tipificación final del catálogo."],
            ["resultadoPublico"] = ["Describe el resultado de la atención."],
        });
        var vm = VistaModelo(Servicio());
        await vm.AlAparecerAsync();
        await vm.AbrirCierre.EjecutarAsync();

        await vm.ConfirmarCierre.EjecutarAsync();

        Assert.True(vm.MostrarCierre);
        Assert.True(vm.HayDespacho);
        Assert.Equal("Elige la tipificación final del catálogo.", vm.ErrorCategoriaCierre);
        Assert.Equal("Describe el resultado de la atención.", vm.ErrorResultadoCierre);
    }

    [Fact]
    public async Task HU24_SiElOperadorYaLoCerro_InformaYVuelveAConsultar()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(EnAtencion());
        var despacho = Servicio();
        await despacho.IniciarAsync();
        api.RespuestaCierre = _ => new ResultadoApi<RespuestaCierreCaso>.Rechazado(409, "El caso ya fue cerrado por otro responsable.", new Dictionary<string, string[]>());
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(null);

        Assert.False(await despacho.CerrarAsync(Guid.NewGuid(), "x", null));

        Assert.Null(despacho.Actual);
        Assert.Equal("El caso ya fue cerrado por otro responsable.", despacho.Error);
    }

    [Fact]
    public async Task HU24_SinConexion_ConservaElDespachoYPideReintentar()
    {
        var caso = EnAtencion();
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(caso);
        api.RespuestaCierre = _ => new ResultadoApi<RespuestaCierreCaso>.SinRespuesta("No hay conexión con el servidor.");
        var despacho = Servicio();
        await despacho.IniciarAsync();

        Assert.False(await despacho.CerrarAsync(Guid.NewGuid(), "x", null));

        Assert.Equal(caso, despacho.Actual);
        Assert.EndsWith("Reinténtalo.", despacho.Error);
    }

    [Fact]
    public async Task HU24_ElBotonDeCierreSoloAparece_EnAtencion()
    {
        api.RespuestaDespacho = () => new ResultadoApi<DespachoSereno?>.Correcto(Despacho() with { Estado = DespachoSereno.EstadoEnCamino });
        var vm = VistaModelo(Servicio());

        await vm.AlAparecerAsync();

        Assert.False(vm.PuedeAbrirCierre);
        Assert.False(vm.MostrarCierre);
    }

    [Fact]
    public async Task SinDespacho_LoDiceSoloTrasConsultar()
    {
        var despacho = Servicio();
        var vm = VistaModelo(despacho);
        Assert.False(vm.SinDespacho); // aún no se sabe

        await vm.AlAparecerAsync();

        Assert.True(vm.SinDespacho);
        Assert.False(vm.HayDespacho);
    }
}
