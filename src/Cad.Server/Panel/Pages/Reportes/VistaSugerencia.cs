using Cad.Server.Modulos.Asistencia;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Panel.Pages.Reportes;

// HU44 y HU45 (S6-T12, S6-T13) — lo que necesita la tarjeta «Sugerencia del sistema» del detalle del reporte. Los candidatos a
// duplicado se calculan sin IA y se muestran aunque no haya sugerencia (HU45-CA05).
public sealed record VistaSugerencia(
    Guid CasoId,
    EstadoCaso Estado,
    SugerenciaVista? Sugerencia,
    IReadOnlyList<DuplicadoPosible> Duplicados,
    string CategoriaCiudadano,
    string CategoriaVigente,
    TimeZoneInfo ZonaHoraria,
    // Hasta dónde y cuánto tiempo se buscan los candidatos (Ia:Duplicados).
    int RadioMetros,
    int VentanaMinutos)
{
    public bool Visible => Sugerencia is not null || Duplicados.Count > 0;
    public bool EnRevision => Estado == EstadoCaso.EnRevision;

    public bool CoincideConCiudadano => Sugerencia?.CategoriaSugerida == CategoriaCiudadano;
    public bool YaCorregida => !CoincideConCiudadano && Sugerencia?.CategoriaSugerida == CategoriaVigente;

    // El tipo del formulario «Decidir» (TipoDecisionReporte) que corresponde a la decisión sugerida.
    public static string? TipoFormulario(DecisionSugerida decision) => decision switch
    {
        DecisionSugerida.Admitir => nameof(TipoDecisionReporte.Admision),
        DecisionSugerida.Derivar => nameof(TipoDecisionReporte.Derivacion),
        DecisionSugerida.Descartar => nameof(TipoDecisionReporte.Descarte),
        _ => null,
    };

    public static (string Texto, string Tono, string Icono) Decision(DecisionSugerida decision) => decision switch
    {
        DecisionSugerida.Admitir => ("Admitir", "exito", "exito"),
        DecisionSugerida.Derivar => ("Derivar", "neutro", "derivar"),
        DecisionSugerida.Descartar => ("Descartar", "neutro", "error"),
        _ => ("Revisar con cuidado", "advertencia", "lupa"),
    };

    public static string Confianza(NivelConfianza confianza) => confianza switch
    {
        NivelConfianza.Alta => "Confianza alta",
        NivelConfianza.Media => "Confianza media",
        _ => "Confianza baja",
    };

    public static string Senal(SenalTriaje senal) => senal switch
    {
        SenalTriaje.Spam => "Parece basura o una prueba",
        SenalTriaje.FueraDeAlcance => "Fuera de alcance",
        SenalTriaje.InformacionInsuficiente => "Información insuficiente",
        SenalTriaje.Ofensivo => "Lenguaje ofensivo",
        SenalTriaje.PosibleUrgencia => "Posible urgencia",
        SenalTriaje.CategoriaCiudadanoDudosa => "Categoría del ciudadano dudosa",
        _ => "Posible duplicado",
    };

    public static string Minutos(int minutos) =>
        minutos < 60 ? $"{minutos} min" : minutos % 60 == 0 ? $"{minutos / 60} h" : $"{minutos / 60} h {minutos % 60} min";
}
