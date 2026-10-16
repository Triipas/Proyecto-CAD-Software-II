using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public sealed record DatosDecision(TipoDecisionReporte Tipo, string? Motivo, string? Destino = null, string? Accion = null);

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
            db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, datos.Tipo, datos.Motivo!.Trim(),
                datos.Tipo == TipoDecisionReporte.Derivacion ? datos.Destino!.Trim() : null,
                datos.Tipo == TipoDecisionReporte.Derivacion ? datos.Accion!.Trim() : null, operadorId, ahora));
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
            Texto("destino", datos.Destino, DecisionReporte.MaximoDestino, "Indica a dónde se deriva.");
            Texto("accion", datos.Accion, DecisionReporte.MaximoAccion, "Indica la acción realizada.");
        }
        return errores;
    }

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
