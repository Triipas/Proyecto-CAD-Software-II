using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Lo que viaja al asistente sobre un reporte (plan de asistencia con IA, 5.4 y 10): descripción y referencia anonimizadas (sin
// los nombres que el servidor conoce de quien reporta), el
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
        var nombres = await NombresConocidosAsync(d, ct);
        return new EntradaAnalisis(d.CasoId, Anonimizador.Limpiar(d.Descripcion, nombres), d.CategoriaOriginalId, d.FuenteUbicacion,
            d.LatitudHecho is not null, d.ReferenciaHecho is { } referencia ? Anonimizador.Limpiar(referencia, nombres) : null,
            d.OcurridoAproximadamenteEn, registradoEn, catalogo, parecidos, candidatos);
    }

    // Nombres que el servidor ya conoce y que pueden aparecer en el texto: quien reporta, su contacto de emergencia y el contacto
    // anotado por el operador en un reporte recibido por teléfono.
    private async Task<List<string?>> NombresConocidosAsync(DetalleReporte d, CancellationToken ct)
    {
        var nombres = new List<string?> { d.ContactoNombre };
        var ciudadanoId = await db.Casos.AsNoTracking().Where(c => c.Id == d.CasoId).Select(c => c.CiudadanoId).SingleOrDefaultAsync(ct);
        if (ciudadanoId is not { } id)
            return nombres;
        if (await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.Nombres, u.Apellidos }).SingleOrDefaultAsync(ct) is { } u)
            nombres.AddRange([u.Nombres, u.Apellidos]);
        nombres.Add(await db.PerfilesCiudadano.AsNoTracking().Where(p => p.UsuarioId == id).Select(p => p.ContactoNombre).SingleOrDefaultAsync(ct));
        return nombres;
    }
}
