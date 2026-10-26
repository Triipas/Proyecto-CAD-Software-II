using Cad.Contratos.Casos;
using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Tests;

// HU09 — Cancelar un SOS pendiente (lado de la app, S5-T01). Datos ficticios.
public class CancelacionSosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Sos = Guid.NewGuid();

    private readonly ApiFalsa api = new();
    private readonly SesionesFalsas sesiones = new() { Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddHours(8)) };
    private readonly RelojFijo reloj = new(Ahora);

    private MiCasoVistaModelo Detalle() => new(api, sesiones, new NavegacionFalsa(), reloj, Lima);

    private static MiCasoDetalle Caso(string estado, string origen = "SOS", string? resultado = null) =>
        new(Sos, origen, Ahora.AddMinutes(-5), estado, resultado,
            [new AvancePublico(Ahora.AddMinutes(-5), EstadosPublicos.Recibido, null)]);

    private static MiCasoDetalle Cancelado(string motivo) =>
        new(Sos, "SOS", Ahora.AddMinutes(-5), EstadosPublicos.Cancelado, $"Cancelado por ti. Motivo: {motivo}",
            [new AvancePublico(Ahora.AddMinutes(-5), EstadosPublicos.Recibido, null), new AvancePublico(Ahora, EstadosPublicos.Cancelado, $"Motivo: {motivo}")]);

    private async Task<MiCasoVistaModelo> AbiertoAsync(string estado = EstadosPublicos.Recibido, string origen = "SOS")
    {
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Caso(estado, origen));
        var vm = Detalle();
        await vm.CargarAsync(Sos);
        return vm;
    }

    [Fact]
    public async Task CA01_SosPendiente_SeOfreceCancelarYConMotivoQuedaCancelado()
    {
        var vm = await AbiertoAsync();
        api.RespuestaCancelacion = (_, s) => new ResultadoApi<MiCasoDetalle>.Correcto(Cancelado(s.Motivo!));
        Assert.True(vm.OfrecerCancelacion);
        Assert.False(vm.FormularioCancelacion);

        await vm.MostrarCancelacion.EjecutarAsync();
        Assert.True(vm.FormularioCancelacion);
        Assert.False(vm.OfrecerCancelacion);
        vm.MotivoCancelacion = "  Fue una falsa alarma.  ";
        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal([(Sos, "Fue una falsa alarma.")], api.Cancelaciones);
        Assert.Equal("SOS · Cancelado", vm.Titulo);
        Assert.Equal("Cancelado por ti. Motivo: Fue una falsa alarma.", vm.Resultado);
        Assert.Equal(EstadosPublicos.Cancelado, vm.Avances[^1].Estado);
        Assert.Equal("Cancelaste el SOS. La central fue avisada.", vm.Confirmacion);
        Assert.False(vm.PuedeCancelar);
        Assert.False(vm.OfrecerCancelacion);
        Assert.False(vm.FormularioCancelacion);
        Assert.False(vm.HayMensaje);
    }

    [Theory]
    [InlineData(EstadosPublicos.EnAtencion, "SOS")]
    [InlineData(EstadosPublicos.Cerrado, "SOS")]
    [InlineData(EstadosPublicos.Cancelado, "SOS")]
    // 26/10/2026: un reporte se retira solo antes de la decisión de la central.
    [InlineData(EstadosPublicos.Admitido, "Reporte")]
    [InlineData(EstadosPublicos.Derivado, "Reporte")]
    public async Task CA03_SosAsignadoOPosterior_OUnReporteDecidido_NoOfreceCancelar(string estado, string origen)
    {
        var vm = await AbiertoAsync(estado, origen);

        Assert.False(vm.PuedeCancelar);
        Assert.False(vm.OfrecerCancelacion);
        await vm.MostrarCancelacion.EjecutarAsync();
        Assert.False(vm.FormularioCancelacion);
    }

    [Fact]
    public async Task CA03_SiLaCentralLoAsignoMientrasTanto_MuestraElRechazoYElEstadoActual()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Ya no lo necesito.";
        api.RespuestaCancelacion = (_, _) => new ResultadoApi<MiCasoDetalle>.Rechazado(409,
            "El SOS ya no está pendiente: la central lo asignó a un sereno y no se puede cancelar.", new Dictionary<string, string[]>());
        api.RespuestaMiCaso = _ => new ResultadoApi<MiCasoDetalle>.Correcto(Caso(EstadosPublicos.EnAtencion));

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal("SOS · En atención", vm.Titulo);
        Assert.Equal("El SOS ya no está pendiente: la central lo asignó a un sereno y no se puede cancelar.", vm.Mensaje);
        Assert.False(vm.FormularioCancelacion);
        Assert.False(vm.OfrecerCancelacion);
        Assert.False(vm.HayConfirmacion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task CA04_SinMotivo_NoSeEnviaLaCancelacion(string motivo)
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = motivo;

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Empty(api.Cancelaciones);
        Assert.Equal("Indica el motivo de la cancelación.", vm.ErrorMotivo);
        Assert.True(vm.FormularioCancelacion);
        Assert.Equal("SOS · Recibido", vm.Titulo);
    }

    [Fact]
    public async Task CA04_ElServidorRechazaElMotivo_SeMuestraJuntoAlCampo()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Motivo";
        api.RespuestaCancelacion = (_, _) => new ResultadoApi<MiCasoDetalle>.Rechazado(400, "No se canceló el SOS.",
            new Dictionary<string, string[]> { ["motivo"] = ["El motivo admite hasta 500 caracteres."] });

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal("El motivo admite hasta 500 caracteres.", vm.ErrorMotivo);
        Assert.True(vm.FormularioCancelacion);
    }

    [Fact]
    public async Task CA05_SosAjenoOInexistente_NoSeMuestra()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Motivo";

        await vm.ConfirmarCancelacion.EjecutarAsync(); // el doble responde 404

        Assert.False(vm.HayCaso);
        Assert.Equal("No se encontró el caso.", vm.Mensaje);
        Assert.False(vm.FormularioCancelacion);
    }

    [Fact]
    public async Task CA08_SinRespuesta_ConservaElMotivoParaReintentarYUnReintentoNoDuplica()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Fue una falsa alarma.";
        api.RespuestaCancelacion = (_, _) => new ResultadoApi<MiCasoDetalle>.SinRespuesta("No hay conexión con el servidor.");

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Equal("No hay respuesta del servidor: el SOS no se canceló. Inténtalo de nuevo.", vm.Mensaje);
        Assert.True(vm.FormularioCancelacion);
        Assert.Equal("Fue una falsa alarma.", vm.MotivoCancelacion);

        // El reintento envía la misma cancelación; el servidor la registra una sola vez y devuelve el caso cancelado.
        api.RespuestaCancelacion = (_, s) => new ResultadoApi<MiCasoDetalle>.Correcto(Cancelado(s.Motivo!));
        await vm.ConfirmarCancelacion.EjecutarAsync();
        Assert.Equal("SOS · Cancelado", vm.Titulo);
        Assert.False(vm.HayMensaje);
        Assert.Equal(2, api.Cancelaciones.Count);
    }

    [Fact]
    public async Task SesionVencida_PideVolverAIngresarSinEnviar()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "Motivo";
        sesiones.Sesion = new SesionApp("token-ficticio", "Ciudadano", Ahora.AddMinutes(-1));

        await vm.ConfirmarCancelacion.EjecutarAsync();

        Assert.Empty(api.Cancelaciones);
        Assert.True(vm.SesionTerminada);
    }

    [Fact]
    public async Task NoCancelar_CierraElFormularioSinEnviar()
    {
        var vm = await AbiertoAsync();
        await vm.MostrarCancelacion.EjecutarAsync();
        vm.MotivoCancelacion = "";
        await vm.ConfirmarCancelacion.EjecutarAsync();

        await vm.OcultarCancelacion.EjecutarAsync();

        Assert.False(vm.FormularioCancelacion);
        Assert.True(vm.OfrecerCancelacion);
        Assert.False(vm.HayErrorMotivo);
        Assert.Empty(api.Cancelaciones);
    }
}
