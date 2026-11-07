namespace Cad.Contratos.Serenos;

// Rango de atención (decisión del 24/10/2026, radio revisado el 06/11/2026): el sereno inicia la atención cerca del punto del
// caso. Fuera del rango puede iniciarla igual, indicando por qué (el ciudadano se movió, la ubicación no era exacta…): no se
// bloquea una emergencia, pero queda registrado y la central lo ve. La regla la aplica el servidor; la app solo la anticipa.
//
// Por qué 50 m: es la precisión horizontal que la FCC de EE. UU. exige para ubicar una llamada de emergencia desde un celular
// (FCC 15-9, x/y dentro de 50 m en el 80 % de las llamadas), la referencia más usada para la ubicación en emergencias. Un celular
// con buena señal suele errar menos de 10 m, así que quien está en el lugar queda dentro con holgura.
public static class RangoAtencion
{
    public const double RadioBaseMetros = 50;

    // Tope del radio: con más imprecisión, el GPS ya no permite afirmar que el sereno está en el lugar, y lo que falte lo
    // cubre el motivo por escrito.
    public const double RadioMaximoMetros = 150;

    // Una posición más antigua no sirve para decidir si el sereno está en el lugar.
    public static readonly TimeSpan AntiguedadMaxima = TimeSpan.FromMinutes(2);

    // El radio crece con la imprecisión informada del punto del caso y de la posición del sereno, para no dejar fuera a quien
    // está en el lugar con un GPS impreciso, hasta el tope.
    public static double Radio(double? precisionCaso, double? precisionSereno) =>
        Math.Clamp((precisionCaso ?? 0) + (precisionSereno ?? 0), RadioBaseMetros, RadioMaximoMetros);

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
