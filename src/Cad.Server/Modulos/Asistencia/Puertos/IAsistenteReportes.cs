namespace Cad.Server.Modulos.Asistencia.Puertos;

// HU44 y HU45 (plan de asistencia con IA, 5.2) — el puerto del asistente. Una sola llamada por reporte devuelve a la vez la
// categoría y el triaje. Lo implementan un asistente por reglas (sin red), el de Gemini y, en las pruebas, uno falso.
public interface IAsistenteReportes
{
    // false: no hay asistente (IA apagada o sin clave). El reporte se trabaja como siempre.
    bool Disponible { get; }

    // null: no hay sugerencia (sin respuesta, cuota agotada o respuesta inutilizable).
    Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct);

    // Segunda opinión independiente, solo para la admisión automática (S6-T16). null si el asistente no la ofrece.
    Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct);
}

// Categórica y no un número: la confianza numérica que declara un modelo de lenguaje no está calibrada (5.2).
public enum NivelConfianza { Baja, Media, Alta }

public enum DecisionSugerida { Admitir, Derivar, Descartar, RevisarConCuidado }

public enum SenalTriaje { Spam, FueraDeAlcance, InformacionInsuficiente, Ofensivo, PosibleUrgencia, CategoriaCiudadanoDudosa, Duplicado }

public sealed record CategoriaParaAnalisis(Guid Id, string Nombre, string Ayuda);

// Otro reporte cercano en el espacio y en el tiempo (BuscarDuplicados): el modelo dice si describe el mismo hecho.
public sealed record CandidatoDuplicado(Guid CasoId, string Descripcion, string Categoria, int Metros, int Minutos);

// Una decisión ya tomada por la central sobre un reporte parecido (S6-T09). Vacío hasta que exista RecuperarEjemplos.
public sealed record EjemploDecidido(string Descripcion, string Categoria, DecisionSugerida Decision, string Motivo);

// Lo que viaja al asistente (10): textos ya anonimizados; sin contacto, identidad del ciudadano ni coordenadas exactas.
public sealed record EntradaAnalisis(
    Guid CasoId,
    string Descripcion,
    Guid CategoriaCiudadanoId,
    string FuenteUbicacion,
    bool ConCoordenadas,
    string? ReferenciaHecho,
    DateTimeOffset OcurridoEn,
    DateTimeOffset RegistradoEn,
    IReadOnlyList<CategoriaParaAnalisis> Catalogo,
    IReadOnlyList<EjemploDecidido> Ejemplos,
    IReadOnlyList<CandidatoDuplicado> Candidatos);

// 27/10/2026: la decisión registra códigos de catálogo (Cad.Contratos.Casos). La sugerencia los propone igual: el destino de una
// derivación (DestinosDerivacion), el motivo de un descarte (MotivosDescarte) y la prioridad de una admisión (PrioridadesCaso).
public sealed record AnalisisReporte(
    Guid CategoriaSugeridaId,
    Guid? CategoriaAlternativaId,
    NivelConfianza Confianza,
    string Justificacion,
    DecisionSugerida Decision,
    string MotivoBorrador,
    string? DestinoCodigo,
    string? AccionBorrador,
    string? MotivoDescarteCodigo,
    string? Prioridad,
    bool PosibleUrgencia,
    string? MotivoUrgencia,
    Guid? DuplicadoDeId,
    IReadOnlyList<SenalTriaje> Senales);

// El análisis y lo que costó: qué modelo respondió, si pasó al modelo preciso y los tokens.
public sealed record ResultadoAsistente(AnalisisReporte Analisis, string Modelo, bool Escalada = false, int TokensEntrada = 0, int TokensSalida = 0);
