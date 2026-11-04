namespace Cad.Proyeccion;

// Un caso del histórico, reducido a lo que la proyección necesita: cuándo, dónde (para saber la celda) y su categoría (para explicar
// la zona). Nunca sale de la proyección: lo que se muestra son celdas (HU29-CA05, HU46-CA03).
public readonly record struct CasoHistorico(DateTimeOffset RegistradoEn, double Latitud, double Longitud, int Categoria = -1);

// Los casos indexados por celda y bloque, para contar en tiempo constante. Todo lo que se calcula para un bloque usa solo los casos
// de bloques anteriores: entrenar y predecir no ven el futuro.
public sealed class Historial
{
    private readonly Dictionary<(Celda, long), int> conteos = [];
    private readonly Dictionary<long, int> totalPorBloque = [];
    private readonly Dictionary<Celda, List<long>> bloquesPorCelda = [];
    private readonly Dictionary<Celda, List<(long Bloque, int Categoria)>> categoriasPorCelda = [];

    public Historial(IEnumerable<CasoHistorico> casos)
    {
        foreach (var caso in casos)
        {
            var celda = Celda.De(caso.Latitud, caso.Longitud);
            var bloque = Bloques.De(caso.RegistradoEn);
            conteos[(celda, bloque)] = conteos.GetValueOrDefault((celda, bloque)) + 1;
            totalPorBloque[bloque] = totalPorBloque.GetValueOrDefault(bloque) + 1;
            if (!bloquesPorCelda.TryGetValue(celda, out var lista))
                bloquesPorCelda[celda] = lista = [];
            lista.Add(bloque);
            if (caso.Categoria >= 0)
            {
                if (!categoriasPorCelda.TryGetValue(celda, out var categorias))
                    categoriasPorCelda[celda] = categorias = [];
                categorias.Add((bloque, caso.Categoria));
            }
            Total++;
            PrimerBloque = Math.Min(PrimerBloque, bloque);
            UltimoBloque = Math.Max(UltimoBloque, bloque);
        }
        foreach (var lista in bloquesPorCelda.Values)
            lista.Sort();
    }

    public int Total { get; }
    public long PrimerBloque { get; } = long.MaxValue;
    public long UltimoBloque { get; } = long.MinValue;

    // Las celdas con al menos un caso: el área que se proyecta.
    public IReadOnlyCollection<Celda> Celdas => bloquesPorCelda.Keys;

    public int Casos(Celda celda, long bloque) => conteos.GetValueOrDefault((celda, bloque));

    public int Casos(Celda celda, long desde, long hasta)
    {
        if (!bloquesPorCelda.TryGetValue(celda, out var lista))
            return 0;
        return Primero(lista, hasta) - Primero(lista, desde);
    }

    public int TotalCiudad(long bloque) => totalPorBloque.GetValueOrDefault(bloque);

    // Horas desde el último caso de la celda antes del bloque (con un tope: una celda sin casos recientes no es «más» lejana).
    public double HorasDesdeUltimo(Celda celda, long bloque, double tope)
    {
        if (!bloquesPorCelda.TryGetValue(celda, out var lista))
            return tope;
        var i = Primero(lista, bloque) - 1;
        return i < 0 ? tope : Math.Min(tope, (bloque - lista[i]) * Bloques.Horas);
    }

    // Las categorías de los casos de la celda en [desde, hasta), para explicar la zona («6 de robo o hurto»).
    public IEnumerable<int> Categorias(Celda celda, long desde, long hasta) =>
        categoriasPorCelda.TryGetValue(celda, out var lista) ? lista.Where(c => c.Bloque >= desde && c.Bloque < hasta).Select(c => c.Categoria) : [];

    // Índice del primer elemento ≥ valor.
    private static int Primero(List<long> lista, long valor)
    {
        int bajo = 0, alto = lista.Count;
        while (bajo < alto)
        {
            var medio = (bajo + alto) / 2;
            if (lista[medio] < valor) bajo = medio + 1; else alto = medio;
        }
        return bajo;
    }
}
