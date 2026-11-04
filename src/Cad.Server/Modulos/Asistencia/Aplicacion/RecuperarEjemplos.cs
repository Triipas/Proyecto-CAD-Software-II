using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// HU45 (S6-T09; plan de asistencia con IA, 5.4) — decisiones que la central ya tomó sobre reportes parecidos, para que el modelo
// decida y redacte como ella. Búsqueda de texto completo de PostgreSQL en español: los reportes que comparten palabras con el nuevo,
// por parecido (ts_rank). La mitad son los más parecidos; la otra mitad equilibra las decisiones (admitir, derivar, descartar) para
// que la mayoría no arrastre al modelo. Excluye el propio caso, los reportes de la central (entran admitidos sin revisión) y las
// admisiones automáticas (el sistema no aprende de sí mismo). Todo sale anonimizado y recortado. Con el uso, los ejemplos crecen
// solos: cada decisión de un operador mejora el siguiente análisis sin entrenar nada.
public sealed class RecuperarEjemplos(CadDbContext db) : IRecuperadorEjemplos
{
    // Lo que se envía de cada ejemplo: suficiente para entenderlo, sin gastar tokens.
    public const int MaximoDescripcion = 300;
    private const int Candidatos = 60;

    public sealed record Fila(Guid CasoId, string Descripcion, string Categoria, string Tipo, string Motivo, string? DestinoCodigo,
        string? MotivoCodigo, float Parecido, DateTimeOffset DecididaEn);

    public async Task<IReadOnlyList<EjemploDecidido>> RecuperarAsync(Guid casoId, string descripcion, int cantidad, CancellationToken ct)
    {
        if (cantidad <= 0 || string.IsNullOrWhiteSpace(descripcion))
            return [];
        var sistema = UsuarioSistema.Id;

        // Consulta «o» con las palabras del reporte (lexemas en español): basta con compartir alguna; ts_rank ordena por cuántas y cuáles.
        var parecidos = await db.Database.SqlQuery<Fila>($"""
            WITH consulta AS (
              SELECT to_tsquery('spanish', string_agg(DISTINCT quote_literal(t.lexeme), ' | ')) AS q
              FROM unnest(to_tsvector('spanish', {descripcion})) AS t
            )
            SELECT d."CasoId", d."Descripcion", c."Nombre" AS "Categoria", r."Tipo", r."Motivo", r."DestinoCodigo", r."MotivoCodigo",
                   ts_rank(to_tsvector('spanish', d."Descripcion"), consulta.q) AS "Parecido", r."DecididaEn"
            FROM "DecisionesReporte" r
            JOIN "DetallesReporte" d ON d."CasoId" = r."CasoId"
            JOIN "Casos" k ON k."Id" = r."CasoId"
            JOIN "CategoriasIncidencia" c ON c."Id" = k."CategoriaId"
            CROSS JOIN consulta
            WHERE consulta.q IS NOT NULL AND r."CasoId" <> {casoId} AND d."RegistradoPorOperadorId" IS NULL AND r."OperadorId" <> {sistema}
              AND to_tsvector('spanish', d."Descripcion") @@ consulta.q
            ORDER BY "Parecido" DESC, r."DecididaEn" DESC
            LIMIT {Candidatos}
            """).ToListAsync(ct);

        var elegidos = parecidos.Take((cantidad + 1) / 2).ToList();
        // Equilibrio: el más parecido de cada decisión que falte; si no hay ninguno parecido, el más reciente de esa decisión.
        foreach (var tipo in new[] { "Admision", "Derivacion", "Descarte" })
        {
            if (elegidos.Count >= cantidad)
                break;
            if (elegidos.Any(e => e.Tipo == tipo))
                continue;
            var fila = parecidos.FirstOrDefault(p => p.Tipo == tipo) ?? await RecienteAsync(tipo, casoId, sistema, ct);
            if (fila is not null)
                elegidos.Add(fila);
        }
        foreach (var fila in parecidos.Except(elegidos))
        {
            if (elegidos.Count >= cantidad)
                break;
            elegidos.Add(fila);
        }

        return elegidos.Take(cantidad).Select(f => new EjemploDecidido(Recortar(Anonimizador.Limpiar(f.Descripcion)), f.Categoria, Decision(f.Tipo),
            Recortar(Anonimizador.Limpiar(f.Motivo)), f.DestinoCodigo, f.MotivoCodigo)).ToList();
    }

    private async Task<Fila?> RecienteAsync(string tipo, Guid casoId, Guid sistema, CancellationToken ct) =>
        (await db.Database.SqlQuery<Fila>($"""
            SELECT d."CasoId", d."Descripcion", c."Nombre" AS "Categoria", r."Tipo", r."Motivo", r."DestinoCodigo", r."MotivoCodigo",
                   0::real AS "Parecido", r."DecididaEn"
            FROM "DecisionesReporte" r
            JOIN "DetallesReporte" d ON d."CasoId" = r."CasoId"
            JOIN "Casos" k ON k."Id" = r."CasoId"
            JOIN "CategoriasIncidencia" c ON c."Id" = k."CategoriaId"
            WHERE r."Tipo" = {tipo} AND r."CasoId" <> {casoId} AND d."RegistradoPorOperadorId" IS NULL AND r."OperadorId" <> {sistema}
            ORDER BY r."DecididaEn" DESC
            LIMIT 1
            """).ToListAsync(ct)).SingleOrDefault();

    private static DecisionSugerida Decision(string tipo) => tipo switch
    {
        "Derivacion" => DecisionSugerida.Derivar,
        "Descarte" => DecisionSugerida.Descartar,
        _ => DecisionSugerida.Admitir,
    };

    private static string Recortar(string texto) => texto.Length <= MaximoDescripcion ? texto : texto[..(MaximoDescripcion - 1)].TrimEnd() + "…";
}
