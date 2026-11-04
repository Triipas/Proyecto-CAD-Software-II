using Cad.Proyeccion;

namespace Cad.DatosDemo;

// Índices de las categorías del catálogo (CategoriaIncidencia.Iniciales), para no depender del servidor.
public static class CategoriasDemo
{
    public const int ViolenciaFamiliar = 0, Robo = 1, Drogas = 2, Otros = 3, Acoso = 4, Extorsion = 5, Alcohol = 6, Peleas = 7,
        Sospechoso = 8, Vandalismo = 9, Accidente = 10, Ruidos = 11, PersonaEnRiesgo = 12;
}

// Un foco: dónde (centro y dispersión), cuánto (casos por día en su hora pico), cuándo (perfil por hora y día) y qué. Puede cambiar con
// las semanas: aparecer y crecer, apagarse o desplazarse.
public sealed record Foco(string Nombre, double Latitud, double Longitud, double SigmaMetros, double CasosPorDia, int[] Categorias,
    Func<int, int, double> Perfil, int SemanaInicio = 0, int? SemanaFin = null, double LatitudFinal = double.NaN, double LongitudFinal = double.NaN,
    bool Emergente = false);

public sealed record CasoSimulado(Guid Id, bool EsSos, DateTimeOffset RegistradoEn, double Latitud, double Longitud, int Categoria, string Origen);

public sealed record Mundo(string Nombre, int Semilla, DateTimeOffset Inicio, int Semanas, IReadOnlyList<CasoSimulado> Casos,
    IReadOnlyDictionary<(Celda, long), double> Intensidad, IReadOnlyList<Celda> Celdas);

// HU46 (S6-T02; plan, 4.1 a 4.3) — un proceso generador más rico que el modelo que tiene que encontrarlo: focos fijos con perfil
// propio sobre lugares reales del distrito J. L. Bustamante y Rivero, un foco que aparece, uno que se apaga y uno que se desplaza,
// repetición cercana (cada caso aumenta la probabilidad de otro cerca en las horas siguientes), calendario (quincenas, feriados,
// aniversario de Arequipa), ruido uniforme y sesgo de reporte (una zona reporta menos). Guarda la intensidad de fondo verdadera de cada
// celda y bloque (sin la repetición cercana): es el oráculo con que se mide qué parte del mejor resultado posible alcanza un modelo.
public static class GeneradorMundo
{
    // Límites del distrito (fuentes/lugares.json del dataset JLByR).
    public const double Sur = -16.4546, Norte = -16.4111, Oeste = -71.5471, Este = -71.5043;
    private const double MetrosPorGrado = 111_320;

    // Mundo A (desarrollo) y B (prueba): focos y semillas distintos; B se usa una sola vez, al final (plan, 4.1).
    public static IReadOnlyList<Foco> Focos(string mundo) => mundo.ToUpperInvariant() == "B" ? FocosB : FocosA;

    private static double Horas(int hora, params (int Desde, int Hasta)[] rangos) =>
        rangos.Any(r => r.Desde <= r.Hasta ? hora >= r.Desde && hora < r.Hasta : hora >= r.Desde || hora < r.Hasta) ? 1 : 0.15;

