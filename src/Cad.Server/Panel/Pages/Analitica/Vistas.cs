using System.Globalization;
using System.Net;
using System.Text;
using Cad.Server.Modulos.Analitica;
using Microsoft.AspNetCore.Html;

namespace Cad.Server.Panel.Pages.Analitica;

// Piezas de presentación del dashboard (27/10/2026): formatos en es-PE, las siete preguntas, los nombres de zona, la regla y los
// carriles (dibujados en SVG desde el servidor: sin JavaScript para leer una respuesta) y las señales de la salud del sistema.
public static class Formato
{
    private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

    // Cifras con coma decimal, como el resto del panel («7,8 %»).
    public static string Decimal(double valor, int decimales = 1) =>
        Math.Round(valor, decimales).ToString(decimales == 0 ? "0" : "0." + new string('#', decimales), Peru).Replace('.', ',');

    public static string Porcentaje(double valor, int decimales = 1) => $"{Decimal(valor, decimales)} %";

    // «40 s», «14 min», «1 h 05 min». Sin dato: «—».
    public static string Minutos(double? minutos)
    {
        if (minutos is not { } m)
            return "—";
        if (m < 1)
            return $"{Math.Round(m * 60)} s";
        if (m < 60)
            return $"{Math.Round(m)} min";
        var total = (int)Math.Round(m);
        return $"{total / 60} h {total % 60:00} min";
    }

    public static string Horas(double? horas) =>
        horas is not { } h ? "—" : h < 1 ? Minutos(h * 60) : h < 48 ? $"{Decimal(h)} h" : $"{Decimal(h / 24)} días";

    public static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";
}

public static class Preguntas
{
    public sealed record Definicion(string Clave, string Titulo, string Icono);

    // Iconos de trazo de 24×24, como _Icono.
    public static readonly IReadOnlyList<Definicion> Todas =
    [
        new("ahora", "¿Cómo está la central ahora?", "<circle cx=\"12\" cy=\"12\" r=\"8\"/><path d=\"M12 8v4l3 2\"/>"),
        new("tiempo", "¿Llegamos a tiempo?", "<path d=\"M4 17h16M4 17V7m4 10v-4m4 4V9m4 8v-6\"/>"),
        new("falsos", "¿Cuánto de lo que entra es falso?", "<path d=\"M12 3 21 19H3z\"/><path d=\"M12 10v4m0 3v.01\"/>"),
        new("cobertura", "¿Dónde falta cobertura?", "<path d=\"M12 21s-7-6.2-7-11a7 7 0 0 1 14 0c0 4.8-7 11-7 11z\"/><circle cx=\"12\" cy=\"10\" r=\"2.5\"/>"),
        new("repite", "¿Qué se repite?", "<path d=\"M4 12a8 8 0 0 1 14-5.3M20 12a8 8 0 0 1-14 5.3\"/><path d=\"M18 3v4h-4M6 21v-4h4\"/>"),
        new("ciudadano", "¿Qué opina el ciudadano?", "<circle cx=\"12\" cy=\"8\" r=\"3.5\"/><path d=\"M5 20c1-4 4-6 7-6s6 2 7 6\"/>"),
        new("sistema", "¿Funciona el sistema?", "<rect x=\"4\" y=\"5\" width=\"16\" height=\"11\" rx=\"2\"/><path d=\"M9 20h6M12 16v4\"/>"),
    ];

    // Sin pregunta en la dirección: «¿Llegamos a tiempo?», la pregunta central de un servicio de respuesta.
    public static Definicion? Buscar(string? clave) => string.IsNullOrEmpty(clave) ? Todas[1] : Todas.FirstOrDefault(p => p.Clave == clave);

    public static HtmlString Icono(Definicion d) =>
        new($"<svg class=\"icono\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{d.Icono}</svg>");
}

