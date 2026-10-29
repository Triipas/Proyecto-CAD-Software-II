using System.Globalization;
using System.Text;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Analitica;

// Bloque F, propuesta 19 — demanda esperada (27/10/2026): cuántos casos esperar en cada hora de un día, según el mismo día de la
// semana y la misma hora de las cuatro semanas anteriores (promedio, mínimo y máximo). Sin modelos ni costo: un promedio honesto
// que sirve para preparar el turno.
public sealed record DemandaHora(int Hora, double Esperados, int Minimo, int Maximo);

public sealed class ConsultarDemandaEsperada(CadDbContext db)
{
    public const int Semanas = 4;

    public async Task<IReadOnlyList<DemandaHora>> EjecutarAsync(DateOnly dia, TimeZoneInfo zona, CancellationToken ct)
    {
        DateTimeOffset Inicio(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), zona.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue)));
        var semanas = Enumerable.Range(1, Semanas).Select(s => dia.AddDays(-7 * s)).ToList();
        var desde = Inicio(semanas[^1]).ToUniversalTime();
        var hasta = Inicio(dia).ToUniversalTime();
        var registros = await db.Casos.AsNoTracking().Where(c => c.RegistradoEn >= desde && c.RegistradoEn < hasta)
            .Select(c => c.RegistradoEn).ToListAsync(ct);
        var conteo = registros.Select(r => TimeZoneInfo.ConvertTime(r, zona))
            .Where(l => l.DayOfWeek == dia.DayOfWeek)
            .GroupBy(l => (Dia: DateOnly.FromDateTime(l.DateTime), l.Hour))
            .ToDictionary(g => g.Key, g => g.Count());

        return Enumerable.Range(0, 24).Select(hora =>
        {
            var porSemana = semanas.Select(s => conteo.GetValueOrDefault((s, hora))).ToList();
            return new DemandaHora(hora, porSemana.Average(), porSemana.Min(), porSemana.Max());
        }).ToList();
    }
}

// Bloque F, propuesta 18 — informe del periodo (27/10/2026): los indicadores principales del periodo frente al periodo anterior de
// igual duración, para la gerencia de seguridad ciudadana. Se exporta a CSV (separado por «;», números en es-PE, UTF-8 con BOM
// para que Excel lo abra bien). Reúne cálculos de los otros bloques; no guarda nada.
public sealed record IndicadorInforme(string Grupo, string Nombre, string Unidad, double? Actual, double? Anterior)
{
    public double? Variacion => Actual is { } a && Anterior is { } b ? a - b : null;
    public double? VariacionPorcentual => Actual is { } a && Anterior is { } b && b != 0 ? 100 * (a - b) / b : null;
}

public sealed record InformePeriodo(PeriodoAnalitica Periodo, PeriodoAnalitica Anterior, IReadOnlyList<IndicadorInforme> Indicadores)
{
    private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

    public byte[] ACsv()
    {
        string Fecha(DateTimeOffset momento) => TimeZoneInfo.ConvertTime(momento, Periodo.Zona).ToString("dd/MM/yyyy HH:mm", Peru);
        string Numero(double? valor) => valor is { } v ? Math.Round(v, 2).ToString("0.##", Peru) : "";
        static string Celda(string texto) => texto.Contains(';') || texto.Contains('"') ? $"\"{texto.Replace("\"", "\"\"")}\"" : texto;

        var csv = new StringBuilder();
        csv.AppendLine($"Informe del periodo;{Fecha(Periodo.Desde)} – {Fecha(Periodo.Hasta)}");
        csv.AppendLine($"Periodo anterior;{Fecha(Anterior.Desde)} – {Fecha(Anterior.Hasta)}");
        csv.AppendLine("MVP académico · datos simulados");
        csv.AppendLine();
        csv.AppendLine("Grupo;Indicador;Unidad;Periodo;Periodo anterior;Variación;Variación %");
        foreach (var i in Indicadores)
            csv.AppendLine(string.Join(';', Celda(i.Grupo), Celda(i.Nombre), Celda(i.Unidad), Numero(i.Actual), Numero(i.Anterior),
                Numero(i.Variacion), Numero(i.VariacionPorcentual)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }
}

