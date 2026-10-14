namespace Cad.Server.Modulos.Serenos.Dominio;

// HU18 — estado operativo del sereno. Una fila por sereno; la disponibilidad no se guarda: se calcula
// (operativo y sin asignación vigente, HU19-CA02; Plan de Arquitectura, modelo de estados).
public sealed class EstadoSereno
{
    public Guid SerenoId { get; private set; }
    public bool Operativo { get; private set; }
    public DateTimeOffset CambiadoEn { get; private set; }

    private EstadoSereno() { } // EF Core

    public static EstadoSereno Nuevo(Guid serenoId, DateTimeOffset ahora) =>
        new() { SerenoId = serenoId, Operativo = false, CambiadoEn = AMicrosegundos(ahora) };

    // Devuelve true si el estado cambió. Repetir el mismo estado no altera la hora del último cambio.
    public bool Cambiar(bool operativo, DateTimeOffset ahora)
    {
        if (Operativo == operativo)
            return false;
        Operativo = operativo;
        CambiadoEn = AMicrosegundos(ahora);
        return true;
    }

    // PostgreSQL guarda microsegundos: así la hora devuelta al cambiar coincide con la que se leerá después.
    private static DateTimeOffset AMicrosegundos(DateTimeOffset momento) => momento.AddTicks(-(momento.Ticks % 10));
}

// HU18-CA01 — posición recibida mientras el sereno está operativo. Solo inserción.
public sealed class PosicionSereno
{
    public Guid Id { get; private set; }
    public Guid SerenoId { get; private set; }
    public double Latitud { get; private set; }
    public double Longitud { get; private set; }
    public double PrecisionMetros { get; private set; }

    // Marca de tiempo del dispositivo (CA01) y momento en que el servidor la recibió.
    public DateTimeOffset ObtenidaEn { get; private set; }
    public DateTimeOffset RecibidaEn { get; private set; }

    private PosicionSereno() { } // EF Core

    // null si la posición no es válida (coordenadas fuera de rango, precisión no positiva o sin marca de tiempo).
    public static PosicionSereno? Crear(Guid serenoId, double latitud, double longitud, double precisionMetros,
        DateTimeOffset obtenidaEn, DateTimeOffset ahora)
    {
        var valida = double.IsFinite(latitud) && latitud is >= -90 and <= 90
            && double.IsFinite(longitud) && longitud is >= -180 and <= 180
            && double.IsFinite(precisionMetros) && precisionMetros > 0
            && obtenidaEn != default;
        if (!valida)
            return null;

        return new PosicionSereno
        {
            Id = Guid.CreateVersion7(),
            SerenoId = serenoId,
            Latitud = latitud,
            Longitud = longitud,
            PrecisionMetros = precisionMetros,
            // Npgsql solo admite timestamptz en UTC.
            ObtenidaEn = obtenidaEn.ToUniversalTime(),
            RecibidaEn = ahora,
        };
    }
}