// Las zonas son celdas de la cuadrícula de HU29 (unos 550 m). Para nombrarlas se usa una referencia de plano, como en una guía de
// calles: letra por columna (de oeste a este) y número por fila (de norte a sur), con el centro del mapa del panel en «K10».
public sealed class Zonas(IConfiguration configuracion)
{
    private readonly Zona centro = Zona.De(
        double.TryParse(configuracion["Panel:Mapa:CentroLatitud"], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) ? la : -16.3988,
        double.TryParse(configuracion["Panel:Mapa:CentroLongitud"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) ? lo : -71.5369);

    public string Nombre(double sur, double oeste)
    {
        var zona = Zona.De(sur + 1e-7, oeste + 1e-7);
        var columna = Math.Clamp(zona.Columna - centro.Columna + 10, 0, 25);
        var fila = centro.Fila - zona.Fila + 10;
        return $"Zona {(char)('A' + columna)}{fila}";
    }
}

// La regla del servicio: la firma visual del dashboard. Una escala horizontal con una banda aceptable, tramos apilados (las etapas
// de un caso típico), marcadores (● mediana, ○ p90), puntos sueltos (cada caso, cuando son pocos) y la referencia anterior en gris.
public sealed class Regla
{
    public const int Ancho = 1000;
    public double Minimo { get; init; }
    public required double Maximo { get; init; }
    public string Unidad { get; init; } = "";
    public double? Paso { get; init; }
    public (double Desde, double Hasta, string Texto)? Banda { get; init; }
    public IReadOnlyList<(string Texto, double Valor, string Color)> Tramos { get; init; } = [];
    public IReadOnlyList<(double Valor, bool Lleno, string Rotulo, string? Sub)> Marcas { get; init; } = [];
    public IReadOnlyList<(double Valor, string Rotulo)> Anteriores { get; init; } = [];
    public IReadOnlyList<(double Valor, string Rotulo, bool Alerta)> Puntos { get; init; } = [];

    private double X(double v) => (Math.Clamp(v, Minimo, Maximo) - Minimo) / (Maximo - Minimo) * Ancho;
    private static string E(string texto) => WebUtility.HtmlEncode(texto);
    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // Alturas de los niveles de rótulos sobre el eje y ancho aproximado de un rótulo (12,5 px de Public Sans).
    private static readonly int[] Niveles = [30, 46, 62];
    private static double AnchoTexto(string texto) => texto.Length * 6.9 + 4;

    // Entre 4 y 8 marcas en la escala, con pasos redondos (5, 10, 15, 30 min, 1 h…), aunque un caso lento estire la regla.
    private static double PasoLegible(double rango, double? pedido)
    {
        if (pedido is { } p && rango / p <= 10) return p;
        double[] pasos = [1, 2, 5, 10, 15, 20, 25, 30, 60, 120, 180, 240, 360, 480, 720, 1440];
        return pasos.FirstOrDefault(c => rango / c <= 8, Math.Ceiling(rango / 8 / 1440) * 1440);
    }

    // Un rótulo centrado en su valor se sale de la regla en los extremos: ahí se alinea hacia adentro.
    private static string Ancla(double x) =>
        x < 80 ? " style=\"text-anchor:start\"" : x > Ancho - 80 ? " style=\"text-anchor:end\"" : "";

