using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// «Comparación por bloques» (10/11/2026, a pedido del profesor): el distrito en tres bloques, uno por unidad policial, y cada
// bloque partido en las zonas de la cuadrícula de HU29. El fundamento de cada bloque está en herramientas/dataset-jlbyr/README.md
// («Bloques»): las jurisdicciones de las dos comisarías del Plan de Acción Distrital 2024-2027 y el Puesto de Auxilio Rápido de la
// Plataforma Andrés Avelino Cáceres. Es una aproximación por celdas: cada celda va entera al bloque de su centro.
public sealed record DefinicionBloque(string Clave, string Nombre, string Unidad, string Direccion, IReadOnlyList<Zona> Zonas);

public static class BloquesDistrito
{
    // Las celdas se escriben con su referencia de plano (la de Zonas.Nombre con el centro del mapa por defecto, -16.3988 y
    // -71.5369), igual que en salida/bloques.json del dataset. El centro va fijo aquí: la lista no cambia si se mueve el mapa.
    private static readonly Zona Centro = Zona.De(-16.3988, -71.5369);

    public static readonly IReadOnlyList<DefinicionBloque> Todos =
    [
        new("bustamante", "Bustamante y Rivero", "Comisaría PNP José Luis Bustamante y Rivero", "calle Cipreses s/n",
            Celdas("M13 N13 O13 K14 L14 M14 N14 O14 N15 O15 N16 O16 P16 I17 J17 K17 L17 M17 N17 I18 J18 L18 M18 M19")),
        new("avelino", "Avelino Cáceres", "Puesto de Auxilio Rápido de la Plataforma A. A. Cáceres", "plataforma comercial",
            Celdas("J15 K15 L15 M15 I16 J16 K16 L16 M16")),
        new("simon", "Simón Bolívar", "Comisaría PNP Simón Bolívar", "Av. Caracas 629",
            Celdas("O17 P17 Q17 N18 O18 P18 Q18 N19 O19 P19 Q19 R19 O20 P20 Q20 R20 O21 P21 Q21 R21")),
    ];

    private static readonly Dictionary<Zona, DefinicionBloque> PorZona =
        Todos.SelectMany(b => b.Zonas.Select(z => (Zona: z, Bloque: b))).ToDictionary(x => x.Zona, x => x.Bloque);

    public static int TotalZonas => PorZona.Count;

    public static DefinicionBloque? De(Zona zona) => PorZona.GetValueOrDefault(zona);

    // «K15»: letra por columna (de oeste a este) y número por fila (de norte a sur), con el centro en «K10» (Zonas.Nombre al revés).
    private static List<Zona> Celdas(string nombres) =>
        nombres.Split(' ').Select(n => new Zona(Centro.Fila + 10 - int.Parse(n[1..]), Centro.Columna + (n[0] - 'A') - 10)).ToList();

    // Los ejes de los radiales: las 12 categorías de seguridad ciudadana (sin «Otros»), en el orden en que se dibujan. «Otros» y los
    // SOS sin clasificar cuentan en el total, no en un eje.
    public sealed record Eje(string Clave, string Nombre, string Corto);

    public static readonly IReadOnlyList<Eje> Ejes =
    [
        new("robo", "Robo o hurto", "Robo"),
        new("sospechoso", "Persona o vehículo sospechoso", "Sospechosos"),
        new("extorsion", "Extorsión o amenazas", "Extorsión"),
        new("vandalismo", "Daños o vandalismo", "Vandalismo"),
        new("drogas", "Consumo o venta de drogas", "Drogas"),
        new("acoso", "Acoso en la vía pública", "Acoso"),
        new("alcohol", "Alcohol en la vía pública", "Alcohol"),
        new("ruido", "Ruidos molestos", "Ruidos"),
        new("pelea", "Peleas o riñas", "Peleas"),
        new("accidente", "Accidentes de tránsito", "Accidentes"),
        new("violencia", "Violencia familiar", "Violencia"),
        new("riesgo", "Persona en riesgo", "En riesgo"),
    ];

    // La categoría de un caso → su eje (por la clave de su icono, que es estable).
    public static readonly IReadOnlyDictionary<Guid, int> EjeDeCategoria = CategoriaIncidencia.Iniciales
        .Select(c => (c.Id, Indice: Ejes.ToList().FindIndex(e => e.Clave == c.Icono)))
        .Where(x => x.Indice >= 0)
        .ToDictionary(x => x.Id, x => x.Indice);
}

// Casos de un bloque o una zona: uno por eje y, al final, el total (con «Otros» y los sin clasificar). Meses: lo mismo por cada uno
// de los 12 meses de la evolución.
public sealed record ConteoBloques(int[] Casos, int[][] Meses)
{
    public int Total => Casos[^1];

    public static ConteoBloques Vacio() =>
        new(new int[BloquesDistrito.Ejes.Count + 1], Enumerable.Range(0, ConsultarComparacionBloques.Meses).Select(_ => new int[BloquesDistrito.Ejes.Count + 1]).ToArray());

