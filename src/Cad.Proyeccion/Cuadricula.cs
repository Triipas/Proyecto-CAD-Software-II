namespace Cad.Proyeccion;

// La misma cuadrícula que el mapa de concentración (HU29, ConsultarConcentracion): celdas de 0,005° (unos 550 m). La proyección
// reutiliza la cuadrícula para que «la zona» signifique lo mismo en el histórico y en la proyección.
public readonly record struct Celda(long Fila, long Columna)
{
    public const double Tamano = 0.005;

    public static Celda De(double latitud, double longitud) => new((long)Math.Floor(latitud / Tamano), (long)Math.Floor(longitud / Tamano));

    public double Sur => Fila * Tamano;
    public double Oeste => Columna * Tamano;
    public double Norte => (Fila + 1) * Tamano;
    public double Este => (Columna + 1) * Tamano;
    public double LatitudCentro => (Fila + 0.5) * Tamano;
    public double LongitudCentro => (Columna + 0.5) * Tamano;

    // Las 8 celdas que la rodean (3 × 3 sin la propia).
    public IEnumerable<Celda> Vecinas()
    {
        for (var df = -1; df <= 1; df++)
            for (var dc = -1; dc <= 1; dc++)
                if (df != 0 || dc != 0)
                    yield return new Celda(Fila + df, Columna + dc);
    }

    public string Clave => $"{Fila}:{Columna}";

    public static Celda DeClave(string clave)
    {
        var partes = clave.Split(':');
        return new Celda(long.Parse(partes[0]), long.Parse(partes[1]));
    }
}

// HU46 (plan, 8.3) — la unidad de tiempo: bloques de 4 h en la hora local de Arequipa (UTC−5, sin horario de verano), numerados
// desde un lunes de referencia. Seis por día y 42 por semana: la «franja semanal» es el día de la semana con el bloque.
public static class Bloques
{
    public const int Horas = 4;
    public const int PorDia = 24 / Horas;
    public const int PorSemana = PorDia * 7;
    public static readonly TimeSpan HusoHorario = TimeSpan.FromHours(-5);
    // Lunes 3 de enero de 2000, 00:00 en hora local.
    private static readonly DateTime Origen = new(2000, 1, 3, 0, 0, 0, DateTimeKind.Unspecified);

    public static long De(DateTimeOffset momento) =>
        (long)Math.Floor((momento.ToOffset(HusoHorario).DateTime - Origen).TotalHours / Horas);

    public static DateTimeOffset Inicio(long bloque) => new(Origen.AddHours(bloque * Horas), HusoHorario);

    // 0 = lunes … 6 = domingo.
    public static int DiaSemana(long bloque) => (int)(((bloque / PorDia) % 7 + 7) % 7);

    public static int DelDia(long bloque) => (int)((bloque % PorDia + PorDia) % PorDia);

    public static int Franja(long bloque) => DiaSemana(bloque) * PorDia + DelDia(bloque);

    public static DateOnly Fecha(long bloque) => DateOnly.FromDateTime(Inicio(bloque).DateTime);

    private static readonly string[] Dias = ["lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo"];

    public static string TextoFranja(long bloque) =>
        $"{Dias[DiaSemana(bloque)]} {DelDia(bloque) * Horas:00}–{(DelDia(bloque) + 1) * Horas:00} h";
}

// Calendario local que cambia la actividad (plan, 4.3): feriados nacionales y de Arequipa, y los días de pago de quincena.
public static class Calendario
{
    private static readonly HashSet<(int Mes, int Dia)> Feriados =
    [
        (1, 1), (5, 1), (6, 7), (6, 29), (7, 23), (7, 28), (7, 29), (8, 6), (8, 15), (8, 30), (10, 8), (11, 1), (12, 8), (12, 9), (12, 25),
    ];

    public static bool EsFeriado(DateOnly dia) => Feriados.Contains((dia.Month, dia.Day));

    // El 15 y el último día del mes, y el día siguiente.
    public static bool EsQuincena(DateOnly dia)
    {
        var ultimo = DateTime.DaysInMonth(dia.Year, dia.Month);
        return dia.Day is 15 or 16 || dia.Day == ultimo || dia.Day == 1;
    }

    // Aniversario de Arequipa (15 de agosto): la fecha local de más actividad del año.
    public static bool EsAniversarioArequipa(DateOnly dia) => dia is { Month: 8, Day: 15 };
}
