using Cad.Server.Modulos.Asistencia.Puertos;

namespace Cad.Server.Modulos.Asistencia.Dominio;

// HU44 y HU45 (S6-T07) — la sugerencia del asistente sobre un reporte. Solo inserción: una por reporte y versión del prompt.
// Lo que hizo el operador no se guarda aquí: se deduce de la categoría vigente, las correcciones y la decisión registradas, así
// el dominio de los casos no depende de la IA.
public sealed class SugerenciaReporte
{
    public const int MaximoJustificacion = 300;
    public const int MaximoMotivoUrgencia = 300;

    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public string VersionPrompt { get; private set; } = "";
    public string VersionCriterios { get; private set; } = "";
    public string Modelo { get; private set; } = "";
    public string? ModeloVerificador { get; private set; }
    public Guid CategoriaSugeridaId { get; private set; }
    public Guid? CategoriaAlternativaId { get; private set; }
    public NivelConfianza Confianza { get; private set; }
    public string Justificacion { get; private set; } = "";
    public DecisionSugerida Decision { get; private set; }
    public string MotivoBorrador { get; private set; } = "";
    public string? DestinoCodigo { get; private set; }
    public string? AccionBorrador { get; private set; }
    public string? MotivoDescarteCodigo { get; private set; }
    public string? Prioridad { get; private set; }
    public bool PosibleUrgencia { get; private set; }
    public string? MotivoUrgencia { get; private set; }
    // Nombres de SenalTriaje separados por comas (legibles en la base y estables aunque cambie el orden del enum).
    public string Senales { get; private set; } = "";
    public Guid? DuplicadoDeId { get; private set; }
    // Nulo si no se verificó (la verificación es solo para la admisión automática, S6-T16).
    public bool? VerificadorCoincide { get; private set; }
    public bool Escalada { get; private set; }
    public int LatenciaMs { get; private set; }
    public int TokensEntrada { get; private set; }
    public int TokensSalida { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }

    private SugerenciaReporte() { } // EF Core

    public IReadOnlyList<SenalTriaje> ListaSenales =>
        Senales.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Enum.TryParse<SenalTriaje>(s, out var senal) ? (SenalTriaje?)senal : null)
            .OfType<SenalTriaje>().ToList();

    public static SugerenciaReporte Registrar(Guid casoId, string versionPrompt, string versionCriterios, ResultadoAsistente resultado,
        int latenciaMs, DateTimeOffset ahora)
    {
        var a = resultado.Analisis;
        return new()
        {
            Id = Guid.CreateVersion7(),
            CasoId = casoId,
            VersionPrompt = versionPrompt,
            VersionCriterios = versionCriterios,
            Modelo = resultado.Modelo,
            CategoriaSugeridaId = a.CategoriaSugeridaId,
            CategoriaAlternativaId = a.CategoriaAlternativaId,
            Confianza = a.Confianza,
            Justificacion = a.Justificacion,
            Decision = a.Decision,
            MotivoBorrador = a.MotivoBorrador,
            DestinoCodigo = a.DestinoCodigo,
            AccionBorrador = a.AccionBorrador,
            MotivoDescarteCodigo = a.MotivoDescarteCodigo,
            Prioridad = a.Prioridad,
            PosibleUrgencia = a.PosibleUrgencia,
            MotivoUrgencia = a.MotivoUrgencia,
            Senales = string.Join(',', a.Senales.Distinct()),
            DuplicadoDeId = a.DuplicadoDeId,
            Escalada = resultado.Escalada,
            LatenciaMs = latenciaMs,
            TokensEntrada = resultado.TokensEntrada,
            TokensSalida = resultado.TokensSalida,
            CreadaEn = ahora,
        };
    }
}
