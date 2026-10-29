using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// Bloque E, propuesta 16 — salud del sistema (27/10/2026): si el sistema falla, los demás indicadores mienten. En el periodo:
// la ubicación de los SOS (sin ubicación, precisión del GPS), cuánto tardan en aparecer en la central abierta y en ser abiertos
// por un operador (MarcasCentral), las notificaciones push al sereno (EnviosPush) y los inicios de atención que registró la
// central por el sereno (sin señal) o que el sereno hizo fuera del rango del caso (InicioAtencion).
public sealed record ResultadoContado(string Resultado, int Veces);

public sealed record SaludSistema(
    int Sos, double PorcentajeSinUbicacion, double? PrecisionP50Metros, double? PrecisionP90Metros, double PorcentajePrecisionBaja,
    int Presentados, double? PresentacionP50Segundos, double? PresentacionP90Segundos,
    int Abiertos, double? AperturaP50Segundos, double? AperturaP90Segundos,
    IReadOnlyList<ResultadoContado> Push, double? PorcentajePushEnviadas,
    int IniciosAtencion, double PorcentajeRegistradosPorCentral, double PorcentajeFueraDeRango);

public sealed class ConsultarSaludSistema(CadDbContext db)
{
    // Una precisión peor que esta es baja: el punto del mapa puede estar a más de una cuadra del ciudadano.
    public const double PrecisionBajaMetros = 100;

    public async Task<SaludSistema> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var casos = await db.Casos.AsNoTracking()
            .Where(c => c.RegistradoEn >= periodo.Desde && c.RegistradoEn < periodo.Hasta)
            .Select(c => new { c.Origen, c.RegistradoEn, c.PrecisionMetros, c.UbicacionObtenidaEn, c.PresentadoEnCentralEn, c.AbiertoEnCentralEn })
            .ToListAsync(ct);
        // Los SOS de la app (un caso urgente lo crea el operador con la última posición del acompañamiento).
        var sos = casos.Where(c => c.Origen == OrigenCaso.Sos).ToList();
        var precisiones = sos.Where(c => c.PrecisionMetros is not null).Select(c => c.PrecisionMetros!.Value).ToList();
        var presentacion = sos.Where(c => c.PresentadoEnCentralEn is not null)
            .Select(c => (c.PresentadoEnCentralEn!.Value - c.RegistradoEn).TotalSeconds).ToList();
        var apertura = casos.Where(c => c.AbiertoEnCentralEn is not null)
            .Select(c => (c.AbiertoEnCentralEn!.Value - c.RegistradoEn).TotalSeconds).ToList();

        var push = await db.EnviosPush.AsNoTracking()
            .Where(e => e.EnviadoEn >= periodo.Desde && e.EnviadoEn < periodo.Hasta)
            .GroupBy(e => e.Resultado).Select(g => new ResultadoContado(g.Key, g.Count()))
            .ToListAsync(ct);
        var intentosPush = push.Sum(p => p.Veces);

        var inicios = await db.IniciosAtencion.AsNoTracking()
            .Where(i => i.IniciadaEn >= periodo.Desde && i.IniciadaEn < periodo.Hasta)
            .Select(i => new { PorCentral = i.RegistradoPorOperadorId != null, i.DentroDelRango })
            .ToListAsync(ct);

        static double Porcentaje(int parte, int total) => total == 0 ? 0 : 100.0 * parte / total;
        return new SaludSistema(
            sos.Count, Porcentaje(sos.Count(c => c.PrecisionMetros is null), sos.Count),
            Estadistica.Percentil(precisiones, 0.5), Estadistica.Percentil(precisiones, 0.9),
            Porcentaje(precisiones.Count(p => p > PrecisionBajaMetros), precisiones.Count),
            presentacion.Count, Estadistica.Percentil(presentacion, 0.5), Estadistica.Percentil(presentacion, 0.9),
            apertura.Count, Estadistica.Percentil(apertura, 0.5), Estadistica.Percentil(apertura, 0.9),
            push.OrderByDescending(p => p.Veces).ToList(),
            intentosPush == 0 ? null : Porcentaje(push.Where(p => p.Resultado == nameof(Compartido.ResultadoPush.Enviada)).Sum(p => p.Veces), intentosPush),
            inicios.Count, Porcentaje(inicios.Count(i => i.PorCentral), inicios.Count), Porcentaje(inicios.Count(i => i.DentroDelRango == false), inicios.Count));
    }
}

