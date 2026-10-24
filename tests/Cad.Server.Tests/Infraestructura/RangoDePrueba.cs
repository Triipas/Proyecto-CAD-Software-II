using Cad.Contratos.Serenos;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Infraestructura;

// Rango de atención (24/10/2026): las pruebas anteriores a la regla inician la atención como lo haría la app en el lugar,
// con la posición del sereno en el punto del caso (sin punto, no hay rango que comprobar).
public static class RangoDePrueba
{
    public static async Task<SolicitudInicioAtencion> EnElLugarAsync(this FabricaCad fabrica, Guid casoId)
    {
        var punto = await fabrica.ConBaseDatosAsync(async db =>
            await db.Casos.AsNoTracking().Where(c => c.Id == casoId && c.Latitud != null)
                .Select(c => new { Latitud = c.Latitud!.Value, Longitud = c.Longitud!.Value }).SingleOrDefaultAsync()
            ?? await db.DetallesReporte.AsNoTracking().Where(d => d.CasoId == casoId && d.LatitudHecho != null)
                .Select(d => new { Latitud = d.LatitudHecho!.Value, Longitud = d.LongitudHecho!.Value }).SingleOrDefaultAsync());
        return new SolicitudInicioAtencion(punto is null ? null : new PosicionSerenoEnviada(punto.Latitud, punto.Longitud, 5, DateTimeOffset.UtcNow));
    }
}
