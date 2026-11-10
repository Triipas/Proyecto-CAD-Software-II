using System.Globalization;
using Cad.Server.Modulos.Analitica;

namespace Cad.Server.Panel.Pages.Analitica;

// «Cobertura por zona» como tablero (16/11/2026): las 53 zonas de los bloques, cada una en su lugar del plano (la misma cuadrícula de
// «Comparación por bloques»), sobre el mapa. Aquí se arma la geometría: la fila y la columna de cada celda, qué lados son borde de su
// bloque, a qué distancia queda de la base del bloque (para que se enciendan desde ahí) y el encuadre del mapa de fondo.
public sealed record CeldaTablero(string Nombre, int Columna, int Fila, DefinicionBloque Bloque, ZonaCobertura Datos,
    bool Norte, bool Sur, bool Oeste, bool Este, bool Base, double Distancia)
{
    public bool Baja => Datos.PorcentajeCubierto < TableroZonas.Mitad;
}

public sealed record ResumenBloque(DefinicionBloque Bloque, int Zonas, double Porcentaje, int Bajas);

public sealed record TableroZonas(IReadOnlyList<CeldaTablero> Celdas, IReadOnlyList<ResumenBloque> Bloques, int Columnas, int Filas,
    double Sur, double Oeste, double Norte, double Este)
{
    public const double Mitad = 50;

    // La base de cada bloque: la zona de la comisaría o del puesto (ubicación aproximada, como en el lienzo de «Comparación por bloques»).
    public static readonly IReadOnlyDictionary<string, string> Bases = new Dictionary<string, string>
    {
        ["bustamante"] = "L17",
        ["avelino"] = "K16",
        ["simon"] = "P19",
    };

    // El color de cada bloque sobre el tablero nocturno: los mismos tonos «oscuro» de «Comparación por bloques».
    public static readonly IReadOnlyDictionary<string, string> Colores = new Dictionary<string, string>
    {
        ["bustamante"] = "#3987E5",
        ["avelino"] = "#D95926",
        ["simon"] = "#199E70",
    };

    // Ancho entre alto del encuadre en la proyección del mapa (Mercator): el contenedor toma esta proporción y el mapa, ajustado al
    // encuadre, queda justo debajo de la cuadrícula.
    public double Proporcion
    {
        get
        {
            static double Y(double latitud) => Math.Log(Math.Tan(Math.PI / 4 + latitud * Math.PI / 360));
            return (Este - Oeste) * Math.PI / 180 / (Y(Norte) - Y(Sur));
        }
    }

    public string ProporcionCss => Proporcion.ToString("0.####", CultureInfo.InvariantCulture);

    public static TableroZonas Armar(IReadOnlyList<ZonaCobertura> zonas)
    {
        var medidas = zonas.ToDictionary(z => Zona.De(z.Sur + 1e-7, z.Oeste + 1e-7));
        var celdas = BloquesDistrito.Todos
            .SelectMany(b => b.Zonas.Where(medidas.ContainsKey).Select(z => (Bloque: b, Zona: z, Posicion: BloquesDistrito.Posicion(z))))
            .ToList();
        if (celdas.Count == 0)
            return new([], [], 0, 0, 0, 0, 0, 0);
        var bloquePorPosicion = celdas.ToDictionary(c => c.Posicion, c => c.Bloque.Clave);
        int c0 = celdas.Min(c => c.Posicion.Columna), f0 = celdas.Min(c => c.Posicion.Fila);
        bool Mismo((int, int) posicion, string clave) => bloquePorPosicion.TryGetValue(posicion, out var otro) && otro == clave;
        var bases = celdas.Where(c => Bases.GetValueOrDefault(c.Bloque.Clave) == BloquesDistrito.Referencia(c.Zona))
            .ToDictionary(c => c.Bloque.Clave, c => c.Posicion);

        var resultado = celdas.Select(c =>
        {
            var (col, fila) = c.Posicion;
            var clave = c.Bloque.Clave;
            var distancia = bases.TryGetValue(clave, out var b) ? Math.Sqrt(Math.Pow(col - b.Columna, 2) + Math.Pow(fila - b.Fila, 2)) : 0;
            return new CeldaTablero(BloquesDistrito.Referencia(c.Zona), col - c0, fila - f0, c.Bloque, medidas[c.Zona],
                !Mismo((col, fila - 1), clave), !Mismo((col, fila + 1), clave), !Mismo((col - 1, fila), clave), !Mismo((col + 1, fila), clave),
                bases.TryGetValue(clave, out var propia) && propia == c.Posicion, distancia);
        }).ToList();

        var resumen = BloquesDistrito.Todos.Select(b =>
        {
            var propias = resultado.Where(c => c.Bloque == b).ToList();
            return new ResumenBloque(b, propias.Count, propias.Count == 0 ? 0 : propias.Average(c => c.Datos.PorcentajeCubierto), propias.Count(c => c.Baja));
        }).ToList();

        return new(resultado, resumen, celdas.Max(c => c.Posicion.Columna) - c0 + 1, celdas.Max(c => c.Posicion.Fila) - f0 + 1,
            celdas.Min(c => c.Zona.Sur), celdas.Min(c => c.Zona.Oeste), celdas.Max(c => c.Zona.Norte), celdas.Max(c => c.Zona.Este));
    }
}
