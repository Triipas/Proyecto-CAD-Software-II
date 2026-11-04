using Microsoft.ML.Data;

namespace Cad.Proyeccion;

// HU46 (plan, 8.3) — los rasgos de una celda en un bloque, tal como los recibe el modelo. Label: casos en el bloque (solo al
// entrenar). El mismo constructor sirve para entrenar y para predecir en el servidor.
public sealed class FilaRasgos
{
    public float Latitud { get; set; }
    public float Longitud { get; set; }
    public float DiaSemana { get; set; }
    public float Bloque { get; set; }
    public float Mes { get; set; }
    public float Feriado { get; set; }
    public float Quincena { get; set; }
    public float Semana1 { get; set; }
    public float Semanas2 { get; set; }
    public float Semanas4 { get; set; }
    public float Semanas8 { get; set; }
    public float Ultimas24h { get; set; }
    public float Ultimas72h { get; set; }
    public float Vecinas4Semanas { get; set; }
    public float HorasDesdeUltimo { get; set; }
    public float Celda8Semanas { get; set; }
    public float LineaBase { get; set; }
    public float Label { get; set; }

    public static readonly string[] Nombres =
    [
        nameof(Latitud), nameof(Longitud), nameof(DiaSemana), nameof(Bloque), nameof(Mes), nameof(Feriado), nameof(Quincena), nameof(Semana1),
        nameof(Semanas2), nameof(Semanas4), nameof(Semanas8), nameof(Ultimas24h), nameof(Ultimas72h), nameof(Vecinas4Semanas),
        nameof(HorasDesdeUltimo), nameof(Celda8Semanas), nameof(LineaBase),
    ];
}

public sealed class PrediccionRasgos
{
    [ColumnName("Score")]
    public float Casos { get; set; }
}

public static class ConstructorRasgos
{
    private const double TopeHoras = 24 * 56;

    // Casos de la celda en la misma franja semanal, en las k semanas anteriores al bloque.
    private static int MismaFranja(Historial h, Celda c, long bloque, int semanas)
    {
        var total = 0;
        for (var k = 1; k <= semanas; k++)
            total += h.Casos(c, bloque - (long)k * Bloques.PorSemana);
        return total;
    }

    public static FilaRasgos Construir(Historial h, LineaBase baseLinea, Celda celda, long bloque, float etiqueta = 0)
    {
        var fecha = Bloques.Fecha(bloque);
        return new FilaRasgos
        {
            Latitud = (float)celda.LatitudCentro,
            Longitud = (float)celda.LongitudCentro,
            DiaSemana = Bloques.DiaSemana(bloque),
            Bloque = Bloques.DelDia(bloque),
            Mes = fecha.Month,
            Feriado = Calendario.EsFeriado(fecha) ? 1 : 0,
            Quincena = Calendario.EsQuincena(fecha) ? 1 : 0,
            Semana1 = MismaFranja(h, celda, bloque, 1),
            Semanas2 = MismaFranja(h, celda, bloque, 2),
            Semanas4 = MismaFranja(h, celda, bloque, 4),
            Semanas8 = MismaFranja(h, celda, bloque, 8),
            Ultimas24h = h.Casos(celda, bloque - Bloques.PorDia, bloque),
            Ultimas72h = h.Casos(celda, bloque - 3 * Bloques.PorDia, bloque),
            Vecinas4Semanas = celda.Vecinas().Sum(v => MismaFranja(h, v, bloque, 4)),
            HorasDesdeUltimo = (float)h.HorasDesdeUltimo(celda, bloque, TopeHoras),
            Celda8Semanas = h.Casos(celda, bloque - 8L * Bloques.PorSemana, bloque),
            LineaBase = (float)baseLinea.Calcular(celda, bloque),
            Label = etiqueta,
        };
    }
}
