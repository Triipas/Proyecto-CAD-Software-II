using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// «Rendimiento de los serenos» (03/11/2026): la única pregunta de la analítica que nombra a una persona. El resto sigue agregado; esta
// la pidió el usuario para que la supervisión vea a cada sereno: cuánto trabaja, qué tan rápido llega, qué opina el ciudadano de su
// atención, cuántos casos se le quitan y por dónde patrulla. No lleva datos del ciudadano: ni nombres ni comentarios.
//
// Se mide sobre lo que ya guardan las asignaciones, el historial del caso, los inicios de atención, los cierres, las evaluaciones,
// las reasignaciones, el historial del estado operativo y las posiciones; no guarda nada. Todo se atribuye por asignación: cada
// etapa del caso cuenta para el sereno que tenía el caso en ese momento, así que un caso reasignado no le cobra al segundo sereno
// el tiempo que el primero no llegó.
public sealed record ZonaSereno(Zona Zona, double Horas, int Casos);

public sealed record RendimientoSereno(
    Guid SerenoId, string Nombre, bool Activo,
    double HorasOperativas, double HorasConCaso,
    int CasosAsignados, int CasosAtendidos, int CasosCerrados,
    int Retirados, int RetiradosPorNoResponder,
    IReadOnlyList<double> Reacciones, IReadOnlyList<double> Llegadas, IReadOnlyList<double> Atenciones,
    int IniciosConPunto, int IniciosFueraDeRango, int IniciosPorCentral, double? DistanciaP50Metros,
    IReadOnlyList<int> Notas, IReadOnlyList<MotivoContado> Motivos, IReadOnlyList<MotivoContado> Resultados,
    IReadOnlyList<int> AtendidosPorHora, IReadOnlyList<ZonaSereno> Zonas)
{
    // Parte de su tiempo en turno con un caso asignado. Sin horas en turno no hay porcentaje.
    public double? PorcentajeOcupado => HorasOperativas <= 0 ? null : Math.Min(100, 100.0 * HorasConCaso / HorasOperativas);

    public double? ReaccionP50 => Estadistica.Percentil(Reacciones, 0.5);
    public double? LlegadaP50 => Estadistica.Percentil(Llegadas, 0.5);
    public double? LlegadaP90 => Estadistica.Percentil(Llegadas, 0.9);
    public double? AtencionP50 => Estadistica.Percentil(Atenciones, 0.5);

    public int Evaluaciones => Notas.Sum();
    public double? Calificacion => Evaluaciones == 0 ? null : Notas.Select((n, i) => n * (i + 1)).Sum() / (double)Evaluaciones;

    // Sobre los inicios que tenían un punto con el que comparar (un reporte con solo una referencia no cuenta).
    public double? PorcentajeFueraDeRango => IniciosConPunto == 0 ? null : 100.0 * IniciosFueraDeRango / IniciosConPunto;
    public double? PorcentajePorCentral => CasosAtendidos == 0 ? null : 100.0 * IniciosPorCentral / CasosAtendidos;
    public double? PorcentajeRetirados => CasosAsignados == 0 ? null : 100.0 * Retirados / CasosAsignados;

    public ZonaSereno? ZonaPrincipal => Zonas.Count == 0 ? null : Zonas[0];
}

// El equipo: los mismos tiempos de todos los serenos juntos, para comparar a cada uno con lo normal del servicio.
public sealed record RendimientoEquipo(IReadOnlyList<RendimientoSereno> Serenos, double? LlegadaP50, double? ReaccionP50, double? AtencionP50,
    double? Calificacion, int Evaluaciones, double? PorcentajeOcupado, double? PorcentajeFueraDeRango);