// Bloque E, propuesta 17 — casos atascados (27/10/2026): los casos abiertos que llevan en su etapa más del tiempo esperado, para
// actuar a tiempo. La etapa empieza con el último evento que llevó el caso a su estado actual (sin él, el registro del caso). Los
// umbrales son parámetros (OpcionesAnalitica), no una regla del servicio: el PO aún no fijó tiempos objetivo.
public sealed record CasoAtascado(Guid CasoId, OrigenCaso Origen, EstadoCaso Estado, DateTimeOffset EnEtapaDesde, double Minutos, int UmbralMinutos)
{
    public double VecesElUmbral => UmbralMinutos == 0 ? 0 : Minutos / UmbralMinutos;
}

public sealed record EtapaAbierta(EstadoCaso Estado, bool Reporte, int Casos, int Atascados);

// Abiertos: todos los casos abiertos con su tiempo en la etapa (el dashboard los dibuja como puntos sobre su carril).
public sealed record CasosAtascados(IReadOnlyList<CasoAtascado> Atascados, IReadOnlyList<EtapaAbierta> PorEtapa, IReadOnlyList<CasoAtascado> Abiertos);

public sealed class ConsultarCasosAtascados(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    private static readonly EstadoCaso[] Abiertos =
        [EstadoCaso.Recibido, EstadoCaso.EnRevision, EstadoCaso.Pendiente, EstadoCaso.Asignada, EstadoCaso.EnCamino, EstadoCaso.EnAtencion];

    // Los eventos con que el caso entra en cada estado.
    private static TipoEventoCaso[] Entradas(EstadoCaso estado) => estado switch
    {
        EstadoCaso.Recibido => [TipoEventoCaso.ReporteRegistrado],
        EstadoCaso.EnRevision => [TipoEventoCaso.RevisionIniciada],
        EstadoCaso.Pendiente => [TipoEventoCaso.SosEmitido, TipoEventoCaso.CasoUrgenteCreado, TipoEventoCaso.ReporteAdmitido, TipoEventoCaso.AsignacionCancelada],
        EstadoCaso.Asignada => [TipoEventoCaso.CasoAsignado, TipoEventoCaso.CasoReasignado],
        EstadoCaso.EnCamino => [TipoEventoCaso.DesplazamientoConfirmado],
        EstadoCaso.EnAtencion => [TipoEventoCaso.AtencionIniciada],
        _ => [],
    };

    private int Umbral(EstadoCaso estado, bool reporte)
    {
        var o = opciones.Value;
        return estado switch
        {
            EstadoCaso.Recibido => o.UmbralRecibidoMinutos,
            EstadoCaso.EnRevision => o.UmbralEnRevisionMinutos,
            EstadoCaso.Pendiente => reporte ? o.UmbralReporteAdmitidoMinutos : o.UmbralSosPendienteMinutos,
            EstadoCaso.Asignada => o.UmbralAsignadaMinutos,
            EstadoCaso.EnCamino => o.UmbralEnCaminoMinutos,
            _ => o.UmbralEnAtencionMinutos,
        };
    }

    public async Task<CasosAtascados> EjecutarAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var casos = await db.Casos.AsNoTracking().Where(c => Abiertos.Contains(c.Estado))
            .Select(c => new { c.Id, c.Origen, c.Estado, c.RegistradoEn })
            .ToListAsync(ct);
        var ids = casos.Select(c => c.Id).ToList();
        var eventos = (await db.EventosCaso.AsNoTracking().Where(e => ids.Contains(e.CasoId))
                .Select(e => new { e.CasoId, e.Tipo, e.OcurridoEn }).ToListAsync(ct))
            .ToLookup(e => e.CasoId);

        var filas = casos.Select(c =>
        {
            var reporte = c.Origen == OrigenCaso.Reporte;
            var entradas = Entradas(c.Estado);
            var desde = eventos[c.Id].Where(e => entradas.Contains(e.Tipo)).Select(e => (DateTimeOffset?)e.OcurridoEn).Max() ?? c.RegistradoEn;
            return new CasoAtascado(c.Id, c.Origen, c.Estado, desde, (ahora - desde).TotalMinutes, Umbral(c.Estado, reporte));
        }).ToList();

        return new CasosAtascados(
            filas.Where(f => f.Minutos > f.UmbralMinutos).OrderByDescending(f => f.VecesElUmbral).ToList(),
            filas.GroupBy(f => (f.Estado, Reporte: f.Origen == OrigenCaso.Reporte)).OrderBy(g => Array.IndexOf(Abiertos, g.Key.Estado))
                .Select(g => new EtapaAbierta(g.Key.Estado, g.Key.Reporte, g.Count(), g.Count(f => f.Minutos > f.UmbralMinutos)))
                .ToList(),
            filas);
    }
}
