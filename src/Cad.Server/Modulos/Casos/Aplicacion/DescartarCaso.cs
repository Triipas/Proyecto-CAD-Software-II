using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoDescarte
{
    public sealed record Descartado(DescarteCaso Descarte) : ResultadoDescarte;
    // Reenvío del mismo descarte: el caso ya estaba descartado por la central; se devuelve el descarte registrado.
    public sealed record YaDescartado(DescarteCaso Descarte) : ResultadoDescarte;
    public sealed record Rechazado(string Motivo) : ResultadoDescarte;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoDescarte;
    public sealed record NoEncontrado : ResultadoDescarte;
}

// Descartar un caso falso o erróneo desde la central (23/10/2026). Un operador lo hace desde cualquier estado abierto de la
// atención (pendiente, asignado, en camino o en atención), con un motivo obligatorio. En una sola transacción: el caso pasa a
// «Descartada», la asignación vigente termina (el sereno queda libre y su despacho desaparece), se guarda el descarte y un
// evento. El ciudadano recibe el aviso de que su caso quedó «Descartado» (CadDbContext); el motivo no se le muestra.
public sealed class DescartarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<DescartarCaso> registro)
{
    public const string MotivoNoDescartable = "El caso ya terminó (cerrado, cancelado, derivado o descartado): no se puede descartar.";
    public const string MotivoEnRevision = "Un reporte recibido o en revisión se descarta con la decisión de su revisión.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se descartaba. Revisa su estado e inténtalo de nuevo.";
    private const int Intentos = 3;

    public async Task<ResultadoDescarte> EjecutarAsync(Guid casoId, string? motivo, Guid operadorId, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
            if (caso is null)
                return new ResultadoDescarte.NoEncontrado();
            if (await db.DescartesCaso.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == casoId, ct) is { } previo)
                return new ResultadoDescarte.YaDescartado(previo);
            if (!caso.PuedeDescartarse)
                return new ResultadoDescarte.Rechazado(caso.Estado is EstadoCaso.Recibido or EstadoCaso.EnRevision ? MotivoEnRevision : MotivoNoDescartable);

            if (string.IsNullOrWhiteSpace(motivo))
                return new ResultadoDescarte.Invalido(new() { ["motivo"] = ["Indica por qué se descarta el caso."] });
            if (motivo.Trim().Length > DescarteCaso.MaximoMotivo)
                return new ResultadoDescarte.Invalido(new() { ["motivo"] = [$"El motivo admite hasta {DescarteCaso.MaximoMotivo} caracteres."] });

            var ahora = reloj.GetUtcNow();
            var estadoPrevio = caso.Estado;
            var asignacion = await db.Asignaciones.SingleOrDefaultAsync(a => a.CasoId == casoId && a.Vigente, ct);
            caso.Descartar();
            asignacion?.Finalizar(ahora);
            var descarte = DescarteCaso.Registrar(caso, estadoPrevio, operadorId, motivo, ahora);
            db.DescartesCaso.Add(descarte);
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoDescartado, operadorId, VisibilidadEvento.Interna, ahora));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException d && d.EsViolacionDeUnicidad())
            {
                // Otro cambio (un avance del sereno, un cierre u otro descarte) confirmó antes: se vuelve a evaluar.
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoDescarte.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Caso {CasoId} descartado por la central.", casoId);
            await AvisarAsync(casoId, asignacion?.SerenoId);
            return new ResultadoDescarte.Descartado(descarte);
        }
    }

    // Después de confirmar: la central retira el caso de sus listas y del mapa, y el sereno (si tenía el caso) deja de verlo.
    private async Task AvisarAsync(Guid casoId, Guid? serenoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            if (serenoId is { } sereno)
            {
                await notificador.SerenoActualizadoAsync(sereno, CancellationToken.None);
                await notificador.DespachoActualizadoAsync(sereno, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar del descarte del caso {CasoId}; se verá al resincronizar.", casoId);
        }
    }
}
