using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoRevision
{
    public sealed record Registrado(string Detalle) : ResultadoRevision;
    // La misma acción reenviada (doble envío del formulario): no se registra nada nuevo.
    public sealed record YaRegistrado(string Detalle) : ResultadoRevision;
    public sealed record Rechazado(string Motivo) : ResultadoRevision;
    public sealed record Invalido(string Motivo) : ResultadoRevision;
    public sealed record NoEncontrado : ResultadoRevision;
}

// HU14 — Iniciar la revisión de un reporte y corregir su categoría (S3-T05). Solo operadores; la autorización la
// aplica la página. Responsable, fecha y hora quedan en el historial (eventos y correcciones de solo inserción).
// Un solo cambio aunque lleguen solicitudes a la vez: token xmin del caso y nueva evaluación (CA05).
public sealed class RevisarReporte(CadDbContext db, TimeProvider reloj, Compartido.INotificador notificador, ILogger<RevisarReporte> registro)
{
    public const string MotivoNoRecibido = "El reporte no está «Recibido»: no se puede iniciar su revisión.";

    // HU25-CA05: la central incorpora el marcador sin recargar (sin el aviso sonoro de un SOS: no es urgente). Después de
    // confirmar; si falla, el caso ya está guardado y aparece al resincronizar.
    private async Task AvisarAsync(Guid casoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central del reporte en revisión {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
    public const string MotivoNoEnRevision = "El reporte no está «En revisión»: no se puede corregir su categoría.";
    public const string MotivoCategoriaInvalida = "Elige una categoría del catálogo.";
    public const string MotivoCambioConcurrente = "El reporte cambió mientras se registraba. Revisa su estado e inténtalo de nuevo.";

    private const int Intentos = 3;

    // CA01, CA02 y CA05.
    public async Task<ResultadoRevision> IniciarAsync(Guid casoId, Guid operadorId, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId && c.Origen == OrigenCaso.Reporte, ct);
            if (caso is null)
                return new ResultadoRevision.NoEncontrado();

            // Primero lo ya registrado: el mismo operador que reenvía no recibe un error; otro operador sí (CA05).
            var responsable = await db.EventosCaso.AsNoTracking()
                .Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.RevisionIniciada)
                .Select(e => (Guid?)e.ActorId)
                .FirstOrDefaultAsync(ct);
            if (responsable is { } previo)
                return previo == operadorId
                    ? new ResultadoRevision.YaRegistrado("Ya habías iniciado la revisión.")
                    : new ResultadoRevision.Rechazado($"La revisión ya la inició {await NombreAsync(previo, ct)}.");

            if (caso.Estado != EstadoCaso.Recibido)
                return new ResultadoRevision.Rechazado(MotivoNoRecibido);

            var ahora = reloj.GetUtcNow();
            caso.IniciarRevision();
            // DP05: «En revisión» es un estado público para el titular.
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.RevisionIniciada, operadorId, VisibilidadEvento.Publica, ahora));

            if (!await GuardarAsync(ct))
            {
                if (intento == Intentos)
                    return new ResultadoRevision.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Reporte {CasoId}: revisión iniciada.", casoId);
            await AvisarAsync(casoId);
            return new ResultadoRevision.Registrado("Quedas como responsable de la revisión.");
        }
    }

    // CA03 y CA04.
    public async Task<ResultadoRevision> CorregirCategoriaAsync(Guid casoId, Guid? categoriaId, Guid operadorId, CancellationToken ct)
    {
        var categoria = categoriaId is { } id
            ? await db.CategoriasIncidencia.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            : null;
        if (categoria is null)
            return new ResultadoRevision.Invalido(MotivoCategoriaInvalida);

        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId && c.Origen == OrigenCaso.Reporte, ct);
            if (caso is null)
                return new ResultadoRevision.NoEncontrado();
            if (caso.Estado != EstadoCaso.EnRevision)
                return new ResultadoRevision.Rechazado(MotivoNoEnRevision);
            // Ya tiene esa categoría (reenvío del formulario o la misma decisión de otro operador): nada que corregir.
            if (caso.CategoriaId == categoria.Id)
                return new ResultadoRevision.YaRegistrado($"El reporte ya tiene la categoría «{categoria.Nombre}».");

            var ahora = reloj.GetUtcNow();
            var anterior = caso.CategoriaId!.Value;
            caso.CorregirCategoria(categoria.Id, operadorId, ahora);
            db.CorreccionesCategoria.Add(CorreccionCategoria.Registrar(caso.Id, anterior, categoria.Id, operadorId, ahora));
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CategoriaCorregida, operadorId, VisibilidadEvento.Interna, ahora));

            if (!await GuardarAsync(ct))
            {
                if (intento == Intentos)
                    return new ResultadoRevision.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Reporte {CasoId}: categoría corregida.", casoId);
            return new ResultadoRevision.Registrado($"Categoría vigente: «{categoria.Nombre}». La original se conserva.");
        }
    }

    // false si otro operador cambió el caso entre la lectura y el guardado: se vuelve a evaluar con los datos nuevos.
    private async Task<bool> GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<string> NombreAsync(Guid usuario, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == usuario).Select(u => u.Nombres + " " + u.Apellidos).SingleOrDefaultAsync(ct)
        ?? "otro operador";
}
