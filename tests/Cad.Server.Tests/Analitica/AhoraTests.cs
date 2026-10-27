using Cad.Contratos.Casos;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// Bloque «Ahora» de la analítica (27/10/2026): tiempos por etapa con p50/p90 (también por prioridad), embudo de reportes, índice
// de falsos y la foto del momento. Los cálculos por periodo usan fechas propias de febrero de 2021. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AhoraTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Violencia = CategoriaIncidencia.Iniciales[0].Id;

    private static DateTimeOffset Local(int mes, int dia, int hora) => new DateTimeOffset(2021, mes, dia, hora, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();
    private static PeriodoAnalitica Semana(int dia) => new(Local(2, dia, 0), Local(2, dia + 7, 0), Lima);

    private Task<T> Servicio<T>(Func<IServiceProvider, Task<T>> uso) => fabrica.ConServiciosAsync(uso);
    private Task Guardar(Action<CadDbContext> agregar) => fabrica.ConBaseDatosAsync(async db => { agregar(db); return await db.SaveChangesAsync(); });

    private sealed record Personas(Guid Ciudadano, Guid Operador, Guid Sereno);
    private async Task<Personas> PersonasAsync() => new((await fabrica.CrearAsync(Roles.Ciudadano)).Id, (await fabrica.CrearAsync(Roles.Operador)).Id,
        (await fabrica.CrearAsync(Roles.Sereno)).Id);

    private static EventoCaso Evento(Caso caso, TipoEventoCaso tipo, Guid actor, double minutos) =>
        EventoCaso.Registrar(caso, tipo, actor, VisibilidadEvento.Interna, caso.RegistradoEn.AddMinutes(minutos));

    private static (Caso, DetalleReporte) Reporte(Guid ciudadano, DateTimeOffset en, Guid? categoria = null, Guid? operadorCentral = null)
    {
        var caso = operadorCentral is { } o
            ? Caso.RegistrarReporteDeLaCentral(o, Guid.NewGuid(), categoria ?? Robo, en)
            : Caso.RegistrarReporte(ciudadano, Guid.NewGuid(), categoria ?? Robo, en);
        return (caso, DetalleReporte.Registrar(caso, categoria ?? Robo, "Descripción ficticia", en, "Texto", null, null, null, "Referencia",
            operadorCentral, null, null, operadorCentral is null ? CanalesEntrada.App : CanalesEntrada.Llamada));
    }

    [Fact]
    public async Task TiemposPorEtapa_ConP50YP90_EnTotalYPorPrioridad()
    {
        var (ciudadano, operador, sereno) = await PersonasAsync();
        var alta = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, Local(2, 1, 10));
        alta.Priorizar(PrioridadesCaso.Alta, PersonasHeridas.Si);
        var media = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, Local(2, 2, 10));
        media.Priorizar(PrioridadesCaso.Media, PersonasHeridas.No);
        var sinPrioridad = Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, Local(2, 3, 10));
        var (reporte, detalle) = Reporte(ciudadano, Local(2, 3, 12));
        await Guardar(db =>
        {
            db.Casos.AddRange(alta, media, sinPrioridad, reporte);
            db.DetallesReporte.Add(detalle);
            db.EventosCaso.AddRange(
                Evento(alta, TipoEventoCaso.IntentoContactoRegistrado, operador, 1), Evento(alta, TipoEventoCaso.CasoAsignado, operador, 3),
                Evento(alta, TipoEventoCaso.DesplazamientoConfirmado, sereno, 5), Evento(alta, TipoEventoCaso.AtencionIniciada, sereno, 15),
                Evento(alta, TipoEventoCaso.CasoCerrado, sereno, 45),
                Evento(media, TipoEventoCaso.CasoAsignado, operador, 5), Evento(media, TipoEventoCaso.CasoAsignado, operador, 8), // la primera cuenta
                Evento(media, TipoEventoCaso.DesplazamientoConfirmado, sereno, 9), Evento(media, TipoEventoCaso.AtencionIniciada, sereno, 25),
                Evento(reporte, TipoEventoCaso.RevisionIniciada, operador, 10), Evento(reporte, TipoEventoCaso.ReporteAdmitido, operador, 20),
                Evento(reporte, TipoEventoCaso.CasoAsignado, operador, 30));
        });
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == alta.Id)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.PresentadoEnCentralEn, alta.RegistradoEn.AddSeconds(3))));

        var grupos = await Servicio(s => s.GetRequiredService<ConsultarTiemposPorEtapa>().EjecutarAsync(Semana(1), CancellationToken.None));

        EtapaTiempo Etapa(string origen, string? prioridad, string etapa) =>
            grupos.Single(g => g.Origen == origen && g.Prioridad == prioridad).Etapas.Single(e => e.Etapa == etapa);
        var despacho = Etapa("SOS", null, "Hasta el despacho");
        Assert.Equal((2, 4.0), (despacho.Casos, despacho.P50Minutos!.Value));
        Assert.Equal(4.8, despacho.P90Minutos!.Value, 6);
        var respuesta = Etapa("SOS", null, "Respuesta (registro → atención)");
        Assert.Equal((2, 20.0, 24.0), (respuesta.Casos, respuesta.P50Minutos!.Value, Math.Round(respuesta.P90Minutos!.Value, 6)));
        Assert.Equal(0.05, Etapa("SOS", null, "Hasta aparecer en la central").P50Minutos!.Value, 6);
        Assert.Equal(15, Etapa("SOS", PrioridadesCaso.Alta, "Respuesta (registro → atención)").P50Minutos);
        Assert.Equal(25, Etapa("SOS", PrioridadesCaso.Media, "Respuesta (registro → atención)").P50Minutos);
        Assert.Equal(0, Etapa("SOS", ConsultarTiemposPorEtapa.SinRegistrar, "Respuesta (registro → atención)").Casos);
        Assert.DoesNotContain(grupos, g => g.Origen == "SOS" && g.Prioridad == PrioridadesCaso.Baja); // sin casos, sin grupo
        Assert.Equal(20, Etapa("Reporte", null, "Hasta la decisión").P50Minutos);
        Assert.Equal(10, Etapa("Reporte", null, "De la admisión al despacho").P50Minutos);
    }

    [Fact]
    public async Task EmbudoDeReportes_PasoAPasoConSusDesgloses()
    {
        var (ciudadano, operador, sereno) = await PersonasAsync();
        var en = Local(2, 9, 9);
        var (admitido, d1) = Reporte(ciudadano, en);
        var (derivado, d2) = Reporte(ciudadano, en);
        var (falso, d3) = Reporte(ciudadano, en);
        var (enRevision, d4) = Reporte(ciudadano, en);
        var (retirado, d5) = Reporte(ciudadano, en);
        var (deLaCentral, d6) = Reporte(ciudadano, en, operadorCentral: operador);
        foreach (var r in new[] { admitido, derivado, falso, enRevision })
            r.IniciarRevision();
        admitido.Decidir(TipoDecisionReporte.Admision);
        admitido.Asignar(); admitido.ConfirmarDesplazamiento(); admitido.IniciarAtencion(); admitido.Cerrar();
        derivado.Decidir(TipoDecisionReporte.Derivacion);
        falso.Decidir(TipoDecisionReporte.Descarte);
        retirado.Cancelar();
        DecisionReporte Decision(Caso caso, TipoDecisionReporte tipo, string? destino = null, string? motivo = null) =>
            DecisionReporte.Registrar(caso, tipo, "Motivo ficticio", destino is null ? null : "SAMU (106)", destino is null ? null : "Llamada", operador,
                en.AddMinutes(20), destino, motivo);
        await Guardar(db =>
        {
            db.Casos.AddRange(admitido, derivado, falso, enRevision, retirado, deLaCentral);
            db.DetallesReporte.AddRange(d1, d2, d3, d4, d5, d6);
            db.EventosCaso.AddRange(new[] { admitido, derivado, falso, enRevision }.Select(r => Evento(r, TipoEventoCaso.RevisionIniciada, operador, 5)));
            db.EventosCaso.AddRange(Evento(admitido, TipoEventoCaso.CasoAsignado, operador, 30), Evento(admitido, TipoEventoCaso.AtencionIniciada, sereno, 50),
                Evento(admitido, TipoEventoCaso.CasoCerrado, sereno, 80));
            db.DecisionesReporte.AddRange(Decision(admitido, TipoDecisionReporte.Admision), Decision(derivado, TipoDecisionReporte.Derivacion, DestinosDerivacion.Samu),
                Decision(falso, TipoDecisionReporte.Descarte, motivo: MotivosDescarte.FalsoOMalintencionado), Decision(deLaCentral, TipoDecisionReporte.Admision));
            db.CierresCaso.Add(CierreCaso.Registrar(admitido, Robo, ResultadosAtencion.ResueltoEnElLugar, "Resultado ficticio", null, sereno, en.AddMinutes(80)));
        });

        var e = await Servicio(s => s.GetRequiredService<ConsultarEmbudoReportes>().EjecutarAsync(Semana(8), CancellationToken.None));

        Assert.Equal((6, 5, 1, 4, 3), (e.Recibidos, e.DeLaApp, e.DeLaCentral, e.Revisados, e.Decididos));
        Assert.Equal((2, 1, 1, 1, 1), (e.Admitidos, e.Derivados, e.Descartados, e.Retirados, e.SinDecidir));
        Assert.Equal((1, 1, 1), (e.Asignados, e.Atendidos, e.Cerrados));
        Assert.Equal(new ConteoCodigo(DestinosDerivacion.Samu, "SAMU (106)", 1), Assert.Single(e.Destinos));
        Assert.Equal(new ConteoCodigo(MotivosDescarte.FalsoOMalintencionado, "Falso o malintencionado", 1), Assert.Single(e.MotivosDescarte));
        Assert.Equal(ResultadosAtencion.ResueltoEnElLugar, Assert.Single(e.ResultadosCierre).Codigo);
    }

    [Fact]
    public async Task IndiceDeFalsos_ReportesPorCategoria_YSosFalsosAccidentalesYFalsasAlarmas()
    {
        var (ciudadano, operador, sereno) = await PersonasAsync();
        var en = Local(2, 16, 9);
        var reportes = new[] { Reporte(ciudadano, en, Robo), Reporte(ciudadano, en, Robo), Reporte(ciudadano, en, Violencia), Reporte(ciudadano, en, Violencia) };
        DecisionReporte Decision(int i, TipoDecisionReporte tipo, string? motivo = null) =>
            DecisionReporte.Registrar(reportes[i].Item1, tipo, "Motivo ficticio", null, null, operador, en.AddHours(1), null, motivo);
        var sos = Enumerable.Range(0, 5).Select(_ => Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, en)).ToList();
        sos[0].Descartar();
        sos[1].Descartar();
        sos[2].Cancelar();
        sos[3].Asignar(); sos[3].ConfirmarDesplazamiento(); sos[3].IniciarAtencion(); sos[3].Cerrar();
        await Guardar(db =>
        {
            db.Casos.AddRange(reportes.Select(r => r.Item1));
            db.DetallesReporte.AddRange(reportes.Select(r => r.Item2));
            db.DecisionesReporte.AddRange(Decision(0, TipoDecisionReporte.Descarte, MotivosDescarte.FalsoOMalintencionado),
                Decision(1, TipoDecisionReporte.Descarte, MotivosDescarte.Duplicado), Decision(2, TipoDecisionReporte.Admision), Decision(3, TipoDecisionReporte.Admision));
            db.Casos.AddRange(sos);
            db.DescartesCaso.AddRange(DescarteCaso.Registrar(sos[0], EstadoCaso.Pendiente, operador, MotivosDescarte.FalsoOMalintencionado, "Broma (ficticio)", en),
                DescarteCaso.Registrar(sos[1], EstadoCaso.Pendiente, operador, MotivosDescarte.ActivacionAccidental, "Bolsillo (ficticio)", en));
            db.CancelacionesSos.Add(CancelacionSos.Registrar(sos[2], MotivosCancelacion.PorError, "Lo envié por error", en));
            db.CierresCaso.Add(CierreCaso.Registrar(sos[3], Robo, ResultadosAtencion.FalsaAlarma, "Resultado ficticio", null, sereno, en.AddHours(1)));
        });

        var i = await Servicio(s => s.GetRequiredService<ConsultarIndiceFalsos>().EjecutarAsync(Semana(15), CancellationToken.None));

        Assert.Equal((4, 1, 25.0), (i.ReportesDecididos, i.ReportesFalsos, i.PorcentajeReportesFalsos!.Value));
        Assert.Equal(new FalsosCategoria("Robo o hurto", 2, 1), i.PorCategoria[0]);
        Assert.Equal(new FalsosCategoria("Violencia familiar", 2, 0), i.PorCategoria[1]);
        Assert.Equal((5, 1, 2, 1), (i.Sos, i.SosDescartadosPorFalsos, i.SosActivacionAccidental, i.SosFalsaAlarmaAlCerrar));
        Assert.Equal((20.0, 40.0), (i.PorcentajeSosFalsos!.Value, i.PorcentajeSosAccidentales!.Value));
        Assert.Equal(2, i.MotivosDescarte.Count);
        Assert.Equal(new ConteoCodigo(MotivosCancelacion.PorError, "Lo envié por error", 1), Assert.Single(i.MotivosCancelacion));
    }

    [Fact]
    public async Task FotoDelMomento_CuentaElSosPendienteSinAbrirYAtascado()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        Task<FotoAhora> Foto() => Servicio(s => s.GetRequiredService<ConsultarAhora>().EjecutarAsync(CancellationToken.None));
        var antes = await Foto();

        await Guardar(db => db.Casos.Add(Caso.EmitirSos(ciudadano, Guid.NewGuid(), null, DateTimeOffset.UtcNow.AddMinutes(-7))));
        var despues = await Foto();

        Assert.Equal(antes.SosPendientes + 1, despues.SosPendientes);
        Assert.Equal(antes.SosSinAbrir + 1, despues.SosSinAbrir);
        Assert.Equal(antes.CasosAtascados + 1, despues.CasosAtascados); // pendiente hace 7 min: umbral de 3
        Assert.True(despues.SosMasAntiguoMinutos >= 7);
    }
}