    private static readonly Foco[] FocosA =
    [
        new("Mercado de productores (Av. Andrés Avelino Cáceres)", -16.4234, -71.5365, 300, 5.0, [CategoriasDemo.Robo, CategoriasDemo.Robo, CategoriasDemo.Sospechoso],
            (h, d) => d < 6 ? Horas(h, (10, 20)) : 0.3),
        new("Zona de bares (calle Paz Soldán)", -16.4210, -71.5255, 250, 3.5, [CategoriasDemo.Peleas, CategoriasDemo.Alcohol, CategoriasDemo.Ruidos],
            (h, d) => d is 4 or 5 ? Horas(h, (22, 3)) : 0.2),
        new("Avenida principal (Av. Dolores)", -16.4170, -71.5185, 200, 2.0, [CategoriasDemo.Accidente],
            (h, d) => Horas(h, (7, 9), (18, 20))),
        new("Urb. La Melgariana", -16.4274, -71.5214, 400, 1.5, [CategoriasDemo.ViolenciaFamiliar, CategoriasDemo.Ruidos],
            (h, d) => d >= 5 ? Horas(h, (14, 23)) : Horas(h, (18, 23)) * 0.7),
        new("Paradero Cruce Simón", -16.4439, -71.5180, 250, 2.0, [CategoriasDemo.Robo, CategoriasDemo.Sospechoso],
            (h, d) => Horas(h, (4, 7), (17, 21))),
        new("Parque de la Urb. La Esperanza", -16.4186, -71.5288, 300, 1.5, [CategoriasDemo.Drogas, CategoriasDemo.Alcohol],
            (h, d) => Horas(h, (16, 24))),
        // Cambiantes (plan, 4.3, punto 2).
        new("Foco emergente (centro ferial El Polo)", -16.4272, -71.5389, 250, 3.0, [CategoriasDemo.Robo, CategoriasDemo.Extorsion],
            (h, d) => Horas(h, (11, 21)), SemanaInicio: 60, Emergente: true),
        new("Foco que se apaga (Urb. La Encalada)", -16.4298, -71.5166, 300, 2.5, [CategoriasDemo.Vandalismo, CategoriasDemo.Sospechoso],
            (h, d) => Horas(h, (19, 2)), SemanaFin: 50),
        new("Foco que se desplaza (de La Isla hacia Kosto)", -16.4263, -71.5336, 250, 2.0, [CategoriasDemo.Acoso, CategoriasDemo.Robo],
            (h, d) => Horas(h, (12, 22)), LatitudFinal: -16.4180, LongitudFinal: -71.5230),
    ];

    private static readonly Foco[] FocosB =
    [
        new("Mercado Virgen del Chapi", -16.4244, -71.5341, 300, 4.5, [CategoriasDemo.Robo, CategoriasDemo.Sospechoso],
            (h, d) => d < 6 ? Horas(h, (9, 19)) : 0.3),
        new("Discotecas (Yuyay y Munays)", -16.4207, -71.5255, 250, 3.0, [CategoriasDemo.Peleas, CategoriasDemo.Alcohol],
            (h, d) => d is 4 or 5 ? Horas(h, (23, 4)) : 0.2),
        new("Av. Estados Unidos", -16.4330, -71.5260, 200, 2.0, [CategoriasDemo.Accidente],
            (h, d) => Horas(h, (7, 9), (17, 20))),
        new("Urb. Simón Bolívar", -16.4380, -71.5300, 400, 1.8, [CategoriasDemo.ViolenciaFamiliar, CategoriasDemo.Ruidos],
            (h, d) => Horas(h, (15, 23))),
        new("Paraderos de la Av. Lambramani", -16.4150, -71.5190, 250, 2.2, [CategoriasDemo.Robo],
            (h, d) => Horas(h, (5, 8), (18, 21))),
        new("Foco emergente (colegio San Vicente de Paúl)", -16.4377, -71.5233, 250, 2.5, [CategoriasDemo.Drogas, CategoriasDemo.Acoso],
            (h, d) => d < 5 ? Horas(h, (12, 18)) : 0.3, SemanaInicio: 55, Emergente: true),
        new("Foco que se apaga (estadio)", -16.4450, -71.5370, 300, 2.0, [CategoriasDemo.Vandalismo],
            (h, d) => Horas(h, (18, 24)), SemanaFin: 45),
        new("Foco que se desplaza (de El Polo hacia La Isla)", -16.4300, -71.5400, 250, 2.0, [CategoriasDemo.Robo, CategoriasDemo.Acoso],
            (h, d) => Horas(h, (10, 21)), LatitudFinal: -16.4250, LongitudFinal: -71.5300),
    ];

    // Repetición cercana: cada caso genera en promedio 0,25 casos más, a ~150 m y en las ~6 h siguientes.
    private const double Descendencia = 0.25, DispersionRepeticionMetros = 150, HorasRepeticion = 6;
    private const double RuidoPorDia = 4.0; // ≈ 15 % de los casos, en cualquier parte del distrito
    // Sesgo de reporte: el sureste reporta el 60 % de lo que ocurre.
    private static bool ZonaQueReportaMenos(double lat, double lon) => lat < -16.440 && lon > -71.520;

