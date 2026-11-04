using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Asistencia;

public sealed record SugerenciaVista(
    Guid CategoriaSugeridaId,
    string CategoriaSugerida,
    string? CategoriaAlternativa,
    NivelConfianza Confianza,
    string Justificacion,
    DecisionSugerida Decision,
    string MotivoBorrador,
    string? DestinoCodigo,
    string? AccionBorrador,
    string? MotivoDescarteCodigo,
    string? Prioridad,
    bool PosibleUrgencia,
    string? MotivoUrgencia,
    IReadOnlyList<SenalTriaje> Senales,
    Guid? DuplicadoDeId,
    string Modelo,
    bool Escalada,
    DateTimeOffset CreadaEn)
{
    public string? TextoDestino => Catalogo.Texto(DestinosDerivacion.Todos, DestinoCodigo);
    public string? TextoMotivoDescarte => Catalogo.Texto(MotivosDescarte.Todos, MotivoDescarteCodigo);
    public string? TextoPrioridad => Catalogo.Texto(PrioridadesCaso.Todas, Prioridad);
    // Rotulado honesto (principio 5 del plan): de dónde sale la sugerencia.
    public bool PorReglas => Modelo == AsistenteReglas.Modelo;
}

public sealed record AjusteIaVista(ModoAdmision Modo, ParametrosAdmision Parametros, DateTimeOffset? CambiadoEn);

public sealed record CambioAjusteVista(ModoAdmision Anterior, ModoAdmision Nuevo, string Parametros, string Motivo, string Actor, DateTimeOffset En);

// Lecturas del módulo para el panel: la sugerencia de un reporte (HU44, HU45), las categorías sugeridas de la lista de reportes, y
// el modo de la admisión automática con su historial (S6-T15).
public sealed class ConsultarAsistencia(CadDbContext db)
{
    // La más reciente: si cambia la versión del prompt, se muestra la última que se calculó.
    public async Task<SugerenciaVista?> SugerenciaAsync(Guid casoId, CancellationToken ct)
    {
        var s = await db.SugerenciasReporte.AsNoTracking().Where(s => s.CasoId == casoId).OrderByDescending(s => s.CreadaEn)
            .FirstOrDefaultAsync(ct);
        if (s is null)
            return null;
        var categorias = await db.CategoriasIncidencia.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        return new SugerenciaVista(s.CategoriaSugeridaId, categorias.GetValueOrDefault(s.CategoriaSugeridaId, ""),
            s.CategoriaAlternativaId is { } alternativa ? categorias.GetValueOrDefault(alternativa) : null,
            s.Confianza, s.Justificacion, s.Decision, s.MotivoBorrador, s.DestinoCodigo, s.AccionBorrador, s.MotivoDescarteCodigo, s.Prioridad,
            s.PosibleUrgencia, s.MotivoUrgencia, s.ListaSenales, s.DuplicadoDeId, s.Modelo, s.Escalada, s.CreadaEn);
    }

    // HU44: en la lista de reportes, la categoría sugerida solo cuando difiere de la vigente.
    public async Task<IReadOnlyDictionary<Guid, string>> CategoriasDistintasAsync(IReadOnlyCollection<Guid> casos, CancellationToken ct)
    {
        if (casos.Count == 0)
            return new Dictionary<Guid, string>();
        var filas = await (from s in db.SugerenciasReporte.AsNoTracking()
                           join caso in db.Casos.AsNoTracking() on s.CasoId equals caso.Id
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on s.CategoriaSugeridaId equals categoria.Id
                           where casos.Contains(s.CasoId) && caso.CategoriaId != s.CategoriaSugeridaId
                           select new { s.CasoId, s.CreadaEn, categoria.Nombre })
            .ToListAsync(ct);
        return filas.GroupBy(f => f.CasoId).ToDictionary(g => g.Key, g => g.MaxBy(f => f.CreadaEn)!.Nombre);
    }

    public async Task<AjusteIaVista> AjusteAsync(CancellationToken ct)
    {
        var ajuste = await db.AjustesIa.AsNoTracking().SingleOrDefaultAsync(a => a.Id == AjusteIa.IdUnico, ct) ?? AjusteIa.PorDefecto();
        return new AjusteIaVista(ajuste.Modo, ajuste.Parametros, ajuste.CambiadoEn);
    }

    public async Task<IReadOnlyList<CambioAjusteVista>> HistorialAsync(int maximo, CancellationToken ct) =>
        await (from cambio in db.CambiosAjusteIa.AsNoTracking()
               join actor in db.Users.AsNoTracking() on cambio.ActorId equals actor.Id
               orderby cambio.En descending
               select new CambioAjusteVista(cambio.ModoAnterior, cambio.ModoNuevo, cambio.Parametros, cambio.Motivo,
                   actor.Nombres + " " + actor.Apellidos, cambio.En))
            .Take(maximo)
            .ToListAsync(ct);

    public static string Texto(ModoAdmision modo) => modo switch
    {
        ModoAdmision.Sombra => "Sombra",
        ModoAdmision.Activa => "Activa",
        _ => "Apagada",
    };
}
