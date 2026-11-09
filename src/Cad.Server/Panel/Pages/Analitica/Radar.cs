using System.Globalization;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Panel.Pages.Analitica;

// «Estado de la central» como radar (13/11/2026). Cada porción es una etapa del recorrido (los SOS arriba, los reportes en el
// resto) y cada caso abierto es un punto: el ángulo dice su etapa y la distancia al centro, cuántas veces lleva su tiempo
// esperado. Todas las porciones comparten esa medida, así el anillo verde (de 0 a 1×) cae en el mismo lugar para todas aunque una
// etapa tolere 3 min y otra 60; más allá de 3×, el caso queda en el borde con su tiempo escrito. Se dibuja en el servidor (SVG y
// puntos con posición en %): se lee sin JavaScript, y analitica-ahora.js solo suma el barrido de entrada, el foco y el vivo.
public static class Radar
{
    public const double Alcance = 3;          // el borde: 3 veces lo esperado
    public const double Centro = 48;          // radio del centro (0×), donde va el total
    public const double Paso = 52;            // cuánto se aleja un caso por cada vez lo esperado
    public const double Borde = Centro + Alcance * Paso;
    public const double Lado = 640;           // viewBox de -320 a 320
    public const int DuracionBarrido = 1700;  // ms: lo que tarda el haz en dar la vuelta (radar-a.css)

    // Los SOS ocupan 120° arriba (30° por etapa) y los reportes el resto; un respiro entre grupos y entre porciones.
    public const double Inicio = -150;
    private const double AnchoSos = 30, Hueco = 10, Respiro = 1.5;

    public sealed record Etapa(string Clave, bool Reporte, EstadoCaso Estado, string Nombre, string Corto, double Desde, double Hasta)
    {
        public double Medio => (Desde + Hasta) / 2;
    }

    public static readonly IReadOnlyList<Etapa> Etapas = Armar();

    private static List<Etapa> Armar()
    {
        var definicion = new (string Clave, bool Reporte, EstadoCaso Estado, string Nombre, string Corto)[]
        {
            ("sos-pendiente", false, EstadoCaso.Pendiente, "Pendiente de despacho", "Pendiente"),
            ("sos-asignada", false, EstadoCaso.Asignada, "Asignado, sin salir", "Asignado"),
            ("sos-en-camino", false, EstadoCaso.EnCamino, "En camino", "En camino"),
            ("sos-en-atencion", false, EstadoCaso.EnAtencion, "En atención", "En atención"),
            ("reporte-recibido", true, EstadoCaso.Recibido, "Recibido, sin revisar", "Recibido"),
            ("reporte-en-revision", true, EstadoCaso.EnRevision, "En revisión", "En revisión"),
            ("reporte-admitido", true, EstadoCaso.Pendiente, "Admitido, sin asignar", "Admitido"),
            ("reporte-asignada", true, EstadoCaso.Asignada, "Asignado, sin salir", "Asignado"),
            ("reporte-en-camino", true, EstadoCaso.EnCamino, "En camino", "En camino"),
            ("reporte-en-atencion", true, EstadoCaso.EnAtencion, "En atención", "En atención"),
        };
        var anchoReporte = (360 - 4 * AnchoSos - 2 * Hueco) / 6;
        var etapas = new List<Etapa>();
        var a = Inicio;
        foreach (var d in definicion)
        {
            if (d.Reporte && etapas.All(e => !e.Reporte)) a += Hueco;
            var ancho = d.Reporte ? anchoReporte : AnchoSos;
            etapas.Add(new Etapa(d.Clave, d.Reporte, d.Estado, d.Nombre, d.Corto, a + Respiro, a + ancho - Respiro));
            a += ancho;
        }
        return etapas;
    }

    public static Etapa? De(EstadoCaso estado, bool reporte) => Etapas.FirstOrDefault(e => e.Estado == estado && e.Reporte == reporte);

    public static double Radio(double veces) => Centro + Math.Clamp(veces, 0, Alcance) * Paso;

