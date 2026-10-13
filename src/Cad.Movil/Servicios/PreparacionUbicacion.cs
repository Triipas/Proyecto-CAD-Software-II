namespace Cad.Movil.Servicios;

public enum ResultadoPreparacion
{
    // CA01: falta decidir; se explica la finalidad antes de pedir el permiso.
    NecesitaExplicacion,
    // CA02: permiso concedido y posición inicial obtenida (no se envía).
    Disponible,
    // CA03: permiso concedido, pero no se obtuvo posición (GPS apagado, interior…).
    SinPosicion,
    // CA03: permiso denegado o restringido.
    Denegado,
}

// HU06 — Preparar la geolocalización al abrir la aplicación.
// Esta clase no recibe la API: no puede transmitir coordenadas ni iniciar un acompañamiento (CA04, RNF07).
// La posición de comprobación se descarta: no se guarda ni se reutiliza en el SOS (HU08-CA03).
public sealed class PreparacionUbicacion(IServicioUbicacion ubicacion)
{
    public static readonly TimeSpan EsperaComprobacion = TimeSpan.FromSeconds(10);

    // Al abrir la app: si el permiso está pendiente, primero hay que explicar (CA01); si está concedido,
    // se comprueba la disponibilidad sin volver a pedir el permiso (CA02).
    public async Task<ResultadoPreparacion> ComprobarAlAbrirAsync(CancellationToken ct = default) =>
        await ubicacion.ConsultarPermisoAsync() switch
        {
            EstadoPermisoUbicacion.Concedido => await ComprobarDisponibilidadAsync(ct),
            EstadoPermisoUbicacion.Desconocido => ResultadoPreparacion.NecesitaExplicacion,
            _ => ResultadoPreparacion.Denegado,
        };

    // Tras mostrar la explicación, el ciudadano decide: se solicita el permiso del sistema.
    public async Task<ResultadoPreparacion> SolicitarTrasExplicarAsync(CancellationToken ct = default) =>
        await ubicacion.SolicitarPermisoAsync() == EstadoPermisoUbicacion.Concedido
            ? await ComprobarDisponibilidadAsync(ct)
            : ResultadoPreparacion.Denegado;

    private async Task<ResultadoPreparacion> ComprobarDisponibilidadAsync(CancellationToken ct)
    {
        try
        {
            var posicion = await ubicacion.ObtenerActualAsync(EsperaComprobacion, ct);
            return posicion is null ? ResultadoPreparacion.SinPosicion : ResultadoPreparacion.Disponible;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ResultadoPreparacion.SinPosicion;
        }
    }
}
