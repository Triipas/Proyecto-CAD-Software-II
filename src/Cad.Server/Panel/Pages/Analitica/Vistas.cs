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

    // Un rótulo centrado en su valor se sale de la regla en los extremos: ahí se alinea hacia adentro.
    private static string Ancla(double x) =>
        x < 80 ? " style=\"text-anchor:start\"" : x > Ancho - 80 ? " style=\"text-anchor:end\"" : "";

    public IHtmlContent Dibujar(string descripcion)
    {
        const int Eje = 104, Alto = 150;
        var s = new StringBuilder($"<svg class=\"regla\" viewBox=\"-24 0 {Ancho + 48} {Alto}\" role=\"img\" aria-label=\"{E(descripcion)}\">");
        if (Banda is var (desde, hasta, texto))
        {
            s.Append($"<rect class=\"regla__banda\" x=\"{N(X(desde))}\" y=\"{Eje - 70}\" width=\"{N(X(hasta) - X(desde))}\" height=\"80\"/>");
            s.Append($"<line class=\"regla__objetivo\" x1=\"{N(X(hasta))}\" y1=\"{Eje - 70}\" x2=\"{N(X(hasta))}\" y2=\"{Eje + 10}\"/>");
            // Sin tramos, el texto va arriba de la banda: abajo chocaría con los puntos cercanos al cero.
            s.Append($"<text class=\"regla__texto-banda\" x=\"{N(X(desde) + 8)}\" y=\"{(Tramos.Count == 0 ? Eje - 54 : Eje - 6)}\">{E(texto)}</text>");
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
        var paso = Paso ?? (Maximo - Minimo) / 8;
        for (var v = Minimo; v <= Maximo + 1e-9; v += paso)
        {
            var rotulo = Math.Abs(v - Maximo) < 1e-9 ? $"{N(v)}{Unidad}" : N(v);
            s.Append($"<line class=\"regla__marca\" x1=\"{N(X(v))}\" y1=\"{Eje}\" x2=\"{N(X(v))}\" y2=\"{Eje + 5}\"/><text class=\"regla__escala\" x=\"{N(X(v))}\" y=\"{Eje + 19}\">{E(rotulo)}</text>");
        }
        foreach (var (valor, rotulo) in Anteriores)
            s.Append($"<path class=\"regla__anterior\" d=\"M{N(X(valor))} {Eje - 76} l-5 -8 h10 z\"><title>{E(rotulo)}</title></path><text class=\"regla__anterior-texto\" x=\"{N(X(valor))}\" y=\"{Eje - 88}\"{Ancla(X(valor))}>{E(rotulo)}</text>");
        var alturas = new[] { 30, 46 };
        for (var i = 0; i < Puntos.Count; i++)
        {
            var (valor, rotulo, alerta) = Puntos[i];
            var y = Eje - alturas[i % 2];
            s.Append($"<line class=\"regla__guia\" x1=\"{N(X(valor))}\" y1=\"{y + 4}\" x2=\"{N(X(valor))}\" y2=\"{Eje}\"/>");
            s.Append($"<circle class=\"regla__punto{(alerta ? " regla__punto--alerta" : "")}\" cx=\"{N(X(valor))}\" cy=\"{Eje}\" r=\"7\"/><text class=\"regla__rotulo\" x=\"{N(X(valor))}\" y=\"{y}\"{Ancla(X(valor))}>{E(rotulo)}</text>");
        }
        foreach (var (valor, lleno, rotulo, sub) in Marcas)
        {
            s.Append($"<line class=\"regla__linea\" x1=\"{N(X(valor))}\" y1=\"{Eje - 64}\" x2=\"{N(X(valor))}\" y2=\"{Eje}\"/>");
            s.Append($"<circle class=\"regla__marcador{(lleno ? " regla__marcador--lleno" : "")}\" cx=\"{N(X(valor))}\" cy=\"{Eje}\" r=\"8\"/>");
            s.Append($"<text class=\"regla__valor\" x=\"{N(X(valor))}\" y=\"{Eje + 38}\"{Ancla(X(valor))}>{E(rotulo)}</text>");
            if (sub is not null)
                s.Append($"<text class=\"regla__sub\" x=\"{N(X(valor))}\" y=\"{Eje + 44 + 10}\"{Ancla(X(valor))}>{E(sub)}</text>");
        }
        return new HtmlString(s.Append("</svg>").ToString());
    }

    // Un carril de «Ahora»: los casos de una etapa como puntos según cuánto llevan; la franja verde es lo esperado y un punto
    // atascado se dibuja en ámbar con sus minutos.
    public static IHtmlContent Carril(IEnumerable<double> minutos, int esperado, string descripcion)
    {
        var lista = minutos.ToList();
        var maximo = Math.Max(esperado * 2.5, lista.DefaultIfEmpty(0).Max() * 1.1);
        double X(double v) => Math.Min(v, maximo) / maximo * Ancho;
        var s = new StringBuilder($"<svg class=\"carril__pista\" viewBox=\"0 0 {Ancho + 30} 40\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{E(descripcion)}\">");
        s.Append($"<rect class=\"carril__esperado\" x=\"0\" y=\"12\" width=\"{N(X(esperado))}\" height=\"16\" rx=\"3\"/><line class=\"carril__limite\" x1=\"{N(X(esperado))}\" y1=\"6\" x2=\"{N(X(esperado))}\" y2=\"34\"/>");
        s.Append($"<line class=\"carril__linea\" x1=\"0\" y1=\"20\" x2=\"{Ancho}\" y2=\"20\"/>");
        foreach (var m in lista)
            s.Append(m > esperado
                ? $"<circle class=\"carril__caso carril__caso--atascado\" cx=\"{N(X(m))}\" cy=\"20\" r=\"8\"><title>{E(Formato.Minutos(m))} en la etapa</title></circle><text class=\"carril__minutos\" x=\"{N(X(m))}\" y=\"40\">{E(Formato.Minutos(m))}</text>"
                : $"<circle class=\"carril__caso\" cx=\"{N(X(m))}\" cy=\"20\" r=\"6\"><title>{E(Formato.Minutos(m))} en la etapa</title></circle>");
        return new HtmlString(s.Append("</svg>").ToString());
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