    public static (double X, double Y) Punto(double radio, double grados)
    {
        var r = grados * Math.PI / 180;
        return (radio * Math.Cos(r), radio * Math.Sin(r));
    }

    // Una coordenada del viewBox como % del lado del radar (los puntos y rótulos son HTML encima del SVG).
    public static string Porcentaje(double coordenada) => N((coordenada + Lado / 2) / Lado * 100) + "%";

    // Cuándo pasa el haz por ese ángulo, en ms desde que empieza la vuelta.
    public static int Llegada(double grados) => (int)Math.Round(((grados - Inicio) % 360 + 360) % 360 / 360 * DuracionBarrido);

    // Una porción de corona, de r0 a r1 y de un ángulo a otro (en el sentido de las agujas del reloj).
    public static string Cuna(double r0, double r1, double desde, double hasta)
    {
        var grande = hasta - desde > 180 ? 1 : 0;
        var (x1, y1) = Punto(r1, desde);
        var (x2, y2) = Punto(r1, hasta);
        var (x3, y3) = Punto(r0, hasta);
        var (x4, y4) = Punto(r0, desde);
        return $"M{N(x1)} {N(y1)}A{N(r1)} {N(r1)} 0 {grande} 1 {N(x2)} {N(y2)}L{N(x3)} {N(y3)}A{N(r0)} {N(r0)} 0 {grande} 0 {N(x4)} {N(y4)}Z";
    }

    public static string Arco(double radio, double desde, double hasta)
    {
        var (x1, y1) = Punto(radio, desde);
        var (x2, y2) = Punto(radio, hasta);
        return $"M{N(x1)} {N(y1)}A{N(radio)} {N(radio)} 0 {(hasta - desde > 180 ? 1 : 0)} 1 {N(x2)} {N(y2)}";
    }

    public sealed record Caso(Guid Id, double Minutos, int Esperado, double Veces, double Grados)
    {
        public bool Tarde => Minutos > Esperado;
        public bool Lejos => Veces > Alcance;
        public (double X, double Y) Posicion => Punto(Radio(Veces), Grados);
    }

    // Los casos de una etapa, repartidos a lo ancho de su porción del más antiguo al más nuevo: uno que entra se suma al final y los
    // demás apenas se corren (en vivo se deslizan a su sitio).
    public static IReadOnlyList<Caso> Casos(Etapa etapa, IEnumerable<CasoAtascado> abiertos)
    {
        var lista = abiertos.Where(c => c.Estado == etapa.Estado && (c.Origen == OrigenCaso.Reporte) == etapa.Reporte)
            .OrderBy(c => c.EnEtapaDesde).ThenBy(c => c.CasoId).ToList();
        return lista.Select((c, i) => new Caso(c.CasoId, c.Minutos, c.UmbralMinutos, c.VecesElUmbral,
            etapa.Desde + (etapa.Hasta - etapa.Desde) * (i + 1) / (lista.Count + 1))).ToList();
    }

    // El rótulo va afuera del borde, alineado según el lado del círculo para no pisarlo.
    public static string EstiloRotulo(Etapa etapa)
    {
        var (x, y) = Punto(Borde + 18, etapa.Medio);
        var coseno = Math.Cos(etapa.Medio * Math.PI / 180);
        // Solo el rótulo casi vertical va centrado; los de arriba a cada lado se abren hacia afuera para no tocarse.
        var ancla = coseno > 0.15 ? "translate(0,-50%)" : coseno < -0.15 ? "translate(-100%,-50%)" : y < 0 ? "translate(-50%,-100%)" : "translate(-50%,0)";
        return $"left:{Porcentaje(x)};top:{Porcentaje(y)};transform:{ancla}";
    }

    // El tiempo de un caso que quedó en el borde se escribe hacia adentro, junto al punto: afuera pisaría el rótulo de la etapa.
    public static string EstiloLejos(double grados)
    {
        var (x, y) = Punto(30, grados);
        return $"--dx:{N(-x)}px;--dy:{N(-y)}px";
    }

    public static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
