namespace Cad.Server.Modulos.Asistencia.Dominio;

// HU45 (S6-T16; plan de asistencia con IA, 7.3) — la primera condición que no se cumplió para admitir solo un reporte. Se guarda
// la primera, en el orden de 7.3, para poder contar por qué no se admite.
public enum CondicionAdmision
{
    NoEsDeLaApp,
    NoRecibido,
    DecisionNoAdmitir,
    ConfianzaNoAlta,
    PosibleUrgencia,
    Duplicado,
    CategoriaDistinta,
    SinPunto,
    DescripcionCorta,
    SinVerificacion,
    VerificadorDiscrepa,
    CupoAgotado,
}

// S6-T16 — lo que la admisión automática concluyó sobre un reporte en «Sombra» o «Activa»: si era elegible y, si no, por qué; con
// qué categoría y prioridad lo admitiría y desde cuándo (solo en «Activa»). Solo inserción: una por reporte. Lo que pasó después
// (la admitió el sistema, un operador tomó la revisión, el operador decidió otra cosa) se deduce de la decisión registrada.
public sealed class EvaluacionAdmision
{
    public const int MaximoMotivo = 2000;

    public Guid CasoId { get; private set; }
    public ModoAdmision Modo { get; private set; }
    public bool Elegible { get; private set; }
    public CondicionAdmision? Condicion { get; private set; }
    public string? ModeloVerificador { get; private set; }
    // Nulo si no se llegó a pedir la segunda opinión (falló antes otra condición).
    public bool? VerificadorCoincide { get; private set; }
    public Guid CategoriaId { get; private set; }
    public string? Prioridad { get; private set; }
    public string Motivo { get; private set; } = "";
    public DateTimeOffset EvaluadaEn { get; private set; }
    // Solo en «Activa» y elegible: pasado este momento, el sistema admite si nadie tomó la revisión.
    public DateTimeOffset? AdmitirDesde { get; private set; }

    private EvaluacionAdmision() { } // EF Core

    public static EvaluacionAdmision Registrar(Guid casoId, ModoAdmision modo, CondicionAdmision? condicion, string? modeloVerificador,
        bool? verificadorCoincide, Guid categoriaId, string? prioridad, string motivo, DateTimeOffset ahora, int retrasoSegundos) => new()
    {
        CasoId = casoId,
        Modo = modo,
        Elegible = condicion is null,
        Condicion = condicion,
        ModeloVerificador = modeloVerificador,
        VerificadorCoincide = verificadorCoincide,
        CategoriaId = categoriaId,
        Prioridad = prioridad,
        Motivo = motivo.Length <= MaximoMotivo ? motivo : motivo[..MaximoMotivo],
        EvaluadaEn = ahora,
        AdmitirDesde = condicion is null && modo == ModoAdmision.Activa ? ahora.AddSeconds(retrasoSegundos) : null,
    };

    public static string Texto(CondicionAdmision condicion) => condicion switch
    {
        CondicionAdmision.NoEsDeLaApp => "no lo registró un ciudadano desde la app",
        CondicionAdmision.NoRecibido => "ya no estaba «Recibido»",
        CondicionAdmision.DecisionNoAdmitir => "la decisión sugerida no es admitir",
        CondicionAdmision.ConfianzaNoAlta => "la confianza no es alta",
        CondicionAdmision.PosibleUrgencia => "puede haber una urgencia",
        CondicionAdmision.Duplicado => "puede ser un duplicado",
        CondicionAdmision.CategoriaDistinta => "la categoría sugerida no es la del ciudadano",
        CondicionAdmision.SinPunto => "no tiene un punto en el mapa",
        CondicionAdmision.DescripcionCorta => "la descripción es demasiado corta",
        CondicionAdmision.SinVerificacion => "no hubo segunda opinión",
        CondicionAdmision.VerificadorDiscrepa => "la segunda opinión no coincide",
        _ => "se alcanzó el máximo de admisiones por hora",
    };
}
