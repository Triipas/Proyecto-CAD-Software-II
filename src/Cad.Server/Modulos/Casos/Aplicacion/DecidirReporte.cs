using Cad.Contratos.Casos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

// 27/10/2026: Destino es un código de DestinosDerivacion y DetalleDestino, el texto que lo precisa (obligatorio con «Otro»);
// la admisión registra la prioridad y si hay heridos; el descarte, su motivo del catálogo (MotivosDescarte).
public sealed record DatosDecision(TipoDecisionReporte Tipo, string? Motivo, string? Destino = null, string? Accion = null,
    string? DetalleDestino = null, string? Prioridad = null, string? Heridos = null, string? MotivoDescarte = null);

public abstract record ResultadoDecision
{
    public sealed record Registrado(string Estado) : ResultadoDecision;
    // La misma decisión reenviada por el mismo operador: no se registra nada nuevo.
    public sealed record YaRegistrado(string Estado) : ResultadoDecision;
    public sealed record Rechazado(string Motivo) : ResultadoDecision;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoDecision;
    public sealed record NoEncontrado : ResultadoDecision;
}

// HU15 — Admitir un reporte (S3-T06), HU16 — Derivarlo (S3-T07) y HU17 — Descartarlo (S3-T09). Solo operadores; la
// autorización la aplica la página. Solo desde «En revisión». Con decisiones simultáneas prevalece una y el estado
// final es consistente: token xmin del caso, clave única de la decisión y nueva evaluación.
public sealed class DecidirReporte(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<DecidirReporte> registro)
{
    public const string MotivoNoEnRevision = "El reporte no está «En revisión»: no se puede decidir sobre él.";
    public const string MotivoCambioConcurrente = "El reporte cambió mientras se registraba. Revisa su estado e inténtalo de nuevo.";

    private const int Intentos = 3;

    public async Task<ResultadoDecision> EjecutarAsync(Guid casoId, DatosDecision datos, Guid operadorId, CancellationToken ct)
    {
        var errores = Validar(datos);
        if (errores.Count > 0)
            return new ResultadoDecision.Invalido(errores); // HU16-CA02, HU17-CA02: el estado no cambia

        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId && c.Origen == OrigenCaso.Reporte, ct);
            if (caso is null)
                return new ResultadoDecision.NoEncontrado();

            if (caso.Estado != EstadoCaso.EnRevision)
                return await PreviaAsync(caso, datos.Tipo, operadorId, ct);

