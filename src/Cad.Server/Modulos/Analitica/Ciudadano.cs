using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Bloque D, propuesta 14 — por qué evalúan mal (27/10/2026). Casos de ciudadanos cerrados en el periodo: cuántos se evaluaron,
// cómo, y por qué (motivos de HU32 desde el 27/10/2026). Bajas: 1 a 3 (la app ofrece entonces «qué falló»); altas: 4 y 5.
// Cruzadas con el tiempo de respuesta (registro → primer inicio de atención, HU28) dicen si la nota baja viene de la demora.
public sealed record DistribucionPuntuacion(int Puntuacion, int Evaluaciones);
public sealed record MotivoContado(string Codigo, string Texto, int Veces);
public sealed record TramoRespuesta(string Tramo, int Evaluaciones, double PorcentajeBajas, double? PuntuacionMedia);

public sealed record EvaluacionesAnalitica(int Cerrados, int Evaluados, IReadOnlyList<DistribucionPuntuacion> Distribucion,
    IReadOnlyList<MotivoContado> MotivosBajas, IReadOnlyList<MotivoContado> MotivosAltas,
    double? RespuestaP50Bajas, double? RespuestaP50Altas, IReadOnlyList<TramoRespuesta> PorTiempoDeRespuesta)
{
    public double PorcentajeEvaluados => Cerrados == 0 ? 0 : 100.0 * Evaluados / Cerrados;
}

public sealed class ConsultarEvaluaciones(CadDbContext db)
{
    public const int UltimaBaja = 3;

    // Tramos del tiempo de respuesta, en minutos (límite superior excluido).
    private static readonly (string Tramo, double Hasta)[] Tramos =
        [("Menos de 10 min", 10), ("De 10 a 20 min", 20), ("De 20 a 30 min", 30), ("30 min o más", double.MaxValue)];

    public async Task<EvaluacionesAnalitica> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var cerrados = await (from cierre in db.CierresCaso.AsNoTracking()
                              join caso in db.Casos.AsNoTracking() on cierre.CasoId equals caso.Id
                              where cierre.CerradoEn >= periodo.Desde && cierre.CerradoEn < periodo.Hasta && caso.CiudadanoId != null
                              join evaluacion in db.EvaluacionesCaso.AsNoTracking() on caso.Id equals evaluacion.CasoId into evaluaciones
                              from evaluacion in evaluaciones.DefaultIfEmpty()
                              select new
                              {
                                  caso.Id,
                                  caso.RegistradoEn,
                                  Puntuacion = evaluacion == null ? (int?)null : evaluacion.Puntuacion,
                                  Motivos = evaluacion == null ? null : evaluacion.Motivos,
                                  Atencion = db.EventosCaso.Where(e => e.CasoId == caso.Id && e.Tipo == TipoEventoCaso.AtencionIniciada)
                                      .Min(e => (DateTimeOffset?)e.OcurridoEn),
                              })
                             .ToListAsync(ct);
        var evaluados = cerrados.Where(c => c.Puntuacion is not null)
            .Select(c => (c.Puntuacion!.Value, Motivos: c.Motivos ?? [], Minutos: c.Atencion is { } a ? (a - c.RegistradoEn).TotalMinutes : (double?)null))
            .ToList();
        var bajas = evaluados.Where(e => e.Value <= UltimaBaja).ToList();
        var altas = evaluados.Where(e => e.Value > UltimaBaja).ToList();

        static IReadOnlyList<MotivoContado> Contar(IEnumerable<string[]> motivos) =>
            motivos.SelectMany(m => m).GroupBy(m => m)
                .Select(g => new MotivoContado(g.Key, Catalogo.Texto(MotivosEvaluacion.Todos, g.Key) ?? g.Key, g.Count()))
                .OrderByDescending(m => m.Veces).ToList();

        var conTiempo = evaluados.Where(e => e.Minutos is not null).ToList();
        var porTramo = Tramos.Select((t, i) =>
        {
            var desde = i == 0 ? double.MinValue : Tramos[i - 1].Hasta;
            var grupo = conTiempo.Where(e => e.Minutos >= desde && e.Minutos < t.Hasta).ToList();
            return new TramoRespuesta(t.Tramo, grupo.Count, grupo.Count == 0 ? 0 : 100.0 * grupo.Count(e => e.Value <= UltimaBaja) / grupo.Count,
                grupo.Count == 0 ? null : grupo.Average(e => e.Value));
        }).ToList();

        return new EvaluacionesAnalitica(cerrados.Count, evaluados.Count,
            Enumerable.Range(1, 5).Select(p => new DistribucionPuntuacion(p, evaluados.Count(e => e.Value == p))).ToList(),
            Contar(bajas.Select(e => e.Motivos)), Contar(altas.Select(e => e.Motivos)),
            Estadistica.Percentil([.. bajas.Where(e => e.Minutos is not null).Select(e => e.Minutos!.Value)], 0.5),
            Estadistica.Percentil([.. altas.Where(e => e.Minutos is not null).Select(e => e.Minutos!.Value)], 0.5),
            porTramo);
    }
}