    public IHtmlContent Dibujar(string descripcion)
    {
        const int Eje = 104;
        var Alto = Marcas.Any(m => m.Sub is not null) ? 176 : 150; // los subtítulos de las marcas piden más aire bajo la escala
        var s = new StringBuilder($"<svg class=\"regla\" viewBox=\"-24 0 {Ancho + 48} {Alto}\" role=\"img\" aria-label=\"{E(descripcion)}\">");
        // Lo que ya ocupa cada nivel de rótulos sobre el eje (de x a x).
        var ocupados = Niveles.Select(_ => new List<(double Desde, double Hasta)>()).ToArray();
        if (Banda is var (desde, hasta, texto))
        {
            s.Append($"<rect class=\"regla__banda\" x=\"{N(X(desde))}\" y=\"{Eje - 70}\" width=\"{N(X(hasta) - X(desde))}\" height=\"80\"/>");
            s.Append($"<line class=\"regla__objetivo\" x1=\"{N(X(hasta))}\" y1=\"{Eje - 70}\" x2=\"{N(X(hasta))}\" y2=\"{Eje + 10}\"/>");
            // Sin tramos, el texto va en lo alto de la banda y ocupa ese nivel de rótulos; con tramos, abajo, bajo los tramos.
            if (Tramos.Count == 0)
                ocupados[^1].Add((X(desde) + 8, X(desde) + 8 + AnchoTexto(texto)));
            s.Append($"<text class=\"regla__texto-banda\" x=\"{N(X(desde) + 8)}\" y=\"{(Tramos.Count == 0 ? Eje - Niveles[^1] : Eje - 6)}\">{E(texto)}</text>");
        }
        var inicio = 0.0;
        foreach (var (nombre, valor, color) in Tramos)
        {
            var ancho = X(inicio + valor) - X(inicio);
            s.Append($"<rect x=\"{N(X(inicio) + 1)}\" y=\"{Eje - 58}\" width=\"{N(Math.Max(ancho - 2, 2))}\" height=\"38\" rx=\"4\" fill=\"{color}\"><title>{E(nombre)}: {E(Formato.Minutos(valor))}</title></rect>");
            if (ancho > 70)
                s.Append($"<text class=\"regla__tramo\" x=\"{N(X(inicio) + 10)}\" y=\"{Eje - 42}\">{E(nombre)}</text><text class=\"regla__tramo-sub\" x=\"{N(X(inicio) + 10)}\" y=\"{Eje - 27}\">{E(Formato.Minutos(valor))}</text>");
            inicio += valor;
        }
        s.Append($"<line class=\"regla__eje\" x1=\"0\" y1=\"{Eje}\" x2=\"{Ancho}\" y2=\"{Eje}\"/>");
        var paso = PasoLegible(Maximo - Minimo, Paso);
        for (var v = Minimo; v <= Maximo + 1e-9; v += paso)
        {
            var rotulo = v + paso > Maximo + 1e-9 ? $"{N(v)}{Unidad}" : N(v);
            s.Append($"<line class=\"regla__marca\" x1=\"{N(X(v))}\" y1=\"{Eje}\" x2=\"{N(X(v))}\" y2=\"{Eje + 5}\"/><text class=\"regla__escala\" x=\"{N(X(v))}\" y=\"{Eje + 19}\">{E(rotulo)}</text>");
        }
        foreach (var (valor, rotulo) in Anteriores)
            s.Append($"<path class=\"regla__anterior\" d=\"M{N(X(valor))} {Eje - 76} l-5 -8 h10 z\"><title>{E(rotulo)}</title></path><text class=\"regla__anterior-texto\" x=\"{N(X(valor))}\" y=\"{Eje - 88}\"{Ancla(X(valor))}>{E(rotulo)}</text>");
        // Puntos casi en el mismo lugar se rotulan juntos («y 2 más»); cada rótulo va al primer nivel donde no pisa a otro, y si
        // no cabe en ninguno queda solo el punto (con su título al pasar el cursor). Los puntos en alerta eligen primero.
        var grupos = new List<List<(double Valor, string Rotulo, bool Alerta)>>();
        foreach (var punto in Puntos.OrderBy(p => p.Valor))
            if (grupos.Count > 0 && X(punto.Valor) - X(grupos[^1][0].Valor) < 4) grupos[^1].Add(punto);
            else grupos.Add([punto]);
        foreach (var grupo in grupos.OrderByDescending(g => g.Any(p => p.Alerta)).ThenBy(g => g[0].Valor))
        {
            var x = X(grupo[0].Valor);
            var rotulo = grupo.Count == 1 ? grupo[0].Rotulo : $"{grupo[0].Rotulo} y {grupo.Count - 1} más";
            var ancho = AnchoTexto(rotulo);
            var (izquierda, derecha) = x < 80 ? (x + 8, x + 8 + ancho) : x > Ancho - 80 ? (x - 8 - ancho, x - 8) : (x - ancho / 2, x + ancho / 2);
            var nivel = Array.FindIndex(ocupados, o => o.All(r => derecha + 8 < r.Desde || izquierda - 8 > r.Hasta));
            foreach (var (valor, titulo, alerta) in grupo)
                s.Append($"<circle class=\"regla__punto{(alerta ? " regla__punto--alerta" : "")}\" cx=\"{N(X(valor))}\" cy=\"{Eje}\" r=\"7\"><title>{E(titulo)}</title></circle>");
            if (nivel < 0) continue;
            ocupados[nivel].Add((izquierda, derecha));
            var y = Eje - Niveles[nivel];
            s.Append($"<line class=\"regla__guia\" x1=\"{N(x)}\" y1=\"{y + 4}\" x2=\"{N(x)}\" y2=\"{Eje - 7}\"/>");
            // En los bordes el rótulo se separa un poco de su guía (y de la línea de una marca en el mismo valor).
            var xRotulo = x < 80 ? x + 8 : x > Ancho - 80 ? x - 8 : x;
            s.Append($"<text class=\"regla__rotulo\" x=\"{N(xRotulo)}\" y=\"{y}\"{Ancla(x)}>{E(rotulo)}</text>");
        }
        // Dos marcas cercanas (mediana y p90 parecidos) separan sus rótulos: la primera hacia la izquierda, la otra a la derecha.
        var marcas = Marcas.OrderBy(m => m.Valor).ToList();
        for (var i = 0; i < marcas.Count; i++)
        {
            var (valor, lleno, rotulo, sub) = marcas[i];
            var x = X(valor);
            var cerca = (i > 0 && x - X(marcas[i - 1].Valor) < 130) || (i < marcas.Count - 1 && X(marcas[i + 1].Valor) - x < 130);
            var ancla = !cerca ? Ancla(x)
                : i > 0 && x - X(marcas[i - 1].Valor) < 130 ? (x > Ancho - 80 ? " style=\"text-anchor:end\"" : " style=\"text-anchor:start\"")
                : (x < 80 ? " style=\"text-anchor:start\"" : " style=\"text-anchor:end\"");
            var desplazado = !cerca ? x : ancla.Contains("start") ? x + 4 : x - 4;
            s.Append($"<line class=\"regla__linea\" x1=\"{N(x)}\" y1=\"{Eje - 64}\" x2=\"{N(x)}\" y2=\"{Eje}\"/>");
            s.Append($"<circle class=\"regla__marcador{(lleno ? " regla__marcador--lleno" : "")}\" cx=\"{N(x)}\" cy=\"{Eje}\" r=\"8\"/>");
            s.Append($"<text class=\"regla__valor\" x=\"{N(desplazado)}\" y=\"{Eje + 38}\"{ancla}>{E(rotulo)}</text>");
            if (sub is not null)
                s.Append($"<text class=\"regla__sub\" x=\"{N(desplazado)}\" y=\"{Eje + 44 + 10}\"{ancla}>{E(sub)}</text>");
        }
        return new HtmlString(s.Append("</svg>").ToString());
    }
}

// Los carriles de «Ahora»: una pista por etapa con los casos abiertos como puntos. Todas las pistas comparten la misma medida, «veces
// lo esperado en esa etapa»: la franja verde (de 0 a 1×) cae en el mismo lugar en cada fila y se ve de un vistazo qué tan pasado
// va cada caso, aunque una etapa tolere 3 min y otra 60. Los minutos reales van en texto (a la izquierda lo esperado, a la derecha
// lo atascado), nunca sobre la pista: ahí solo hay puntos, y los que quedan casi juntos se agrupan en uno con su cantidad.
public static class Carriles
{
    // La pista llega hasta 3 veces lo esperado; un caso más lento se queda en el borde (su tiempo está en el texto).
    public const double Alcance = 3;

