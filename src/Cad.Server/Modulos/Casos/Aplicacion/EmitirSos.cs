using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoEmisionSos
{
    public sealed record Registrado(Caso Caso) : ResultadoEmisionSos;
    // Reiteración técnica de la misma solicitud: se devuelve el SOS ya registrado (HU07-CA07).
    public sealed record YaRegistrado(Caso Caso) : ResultadoEmisionSos;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoEmisionSos;
}

// HU07 — Emitir un SOS con una única activación (S1-T13, S1-T14) y HU08 — ubicación del SOS (parte de servidor de S1-T15).
public sealed class EmitirSos(CadDbContext db, TimeProvider reloj, ILogger<EmitirSos> registro)
{
    public async Task<ResultadoEmisionSos> EjecutarAsync(Guid ciudadanoId, SolicitudEmisionSos solicitud, CancellationToken ct)
    {
        // Sin IdSolicitud no se puede garantizar que un reintento no duplique el SOS.
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoEmisionSos.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });

        if (await BuscarAsync(ciudadanoId, solicitud.IdSolicitud, ct) is { } existente)
            return new ResultadoEmisionSos.YaRegistrado(existente);

        var ubicacion = solicitud.Ubicacion is { } u
            ? Posicion.ComoActual(u.Latitud, u.Longitud, u.PrecisionMetros, u.ObtenidaEn, solicitud.ActivadoEn)
            : null;
        if (solicitud.Ubicacion is not null && ubicacion is null)
            // Sin coordenadas en el registro (RNF07): solo se anota que se descartó.
            registro.LogWarning("Ubicación del SOS descartada por inválida o anterior a la activación; se registra sin ubicación.");

        var ahora = reloj.GetUtcNow();
        var caso = Caso.EmitirSos(ciudadanoId, solicitud.IdSolicitud, ubicacion, ahora);

        // Caso y evento en un solo SaveChanges: una sola transacción, sin SOS parcial (HU07-CA04).
        db.Casos.Add(caso);
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.SosEmitido, ciudadanoId, VisibilidadEvento.Publica, ahora));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Dos reintentos simultáneos: el índice único deja pasar solo uno.
            db.ChangeTracker.Clear();
            return new ResultadoEmisionSos.YaRegistrado((await BuscarAsync(ciudadanoId, solicitud.IdSolicitud, ct))!);
        }

        registro.LogInformation("SOS {CasoId} registrado.", caso.Id);
        return new ResultadoEmisionSos.Registrado(caso);
    }

    private Task<Caso?> BuscarAsync(Guid ciudadanoId, Guid idSolicitud, CancellationToken ct) =>
        db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.CiudadanoId == ciudadanoId && c.IdSolicitud == idSolicitud, ct);
}
