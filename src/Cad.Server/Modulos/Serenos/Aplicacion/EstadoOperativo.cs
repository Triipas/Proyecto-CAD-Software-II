using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Serenos.Aplicacion;

// DP15 (decidida el 13/10/2026): intervalo inicial de 30 s, configurable.
public sealed class OpcionesSerenos
{
    public const string Seccion = "Serenos";
    public int IntervaloPosicionesSegundos { get; set; } = 30;
}

public abstract record ResultadoCambioEstado
{
    public sealed record Correcto(EstadoSereno Estado) : ResultadoCambioEstado;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCambioEstado;
}

public abstract record ResultadoPosicion
{
    public sealed record Registrada : ResultadoPosicion;
    // La misma posición reenviada (mismo sereno y misma marca de tiempo): no se duplica.
    public sealed record YaRegistrada : ResultadoPosicion;
    // El sereno no está operativo: la posición no se registra (HU18-CA02, CA03).
    public sealed record NoOperativo : ResultadoPosicion;
    public sealed record Invalida(Dictionary<string, string[]> Errores) : ResultadoPosicion;
}

// HU18 — Activar mi estado operativo y compartir mi ubicación (S2-T02, S2-T03).
// La autorización (solo sereno, CA05) la aplica la política de los endpoints.
public sealed class GestionarEstadoOperativo(CadDbContext db, TimeProvider reloj, IOptions<OpcionesSerenos> opciones,
    ILogger<GestionarEstadoOperativo> registro)
{
    public int IntervaloPosicionesSegundos => opciones.Value.IntervaloPosicionesSegundos;

    // null: el sereno nunca cambió su estado (no operativo, sin hora de cambio).
    public Task<EstadoSereno?> ConsultarAsync(Guid serenoId, CancellationToken ct) =>
        db.EstadosSereno.AsNoTracking().SingleOrDefaultAsync(e => e.SerenoId == serenoId, ct);

    public async Task<ResultadoCambioEstado> CambiarAsync(Guid serenoId, SolicitudCambioEstadoOperativo solicitud, CancellationToken ct)
    {
        // DP16-a (decidida el 13/10/2026): no se puede activar el estado operativo sin permiso de ubicación.
        if (solicitud.Operativo && !solicitud.UbicacionAutorizada)
            return new ResultadoCambioEstado.Invalido(new()
            {
                ["ubicacionAutorizada"] = ["Para activar tu estado operativo debes permitir el acceso a la ubicación."],
            });

        // DP16-b (decidida el 13/10/2026): desactivarse siempre está permitido; si hay una asignación vigente,
        // sigue vigente (este caso de uso no toca asignaciones). Al desactivarse deja de recibir posiciones
        // (CA02) y no figura como disponible (HU19-CA02).
        for (var intento = 0; ; intento++)
        {
            var ahora = reloj.GetUtcNow();
            var estado = await db.EstadosSereno.SingleOrDefaultAsync(e => e.SerenoId == serenoId, ct);
            if (estado is null)
            {
                estado = EstadoSereno.Nuevo(serenoId, ahora);
                db.EstadosSereno.Add(estado);
            }

            var cambio = estado.Cambiar(solicitud.Operativo, ahora);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad() && intento == 0)
            {
                // Primera activación simultánea desde dos peticiones: la otra creó la fila; se vuelve a leer.
                db.ChangeTracker.Clear();
                continue;
            }

            if (cambio)
                registro.LogInformation("Sereno {SerenoId} {Estado}.", serenoId, estado.Operativo ? "operativo" : "no operativo");
            return new ResultadoCambioEstado.Correcto(estado);
        }
    }

    public async Task<ResultadoPosicion> RegistrarPosicionAsync(Guid serenoId, PosicionSerenoEnviada enviada, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var posicion = PosicionSereno.Crear(serenoId, enviada.Latitud, enviada.Longitud, enviada.PrecisionMetros, enviada.ObtenidaEn, ahora);
        if (posicion is null)
            return new ResultadoPosicion.Invalida(new()
            {
                ["posicion"] = ["La posición no es válida: revisa las coordenadas, la precisión y la marca de tiempo."],
            });

        // Se decide con el estado del servidor en el momento de recibirla (no con el reloj del dispositivo).
        var operativo = await db.EstadosSereno.AsNoTracking().AnyAsync(e => e.SerenoId == serenoId && e.Operativo, ct);
        if (!operativo)
            return new ResultadoPosicion.NoOperativo();

        db.PosicionesSereno.Add(posicion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
            return new ResultadoPosicion.YaRegistrada();
        }

        // Sin coordenadas en el registro (RNF07).
        registro.LogDebug("Posición del sereno {SerenoId} registrada.", serenoId);
        return new ResultadoPosicion.Registrada();
    }
}
