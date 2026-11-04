using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Plan de asistencia con IA, 5.5 — la respuesta del asistente se valida en el servidor antes de guardarla. Lo que contradice el
// catálogo o los candidatos entregados invalida toda la sugerencia (HU44-CA05: no hay sugerencia antes que una equivocada); los
// textos largos se recortan a los límites del dominio. Nunca baja una urgencia: con posible urgencia no se sugiere descartar
// (HU45-CA04).
public static class ValidarAnalisis
{
    public static AnalisisReporte? Aplicar(AnalisisReporte analisis, EntradaAnalisis entrada)
    {
        var catalogo = entrada.Catalogo.Select(c => c.Id).ToHashSet();
        if (!catalogo.Contains(analisis.CategoriaSugeridaId))
            return null;
        if (analisis.CategoriaAlternativaId is { } alternativa && !catalogo.Contains(alternativa))
            return null;
        if (!Enum.IsDefined(analisis.Confianza) || !Enum.IsDefined(analisis.Decision))
            return null;
        if (analisis.Senales.Any(s => !Enum.IsDefined(s)))
            return null;
        // El duplicado debe ser uno de los candidatos que se le entregaron, nunca un caso que el asistente se invente.
        if (analisis.DuplicadoDeId is { } duplicado && entrada.Candidatos.All(c => c.CasoId != duplicado))
            return null;

        var motivo = Recortar(analisis.MotivoBorrador, DecisionReporte.MaximoMotivo);
        if (motivo is null && analisis.Decision != DecisionSugerida.RevisarConCuidado)
            return null;

        var decision = analisis.Decision;
        var senales = analisis.Senales.ToList();
        if (analisis.PosibleUrgencia)
        {
            if (decision == DecisionSugerida.Descartar)
                decision = DecisionSugerida.RevisarConCuidado;
            senales.Add(SenalTriaje.PosibleUrgencia);
        }
        if (analisis.DuplicadoDeId is not null)
            senales.Add(SenalTriaje.Duplicado);

        // Cada código solo acompaña a su decisión y solo si es del catálogo; un código ajeno se deja vacío para elegirlo a mano.
        return analisis with
        {
            CategoriaAlternativaId = analisis.CategoriaAlternativaId == analisis.CategoriaSugeridaId ? null : analisis.CategoriaAlternativaId,
            Justificacion = Recortar(analisis.Justificacion, SugerenciaReporte.MaximoJustificacion) ?? "",
            Decision = decision,
            MotivoBorrador = motivo ?? "",
            DestinoCodigo = decision == DecisionSugerida.Derivar && Catalogo.Contiene(DestinosDerivacion.Todos, analisis.DestinoCodigo) ? analisis.DestinoCodigo : null,
            AccionBorrador = decision == DecisionSugerida.Derivar ? Recortar(analisis.AccionBorrador, DecisionReporte.MaximoAccion) : null,
            MotivoDescarteCodigo = decision == DecisionSugerida.Descartar && Catalogo.Contiene(MotivosDescarte.Todos, analisis.MotivoDescarteCodigo)
                ? analisis.MotivoDescarteCodigo : null,
            Prioridad = decision == DecisionSugerida.Admitir && Catalogo.Contiene(PrioridadesCaso.Todas, analisis.Prioridad) ? analisis.Prioridad : null,
            MotivoUrgencia = analisis.PosibleUrgencia ? Recortar(analisis.MotivoUrgencia, SugerenciaReporte.MaximoMotivoUrgencia) : null,
            Senales = senales.Distinct().ToList(),
        };
    }

    private static string? Recortar(string? texto, int maximo)
    {
        var limpio = texto?.Trim();
        if (string.IsNullOrEmpty(limpio))
            return null;
        return limpio.Length <= maximo ? limpio : limpio[..(maximo - 1)].TrimEnd() + "…";
    }
}