    public void Sumar(ConteoBloques otro)
    {
        for (var i = 0; i < Casos.Length; i++) Casos[i] += otro.Casos[i];
        for (var m = 0; m < Meses.Length; m++)
            for (var i = 0; i < Casos.Length; i++) Meses[m][i] += otro.Meses[m][i];
    }
}

public sealed record ZonaComparada(Zona Zona, ConteoBloques Conteo);

// Zonas: las que tienen MinimoCasosPorZona o más casos en el periodo (privacidad: una zona con menos no se muestra sola).
// ZonasReunidas: cuántas zonas con casos quedaron por debajo; cuentan en el bloque, nunca solas.
public sealed record BloqueComparado(DefinicionBloque Definicion, ConteoBloques Conteo, IReadOnlyList<ZonaComparada> Zonas, int ZonasReunidas);

// Meses: el primer día de cada uno de los 12 meses de la evolución (los últimos completos hasta el fin del periodo). FueraDeLosBloques: casos del
// periodo con ubicación fuera de las zonas de los bloques (no entran en la comparación).
public sealed record ComparacionBloques(IReadOnlyList<BloqueComparado> Bloques, IReadOnlyList<DateOnly> Meses, int FueraDeLosBloques)
{
    public int Total => Bloques.Sum(b => b.Conteo.Total);
}

public sealed class ConsultarComparacionBloques(CadDbContext db, TimeProvider reloj, IOptions<OpcionesAnalitica> opciones)
{
    public const int Meses = 12;

    public async Task<ComparacionBloques> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var fin = periodo.Hasta < ahora ? periodo.Hasta : ahora; // el futuro no se cuenta
        // Los 12 últimos meses completos hasta el fin del periodo, en hora local. Un mes a medias (el que está en curso) se vería
        // como una caída que no ocurrió: no entra.
        var ultimo = TimeZoneInfo.ConvertTime(fin > periodo.Desde ? fin.AddTicks(-1) : periodo.Desde, periodo.Zona);
        var ultimoMes = new DateOnly(ultimo.Year, ultimo.Month, 1);
        if (fin < Inicio(ultimoMes.AddMonths(1), periodo.Zona))
            ultimoMes = ultimoMes.AddMonths(-1);
        var primerMes = ultimoMes.AddMonths(-(Meses - 1));
        var inicioMeses = Inicio(primerMes, periodo.Zona);
        var desde = periodo.Desde < inicioMeses ? periodo.Desde : inicioMeses;

        var casos = (await CasosAnalitica.LeerAsync(db, desde, fin, ct)).Where(c => CasosAnalitica.EsIncidencia(c) && c.Zona is not null);
        var porZona = new Dictionary<Zona, ConteoBloques>();
        var fuera = 0;
        foreach (var caso in casos)
        {
            var enPeriodo = caso.RegistradoEn >= periodo.Desde && caso.RegistradoEn < fin;
            if (BloquesDistrito.De(caso.Zona!) is null)
            {
                if (enPeriodo) fuera++;
                continue;
            }
            if (!porZona.TryGetValue(caso.Zona!, out var conteo))
                porZona[caso.Zona!] = conteo = ConteoBloques.Vacio();
            int? eje = caso.CategoriaId is { } id && BloquesDistrito.EjeDeCategoria.TryGetValue(id, out var k) ? k : null;
            if (enPeriodo)
            {
                conteo.Casos[^1]++;
                if (eje is { } e) conteo.Casos[e]++;
            }
            var local = TimeZoneInfo.ConvertTime(caso.RegistradoEn, periodo.Zona);
            var mes = (local.Year - primerMes.Year) * 12 + local.Month - primerMes.Month;
            if (mes is >= 0 and < Meses)
            {
                conteo.Meses[mes][^1]++;
                if (eje is { } e) conteo.Meses[mes][e]++;
            }
        }

        var minimo = opciones.Value.MinimoCasosPorZona;
        var bloques = BloquesDistrito.Todos.Select(b =>
        {
            var total = ConteoBloques.Vacio();
            var zonas = b.Zonas.Where(porZona.ContainsKey).Select(z => new ZonaComparada(z, porZona[z])).ToList();
            foreach (var z in zonas) total.Sumar(z.Conteo);
            var visibles = zonas.Where(z => z.Conteo.Total >= minimo).OrderByDescending(z => z.Conteo.Total).ToList();
            return new BloqueComparado(b, total, visibles, zonas.Count(z => z.Conteo.Total is > 0 && z.Conteo.Total < minimo));
        }).ToList();
        return new ComparacionBloques(bloques, Enumerable.Range(0, Meses).Select(primerMes.AddMonths).ToList(), fuera);
    }

    private static DateTimeOffset Inicio(DateOnly dia, TimeZoneInfo zona)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zona.GetUtcOffset(local)).ToUniversalTime();
    }
}