    public static Mundo Generar(string mundo, int semanas, int semilla, DateTimeOffset? fin = null)
    {
        var azar = new Random(semilla);
        var focos = Focos(mundo);
        // El mundo termina en «fin» (por omisión, el inicio del bloque actual) y empieza un número entero de semanas antes, un lunes.
        var ultimo = Bloques.De(fin ?? DateTimeOffset.UtcNow);
        var primerBloque = ultimo - (long)semanas * Bloques.PorSemana;
        primerBloque -= (primerBloque % Bloques.PorSemana + Bloques.PorSemana) % Bloques.PorSemana;
        var inicio = Bloques.Inicio(primerBloque);
        var celdas = CeldasDelDistrito();
        var intensidad = new Dictionary<(Celda, long), double>();
        var casos = new List<CasoSimulado>();
        var sombra = new Dictionary<int, Dictionary<Celda, double>>(); // reparto de cada foco entre celdas, por semana

        var totalHoras = semanas * 7 * 24;
        for (var hora = 0; hora < totalHoras; hora++)
        {
            var momento = inicio.AddHours(hora);
            var local = momento.ToOffset(Bloques.HusoHorario);
            var dia = DateOnly.FromDateTime(local.DateTime);
            var semana = hora / (7 * 24);
            var diaSemana = ((int)local.DayOfWeek + 6) % 7;
            var calendario = (Calendario.EsQuincena(dia) ? 1.2 : 1) * (Calendario.EsFeriado(dia) ? 1.3 : 1) * (Calendario.EsAniversarioArequipa(dia) ? 1.5 : 1);
            var bloque = Bloques.De(momento);

            for (var f = 0; f < focos.Count; f++)
            {
                var foco = focos[f];
                var factor = FactorSemanal(foco, semana, semanas);
                if (factor <= 0)
                    continue;
                var (lat, lon) = Centro(foco, semana, semanas);
                // casos por hora = por día en su pico / 24 × perfil (normalizado para que el pico valga 1) × calendario
                var tasa = foco.CasosPorDia / 24 * foco.Perfil(local.Hour, diaSemana) * calendario * factor * 2.2;
                // Oráculo: la intensidad de fondo repartida entre las celdas según la dispersión del foco.
                var clave = f * 10_000 + semana;
                if (!sombra.TryGetValue(clave, out var reparto))
                    sombra[clave] = reparto = Reparto(lat, lon, foco.SigmaMetros, celdas);
                foreach (var (celda, parte) in reparto)
                    intensidad[(celda, bloque)] = intensidad.GetValueOrDefault((celda, bloque)) + tasa * parte;
                for (var k = Poisson(azar, tasa); k > 0; k--)
                {
                    var (pl, plon) = Punto(azar, lat, lon, foco.SigmaMetros);
                    casos.Add(Caso(azar, momento.AddMinutes(azar.Next(60)), pl, plon, foco.Categorias[azar.Next(foco.Categorias.Length)], foco.Nombre));
                }
            }

            // Ruido uniforme, con más actividad de día.
            var tasaRuido = RuidoPorDia / 24 * (local.Hour is >= 8 and < 23 ? 1.3 : 0.5) * calendario;
            foreach (var celda in celdas)
                intensidad[(celda, bloque)] = intensidad.GetValueOrDefault((celda, bloque)) + tasaRuido / celdas.Count;
            for (var k = Poisson(azar, tasaRuido); k > 0; k--)
                casos.Add(Caso(azar, momento.AddMinutes(azar.Next(60)), Sur + azar.NextDouble() * (Norte - Sur), Oeste + azar.NextDouble() * (Este - Oeste),
                    azar.Next(13), "Ruido"));
        }

        // Repetición cercana (proceso autoexcitado de una generación): hijos cerca en el espacio y poco después.
        var hijos = new List<CasoSimulado>();
        foreach (var padre in casos)
        {
            for (var k = Poisson(azar, Descendencia); k > 0; k--)
            {
                var retraso = TimeSpan.FromHours(-HorasRepeticion * Math.Log(1 - azar.NextDouble()));
                var (lat, lon) = Punto(azar, padre.Latitud, padre.Longitud, DispersionRepeticionMetros);
                if (padre.RegistradoEn + retraso < inicio.AddDays(7 * semanas))
                    hijos.Add(Caso(azar, padre.RegistradoEn + retraso, lat, lon, padre.Categoria, "Repetición cercana"));
            }
        }
        casos.AddRange(hijos);

        // Sesgo de reporte: lo que ocurre allí se reporta menos (la intensidad verdadera no cambia).
        var reportados = casos.Where(c => !ZonaQueReportaMenos(c.Latitud, c.Longitud) || azar.NextDouble() < 0.6)
            .Where(c => c.Latitud is >= Sur and <= Norte && c.Longitud is >= Oeste and <= Este)
            .OrderBy(c => c.RegistradoEn)
            .ToList();
        return new Mundo(mundo.ToUpperInvariant(), semilla, inicio, semanas, reportados, intensidad, celdas);
    }

