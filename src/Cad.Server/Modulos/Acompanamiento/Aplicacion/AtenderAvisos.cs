using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

public abstract record ResultadoAvisoAtendido
{
    public sealed record Correcto(AvisoAcompanamiento Aviso) : ResultadoAvisoAtendido;
    public sealed record Rechazado(string Motivo) : ResultadoAvisoAtendido;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoAvisoAtendido;
    public sealed record NoEncontrado : ResultadoAvisoAtendido;
}

// HU43 — Revisar avisos y registrar su atención (S5-T08, S5-T09). La autorización (operador, DP04) la aplica la página. Nada
// asigna un sereno: un caso vinculado o creado se asigna después con HU20 (CA06).
public sealed class AtenderAvisos(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<AtenderAvisos> registro)
{
    public const string MotivoNoEnRevision = "El aviso no está en revisión.";
    public const string MotivoYaVinculado = "El aviso ya está vinculado con un caso.";
    private const int Intentos = 3;

    // CA01: «En revisión» con su responsable. Si ya estaba en revisión o resuelto, no cambia.
    public Task<ResultadoAvisoAtendido> RevisarAsync(Guid avisoId, Guid operadorId, CancellationToken ct) =>
        CambiarAsync(avisoId, aviso => aviso.Estado == EstadoAviso.Generado
            ? aviso.IniciarRevision(operadorId, reloj.GetUtcNow()) ? null : MotivoNoEnRevision
            : aviso.Estado == EstadoAviso.EnRevision ? "" : "El aviso ya está resuelto.", ct);

    // CA03: «Resuelto», conservando responsable y actuaciones.
    public Task<ResultadoAvisoAtendido> ResolverAsync(Guid avisoId, Guid operadorId, CancellationToken ct) =>
        CambiarAsync(avisoId, aviso => aviso.Estado == EstadoAviso.Resuelto ? ""
            : aviso.Resolver(operadorId, reloj.GetUtcNow()) ? null : MotivoNoEnRevision, ct);