public sealed class GenerarInformePeriodo(CadDbContext db, ConsultarRespuestaPorZona respuesta, ConsultarEvaluaciones evaluaciones,
    ConsultarPuntosCriticos puntos, ConsultarIndiceFalsos falsos)
{
    public async Task<InformePeriodo> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var anterior = periodo with { Desde = periodo.Desde - (periodo.Hasta - periodo.Desde), Hasta = periodo.Desde };
        var actual = await IndicadoresAsync(periodo, ct);
        var previo = await IndicadoresAsync(anterior, ct);
        return new InformePeriodo(periodo, anterior,
            actual.Select((i, n) => new IndicadorInforme(i.Grupo, i.Nombre, i.Unidad, i.Valor, previo[n].Valor)).ToList());
    }

    private sealed record Medida(string Grupo, string Nombre, string Unidad, double? Valor);

    private async Task<List<Medida>> IndicadoresAsync(PeriodoAnalitica p, CancellationToken ct)
    {
        static double? Porcentaje(int parte, int total) => total == 0 ? null : 100.0 * parte / total;

        var casos = await db.Casos.AsNoTracking().Where(c => c.RegistradoEn >= p.Desde && c.RegistradoEn < p.Hasta)
            .Select(c => new { c.Origen, c.Estado }).ToListAsync(ct);
        var sos = casos.Where(c => c.Origen == OrigenCaso.Sos).ToList();
        var tiempos = (await respuesta.EjecutarAsync(p, reportes: false, ct)).Total;

        var cierres = await db.CierresCaso.AsNoTracking().Where(c => c.CerradoEn >= p.Desde && c.CerradoEn < p.Hasta && c.ResultadoAtencion != null)
            .Select(c => c.ResultadoAtencion!).ToListAsync(ct);
        string[] efectivos = [ResultadosAtencion.ResueltoEnElLugar, ResultadosAtencion.EntregadoAPnp, ResultadosAtencion.Derivado];

        // El mismo índice que el bloque «Ahora» (ConsultarIndiceFalsos): el informe y el dashboard nunca dan dos cifras distintas.
        var indice = await falsos.EjecutarAsync(p, ct);
        var evaluacion = await evaluaciones.EjecutarAsync(p, ct);
        var evaluadas = evaluacion.Distribucion.Sum(d => d.Evaluaciones);
        var acompanamientos = await db.SesionesAcompanamiento.AsNoTracking().CountAsync(s => s.IniciadaEn >= p.Desde && s.IniciadaEn < p.Hasta, ct);

        return
        [
            new("Demanda", "Casos recibidos", "casos", casos.Count),
            new("Demanda", "SOS", "casos", sos.Count),
            new("Demanda", "Reportes", "casos", casos.Count(c => c.Origen == OrigenCaso.Reporte)),
            new("Demanda", "Casos urgentes de acompañamientos", "casos", casos.Count(c => c.Origen == OrigenCaso.Acompanamiento)),
            new("Demanda", "Acompañamientos iniciados", "sesiones", acompanamientos),
            new("Tiempos", "Despacho de SOS (p50)", "min", tiempos.DespachoP50),
            new("Tiempos", "Respuesta a SOS (p50)", "min", tiempos.RespuestaP50),
            new("Tiempos", "Respuesta a SOS (p90)", "min", tiempos.RespuestaP90),
            new("Resultados", "Efectividad de la atención", "%", Porcentaje(cierres.Count(efectivos.Contains), cierres.Count)),
            new("Resultados", "Atenciones sin hallazgo", "%", Porcentaje(cierres.Count(c => c == ResultadosAtencion.SinHallazgo), cierres.Count)),
            new("Resultados", "Falsas alarmas al cerrar", "%", Porcentaje(cierres.Count(c => c == ResultadosAtencion.FalsaAlarma), cierres.Count)),
            new("Veracidad", "Índice de reportes falsos", "%", indice.PorcentajeReportesFalsos),
            new("Veracidad", "Casos descartados por falsos", "casos", indice.SosDescartadosPorFalsos + indice.ReportesFalsos),
            new("Veracidad", "SOS por activación accidental", "%", indice.PorcentajeSosAccidentales),
            new("Veracidad", "SOS cancelados por el ciudadano", "%", Porcentaje(sos.Count(c => c.Estado == EstadoCaso.Cancelada), sos.Count)),
            new("Servicio", "Casos cerrados evaluados", "%", evaluacion.Cerrados == 0 ? null : evaluacion.PorcentajeEvaluados),
            new("Servicio", "Evaluación promedio", "de 5", evaluadas == 0 ? null : evaluacion.Distribucion.Sum(d => d.Puntuacion * d.Evaluaciones) / (double)evaluadas),
            new("Patrones", "Puntos críticos", "lugares", (await puntos.EjecutarAsync(p, ct)).Count),
        ];
    }
}
