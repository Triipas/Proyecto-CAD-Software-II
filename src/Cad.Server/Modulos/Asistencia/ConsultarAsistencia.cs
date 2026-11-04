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

// Freno: el motivo, si el modo vigente lo puso el freno automático (7.6), para avisarlo en el panel.
public sealed record AjusteIaVista(ModoAdmision Modo, ParametrosAdmision Parametros, DateTimeOffset? CambiadoEn, string? Freno = null);

// S6-T16: lo que concluyó la admisión automática sobre un reporte.
public sealed record EvaluacionAdmisionVista(ModoAdmision Modo, bool Elegible, CondicionAdmision? Condicion, DateTimeOffset? AdmitirDesde,
    string? ModeloVerificador);

// 7.7: en «Sombra», lo que habría admitido frente a lo que decidieron los operadores.
public sealed record PrecisionSombra(int Elegibles, int Decididos, int Admitidos, IReadOnlyList<NoAdmitidoEnSombra> NoAdmitidos)
{
    public int? Porcentaje => Decididos == 0 ? null : (int)Math.Floor(100.0 * Admitidos / Decididos);
}

public sealed record NoAdmitidoEnSombra(Guid CasoId, string Categoria, string Decision, DateTimeOffset EvaluadaEn);

// S6-T17 (plan, 9.2) — el rendimiento del asistente en el uso real, por modelo: cuántas sugerencias aceptaron los operadores (la
// categoría final y la decisión registrada frente a las sugeridas), cuánto tardó y cuántos tokens gastó. Lo que hizo el operador se
// deduce de los datos de siempre: la sugerencia nunca se modifica.
public sealed record RendimientoModelo(string Modelo, int Sugerencias, int Decididas, int CategoriaAceptada, int DecisionComparable,
    int DecisionAceptada, int Escaladas, int LatenciaMedianaMs, int LatenciaP90Ms, long TokensEntrada, long TokensSalida)
{
    public int? PorcentajeCategoria => Decididas == 0 ? null : (int)Math.Round(100.0 * CategoriaAceptada / Decididas);
    public int? PorcentajeDecision => DecisionComparable == 0 ? null : (int)Math.Round(100.0 * DecisionAceptada / DecisionComparable);
}

public sealed record CambioAjusteVista(ModoAdmision Anterior, ModoAdmision Nuevo, string Parametros, string Motivo, string Actor, DateTimeOffset En);