    // Separación mínima entre puntos, en % del ancho de la pista (unos 24 px en pantalla de escritorio); más cerca se agrupan.
    private const double Cerca = 3.5;

    private static string E(string texto) => WebUtility.HtmlEncode(texto);
    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // Dónde cae un tiempo en la pista, de 0 a 100 %.
    public static double Posicion(double minutos, int esperado) =>
        esperado <= 0 ? 100 : Math.Min(minutos / esperado, Alcance) / Alcance * 100;

    public static IHtmlContent Pista(IEnumerable<double> minutos, int esperado, string descripcion)
    {
        var s = new StringBuilder($"<div class=\"pista-a\" role=\"img\" aria-label=\"{E(descripcion)}\">");
        var limite = N(Posicion(esperado, esperado));
        s.Append($"<span class=\"pista-a__esperado\" style=\"width:{limite}%\"></span><span class=\"pista-a__linea\"></span><span class=\"pista-a__limite\" style=\"left:{limite}%\"></span>");
        var grupos = new List<List<double>>();
        foreach (var m in minutos.OrderBy(m => m))
            if (grupos.Count > 0 && Posicion(m, esperado) - Posicion(grupos[^1][0], esperado) < Cerca) grupos[^1].Add(m);
            else grupos.Add([m]);
        foreach (var grupo in grupos)
        {
            var atascado = grupo.Any(m => m > esperado);
            var lejos = grupo.Any(m => m > esperado * Alcance);
            var x = N(grupo.Average(m => Posicion(m, esperado)));
            var clases = $"pista-a__caso{(atascado ? " pista-a__caso--atascado" : "")}{(grupo.Count > 1 ? " pista-a__caso--grupo" : "")}{(lejos ? " pista-a__caso--lejos" : "")}";
            var posicion = grupo.Average(m => Posicion(m, esperado));
            // Globo propio (no el «title» del navegador): al pasar el cursor o enfocar el punto, con cada tiempo y si ya pasó lo esperado.
            var globo = new StringBuilder("<span class=\"pista-a__globo\" role=\"tooltip\">");
            globo.Append($"<b>{(grupo.Count == 1 ? E(Formato.Minutos(grupo[0])) + " en la etapa" : Formato.Plural(grupo.Count, "caso", "casos") + " en la etapa")}</b>");
            if (grupo.Count > 1)
            {
                globo.Append("<ul>");
                foreach (var m in grupo.OrderByDescending(m => m).Take(5))
                    globo.Append($"<li{(m > esperado ? " class=\"tarde\"" : "")}>{E(Formato.Minutos(m))}</li>");
                if (grupo.Count > 5) globo.Append($"<li class=\"mas\">y {grupo.Count - 5} más</li>");
                globo.Append("</ul>");
            }
            globo.Append($"<span class=\"{(atascado ? "tarde" : "bien")}\">{(atascado ? "▲ Ya pasó lo esperado" : "✓ A tiempo")} (hasta {E(Formato.Minutos(esperado))})</span></span>");
            s.Append($"<span class=\"{clases}{(posicion > 60 ? " pista-a__caso--der" : "")}\" style=\"left:{x}%\" tabindex=\"0\">{(grupo.Count > 1 ? grupo.Count.ToString(CultureInfo.InvariantCulture) : "")}{globo}</span>");
        }
        return new HtmlString(s.Append("</div>").ToString());
    }
}

