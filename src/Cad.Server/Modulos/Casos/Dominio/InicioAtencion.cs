namespace Cad.Server.Modulos.Casos.Dominio;

// Inicio de la atención por el sereno, con dónde estaba respecto del punto del caso (rango de atención, 24/10/2026).
// DentroDelRango nulo: el caso no tiene un punto con el que comparar (p. ej., un reporte solo con referencia). Fuera del rango
// (o sin posición del sereno) lleva el motivo que indicó. Solo inserción, como el historial del caso.
public sealed class InicioAtencion
{
    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid SerenoId { get; private set; }
    public double? Latitud { get; private set; }
    public double? Longitud { get; private set; }
    public double? PrecisionMetros { get; private set; }
    public double? DistanciaMetros { get; private set; }
    public double? RadioMetros { get; private set; }
    public bool? DentroDelRango { get; private set; }
    public string? Motivo { get; private set; }
    public string? Detalle { get; private set; }
    public DateTimeOffset IniciadaEn { get; private set; }

    public bool FueraDeRango => DentroDelRango == false;

    private InicioAtencion() { } // EF Core

    public static InicioAtencion Registrar(Caso caso, Guid serenoId, (double Latitud, double Longitud, double Precision)? posicion,
        double? distancia, double? radio, bool? dentro, string? motivo, string? detalle, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = caso.Id,
        SerenoId = serenoId,
        Latitud = posicion?.Latitud,
        Longitud = posicion?.Longitud,
        PrecisionMetros = posicion?.Precision,
        DistanciaMetros = distancia,
        RadioMetros = radio,
        DentroDelRango = dentro,
        Motivo = motivo,
        Detalle = detalle,
        IniciadaEn = ahora,
    };
}
