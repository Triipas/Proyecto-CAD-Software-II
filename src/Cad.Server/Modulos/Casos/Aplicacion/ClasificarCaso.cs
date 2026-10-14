using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoClasificacion
{
    public sealed record Clasificado(string Categoria) : ResultadoClasificacion;
    // El caso ya tenía esa misma categoría (reenvío del formulario o dos operadores con la misma decisión).
    public sealed record YaClasificado(string Categoria) : ResultadoClasificacion;
    public sealed record Rechazado(string Motivo) : ResultadoClasificacion;
    public sealed record CasoNoEncontrado : ResultadoClasificacion;
    public sealed record Invalido(string Motivo) : ResultadoClasificacion;
}

// HU13 — Clasificar un SOS después de su registro (S2-T08). Solo operadores (DP09); la autorización la aplica
// la página. La clasificación no condiciona la emisión, la presentación ni la asignación (CA02).
public sealed class ClasificarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<ClasificarCaso> registro)
{
    public const string MotivoCategoriaInvalida = "Elige una categoría del catálogo.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se clasificaba. Revisa su estado e inténtalo de nuevo.";

    // Si otro operador cambia el caso a la vez (otra clasificación o una asignación), se vuelve a evaluar
    // con los datos nuevos en lugar de rechazar por un conflicto que no afecta a esta decisión.
    private const int Intentos = 3;

    public async Task<ResultadoClasificacion> EjecutarAsync(Guid casoId, Guid? categoriaId, Guid operadorId, CancellationToken ct)
    {
        // CA04: solo categorías del catálogo vigente.
        var categoria = categoriaId is { } id
            ? await db.CategoriasIncidencia.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            : null;
        if (categoria is null)
            return new ResultadoClasificacion.Invalido(MotivoCategoriaInvalida);

        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId && c.Origen == OrigenCaso.Sos, ct);
            if (caso is null)
                return new ResultadoClasificacion.CasoNoEncontrado();

            if (caso.Clasificado)
                return caso.CategoriaId == categoria.Id
                    ? new ResultadoClasificacion.YaClasificado(categoria.Nombre)
                    : new ResultadoClasificacion.Rechazado(await MotivoYaClasificadoAsync(caso.CategoriaId!.Value, ct));

            var ahora = reloj.GetUtcNow();
            caso.Clasificar(categoria.Id, operadorId, ahora); // CA01: responsable, fecha y hora
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoClasificado, operadorId, VisibilidadEvento.Interna, ahora));

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoClasificacion.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Caso {CasoId} clasificado.", casoId);
            await AvisarAsync(casoId);
            return new ResultadoClasificacion.Clasificado(categoria.Nombre);
        }
    }

    // Reclasificar no está definido en HU13: se rechaza y queda como decisión pendiente del PO.
    private async Task<string> MotivoYaClasificadoAsync(Guid categoriaActual, CancellationToken ct)
    {
        var nombre = await db.CategoriasIncidencia.AsNoTracking().Where(c => c.Id == categoriaActual).Select(c => c.Nombre).SingleAsync(ct);
        return $"El SOS ya está clasificado como «{nombre}». No se puede cambiar la clasificación.";
    }

    // Después del COMMIT: la central muestra el tipo en lugar de «Tipo por determinar».
    private async Task AvisarAsync(Guid casoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central de la clasificación del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
