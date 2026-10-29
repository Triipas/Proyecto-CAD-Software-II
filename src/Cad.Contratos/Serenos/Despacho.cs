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
    DateTimeOffset? EnAtencionEn = null,
    // HU20-CA07 y HU21-CA02: en un reporte admitido, el lugar del hecho que indicó el ciudadano (punto o referencia).
    // Es distinto de la ubicación del dispositivo (Ubicacion), que un reporte no tiene.
    LugarHechoDespacho? LugarHecho = null,
    // DP06 ampliada por el PO el 16/10/2026: la descripción que escribió el ciudadano en su reporte (sin sus datos personales).
    string? DescripcionReporte = null)
{
    // Textos de estado que envía el servidor (HU22, HU23).
    public const string EstadoAsignada = "Asignada";
    public const string EstadoEnCamino = "En camino";
    public const string EstadoEnAtencion = "En atención";
}

// Lugar del hecho de un reporte: punto (con la precisión, si el ciudadano usó su posición) y/o referencia textual (DP10).
public sealed record LugarHechoDespacho(double? Latitud, double? Longitud, double? PrecisionMetros, string? Referencia);

// Ubicación que envió el dispositivo del ciudadano. Nula = «Ubicación no disponible» (CA03: sin coordenadas inventadas).
public sealed record UbicacionDespacho(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn);

// Lo que registró el operador al contactar al ciudadano (HU12). La referencia es texto y se presenta
// separada de la ubicación del dispositivo (CA03).
public sealed record InformacionDespacho(DateTimeOffset RegistradaEn, string? InformacionHecho, string? ReferenciaUbicacion);

// ADR-07: token de FCM del dispositivo del sereno, para la notificación push del despacho. Dato sensible:
// no se escribe en los registros.
public sealed record SolicitudRegistroDispositivo(string Token);

// HU24 — cierre del caso desde la app del sereno. Tipificación del catálogo (DP07) y resultado obligatorios (CA02);
// notas internas opcionales y separadas del resultado público (CA04). ResultadoAtencion (27/10/2026): código de
// Cad.Contratos.Casos.ResultadosAtencion, obligatorio; al final y opcional en el tipo para que el contrato siga leyéndose.
public sealed record SolicitudCierreCaso(Guid? CategoriaId, string? ResultadoPublico, string? NotasInternas, string? ResultadoAtencion = null);

public sealed record RespuestaCierreCaso(Guid CasoId, string Estado, DateTimeOffset CerradoEn);

// DP07: una categoría del catálogo (reporte del ciudadano y tipificación final). Ayuda: ejemplos breves; Icono: clave del
// icono dibujado de la app. Ambos opcionales para que un cliente anterior siga funcionando.
public sealed record CategoriaCatalogo(Guid Id, string Nombre, string Ayuda = "", string Icono = "");