    private static double FactorSemanal(Foco foco, int semana, int semanas)
    {
        // Un mundo más corto que el pensado (78 semanas) escala las semanas de cambio.
        var escala = semanas / 78.0;
        if (semana < foco.SemanaInicio * escala)
            return 0;
        if (foco.Emergente)
            return Math.Min(1, (semana - foco.SemanaInicio * escala + 1) / (8 * escala + 1));
        if (foco.SemanaFin is { } finSemana && semana >= finSemana * escala)
            return Math.Max(0, 1 - (semana - finSemana * escala) / (4 * escala + 1));
        return 1;
    }

    private static (double, double) Centro(Foco foco, int semana, int semanas)
    {
        if (double.IsNaN(foco.LatitudFinal))
            return (foco.Latitud, foco.Longitud);
        var t = Math.Clamp(semana / (double)Math.Max(1, semanas - 1), 0, 1);
        return (foco.Latitud + t * (foco.LatitudFinal - foco.Latitud), foco.Longitud + t * (foco.LongitudFinal - foco.Longitud));
    }

    private static List<Celda> CeldasDelDistrito()
    {
        var celdas = new List<Celda>();
        var a = Celda.De(Sur, Oeste);
        var b = Celda.De(Norte, Este);
        for (var f = a.Fila; f <= b.Fila; f++)
            for (var c = a.Columna; c <= b.Columna; c++)
                celdas.Add(new Celda(f, c));
        return celdas;
    }

    // Qué parte de una gaussiana centrada en (lat, lon) cae en cada celda (integral exacta por separado en cada eje).
    private static Dictionary<Celda, double> Reparto(double lat, double lon, double sigmaMetros, List<Celda> celdas)
    {
        var sLat = sigmaMetros / MetrosPorGrado;
        var sLon = sigmaMetros / (MetrosPorGrado * Math.Cos(lat * Math.PI / 180));
        double Masa(double desde, double hasta, double centro, double sigma) => 0.5 * (Erf((hasta - centro) / (sigma * Math.Sqrt(2))) - Erf((desde - centro) / (sigma * Math.Sqrt(2))));
        var reparto = new Dictionary<Celda, double>();
        foreach (var c in celdas)
        {
            var parte = Masa(c.Sur, c.Norte, lat, sLat) * Masa(c.Oeste, c.Este, lon, sLon);
            if (parte > 1e-6)
                reparto[c] = parte;
        }
        return reparto;
    }

    private static (double, double) Punto(Random azar, double lat, double lon, double sigmaMetros)
    {
        double Normal() => Math.Sqrt(-2 * Math.Log(1 - azar.NextDouble())) * Math.Cos(2 * Math.PI * azar.NextDouble());
        return (lat + Normal() * sigmaMetros / MetrosPorGrado, lon + Normal() * sigmaMetros / (MetrosPorGrado * Math.Cos(lat * Math.PI / 180)));
    }

    private static CasoSimulado Caso(Random azar, DateTimeOffset momento, double lat, double lon, int categoria, string origen) =>
        new(new Guid(Enumerable.Range(0, 16).Select(_ => (byte)azar.Next(256)).ToArray()), azar.NextDouble() < 0.15, momento,
            Math.Round(lat, 6), Math.Round(lon, 6), categoria, origen);

    private static int Poisson(Random azar, double media)
    {
        if (media <= 0)
            return 0;
        var limite = Math.Exp(-media);
        var k = 0;
        var p = azar.NextDouble();
        while (p > limite)
        {
            k++;
            p *= azar.NextDouble();
        }
        return k;
    }

    // Aproximación de Abramowitz y Stegun (error < 1,5e-7).
    private static double Erf(double x)
    {
        var signo = Math.Sign(x);
        x = Math.Abs(x);
        var t = 1 / (1 + 0.3275911 * x);
        var y = 1 - ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return signo * y;
    }
}
