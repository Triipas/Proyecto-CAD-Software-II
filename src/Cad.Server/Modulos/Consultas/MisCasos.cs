using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU30 — Consultar mis casos (S3-T13). Solo lectura y solo los casos del ciudadano que consulta (CA04: otro caso no
// existe para él). Proyección cerrada a DP05: nunca lee notas internas, intentos de contacto, operador ni sereno (CA05).
public sealed class ConsultarMisCasos(CadDbContext db)
{
    // DP05: el estado que ve el titular (definido en el dominio; lo comparten HU30 y HU31).
    public static string EstadoPublico(OrigenCaso origen, EstadoCaso estado) => EstadoPublicoCaso.De(origen, estado);

    private static string TextoOrigen(OrigenCaso origen) => OrigenesCaso.Texto(origen);

    // CA01: los casos propios, los más recientes primero.
    public async Task<IReadOnlyList<MiCasoResumen>> ListarAsync(Guid ciudadanoId, CancellationToken ct)
    {
        var filas = await (from caso in db.Casos.AsNoTracking()
                           where caso.CiudadanoId == ciudadanoId
                           join cierre in db.CierresCaso.AsNoTracking() on caso.Id equals cierre.CasoId into cierres
                           from cierre in cierres.DefaultIfEmpty()
                           join decision in db.DecisionesReporte.AsNoTracking() on caso.Id equals decision.CasoId into decisiones
                           from decision in decisiones.DefaultIfEmpty()
                           join cancelacion in db.CancelacionesSos.AsNoTracking() on caso.Id equals cancelacion.CasoId into cancelaciones
                           from cancelacion in cancelaciones.DefaultIfEmpty()
                           orderby caso.RegistradoEn descending
                           select new
                           {
                               caso.Id, caso.Origen, caso.Estado, caso.RegistradoEn,
                               ResultadoPublico = cierre == null ? null : cierre.ResultadoPublico,
                               DecisionTipo = decision == null ? (TipoDecisionReporte?)null : decision.Tipo,
                               Destino = decision == null ? null : decision.Destino,
                               Motivo = decision == null ? null : decision.Motivo,
                               MotivoCancelacion = cancelacion == null ? null : cancelacion.Motivo,
                           })
                          .ToListAsync(ct);
        return filas.Select(f => new MiCasoResumen(f.Id, TextoOrigen(f.Origen), f.RegistradoEn, EstadoPublico(f.Origen, f.Estado),
                Resultado(f.Estado, f.ResultadoPublico, f.DecisionTipo, f.Destino, f.Motivo, f.MotivoCancelacion)))
            .ToList();
    }

    // CA01–CA03. null si el caso no existe o no es del ciudadano: no se distingue (CA04).
    public async Task<MiCasoDetalle?> DetalleAsync(Guid ciudadanoId, Guid casoId, CancellationToken ct)
    {
        var caso = await db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == casoId && c.CiudadanoId == ciudadanoId, ct);
        if (caso is null)
            return null;

        var cierre = await db.CierresCaso.AsNoTracking().Where(c => c.CasoId == casoId)
            .Select(c => new { c.CerradoEn, c.ResultadoPublico }).SingleOrDefaultAsync(ct);
        var decision = await db.DecisionesReporte.AsNoTracking().Where(d => d.CasoId == casoId)
            .Select(d => new { d.Tipo, d.DecididaEn, d.Destino, d.Motivo }).SingleOrDefaultAsync(ct);
        var cancelacion = await db.CancelacionesSos.AsNoTracking().Where(c => c.CasoId == casoId)
            .Select(c => new { c.CanceladaEn, c.Motivo }).SingleOrDefaultAsync(ct);
        // Descarte por la central (23/10/2026): el ciudadano ve cuándo terminó así, pero no el motivo (es interno).
        var descartadoEn = await db.DescartesCaso.AsNoTracking().Where(d => d.CasoId == casoId)
            .Select(d => (DateTimeOffset?)d.DescartadoEn).SingleOrDefaultAsync(ct);
        var evaluacion = await db.EvaluacionesCaso.AsNoTracking().Where(e => e.CasoId == casoId)
            .Select(e => new MiEvaluacion(e.Puntuacion, e.Comentario, e.EvaluadaEn)).SingleOrDefaultAsync(ct);
        var revision = await db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.RevisionIniciada)
            .Select(e => (DateTimeOffset?)e.OcurridoEn).FirstOrDefaultAsync(ct);
        // El primer paso a «En atención» es la primera asignación; los avances siguientes (en camino, inicio de la
        // atención, otra asignación) quedan dentro del mismo estado público.
        var primeraAsignacion = await db.Asignaciones.AsNoTracking().Where(a => a.CasoId == casoId)
            .OrderBy(a => a.AsignadaEn).Select(a => (DateTimeOffset?)a.AsignadaEn).FirstOrDefaultAsync(ct);

        var avances = new List<AvancePublico> { new(caso.RegistradoEn, EstadosPublicos.Recibido, null) };
        if (revision is { } enRevision)
            avances.Add(new(enRevision, EstadosPublicos.EnRevision, null));
        if (decision is not null)
            avances.Add(decision.Tipo switch
            {
                TipoDecisionReporte.Admision => new AvancePublico(decision.DecididaEn, EstadosPublicos.Admitido, null),
                TipoDecisionReporte.Derivacion => new AvancePublico(decision.DecididaEn, EstadosPublicos.Derivado, $"Derivado a {decision.Destino}"),
                _ => new AvancePublico(decision.DecididaEn, EstadosPublicos.Descartado, $"Motivo: {decision.Motivo}"),
            });
        if (primeraAsignacion is { } asignada)
            avances.Add(new(asignada, EstadosPublicos.EnAtencion, null));
        if (cierre is not null)
            avances.Add(new(cierre.CerradoEn, EstadosPublicos.Cerrado, cierre.ResultadoPublico));
        if (cancelacion is not null)
            avances.Add(new(cancelacion.CanceladaEn, EstadosPublicos.Cancelado, $"Motivo: {cancelacion.Motivo}"));
        if (descartadoEn is { } descartado)
            avances.Add(new(descartado, EstadosPublicos.Descartado, ResultadoDescarteCentral));

        return new MiCasoDetalle(caso.Id, TextoOrigen(caso.Origen), caso.RegistradoEn, EstadoPublico(caso.Origen, caso.Estado),
            Resultado(caso.Estado, cierre?.ResultadoPublico, decision?.Tipo, decision?.Destino, decision?.Motivo, cancelacion?.Motivo),
            avances.OrderBy(a => a.En).ToList(), evaluacion, caso.Origen == OrigenCaso.Reporte ? await ReporteAsync(casoId, ct) : null);
    }

    // 26/10/2026: lo que el titular dio en su reporte, tal como lo envió (solo lectura).
    private async Task<MiReporte?> ReporteAsync(Guid casoId, CancellationToken ct) =>
        await (from detalle in db.DetallesReporte.AsNoTracking()
               join categoria in db.CategoriasIncidencia.AsNoTracking() on detalle.CategoriaOriginalId equals categoria.Id
               where detalle.CasoId == casoId
               select new MiReporte(categoria.Nombre, detalle.Descripcion, detalle.OcurridoAproximadamenteEn, detalle.FuenteUbicacion,
                   detalle.LatitudHecho, detalle.LongitudHecho, detalle.ReferenciaHecho, db.FotosReporte.Count(f => f.CasoId == casoId)))
            .SingleOrDefaultAsync(ct);

    // 26/10/2026: una foto del reporte propio (1–3), o null si el caso no es del ciudadano o no tiene esa foto (no se distingue).
    public Task<byte[]?> FotoAsync(Guid ciudadanoId, Guid casoId, int numero, CancellationToken ct) =>
        (from foto in db.FotosReporte.AsNoTracking()
         join caso in db.Casos.AsNoTracking() on foto.CasoId equals caso.Id
         where foto.CasoId == casoId && foto.Numero == numero && caso.CiudadanoId == ciudadanoId
         select foto.Contenido).SingleOrDefaultAsync(ct);

    public const string ResultadoDescarteCentral = "Descartado por la central.";

    // CA03 y DP05: el resultado público de un caso finalizado (cerrado, derivado, descartado o cancelado por el titular).
    private static string? Resultado(EstadoCaso estado, string? resultadoPublico, TipoDecisionReporte? decision, string? destino, string? motivo,
        string? motivoCancelacion) =>
        estado switch
        {
            EstadoCaso.Cerrada => resultadoPublico,
            EstadoCaso.Derivada when decision == TipoDecisionReporte.Derivacion => $"Derivado a {destino}",
            EstadoCaso.Descartada when decision == TipoDecisionReporte.Descarte => $"Descartado. Motivo: {motivo}",
            EstadoCaso.Descartada => ResultadoDescarteCentral, // descartado por la central: sin el motivo interno
            EstadoCaso.Cancelada when motivoCancelacion is not null => $"Cancelado por ti. Motivo: {motivoCancelacion}",
            _ => null,
        };
}
