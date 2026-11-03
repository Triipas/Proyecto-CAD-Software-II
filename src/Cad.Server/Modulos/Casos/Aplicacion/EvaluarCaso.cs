using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoEvaluacion
{
    public sealed record Registrada(EvaluacionCaso Evaluacion) : ResultadoEvaluacion;
    // Reenvío técnico de la misma solicitud: se devuelve la evaluación ya registrada.
    public sealed record YaRegistrada(EvaluacionCaso Evaluacion) : ResultadoEvaluacion;
    // CA03 y CA04: rechazos sin registrar nada.
    public sealed record Rechazada(string Motivo) : ResultadoEvaluacion;
    public sealed record Invalida(Dictionary<string, string[]> Errores) : ResultadoEvaluacion;
    public sealed record NoEncontrado : ResultadoEvaluacion;
}

// HU32 — Evaluar la atención de un caso cerrado (S5-T11). Solo el titular (un caso ajeno responde igual que uno inexistente,
// CA05) y solo con el caso «Cerrada» (CA04). La clave única por caso garantiza una sola evaluación, también con dos
// solicitudes simultáneas (CA03). Evaluar es voluntario: nada en el sistema lo exige (CA06).
public sealed class EvaluarCaso(CadDbContext db, TimeProvider reloj, ILogger<EvaluarCaso> registro)
{
    public const string MotivoYaEvaluado = "Ya evaluaste este caso: solo se admite una evaluación.";
    public const string MotivoNoCerrado = "Solo se puede evaluar un caso cerrado.";

    public async Task<ResultadoEvaluacion> EjecutarAsync(Guid ciudadanoId, Guid casoId, SolicitudEvaluacionCaso solicitud, CancellationToken ct)
    {
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoEvaluacion.Invalida(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });

        var caso = await db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == casoId && c.CiudadanoId == ciudadanoId, ct);
        if (caso is null)
            return new ResultadoEvaluacion.NoEncontrado();
        if (await ExistenteAsync(casoId, solicitud.IdSolicitud, ct) is { } existente)
            return existente;
        if (caso.Estado != EstadoCaso.Cerrada)
            return new ResultadoEvaluacion.Rechazada(MotivoNoCerrado);

        // CA02: puntuación obligatoria, de 1 a 5.
        var errores = new Dictionary<string, string[]>();
        if (!EvaluacionCaso.PuntuacionValida(solicitud.Puntuacion))
            errores["puntuacion"] = [$"Elige una puntuación de {EvaluacionCaso.PuntuacionMinima} a {EvaluacionCaso.PuntuacionMaxima}."];
        if (solicitud.Comentario?.Trim().Length > EvaluacionCaso.MaximoComentario)
            errores["comentario"] = [$"El comentario admite hasta {EvaluacionCaso.MaximoComentario} caracteres."];
        // 27/10/2026: los motivos, opcionales, solo del catálogo.
        if (solicitud.Motivos?.Any(m => !Cad.Contratos.Casos.Catalogo.Contiene(Cad.Contratos.Casos.MotivosEvaluacion.Todos, m)) == true)
            errores["motivos"] = ["Elige los motivos de la lista."];
        if (errores.Count > 0)
            return new ResultadoEvaluacion.Invalida(errores);

        var evaluacion = EvaluacionCaso.Registrar(caso, solicitud.IdSolicitud, solicitud.Puntuacion!.Value, solicitud.Comentario, reloj.GetUtcNow(),
            solicitud.Motivos);
        db.EvaluacionesCaso.Add(evaluacion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // CA03: otra solicitud registró la evaluación a la vez; prevalece la primera.
            db.ChangeTracker.Clear();
            return await ExistenteAsync(casoId, solicitud.IdSolicitud, ct) ?? throw new InvalidOperationException("Evaluación no encontrada tras el conflicto.", ex);
        }

        registro.LogInformation("Caso {CasoId} evaluado por su titular.", casoId);
        return new ResultadoEvaluacion.Registrada(evaluacion);
    }

    private async Task<ResultadoEvaluacion?> ExistenteAsync(Guid casoId, Guid idSolicitud, CancellationToken ct) =>
        await db.EvaluacionesCaso.AsNoTracking().SingleOrDefaultAsync(e => e.CasoId == casoId, ct) switch
        {
            null => null,
            { } e when e.IdSolicitud == idSolicitud => new ResultadoEvaluacion.YaRegistrada(e),
            _ => new ResultadoEvaluacion.Rechazada(MotivoYaEvaluado),
        };
}
