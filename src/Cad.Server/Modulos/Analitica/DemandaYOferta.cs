using Cad.Server.Persistencia;

namespace Cad.Server.Modulos.Analitica;

// Bloque B, propuesta 7 — demanda frente a oferta por hora (27/10/2026): cuántos casos entran en cada hora del día y cuántos
// serenos estaban operativos y disponibles en esa hora, en promedio del periodo. Muestra si los turnos acompañan a la demanda.
// La demanda es todo lo que entra (también lo que luego se cancela o descarta: también ocupa a la central).
public sealed record FranjaDemandaOferta(int Hora, double CasosPorDia, double SerenosOperativos, double SerenosDisponibles);

// Lo mismo por día de la semana y hora (mapa de calor día × hora).
public sealed record CeldaDemandaOferta(DayOfWeek Dia, int Hora, double Casos, double SerenosOperativos, double SerenosDisponibles);

// Dias: cuántos días del periodo entran en el cálculo (el periodo que ya pasó). Casos: todos los del periodo.
public sealed record DemandaYOferta(IReadOnlyList<FranjaDemandaOferta> PorHora, IReadOnlyList<CeldaDemandaOferta> PorDiaYHora, double Dias, int Casos);

public sealed class ConsultarDemandaYOferta(CadDbContext db, TimeProvider reloj)
{
    public async Task<DemandaYOferta> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora;
        var casos = await CasosAnalitica.LeerAsync(db, periodo.Desde, fin, ct);
        var serenos = await LineaDeTiempoSerenos.CargarAsync(db, periodo.Desde, fin, ahora, ct);

        // Acumulados por (día, hora) local: segundos de periodo que caen en esa franja, casos y segundos-sereno.
        var franjas = new Dictionary<(DayOfWeek Dia, int Hora), Acumulado>();
        Acumulado En(DateTimeOffset momento)
        {
            var local = TimeZoneInfo.ConvertTime(momento, periodo.Zona);
            var clave = (local.DayOfWeek, local.Hour);
            return franjas.TryGetValue(clave, out var a) ? a : franjas[clave] = new Acumulado();
        }

        // Se recorre el periodo en tramos de una hora local (el primero y el último pueden ser parciales).
        for (var inicio = periodo.Desde; inicio < fin;)
        {
            var local = TimeZoneInfo.ConvertTime(inicio, periodo.Zona);
            var siguienteHoraLocal = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).AddHours(1);
            var tramoFin = siguienteHoraLocal < fin ? siguienteHoraLocal : fin;
            var acumulado = En(inicio);
            acumulado.Segundos += (tramoFin - inicio).TotalSeconds;
            acumulado.SegundosOperativos += serenos.SegundosOperativos(inicio, tramoFin);
            acumulado.SegundosDisponibles += serenos.SegundosDisponibles(inicio, tramoFin);
            inicio = tramoFin;
        }
        foreach (var caso in casos)
            En(caso.RegistradoEn).Casos++;

        // Promedios: casos por cada hora completa de esa franja; serenos = segundos-sereno / segundos de la franja.
        var porDiaYHora = franjas.OrderBy(f => f.Key.Dia).ThenBy(f => f.Key.Hora)
            .Select(f => new CeldaDemandaOferta(f.Key.Dia, f.Key.Hora, Promedio(f.Value.Casos * 3600.0, f.Value.Segundos),
                Promedio(f.Value.SegundosOperativos, f.Value.Segundos), Promedio(f.Value.SegundosDisponibles, f.Value.Segundos)))
            .ToList();
        var porHora = Enumerable.Range(0, 24).Select(hora =>
        {
            var deLaHora = franjas.Where(f => f.Key.Hora == hora).Select(f => f.Value).ToList();
            var segundos = deLaHora.Sum(a => a.Segundos);
            return new FranjaDemandaOferta(hora, Promedio(deLaHora.Sum(a => a.Casos) * 3600.0, segundos),
                Promedio(deLaHora.Sum(a => a.SegundosOperativos), segundos), Promedio(deLaHora.Sum(a => a.SegundosDisponibles), segundos));
        }).ToList();

        return new DemandaYOferta(porHora, porDiaYHora, Math.Max(0, (fin - periodo.Desde).TotalDays), casos.Count);
    }

    private static double Promedio(double total, double segundos) => segundos <= 0 ? 0 : total / segundos;

    private sealed class Acumulado
    {
        public double Segundos { get; set; }
        public int Casos { get; set; }
        public double SegundosOperativos { get; set; }
        public double SegundosDisponibles { get; set; }
    }
}
