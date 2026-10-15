namespace Cad.Server.Modulos.Casos.Dominio;

public enum TipoDecisionReporte { Admision, Derivacion, Descarte }

// HU15, HU16 y HU17 — la decisión sobre un reporte en revisión: una por reporte, con responsable, fecha, hora y motivo
// (DP21: texto libre obligatorio). La derivación guarda además el destino (texto libre, sin catálogo: DP21) y la acción
// realizada; es solo un registro: el sistema no envía nada a entidades externas (HU16-CA03).
public sealed class DecisionReporte
{
    // Límites técnicos de longitud, como el resultado del cierre (2000) y las referencias (500).
    public const int MaximoMotivo = 2000;
    public const int MaximoDestino = 500;
    public const int MaximoAccion = 2000;

    public Guid CasoId { get; private set; }
    public TipoDecisionReporte Tipo { get; private set; }
    public string Motivo { get; private set; } = "";
    public string? Destino { get; private set; }
    public string? Accion { get; private set; }
    public Guid OperadorId { get; private set; }
    public DateTimeOffset DecididaEn { get; private set; }

    private DecisionReporte() { } // EF Core

    public static DecisionReporte Registrar(Caso caso, TipoDecisionReporte tipo, string motivo, string? destino, string? accion,
        Guid operadorId, DateTimeOffset ahora) => new()
    {
        CasoId = caso.Id,
        Tipo = tipo,
        Motivo = motivo,
        Destino = destino,
        Accion = accion,
        OperadorId = operadorId,
        DecididaEn = ahora,
    };
}
