namespace Cad.Server.Modulos.Casos.Dominio;

// HU24 — cierre del caso (S2-T12). Uno por caso: la clave es el propio caso, así que dos cierres simultáneos no
// pueden coexistir (CA08). El resultado público y las notas internas se guardan por separado (CA04, DP05): el
// ciudadano solo podrá ver el resultado público cuando exista su vista (HU30).
public sealed class CierreCaso
{
    // Límites técnicos de longitud (protección de la base de datos), no reglas de negocio.
    public const int MaximoResultado = 2000;
    public const int MaximoNotas = 2000;

    public Guid CasoId { get; private set; }

    // Tipificación final, del mismo catálogo que la clasificación de HU13 (DP07).
    public Guid CategoriaId { get; private set; }

    public string ResultadoPublico { get; private set; } = "";
    // 27/10/2026 — cómo terminó la atención, del catálogo ResultadosAtencion (Cad.Contratos). Nulo solo en cierres anteriores.
    public string? ResultadoAtencion { get; private set; }
    public string? NotasInternas { get; private set; }

    // CA01/CA05: el sereno asignado o el operador que cerró.
    public Guid ResponsableId { get; private set; }
    public DateTimeOffset CerradoEn { get; private set; }

    private CierreCaso() { } // EF Core

    public static CierreCaso Registrar(Caso caso, Guid categoriaId, string resultadoAtencion, string resultadoPublico, string? notasInternas,
        Guid responsableId, DateTimeOffset ahora) => new()
    {
        CasoId = caso.Id,
        CategoriaId = categoriaId,
        ResultadoAtencion = resultadoAtencion,
        ResultadoPublico = resultadoPublico,
        NotasInternas = notasInternas,
        ResponsableId = responsableId,
        CerradoEn = ahora,
    };
}