    // CA02: contacto, acción o resultado con responsable, fecha y hora; solo con el aviso en revisión.
    public async Task<ResultadoAvisoAtendido> RegistrarActuacionAsync(Guid avisoId, Guid operadorId, string? tipo, string? descripcion, Guid idSolicitud,
        CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        if (!Enum.TryParse<TipoActuacionAviso>(tipo, out var tipoActuacion) || !Enum.IsDefined(tipoActuacion))
            errores["tipo"] = ["Elige si es un contacto, una acción o un resultado."];
        if (string.IsNullOrWhiteSpace(descripcion))
            errores["descripcion"] = ["Describe el contacto, la acción o el resultado."];
        else if (descripcion.Trim().Length > ActuacionAviso.MaximoDescripcion)
            errores["descripcion"] = [$"La descripción admite hasta {ActuacionAviso.MaximoDescripcion} caracteres."];
        if (idSolicitud == Guid.Empty)
            errores["idSolicitud"] = ["Falta el identificador de la solicitud. Recarga la página."];
        if (errores.Count > 0)
            return new ResultadoAvisoAtendido.Invalido(errores);

        var aviso = await db.AvisosAcompanamiento.AsNoTracking().SingleOrDefaultAsync(a => a.Id == avisoId, ct);
        if (aviso is null)
            return new ResultadoAvisoAtendido.NoEncontrado();
        if (await db.ActuacionesAviso.AnyAsync(a => a.OperadorId == operadorId && a.IdSolicitud == idSolicitud, ct))
            return new ResultadoAvisoAtendido.Correcto(aviso); // reenvío del mismo formulario
        if (aviso.Estado != EstadoAviso.EnRevision)
            return new ResultadoAvisoAtendido.Rechazado(MotivoNoEnRevision);

        db.ActuacionesAviso.Add(ActuacionAviso.Registrar(aviso, tipoActuacion, descripcion!, operadorId, idSolicitud, reloj.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
        }
        return new ResultadoAvisoAtendido.Correcto(aviso);
    }

    // CA04: vincular con un caso existente.
    public async Task<ResultadoAvisoAtendido> VincularAsync(Guid avisoId, Guid? casoId, Guid operadorId, CancellationToken ct)
    {
        if (casoId is not { } id || !await db.Casos.AnyAsync(c => c.Id == id, ct))
            return new ResultadoAvisoAtendido.Invalido(new() { ["casoId"] = ["Escribe el identificador de un caso existente (lo encuentras en «Casos»)."] });
        return await CambiarAsync(avisoId, aviso =>
        {
            if (aviso.CasoId == id)
                return "";
            if (aviso.CasoId is not null)
                return MotivoYaVinculado;
            aviso.Vincular(id, operadorId, reloj.GetUtcNow());
            return null;
        }, ct);
    }

    // CA05 y DP19: crear un caso urgente con justificación (sin ella no se crea) y vincularlo. El caso queda «Pendiente» en la
    // central para asignarlo con HU20 (CA06). Un reenvío (mismo IdSolicitud) devuelve el mismo caso.
    public async Task<ResultadoAvisoAtendido> CrearCasoUrgenteAsync(Guid avisoId, string? justificacion, Guid operadorId, Guid idSolicitud,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(justificacion))
            return new ResultadoAvisoAtendido.Invalido(new() { ["justificacion"] = ["Indica la justificación del caso urgente."] });
        if (justificacion.Trim().Length > AvisoAcompanamiento.MaximoJustificacion)
            return new ResultadoAvisoAtendido.Invalido(new() { ["justificacion"] = [$"La justificación admite hasta {AvisoAcompanamiento.MaximoJustificacion} caracteres."] });
        if (idSolicitud == Guid.Empty)
            return new ResultadoAvisoAtendido.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud. Recarga la página."] });

        for (var intento = 1; ; intento++)
        {
            var aviso = await db.AvisosAcompanamiento.SingleOrDefaultAsync(a => a.Id == avisoId, ct);
            if (aviso is null)
                return new ResultadoAvisoAtendido.NoEncontrado();
            if (aviso.CasoId is { } vinculado)
                return await db.Casos.AnyAsync(c => c.Id == vinculado && c.CiudadanoId == aviso.CiudadanoId && c.IdSolicitud == idSolicitud, ct)
                    ? new ResultadoAvisoAtendido.Correcto(aviso)
                    : new ResultadoAvisoAtendido.Rechazado(MotivoYaVinculado);

            // DP19: la última posición conocida de la sesión (con su hora real de obtención).
            var ultima = await db.PosicionesAcompanamiento.AsNoTracking().Where(p => p.SesionId == aviso.SesionId)
                .OrderByDescending(p => p.ObtenidaEn).Select(p => new { p.Latitud, p.Longitud, p.PrecisionMetros, p.ObtenidaEn })
                .FirstOrDefaultAsync(ct);
            var ahora = reloj.GetUtcNow();
            var caso = Caso.CrearDesdeAviso(aviso.CiudadanoId, idSolicitud,
                ultima is null ? null : (ultima.Latitud, ultima.Longitud, ultima.PrecisionMetros, ultima.ObtenidaEn), ahora);
            db.Casos.Add(caso);
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.CasoUrgenteCreado, operadorId, VisibilidadEvento.Interna, ahora));
            aviso.Vincular(caso.Id, operadorId, ahora, justificacion);
            try
            {
                // Caso, evento y vínculo en una sola transacción.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (intento < Intentos && (ex is DbUpdateConcurrencyException || ex is DbUpdateException d && d.EsViolacionDeUnicidad()))
            {
                db.ChangeTracker.Clear();
                continue;
            }

            registro.LogInformation("Caso urgente {CasoId} creado desde el aviso {AvisoId}.", caso.Id, aviso.Id);
            try
            {
                // La central resincroniza: el caso aparece pendiente para asignarlo (HU20). No se avisa como un SOS nuevo.
                await notificador.CasoActualizadoAsync(caso.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                registro.LogError(ex, "No se pudo avisar a la central del caso urgente {CasoId}; se mostrará al resincronizar.", caso.Id);
            }
            return new ResultadoAvisoAtendido.Correcto(aviso);
        }
    }

    // cambio: null si cambió (se guarda); "" si ya estaba así (sin cambios); otro texto = motivo del rechazo.
    private async Task<ResultadoAvisoAtendido> CambiarAsync(Guid avisoId, Func<AvisoAcompanamiento, string?> cambio, CancellationToken ct)
    {
        for (var intento = 1; ; intento++)
        {
            var aviso = await db.AvisosAcompanamiento.SingleOrDefaultAsync(a => a.Id == avisoId, ct);
            if (aviso is null)
                return new ResultadoAvisoAtendido.NoEncontrado();
            switch (cambio(aviso))
            {
                case "":
                    return new ResultadoAvisoAtendido.Correcto(aviso);
                case { } motivo:
                    return new ResultadoAvisoAtendido.Rechazado(motivo);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return new ResultadoAvisoAtendido.Correcto(aviso);
            }
            catch (DbUpdateConcurrencyException) when (intento < Intentos)
            {
                // Otro operador lo cambió a la vez: se vuelve a evaluar.
                db.ChangeTracker.Clear();
            }
        }
    }
}
