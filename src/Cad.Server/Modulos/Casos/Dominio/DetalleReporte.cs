namespace Cad.Server.Modulos.Casos.Dominio;

// HU10 — datos propios de un reporte (uno por caso de origen «Reporte»). La ubicación del hecho se guarda aquí, separada
// de la ubicación del dispositivo del caso (CA03), con la fuente que eligió el ciudadano (CA04).
public sealed class DetalleReporte
{
    // Límites técnicos de longitud (DP08 y DP10), no reglas de negocio.
    public const int MaximoDescripcion = 2000;
    public const int MaximoReferencia = 500;

    public Guid CasoId { get; private set; }
    public string Descripcion { get; private set; } = "";
    public DateTimeOffset OcurridoAproximadamenteEn { get; private set; }

    // «Mapa», «Texto» o «PosicionActual» (Cad.Contratos.Reportes.FuentesUbicacionHecho).
    public string FuenteUbicacion { get; private set; } = "";
    public double? LatitudHecho { get; private set; }
    public double? LongitudHecho { get; private set; }
    public double? PrecisionHechoMetros { get; private set; }
    public string? ReferenciaHecho { get; private set; }

    // CA07: la categoría que eligió el ciudadano se conserva aunque el operador la corrija después (HU14).
    public Guid CategoriaOriginalId { get; private set; }

    private DetalleReporte() { } // EF Core

    public static DetalleReporte Registrar(Caso caso, Guid categoriaId, string descripcion, DateTimeOffset ocurridoEn, string fuente,
        double? latitud, double? longitud, double? precision, string? referencia) => new()
    {
        CasoId = caso.Id,
        CategoriaOriginalId = categoriaId,
        Descripcion = descripcion,
        OcurridoAproximadamenteEn = ocurridoEn,
        FuenteUbicacion = fuente,
        LatitudHecho = latitud,
        LongitudHecho = longitud,
        PrecisionHechoMetros = precision,
        ReferenciaHecho = referencia,
    };
}
