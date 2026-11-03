using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Analitica;

// Bloque C, propuesta 13 — percepción de inseguridad (27/10/2026). El acompañamiento dice dónde y a qué hora la gente se siente
// insegura, aunque no pase nada: por dónde pasan los recorridos acompañados y dónde el ciudadano avisó que algo no estaba bien
// («Algo no está bien» o «Necesito ayuda», por su cuenta o al responder una confirmación). Solo por zona, nunca un recorrido.
//
// Las posiciones de un acompañamiento se eliminan al cumplirse su plazo (HU39-B). Para no perder este dato, justo antes de
// eliminarlas se guarda su agregado anónimo (PercepcionZona); las sesiones que aún conservan sus posiciones se calculan al
// consultar. Cada sesión cuenta una vez en cada zona por la que pasó (en la hora en que entró), y cada aviso, en la zona de la
// última posición anterior a él.
public sealed class PercepcionZona
{
    // Hora (UTC, al inicio de la hora) y zona de la cuadrícula de HU29. Sin ninguna referencia a la sesión ni al ciudadano.
    public DateTimeOffset Hora { get; private set; }
    public long Fila { get; private set; }
    public long Columna { get; private set; }
    public int Recorridos { get; private set; }
    public int Avisos { get; private set; }

    private PercepcionZona() { } // EF Core
}

public static class PercepcionCalculo
{
    public readonly record struct Clave(DateTimeOffset Hora, Zona Zona);

    // El agregado de un conjunto de sesiones (las que se van a eliminar, o las que aún tienen sus posiciones).
    public static async Task<Dictionary<Clave, (int Recorridos, int Avisos)>> AgregarAsync(CadDbContext db, IReadOnlyCollection<Guid> sesiones,
        CancellationToken ct)
    {
        var resultado = new Dictionary<Clave, (int Recorridos, int Avisos)>();
        if (sesiones.Count == 0)
            return resultado;
        var posiciones = (await db.PosicionesAcompanamiento.AsNoTracking()
                .Where(p => sesiones.Contains(p.SesionId))
                .Select(p => new { p.SesionId, p.ObtenidaEn, p.Latitud, p.Longitud })
                .ToListAsync(ct))
            .GroupBy(p => p.SesionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ObtenidaEn).ToList());
        var informados = await db.EstadosInformados.AsNoTracking()
            .Where(e => sesiones.Contains(e.SesionId) && (e.Estado == EstadoCiudadano.AlgoNoEstaBien || e.Estado == EstadoCiudadano.NecesitoAyuda))
            .Select(e => new { e.SesionId, En = e.InformadoEn })
            .ToListAsync(ct);
        var confirmaciones = await db.ConfirmacionesBienestar.AsNoTracking()
            .Where(c => sesiones.Contains(c.SesionId) && c.Respuesta == RespuestaBienestar.NecesitoAyuda && c.RespondidaEn != null)
            .Select(c => new { c.SesionId, En = c.RespondidaEn!.Value })
            .ToListAsync(ct);

        void Sumar(Clave clave, int recorridos, int avisos)
        {
            var (r, a) = resultado.GetValueOrDefault(clave);
            resultado[clave] = (r + recorridos, a + avisos);
        }

