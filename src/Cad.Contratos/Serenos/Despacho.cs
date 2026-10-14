namespace Cad.Contratos.Serenos;

// HU21 — despacho del sereno asignado. Datos limitados por DP06 (mínimo aceptable, 13/10/2026):
// identificador, origen, estado y horas; ubicación del dispositivo o ninguna; la información del hecho y las
// referencias que registró el operador. Sin nombre ni teléfono del ciudadano y sin notas internas (CA05).
public sealed record DespachoSereno(
    Guid CasoId,
    string Origen,
    string Estado,
    DateTimeOffset RegistradoEn,
    DateTimeOffset AsignadoEn,
    UbicacionDespacho? Ubicacion,
    IReadOnlyList<InformacionDespacho> Informacion,
    // HU22 y HU23: horas de los avances del caso (sin el responsable: DP06).
    DateTimeOffset? EnCaminoEn = null,
    DateTimeOffset? EnAtencionEn = null)
{
    // Textos de estado que envía el servidor (HU22, HU23).
    public const string EstadoAsignada = "Asignada";
    public const string EstadoEnCamino = "En camino";
    public const string EstadoEnAtencion = "En atención";
}

// Ubicación que envió el dispositivo del ciudadano. Nula = «Ubicación no disponible» (CA03: sin coordenadas inventadas).
public sealed record UbicacionDespacho(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn);

// Lo que registró el operador al contactar al ciudadano (HU12). La referencia es texto y se presenta
// separada de la ubicación del dispositivo (CA03).
public sealed record InformacionDespacho(DateTimeOffset RegistradaEn, string? InformacionHecho, string? ReferenciaUbicacion);

// ADR-07: token de FCM del dispositivo del sereno, para la notificación push del despacho. Dato sensible:
// no se escribe en los registros.
public sealed record SolicitudRegistroDispositivo(string Token);