            var ahora = reloj.GetUtcNow();
            caso.Decidir(datos.Tipo);
            if (datos.Tipo == TipoDecisionReporte.Admision)
                caso.Priorizar(datos.Prioridad!, datos.Heridos!);
            var derivacion = datos.Tipo == TipoDecisionReporte.Derivacion;
            db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, datos.Tipo, datos.Motivo!.Trim(),
                derivacion ? TextoDestino(datos) : null,
                derivacion ? datos.Accion!.Trim() : null, operadorId, ahora,
                derivacion ? datos.Destino : null,
                datos.Tipo == TipoDecisionReporte.Descarte ? datos.MotivoDescarte : null));
            // DP05: «Admitido», «Derivado» y «Descartado» son estados públicos para el titular.
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEvento(datos.Tipo), operadorId, VisibilidadEvento.Publica, ahora));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.EsViolacionDeUnicidad())
            {
                // Otro operador decidió a la vez: se vuelve a evaluar con el estado que dejó.
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoDecision.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            // Sin el motivo, el destino ni la acción en el registro: solo el identificador y la decisión.
            registro.LogInformation("Reporte {CasoId}: decisión {Decision} registrada.", casoId, datos.Tipo);
            if (datos.Tipo == TipoDecisionReporte.Admision)
                await AvisarAsync(casoId); // HU15-CA02: la central lo muestra para asignarlo
            return new ResultadoDecision.Registrado(EstadosCaso.Texto(caso.Estado));
        }
    }

    private static Dictionary<string, string[]> Validar(DatosDecision datos)
    {
        var errores = new Dictionary<string, string[]>();
        void Texto(string campo, string? valor, int maximo, string falta)
        {
            if (string.IsNullOrWhiteSpace(valor))
                errores[campo] = [falta];
            else if (valor.Trim().Length > maximo)
                errores[campo] = [$"Admite hasta {maximo} caracteres."];
        }

        if (!Enum.IsDefined(datos.Tipo))
            errores["tipo"] = ["Decisión no válida."];
        Texto("motivo", datos.Motivo, DecisionReporte.MaximoMotivo, datos.Tipo switch
        {
            TipoDecisionReporte.Descarte => "Justifica el descarte.",
            TipoDecisionReporte.Derivacion => "Indica el motivo de la derivación.",
            _ => "Indica el motivo de la admisión.",
        });
        if (datos.Tipo == TipoDecisionReporte.Derivacion)
        {
            if (!Catalogo.Contiene(DestinosDerivacion.Todos, datos.Destino))
                errores["destino"] = ["Elige a dónde se deriva."];
            if (datos.Destino == DestinosDerivacion.Otro)
                Texto("detalleDestino", datos.DetalleDestino, DecisionReporte.MaximoDestino, "Indica la entidad a la que se deriva.");
            else if (datos.DetalleDestino?.Trim().Length > DecisionReporte.MaximoDestino)
                errores["detalleDestino"] = [$"Admite hasta {DecisionReporte.MaximoDestino} caracteres."];
            Texto("accion", datos.Accion, DecisionReporte.MaximoAccion, "Indica la acción realizada.");
        }
        if (datos.Tipo == TipoDecisionReporte.Admision)
        {
            if (!Catalogo.Contiene(PrioridadesCaso.Todas, datos.Prioridad))
                errores["prioridad"] = ["Elige la prioridad del caso."];
            if (!Catalogo.Contiene(PersonasHeridas.Todas, datos.Heridos))
                errores["heridos"] = ["Indica si hay personas heridas (o que no se sabe)."];
        }
        if (datos.Tipo == TipoDecisionReporte.Descarte && !Catalogo.Contiene(MotivosDescarte.Todos, datos.MotivoDescarte))
            errores["motivoDescarte"] = ["Elige el motivo del descarte."];
        return errores;
    }

    // Lo que se muestra como destino: la entidad que escribió el operador o, sin detalle, la del catálogo.
    private static string TextoDestino(DatosDecision datos) =>
        string.IsNullOrWhiteSpace(datos.DetalleDestino) ? Catalogo.Texto(DestinosDerivacion.Todos, datos.Destino)! : datos.DetalleDestino.Trim();

    // El reporte ya salió de la revisión: un reenvío de la misma decisión por el mismo operador no es un error.
    private async Task<ResultadoDecision> PreviaAsync(Caso caso, TipoDecisionReporte tipo, Guid operadorId, CancellationToken ct)
    {
        var previa = await db.DecisionesReporte.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == caso.Id, ct);
        if (previa is not null && previa.Tipo == tipo && previa.OperadorId == operadorId)
            return new ResultadoDecision.YaRegistrado(EstadosCaso.Texto(caso.Estado));
        if (previa is null)
            return new ResultadoDecision.Rechazado(MotivoNoEnRevision);

        var responsable = await db.Users.AsNoTracking().Where(u => u.Id == previa.OperadorId)
            .Select(u => u.Nombres + " " + u.Apellidos).SingleOrDefaultAsync(ct) ?? "otro operador";
        var hecho = previa.Tipo switch
        {
            TipoDecisionReporte.Admision => "admitido",
            TipoDecisionReporte.Derivacion => "derivado",
            _ => "descartado",
        };
        return new ResultadoDecision.Rechazado($"El reporte ya fue {hecho} por {responsable}.");
    }

    private static TipoEventoCaso TipoEvento(TipoDecisionReporte tipo) => tipo switch
    {
        TipoDecisionReporte.Admision => TipoEventoCaso.ReporteAdmitido,
        TipoDecisionReporte.Derivacion => TipoEventoCaso.ReporteDerivado,
        _ => TipoEventoCaso.ReporteDescartado,
    };

    // Después del COMMIT: las centrales abiertas vuelven a pedir sus listas.
    private async Task AvisarAsync(Guid casoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central de la admisión del reporte {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