        foreach (var (_, lista) in posiciones)
            foreach (var primera in lista.GroupBy(p => Zona.De(p.Latitud, p.Longitud)).Select(g => g.First()))
                Sumar(new Clave(InicioDeHora(primera.ObtenidaEn), Zona.De(primera.Latitud, primera.Longitud)), 1, 0);
        foreach (var aviso in informados.Concat(confirmaciones))
        {
            if (!posiciones.TryGetValue(aviso.SesionId, out var lista))
                continue;
            var antes = lista.LastOrDefault(p => p.ObtenidaEn <= aviso.En) ?? lista[0];
            Sumar(new Clave(InicioDeHora(aviso.En), Zona.De(antes.Latitud, antes.Longitud)), 0, 1);
        }
        return resultado;
    }

    // Se guarda en la misma transacción que la eliminación de las posiciones (ConservarRecorridos): o las dos cosas, o ninguna.
    public static async Task GuardarAsync(CadDbContext db, IReadOnlyCollection<Guid> sesiones, CancellationToken ct)
    {
        foreach (var (clave, (recorridos, avisos)) in await AgregarAsync(db, sesiones, ct))
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "PercepcionZonas" ("Hora", "Fila", "Columna", "Recorridos", "Avisos")
                VALUES ({clave.Hora}, {clave.Zona.Fila}, {clave.Zona.Columna}, {recorridos}, {avisos})
                ON CONFLICT ("Hora", "Fila", "Columna") DO UPDATE
                SET "Recorridos" = "PercepcionZonas"."Recorridos" + EXCLUDED."Recorridos", "Avisos" = "PercepcionZonas"."Avisos" + EXCLUDED."Avisos"
                """, ct);
    }

    public static DateTimeOffset InicioDeHora(DateTimeOffset momento)
    {
        var utc = momento.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}

public sealed record ZonaPercepcion(double Sur, double Oeste, double Norte, double Este, int Recorridos, int Avisos)
{
    public double AvisosPorCadaCienRecorridos => Recorridos == 0 ? 0 : 100.0 * Avisos / Recorridos;
}

public sealed record HoraPercepcion(int Hora, int Recorridos, int Avisos);

// Zonas: las que tienen al menos MinimoCasosPorZona recorridos (privacidad); las demás, sumadas en Agrupadas.
public sealed record Percepcion(IReadOnlyList<ZonaPercepcion> Zonas, IReadOnlyList<HoraPercepcion> PorHora, int ZonasAgrupadas,
    int RecorridosAgrupados, int AvisosAgrupados);

public sealed class ConsultarPercepcion(CadDbContext db, IOptions<OpcionesAnalitica> opciones)
{
    public async Task<Percepcion> EjecutarAsync(PeriodoAnalitica periodo, CancellationToken ct)
    {
        var guardado = await db.PercepcionZonas.AsNoTracking()
            .Where(p => p.Hora >= periodo.Desde && p.Hora < periodo.Hasta)
            .ToListAsync(ct);
        // Las sesiones del periodo que aún tienen sus posiciones (no se cuentan dos veces: el agregado guardado es solo de
        // las ya eliminadas).
        var vigentes = await db.SesionesAcompanamiento.AsNoTracking()
            .Where(s => s.PosicionesEliminadasEn == null && s.IniciadaEn < periodo.Hasta && (s.CerradaEn == null || s.CerradaEn >= periodo.Desde))
            .Select(s => s.Id)
            .ToListAsync(ct);
        var calculado = (await PercepcionCalculo.AgregarAsync(db, vigentes, ct))
            .Where(c => c.Key.Hora >= PercepcionCalculo.InicioDeHora(periodo.Desde) && c.Key.Hora < periodo.Hasta);

        var filas = guardado.Select(p => (Hora: p.Hora, Zona: new Zona(p.Fila, p.Columna), p.Recorridos, p.Avisos))
            .Concat(calculado.Select(c => (c.Key.Hora, c.Key.Zona, c.Value.Recorridos, c.Value.Avisos)))
            .ToList();
        var porZona = filas.GroupBy(f => f.Zona)
            .Select(g => new ZonaPercepcion(g.Key.Sur, g.Key.Oeste, g.Key.Norte, g.Key.Este, g.Sum(f => f.Recorridos), g.Sum(f => f.Avisos)))
            .ToList();
        var minimo = opciones.Value.MinimoCasosPorZona;
        var agrupadas = porZona.Where(z => z.Recorridos < minimo).ToList();
        var porHora = Enumerable.Range(0, 24).Select(h =>
        {
            var deLaHora = filas.Where(f => TimeZoneInfo.ConvertTime(f.Hora, periodo.Zona).Hour == h).ToList();
            return new HoraPercepcion(h, deLaHora.Sum(f => f.Recorridos), deLaHora.Sum(f => f.Avisos));
        }).ToList();
        return new Percepcion(
            porZona.Where(z => z.Recorridos >= minimo).OrderByDescending(z => z.AvisosPorCadaCienRecorridos).ThenByDescending(z => z.Recorridos).ToList(),
            porHora, agrupadas.Count, agrupadas.Sum(z => z.Recorridos), agrupadas.Sum(z => z.Avisos));
    }
}
