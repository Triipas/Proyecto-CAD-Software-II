using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Lo que viaja al asistente sobre un reporte (plan de asistencia con IA, 5.4 y 10): descripción y referencia anonimizadas, el
// catálogo, los candidatos a duplicado y las decisiones parecidas ya tomadas. Lo usan el análisis del servidor y la herramienta de
// evaluación (S6-T14), así los dos envían exactamente lo mismo.
public sealed class ArmarEntrada(CadDbContext db, BuscarDuplicados duplicados, IRecuperadorEjemplos ejemplos, IOptions<OpcionesIa> opciones)
{
    public async Task<EntradaAnalisis> EjecutarAsync(DetalleReporte d, DateTimeOffset registradoEn, CancellationToken ct)
    {
        var catalogo = await db.CategoriasIncidencia.AsNoTracking().OrderBy(c => c.Orden)
            .Select(c => new CategoriaParaAnalisis(c.Id, c.Nombre, c.Ayuda)).ToListAsync(ct);
        var candidatos = (await duplicados.EjecutarAsync(d.CasoId, ct))
            .Select(c => new CandidatoDuplicado(c.CasoId, Anonimizador.Limpiar(c.Descripcion), c.Categoria, c.Metros, c.Minutos))
            .ToList();
        var parecidos = await ejemplos.RecuperarAsync(d.CasoId, d.Descripcion, opciones.Value.Ejemplos, ct);
        return new EntradaAnalisis(d.CasoId, Anonimizador.Limpiar(d.Descripcion), d.CategoriaOriginalId, d.FuenteUbicacion,
            d.LatitudHecho is not null, d.ReferenciaHecho is { } referencia ? Anonimizador.Limpiar(referencia) : null,
            d.OcurridoAproximadamenteEn, registradoEn, catalogo, parecidos, candidatos);
    }
}