public sealed class ConsultarRendimientoSerenos(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    private static readonly Guid RolSereno = Roles.Todos.Single(r => r.Nombre == Roles.Sereno).Id;
    private static readonly TipoEventoCaso[] Etapas = [TipoEventoCaso.DesplazamientoConfirmado, TipoEventoCaso.AtencionIniciada, TipoEventoCaso.CasoCerrado];

    public async Task<RendimientoEquipo> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora;

        var asignaciones = await db.Asignaciones.AsNoTracking()
            .Where(a => a.AsignadaEn >= periodo.Desde && a.AsignadaEn < periodo.Hasta)
            .Select(a => new { a.CasoId, a.SerenoId, a.AsignadaEn, a.FinalizadaEn, a.Cancelada })
            .ToListAsync(ct);
        var casos = asignaciones.Select(a => a.CasoId).Distinct().ToList();
        var eventos = (await db.EventosCaso.AsNoTracking()
                .Where(e => casos.Contains(e.CasoId) && Etapas.Contains(e.Tipo))
                .Select(e => new { e.CasoId, e.Tipo, e.OcurridoEn })
                .ToListAsync(ct))
            .ToLookup(e => e.CasoId);
        var inicios = (await db.IniciosAtencion.AsNoTracking()
                .Where(i => casos.Contains(i.CasoId))
                .Select(i => new { i.CasoId, i.SerenoId, i.DentroDelRango, i.DistanciaMetros, i.RegistradoPorOperadorId, i.IniciadaEn })
                .ToListAsync(ct))
            .ToLookup(i => (i.CasoId, i.SerenoId));
        var cierres = await db.CierresCaso.AsNoTracking()
            .Where(c => casos.Contains(c.CasoId))
            .Select(c => new { c.CasoId, c.ResultadoAtencion })
            .ToDictionaryAsync(c => c.CasoId, c => c.ResultadoAtencion, ct);
        var evaluaciones = await db.EvaluacionesCaso.AsNoTracking()
            .Where(e => casos.Contains(e.CasoId))
            .Select(e => new { e.CasoId, e.Puntuacion, e.Motivos })
            .ToDictionaryAsync(e => e.CasoId, ct);
        // Dónde ocurrió cada caso: la ubicación del dispositivo para un SOS, el punto del hecho para un reporte (como en HU29).
        var lugares = (await (from caso in db.Casos.AsNoTracking()
                              where casos.Contains(caso.Id)
                              join detalle in db.DetallesReporte.AsNoTracking() on caso.Id equals detalle.CasoId into detalles
                              from detalle in detalles.DefaultIfEmpty()
                              select new
                              {
                                  caso.Id,
                                  Latitud = caso.Origen == OrigenCaso.Reporte ? (detalle == null ? null : detalle.LatitudHecho) : caso.Latitud,
                                  Longitud = caso.Origen == OrigenCaso.Reporte ? (detalle == null ? null : detalle.LongitudHecho) : caso.Longitud,
                              }).ToListAsync(ct))
            .Where(l => l.Latitud is not null && l.Longitud is not null)
            .ToDictionary(l => l.Id, l => Zona.De(l.Latitud!.Value, l.Longitud!.Value));
        var retiros = await db.Reasignaciones.AsNoTracking()
            .Where(r => r.ReasignadoEn >= periodo.Desde && r.ReasignadoEn < periodo.Hasta)
            .Select(r => new { r.SerenoAnteriorId, r.MotivoCodigo })
            .ToListAsync(ct);

        var turnos = await LineaDeTiempoSerenos.CargarAsync(db, periodo.Desde, fin, ahora, ct);
        var tiempoPorZona = await TiempoPorZonaAsync(periodo.Desde, fin, ct);

        // Los serenos del periodo: los que tuvieron un caso, estuvieron en turno o patrullaron. Con su nombre, aunque hoy la cuenta
        // esté desactivada (sus casos siguen siendo suyos).
        var ids = asignaciones.Select(a => a.SerenoId).Concat(turnos.Serenos.Where(s => turnos.SegundosOperativos(s) > 0)).Concat(tiempoPorZona.Keys)
            .ToHashSet();
        var personas = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == RolSereno))
            .Select(u => new { u.Id, Nombre = u.Nombres + " " + u.Apellidos, u.Activo })
            .ToListAsync(ct);

        var serenos = new List<RendimientoSereno>();
        foreach (var persona in personas)
        {
            var suyas = asignaciones.Where(a => a.SerenoId == persona.Id).ToList();
            var reacciones = new List<double>();
            var llegadas = new List<double>();
            var atenciones = new List<double>();
            var notas = new int[5];
            var motivos = new Dictionary<string, int>();
            var resultados = new Dictionary<string, int>();
            var porHora = new int[24];
            var casosPorZona = new Dictionary<Zona, int>();
            int atendidos = 0, cerrados = 0, conPunto = 0, fueraDeRango = 0, porCentral = 0;
            var distancias = new List<double>();
            foreach (var a in suyas)
            {
                // La etapa cuenta si ocurrió mientras el caso era suyo (el cierre termina la asignación en el mismo instante).
                var hasta = a.FinalizadaEn ?? DateTimeOffset.MaxValue;
                DateTimeOffset? Etapa(TipoEventoCaso tipo) => eventos[a.CasoId]
                    .Where(e => e.Tipo == tipo && e.OcurridoEn >= a.AsignadaEn && e.OcurridoEn <= hasta)
                    .Select(e => (DateTimeOffset?)e.OcurridoEn).Min();
                var enCamino = Etapa(TipoEventoCaso.DesplazamientoConfirmado);
                var atencion = Etapa(TipoEventoCaso.AtencionIniciada);
                var cierre = a.Cancelada ? null : Etapa(TipoEventoCaso.CasoCerrado);
                if (enCamino is { } c)
                    reacciones.Add((c - a.AsignadaEn).TotalMinutes);
                if (atencion is { } t)
                {
                    atendidos++;
                    llegadas.Add((t - a.AsignadaEn).TotalMinutes);
                    porHora[TimeZoneInfo.ConvertTime(t, periodo.Zona).Hour]++;
                    if (lugares.TryGetValue(a.CasoId, out var zona))
                        casosPorZona[zona] = casosPorZona.GetValueOrDefault(zona) + 1;
                    if (cierre is { } finAtencion)
                        atenciones.Add((finAtencion - t).TotalMinutes);
                }
                var inicio = inicios[(a.CasoId, persona.Id)].Where(i => i.IniciadaEn >= a.AsignadaEn && i.IniciadaEn <= hasta).MinBy(i => i.IniciadaEn);
                if (inicio is not null)
                {
                    if (inicio.RegistradoPorOperadorId is not null)
                        porCentral++;
                    if (inicio.DentroDelRango is { } dentro)
                    {
                        conPunto++;
                        if (!dentro) fueraDeRango++;
                    }
                    if (inicio.DistanciaMetros is { } metros)
                        distancias.Add(metros);
                }
                if (cierre is null)
                    continue;
                cerrados++;
                if (cierres.GetValueOrDefault(a.CasoId) is { } resultado)
                    resultados[resultado] = resultados.GetValueOrDefault(resultado) + 1;
                // La evaluación del ciudadano es de la atención de quien cerró el caso.
                if (evaluaciones.TryGetValue(a.CasoId, out var evaluacion) && evaluacion.Puntuacion is >= 1 and <= 5)
                {
                    notas[evaluacion.Puntuacion - 1]++;
                    foreach (var motivo in evaluacion.Motivos)
                        motivos[motivo] = motivos.GetValueOrDefault(motivo) + 1;
                }
            }

            var suyosRetirados = retiros.Where(r => r.SerenoAnteriorId == persona.Id).ToList();
            var operativas = turnos.SegundosOperativos(persona.Id) / 3600;
            var libres = turnos.SegundosDisponibles(persona.Id) / 3600;
            var zonas = tiempoPorZona.GetValueOrDefault(persona.Id) ?? [];
            var zonasDelSereno = zonas.Keys.Concat(casosPorZona.Keys).Distinct()
                .Select(z => new ZonaSereno(z, zonas.GetValueOrDefault(z), casosPorZona.GetValueOrDefault(z)))
                .OrderByDescending(z => z.Horas).ThenByDescending(z => z.Casos)
                .ToList();
            serenos.Add(new RendimientoSereno(
                persona.Id, persona.Nombre, persona.Activo,
                operativas, Math.Max(0, operativas - libres),
                suyas.Select(a => a.CasoId).Distinct().Count(), atendidos, cerrados,
                suyosRetirados.Count, suyosRetirados.Count(r => r.MotivoCodigo == MotivosReasignacion.SerenoNoResponde),
                reacciones, llegadas, atenciones,
                conPunto, fueraDeRango, porCentral, Estadistica.Percentil(distancias, 0.5),
                notas,
                motivos.Select(m => new MotivoContado(m.Key, Catalogo.Texto(MotivosEvaluacion.Todos, m.Key) ?? m.Key, m.Value)).OrderByDescending(m => m.Veces).ToList(),
                ResultadosAtencion.Todos.Select(r => new MotivoContado(r.Codigo, r.Texto, resultados.GetValueOrDefault(r.Codigo))).ToList(),
                porHora, zonasDelSereno));
        }

        var notasEquipo = serenos.Sum(s => s.Evaluaciones);
        var horasEquipo = serenos.Sum(s => s.HorasOperativas);
        var iniciosEquipo = serenos.Sum(s => s.IniciosConPunto);
        return new RendimientoEquipo(
            serenos.OrderBy(s => s.Nombre, StringComparer.CurrentCulture).ToList(),
            Estadistica.Percentil(serenos.SelectMany(s => s.Llegadas).ToList(), 0.5),
            Estadistica.Percentil(serenos.SelectMany(s => s.Reacciones).ToList(), 0.5),
            Estadistica.Percentil(serenos.SelectMany(s => s.Atenciones).ToList(), 0.5),
            notasEquipo == 0 ? null : serenos.Sum(s => (s.Calificacion ?? 0) * s.Evaluaciones) / notasEquipo,
            notasEquipo,
            horasEquipo <= 0 ? null : 100.0 * serenos.Sum(s => s.HorasConCaso) / horasEquipo,
            iniciosEquipo == 0 ? null : 100.0 * serenos.Sum(s => s.IniciosFueraDeRango) / iniciosEquipo);
    }

    // Horas que pasó cada sereno en cada zona: cada posición vale hasta la siguiente, con el mismo tope de antigüedad que la
    // cobertura (una posición más vieja ya no dice dónde está). Las posiciones solo llegan mientras el sereno está en turno (HU18).
    private async Task<Dictionary<Guid, Dictionary<Zona, double>>> TiempoPorZonaAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var tope = TimeSpan.FromMinutes(Math.Max(1, opciones.Value.AntiguedadPosicionMinutos));
        var posiciones = await db.PosicionesSereno.AsNoTracking()
            .Where(p => p.ObtenidaEn >= desde && p.ObtenidaEn < hasta)
            .OrderBy(p => p.SerenoId).ThenBy(p => p.ObtenidaEn)
            .Select(p => new { p.SerenoId, p.ObtenidaEn, p.Latitud, p.Longitud })
            .ToListAsync(ct);
        var resultado = new Dictionary<Guid, Dictionary<Zona, double>>();
        foreach (var grupo in posiciones.GroupBy(p => p.SerenoId))
        {
            var lista = grupo.ToList();
            var horas = new Dictionary<Zona, double>();
            for (var i = 0; i < lista.Count; i++)
            {
                var siguiente = i + 1 < lista.Count ? lista[i + 1].ObtenidaEn : hasta;
                var dura = siguiente - lista[i].ObtenidaEn;
                if (dura > tope) dura = tope;
                var zona = Zona.De(lista[i].Latitud, lista[i].Longitud);
                horas[zona] = horas.GetValueOrDefault(zona) + dura.TotalHours;
            }
            resultado[grupo.Key] = horas;
        }
        return resultado;
    }
}