public static class Ciudadano
{
    public static double Promedio(EvaluacionesAnalitica e)
    {
        var total = e.Distribucion.Sum(d => d.Evaluaciones);
        return total == 0 ? 0 : e.Distribucion.Sum(d => d.Puntuacion * d.Evaluaciones) / (double)total;
    }
}

// Las señales de «¿Funciona el sistema?», cada una con su límite aceptable (provisional: lo fija el PO).
public static class Sistema
{
    public sealed record Senal(string Nombre, string Ayuda, double? Valor, double Limite, double Maximo, bool MayorEsMejor)
    {
        public bool Bien => Valor is not { } v || (MayorEsMejor ? v >= Limite : v <= Limite);
        public string Texto => Valor is { } v ? Formato.Porcentaje(v, 0) : "Sin datos";
    }

    public static IReadOnlyList<Senal> Senales(SaludSistema s) =>
    [
        new("SOS sin ubicación", "aceptable: menos del 10 %", s.Sos == 0 ? null : s.PorcentajeSinUbicacion, 10, 30, false),
        new("GPS con precisión baja (más de 100 m)", "aceptable: menos del 15 %", s.PrecisionP50Metros is null ? null : s.PorcentajePrecisionBaja, 15, 40, false),
        new("Avisos push que llegaron al sereno", "aceptable: más del 95 %", s.PorcentajePushEnviadas, 95, 100, true),
        new("Atenciones registradas por la central", "el sereno no tenía señal · aceptable: menos del 10 %", s.IniciosAtencion == 0 ? null : s.PorcentajeRegistradosPorCentral, 10, 30, false),
        new("Inicios de atención fuera de rango", "lejos del caso · aceptable: menos del 10 %", s.IniciosAtencion == 0 ? null : s.PorcentajeFueraDeRango, 10, 30, false),
    ];

    // La regla corta de una señal: banda aceptable y el valor.
    public static IHtmlContent Regla(Senal senal)
    {
        const int Ancho = 520;
        double X(double v) => Math.Clamp(v, 0, senal.Maximo) / senal.Maximo * Ancho;
        static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        var banda = senal.MayorEsMejor
            ? $"<rect class=\"senal__banda\" x=\"{N(X(senal.Limite))}\" y=\"6\" width=\"{N(Ancho - X(senal.Limite))}\" height=\"14\"/>"
            : $"<rect class=\"senal__banda\" x=\"0\" y=\"6\" width=\"{N(X(senal.Limite))}\" height=\"14\"/>";
        var punto = senal.Valor is { } v
            ? $"<circle class=\"senal__valor{(senal.Bien ? "" : " senal__valor--alerta")}\" cx=\"{N(X(v))}\" cy=\"13\" r=\"7\"/>"
            : "";
        return new HtmlString($"<svg class=\"senal__regla\" viewBox=\"-8 0 {Ancho + 16} 26\" aria-hidden=\"true\">{banda}<line class=\"senal__linea\" x1=\"0\" y1=\"13\" x2=\"{Ancho}\" y2=\"13\"/><line class=\"senal__limite\" x1=\"{N(X(senal.Limite))}\" y1=\"2\" x2=\"{N(X(senal.Limite))}\" y2=\"24\"/>{punto}</svg>");
    }
}

// Una respuesta sin datos (estado vacío): qué falta, por qué y qué probar.
public sealed record Vacio(string Titulo, string Texto, IReadOnlyList<(string Texto, string Enlace, bool Principal)> Acciones, string? Nota = null);
