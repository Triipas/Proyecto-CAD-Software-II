using System.Globalization;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU28 — tiempos de un caso calculados con las marcas que ya registran las demás historias (S5-T10). DP18 (PO, 16/10/2026):
// por caso, en su detalle y como columnas de la búsqueda de «Casos»; sin promedios. Todos se miden desde el registro.
public sealed record TiemposCaso(
    OrigenCaso Origen,
    DateTimeOffset RegistradoEn,
    // HU15–HU17: decisión de la revisión del reporte y su tipo.
    DateTimeOffset? DecididoEn,
    TipoDecisionReporte? Decision,
    // HU23: primer inicio de atención (del sereno o del operador).
    DateTimeOffset? AtencionIniciadaEn)
{
    public const string NoDisponible = "No disponible";
    public const string NoCorresponde = "No corresponde";

    // Un caso urgente creado desde un aviso (DP19) se atiende como un SOS: su tiempo es el de respuesta.
    public bool EsSos => OrigenesCaso.SeAtiendeComoSos(Origen);

    // CA01 y CA04: SOS, del registro al inicio de la atención; no es el tiempo de presentación en la central de RNF02.
    public string Respuesta => EsSos ? Desde(AtencionIniciadaEn) : NoCorresponde;

    // CA02: reporte, del registro a la decisión de la revisión.
    public string HastaDecision => EsSos ? NoCorresponde : Desde(DecididoEn);

    // CA02: reporte, del registro al inicio de la atención «cuando corresponda»: un reporte derivado o descartado no se atiende.
    public string HastaAtencion => EsSos || Decision is TipoDecisionReporte.Derivacion or TipoDecisionReporte.Descarte
        ? NoCorresponde
        : Desde(AtencionIniciadaEn);

    // Columna común de la búsqueda: en un SOS, el tiempo de respuesta; en un reporte, hasta el inicio de la atención.
    public string HastaInicioAtencion => EsSos ? Respuesta : HastaAtencion;

    public static bool SinValor(string tiempo) => tiempo is NoDisponible or NoCorresponde;

    // CA03: sin la marca necesaria, «No disponible».
    private string Desde(DateTimeOffset? marca) => marca is { } m && m >= RegistradoEn ? Duracion(m - RegistradoEn) : NoDisponible;

    public static string Duracion(TimeSpan t) => t switch
    {
        { TotalHours: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours} h {t.Minutes:00} min {t.Seconds:00} s"),
        { TotalMinutes: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{t.Minutes} min {t.Seconds:00} s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{t.Seconds} s"),
    };
}

public sealed class ConsultarTiemposCaso(CadDbContext db)
{
    public async Task<TiemposCaso?> EjecutarAsync(Guid casoId, CancellationToken ct) =>
        await db.Casos.AsNoTracking().Where(c => c.Id == casoId)
            .Select(c => new TiemposCaso(c.Origen, c.RegistradoEn,
                db.DecisionesReporte.Where(d => d.CasoId == c.Id).Select(d => (DateTimeOffset?)d.DecididaEn).FirstOrDefault(),
                db.DecisionesReporte.Where(d => d.CasoId == c.Id).Select(d => (TipoDecisionReporte?)d.Tipo).FirstOrDefault(),
                db.EventosCaso.Where(e => e.CasoId == c.Id && e.Tipo == TipoEventoCaso.AtencionIniciada)
                    .OrderBy(e => e.OcurridoEn).Select(e => (DateTimeOffset?)e.OcurridoEn).FirstOrDefault()))
            .SingleOrDefaultAsync(ct);
}