// Lecturas del módulo para el panel: la sugerencia de un reporte (HU46, HU47), las categorías sugeridas de la lista de reportes, y
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

    // HU46: en la lista de reportes, la categoría sugerida solo cuando difiere de la vigente.
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
        var ultimo = ajuste.Modo == ModoAdmision.Sombra
            ? await db.CambiosAjusteIa.AsNoTracking().OrderByDescending(c => c.En).FirstOrDefaultAsync(ct)
            : null;
        var freno = ultimo is { ModoNuevo: ModoAdmision.Sombra } && ultimo.ActorId == Cuentas.Dominio.UsuarioSistema.Id ? ultimo.Motivo : null;
        return new AjusteIaVista(ajuste.Modo, ajuste.Parametros, ajuste.CambiadoEn, freno);
    }

    public async Task<IReadOnlyList<RendimientoModelo>> RendimientoAsync(DateTimeOffset desde, CancellationToken ct)
    {
        var sistema = Cuentas.Dominio.UsuarioSistema.Id;
        var filas = await (from s in db.SugerenciasReporte.AsNoTracking()
                           join caso in db.Casos.AsNoTracking() on s.CasoId equals caso.Id
                           from d in db.DecisionesReporte.AsNoTracking().Where(d => d.CasoId == s.CasoId && d.OperadorId != sistema).DefaultIfEmpty()
                           where s.CreadaEn >= desde
                           select new
                           {
                               s.Modelo, s.Decision, s.CategoriaSugeridaId, caso.CategoriaId, s.Escalada, s.LatenciaMs, s.TokensEntrada, s.TokensSalida,
                               Tipo = (Casos.Dominio.TipoDecisionReporte?)d.Tipo,
                           })
            .ToListAsync(ct);
        static bool Coincide(DecisionSugerida s, Casos.Dominio.TipoDecisionReporte t) => (s, t) switch
        {
            (DecisionSugerida.Admitir, Casos.Dominio.TipoDecisionReporte.Admision) => true,
            (DecisionSugerida.Derivar, Casos.Dominio.TipoDecisionReporte.Derivacion) => true,
            (DecisionSugerida.Descartar, Casos.Dominio.TipoDecisionReporte.Descarte) => true,
            _ => false,
        };
        static int Percentil(List<int> ordenadas, double p) => ordenadas.Count == 0 ? 0 : ordenadas[(int)Math.Min(ordenadas.Count - 1, Math.Floor(p * ordenadas.Count))];
        return filas.GroupBy(f => f.Modelo).Select(g =>
        {
            var decididas = g.Where(f => f.Tipo is not null).ToList();
            var comparables = decididas.Where(f => f.Decision != DecisionSugerida.RevisarConCuidado).ToList();
            var latencias = g.Select(f => f.LatenciaMs).Order().ToList();
            return new RendimientoModelo(g.Key, g.Count(), decididas.Count, decididas.Count(f => f.CategoriaId == f.CategoriaSugeridaId),
                comparables.Count, comparables.Count(f => Coincide(f.Decision, f.Tipo!.Value)), g.Count(f => f.Escalada),
                Percentil(latencias, 0.5), Percentil(latencias, 0.9), g.Sum(f => (long)f.TokensEntrada), g.Sum(f => (long)f.TokensSalida));
        }).OrderByDescending(r => r.Sugerencias).ToList();
    }

    public async Task<EvaluacionAdmisionVista?> EvaluacionAsync(Guid casoId, CancellationToken ct) =>
        await db.EvaluacionesAdmision.AsNoTracking().Where(e => e.CasoId == casoId)
            .Select(e => new EvaluacionAdmisionVista(e.Modo, e.Elegible, e.Condicion, e.AdmitirDesde, e.ModeloVerificador))
            .SingleOrDefaultAsync(ct);

    // 7.4: en la lista, cuándo se admitirá solo cada reporte que espera el retraso (solo con el modo en «Activa»).
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ProgramadasAsync(IReadOnlyCollection<Guid> casos, CancellationToken ct)
    {
        if (casos.Count == 0 || (await AjusteAsync(ct)).Modo != ModoAdmision.Activa)
            return new Dictionary<Guid, DateTimeOffset>();
        return await (from e in db.EvaluacionesAdmision.AsNoTracking()
                      join caso in db.Casos.AsNoTracking() on e.CasoId equals caso.Id
                      where casos.Contains(e.CasoId) && e.Modo == ModoAdmision.Activa && e.Elegible && e.AdmitirDesde != null
                            && caso.Estado == Casos.Dominio.EstadoCaso.Recibido
                      select new { e.CasoId, AdmitirDesde = e.AdmitirDesde!.Value })
            .ToDictionaryAsync(e => e.CasoId, e => e.AdmitirDesde, ct);
    }

    // 7.7: de los reportes que «Sombra» habría admitido y que un operador ya decidió, cuántos admitió; y la lista de los que no.
    public async Task<PrecisionSombra> PrecisionSombraAsync(CancellationToken ct)
    {
        var filas = await (from e in db.EvaluacionesAdmision.AsNoTracking()
                           join caso in db.Casos.AsNoTracking() on e.CasoId equals caso.Id
                           join categoria in db.CategoriasIncidencia.AsNoTracking() on e.CategoriaId equals categoria.Id
                           from d in db.DecisionesReporte.AsNoTracking().Where(d => d.CasoId == e.CasoId).DefaultIfEmpty()
                           where e.Modo == ModoAdmision.Sombra && e.Elegible
                           select new { e.CasoId, e.EvaluadaEn, Categoria = categoria.Nombre, Tipo = (Casos.Dominio.TipoDecisionReporte?)d.Tipo })
            .ToListAsync(ct);
        var decididos = filas.Where(f => f.Tipo is not null).ToList();
        var noAdmitidos = decididos.Where(f => f.Tipo != Casos.Dominio.TipoDecisionReporte.Admision)
            .OrderByDescending(f => f.EvaluadaEn)
            .Select(f => new NoAdmitidoEnSombra(f.CasoId, f.Categoria, f.Tipo == Casos.Dominio.TipoDecisionReporte.Derivacion ? "Derivado" : "Descartado", f.EvaluadaEn))
            .Take(20)
            .ToList();
        return new PrecisionSombra(filas.Count, decididos.Count, decididos.Count(f => f.Tipo == Casos.Dominio.TipoDecisionReporte.Admision), noAdmitidos);
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