// Bloque D, propuesta 15 — adopción y retención (27/10/2026). Por semana de registro (lunes, hora local): cuántos ciudadanos se
// registraron, cuántos usaron la app alguna vez y cuántos volvieron en otra semana. Por semana del periodo: cuántos la usaron y
// cuántos de ellos eran nuevos. Un uso es un día con actividad (ActividadCiudadano) o en que emitió un SOS, registró un reporte
// o inició un acompañamiento (así cuenta también lo anterior a la actividad diaria). Y la adopción frente a los demás canales:
// reportes por canal de entrada (desde el 27/10/2026).
public sealed record CohorteSemanal(DateOnly Semana, int Registrados, int UsaronAlgunaVez, int VolvieronOtraSemana);
public sealed record ActividadSemanal(DateOnly Semana, int Activos, int Nuevos);
public sealed record ReportesPorCanal(string Canal, string Texto, int Reportes);

public sealed record Adopcion(IReadOnlyList<CohorteSemanal> Cohortes, IReadOnlyList<ActividadSemanal> Actividad, IReadOnlyList<ReportesPorCanal> Canales);

public sealed class ConsultarAdopcion(CadDbContext db)
{
    private static readonly Guid RolCiudadano = Roles.Todos.Single(r => r.Nombre == Roles.Ciudadano).Id;

    public async Task<Adopcion> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        DateOnly Dia(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, periodo.Zona).DateTime);
        static DateOnly Lunes(DateOnly dia) => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
        var primerDia = Dia(periodo.Desde);
        var ultimoDia = Dia(periodo.Hasta.AddTicks(-1));

        var ciudadanos = await db.Users.AsNoTracking()
            .Where(u => db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == RolCiudadano))
            .Select(u => new { u.Id, u.CreadoEn })
            .ToListAsync(ct);
        // Todos los días de uso hasta el fin del periodo (también los anteriores: para saber si alguien es nuevo o vuelve).
        var actividad = await db.ActividadesCiudadano.AsNoTracking().Where(a => a.Dia <= ultimoDia).Select(a => new { a.UsuarioId, a.Dia }).ToListAsync(ct);
        var casos = await db.Casos.AsNoTracking().Where(c => c.CiudadanoId != null && c.RegistradoEn < periodo.Hasta)
            .Select(c => new { UsuarioId = c.CiudadanoId!.Value, c.RegistradoEn }).ToListAsync(ct);
        var sesiones = await db.SesionesAcompanamiento.AsNoTracking().Where(s => s.IniciadaEn < periodo.Hasta)
            .Select(s => new { UsuarioId = s.CiudadanoId, s.IniciadaEn }).ToListAsync(ct);
        var usos = actividad.Select(a => (a.UsuarioId, a.Dia))
            .Concat(casos.Select(c => (c.UsuarioId, Dia: Dia(c.RegistradoEn))))
            .Concat(sesiones.Select(s => (s.UsuarioId, Dia: Dia(s.IniciadaEn))))
            .GroupBy(u => u.UsuarioId)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Dia).ToHashSet());

        var cohortes = ciudadanos
            .Select(c => (c.Id, Semana: Lunes(Dia(c.CreadoEn))))
            .Where(c => c.Semana >= Lunes(primerDia) && c.Semana <= ultimoDia)
            .GroupBy(c => c.Semana).OrderBy(g => g.Key)
            .Select(g => new CohorteSemanal(g.Key, g.Count(),
                g.Count(c => usos.ContainsKey(c.Id)),
                g.Count(c => usos.TryGetValue(c.Id, out var dias) && dias.Select(Lunes).Distinct().Count() >= 2)))
            .ToList();

        var semanas = new List<ActividadSemanal>();
        for (var lunes = Lunes(primerDia); lunes <= ultimoDia; lunes = lunes.AddDays(7))
        {
            var fin = lunes.AddDays(7);
            var activos = usos.Where(u => u.Value.Any(d => d >= lunes && d < fin && d >= primerDia && d <= ultimoDia)).ToList();
            semanas.Add(new ActividadSemanal(lunes, activos.Count, activos.Count(u => u.Value.Min() >= lunes)));
        }

        var canales = await (from detalle in db.DetallesReporte.AsNoTracking()
                             join caso in db.Casos.AsNoTracking() on detalle.CasoId equals caso.Id
                             where caso.RegistradoEn >= periodo.Desde && caso.RegistradoEn < periodo.Hasta
                             group detalle by detalle.Canal into g
                             select new { Canal = g.Key, Reportes = g.Count() })
                            .ToListAsync(ct);
        return new Adopcion(cohortes, semanas, canales
            .Select(c => new ReportesPorCanal(c.Canal ?? "SinRegistrar", Catalogo.Texto(CanalesEntrada.Todos, c.Canal) ?? "Sin registrar", c.Reportes))
            .OrderByDescending(c => c.Reportes).ToList());
    }
}
