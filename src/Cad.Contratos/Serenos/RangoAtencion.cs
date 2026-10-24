namespace Cad.Contratos.Serenos;

// Rango de atención (decisión del 24/10/2026): el sereno inicia la atención cerca del punto del caso. Fuera del rango puede
// iniciarla igual, indicando por qué (el ciudadano se movió, la ubicación no era exacta…): no se bloquea una emergencia, pero
// queda registrado y la central lo ve. La regla la aplica el servidor; la app solo la anticipa.
public static class RangoAtencion
{
    public const double RadioBaseMetros = 150;

    // Una posición más antigua no sirve para decidir si el sereno está en el lugar.
    public static readonly TimeSpan AntiguedadMaxima = TimeSpan.FromMinutes(2);

    // El radio crece con la imprecisión del punto del caso y de la posición del sereno, para no dejar fuera a quien está
    // en el lugar con un GPS impreciso.
    public static double Radio(double? precisionCaso, double? precisionSereno) =>
        Math.Max(RadioBaseMetros, (precisionCaso ?? 0) + (precisionSereno ?? 0));

    // Clave de los errores de la respuesta 422 cuando falta el motivo: [distancia en metros (vacía si no se conoce), radio].
    public const string ErrorFueraDeRango = "fueraDeRango";

    public static string TextoDistancia(double metros) =>
        metros < 1000 ? $"{Math.Round(metros):0} m" : $"{metros / 1000:0.0} km".Replace('.', ',');
}

// Motivos para iniciar la atención fuera del rango. «Otro» pide un detalle.
public static class MotivosInicioFueraDeRango
{
    public const string CiudadanoSeMovio = "CiudadanoSeMovio";
    public const string UbicacionInexacta = "UbicacionInexacta";
    public const string OtroPunto = "OtroPunto";
    public const string SinPosicion = "SinPosicion";
    public const string Otro = "Otro";
    public const int MaximoDetalle = 300;

    public static readonly IReadOnlyList<string> Todos = [CiudadanoSeMovio, UbicacionInexacta, OtroPunto, SinPosicion, Otro];

    public static string Texto(string motivo) => motivo switch
    {
        CiudadanoSeMovio => "El ciudadano se movió",
        UbicacionInexacta => "La ubicación del caso no era exacta",
        OtroPunto => "Atiendo el caso en otro punto",
        SinPosicion => "Mi teléfono no tiene una posición actual",
        Otro => "Otro motivo",
        _ => motivo,
    };
}

// HU23 — inicio de la atención por el sereno. Todo es opcional: sin posición se usa la última recibida; el motivo solo hace
// falta fuera del rango.
public sealed record SolicitudInicioAtencion(PosicionSerenoEnviada? Posicion = null, string? Motivo = null, string? Detalle = null);
