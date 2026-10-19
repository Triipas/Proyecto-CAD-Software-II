using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Acompanamiento.Consultas;

// Una posición con su hora de obtención y su precisión (HU38-CA01). Antigüedad: desde la hora de obtención hasta ahora
// (CA02). Desactualizada: más antigua que el umbral de DP12 (60 s); no se presenta como actual (CA03).
public sealed record PosicionVista(double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn, DateTimeOffset RecibidaEn,
    TimeSpan Antiguedad, bool Desactualizada);

// HU38: una sesión activa con su última posición. SinConexion (DP20): más del umbral sin recibir ningún envío del dispositivo.
public sealed record AcompanamientoActivo(Guid Id, string Ciudadano, string? Telefono, DateTimeOffset IniciadaEn, DateTimeOffset VenceEn,
    DateTimeOffset? UltimoContactoEn, bool SinConexion, PosicionVista? Ultima, int Posiciones, ConfirmacionVista? UltimaConfirmacion = null);

// HU40 y HU41: una confirmación de bienestar con quién la pidió, cuándo se presentó y la respuesta (con su SOS, si pidió ayuda).
public sealed record ConfirmacionVista(Guid Id, string Operador, DateTimeOffset SolicitadaEn, DateTimeOffset? PresentadaEn,
    RespuestaBienestar? Respuesta, DateTimeOffset? RespondidaEn, Guid? CasoId)
{
    public bool Pendiente => Respuesta is null;
    public string TextoRespuesta => Respuesta switch
    {
        RespuestaBienestar.EstoyBien => "Estoy bien",
        RespuestaBienestar.NecesitoAyuda => "Necesito ayuda",
        _ => PresentadaEn is null ? "Pendiente: aún no se mostró al ciudadano" : "Pendiente: mostrada al ciudadano, sin respuesta",
    };
}

// HU39-A: una sesión terminada (finalizada por el ciudadano o vencida), con sus datos de inicio, vencimiento y cierre (CA02).
public sealed record SesionTerminada(Guid Id, string Ciudadano, EstadoSesion Estado, DateTimeOffset IniciadaEn, DateTimeOffset VenceEn,
    DateTimeOffset CerradaEn, int Posiciones, DateTimeOffset? PosicionesEliminadasEn = null, Guid? CasoConservado = null);

// HU39-B (CA05): conservación excepcional del recorrido, con su caso, finalidad, responsable y momento.
public sealed record ConservacionVista(Guid CasoId, string Finalidad, string Operador, DateTimeOffset RegistradaEn);

public sealed record PaginaSesiones(IReadOnlyList<SesionTerminada> Sesiones, int Total, int Pagina, int TotalPaginas);

// Recorrido de una sesión (activa o terminada), de la posición más reciente a la más antigua.
public sealed record DetalleAcompanamiento(Guid Id, string Ciudadano, string? Telefono, EstadoSesion Estado, DateTimeOffset IniciadaEn,
    DateTimeOffset VenceEn, DateTimeOffset? CerradaEn, DateTimeOffset? UltimoContactoEn, bool SinConexion, IReadOnlyList<PosicionVista> Posiciones,
    DateTimeOffset? PosicionesEliminadasEn = null, ConservacionVista? Conservacion = null, IReadOnlyList<ConfirmacionVista>? Confirmaciones = null)
{
    public bool ConfirmacionPendiente => Confirmaciones?.Any(c => c.Pendiente) == true;

    public bool Activa => Estado == EstadoSesion.Activa;
}

// HU38 — Consultar los acompañamientos activos y sus posiciones (S4-T09) y HU39-A — historial de sesiones (S4-T11). Solo
// lectura; la política OperadorAutorizado (DP04) la aplican las páginas. Separado del historial de los casos (HU39-CA01).
public sealed class ConsultarAcompanamientos(CadDbContext db, GestionarAcompanamiento gestionar, TimeProvider reloj,
    IOptions<OpcionesAcompanamiento> opciones)
{
    public const int TamanoPagina = 50;

    public TimeSpan Umbral => TimeSpan.FromSeconds(Math.Max(1, opciones.Value.UmbralDesactualizadaSegundos));

    public async Task<IReadOnlyList<AcompanamientoActivo>> ActivasAsync(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        await gestionar.VencerAsync(ahora, null, ct); // una sesión vencida no se muestra como activa
        var filas = await (from sesion in db.SesionesAcompanamiento.AsNoTracking()
                           join ciudadano in db.Users.AsNoTracking() on sesion.CiudadanoId equals ciudadano.Id
                           where sesion.Estado == EstadoSesion.Activa
                           orderby sesion.IniciadaEn descending
                           select new
                           {
                               sesion.Id, Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos, ciudadano.PhoneNumber,
                               sesion.IniciadaEn, sesion.VenceEn, sesion.UltimoContactoEn,
                               Ultima = db.PosicionesAcompanamiento.Where(p => p.SesionId == sesion.Id)
                                   .OrderByDescending(p => p.ObtenidaEn).FirstOrDefault(),
                               Posiciones = db.PosicionesAcompanamiento.Count(p => p.SesionId == sesion.Id),
                           })
                          .ToListAsync(ct);
        var ids = filas.Select(f => f.Id).ToList();
        var confirmaciones = (await ConfirmacionesAsync(c => ids.Contains(c.SesionId), ct))
            .GroupBy(c => c.SesionId).ToDictionary(g => g.Key, g => g.First().Vista);
        return filas.Select(f => new AcompanamientoActivo(f.Id, f.Ciudadano, f.PhoneNumber, f.IniciadaEn, f.VenceEn, f.UltimoContactoEn,
                SinConexion(f.UltimoContactoEn, ahora), f.Ultima is null ? null : Vista(f.Ultima, ahora), f.Posiciones,
                confirmaciones.GetValueOrDefault(f.Id)))
            .ToList();
    }

    public async Task<DetalleAcompanamiento?> DetalleAsync(Guid id, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        await gestionar.VencerAsync(ahora, null, ct);
        var sesion = await (from s in db.SesionesAcompanamiento.AsNoTracking()
                            join ciudadano in db.Users.AsNoTracking() on s.CiudadanoId equals ciudadano.Id
                            where s.Id == id
                            select new { Sesion = s, Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos, ciudadano.PhoneNumber })
                           .SingleOrDefaultAsync(ct);
        if (sesion is null)
            return null;
        var posiciones = await db.PosicionesAcompanamiento.AsNoTracking().Where(p => p.SesionId == id)
            .OrderByDescending(p => p.ObtenidaEn).ToListAsync(ct);
        var conservacion = await (from c in db.ConservacionesRecorrido.AsNoTracking()
                                  join operador in db.Users.AsNoTracking() on c.OperadorId equals operador.Id
                                  where c.SesionId == id
                                  select new ConservacionVista(c.CasoId, c.Finalidad, operador.Nombres + " " + operador.Apellidos, c.RegistradaEn))
                                 .SingleOrDefaultAsync(ct);
        var datos = sesion.Sesion;
        var activa = datos.Estado == EstadoSesion.Activa;
        // En una sesión terminada no hay posición actual: la antigüedad se mide hasta el cierre y todas son históricas.
        var referencia = activa ? ahora : datos.CerradaEn ?? ahora;
        return new DetalleAcompanamiento(datos.Id, sesion.Ciudadano, sesion.PhoneNumber, datos.Estado, datos.IniciadaEn, datos.VenceEn, datos.CerradaEn,
            datos.UltimoContactoEn, activa && SinConexion(datos.UltimoContactoEn, ahora),
            posiciones.Select(p => Vista(p, referencia, activa)).ToList(), datos.PosicionesEliminadasEn, conservacion,
            (await ConfirmacionesAsync(c => c.SesionId == id, ct)).Select(c => c.Vista).ToList());
    }

    // HU40/HU41: confirmaciones de bienestar, de la más reciente a la más antigua, con el nombre del operador.
    private async Task<List<(Guid SesionId, ConfirmacionVista Vista)>> ConfirmacionesAsync(
        System.Linq.Expressions.Expression<Func<ConfirmacionBienestar, bool>> filtro, CancellationToken ct) =>
        (await (from c in db.ConfirmacionesBienestar.AsNoTracking().Where(filtro)
                join operador in db.Users.AsNoTracking() on c.OperadorId equals operador.Id
                orderby c.SolicitadaEn descending
                select new { c.SesionId, Vista = new ConfirmacionVista(c.Id, operador.Nombres + " " + operador.Apellidos, c.SolicitadaEn, c.PresentadaEn,
                    c.Respuesta, c.RespondidaEn, c.CasoId) })
            .ToListAsync(ct))
        .Select(x => (x.SesionId, x.Vista)).ToList();

    // HU39-A: solo las sesiones terminadas, de la más reciente a la más antigua, de 50 en 50 (como la búsqueda de casos).
    public async Task<PaginaSesiones> HistorialAsync(int pagina, CancellationToken ct)
    {
        await gestionar.VencerAsync(reloj.GetUtcNow(), null, ct);
        var consulta = from sesion in db.SesionesAcompanamiento.AsNoTracking()
                       join ciudadano in db.Users.AsNoTracking() on sesion.CiudadanoId equals ciudadano.Id
                       where sesion.Estado != EstadoSesion.Activa
                       select new { sesion, Ciudadano = ciudadano.Nombres + " " + ciudadano.Apellidos };
        var total = await consulta.CountAsync(ct);
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)TamanoPagina));
        pagina = Math.Clamp(pagina, 1, totalPaginas);
        var filas = await consulta.OrderByDescending(x => x.sesion.CerradaEn).ThenByDescending(x => x.sesion.Id)
            .Skip((pagina - 1) * TamanoPagina).Take(TamanoPagina)
            .Select(x => new SesionTerminada(x.sesion.Id, x.Ciudadano, x.sesion.Estado, x.sesion.IniciadaEn, x.sesion.VenceEn, x.sesion.CerradaEn!.Value,
                db.PosicionesAcompanamiento.Count(p => p.SesionId == x.sesion.Id), x.sesion.PosicionesEliminadasEn,
                db.ConservacionesRecorrido.Where(c => c.SesionId == x.sesion.Id).Select(c => (Guid?)c.CasoId).FirstOrDefault()))
            .ToListAsync(ct);
        return new PaginaSesiones(filas, total, pagina, totalPaginas);
    }

    private bool SinConexion(DateTimeOffset? ultimoContacto, DateTimeOffset ahora) => ultimoContacto is null || ahora - ultimoContacto > Umbral;

    private PosicionVista Vista(PosicionAcompanamiento p, DateTimeOffset referencia, bool puedeSerActual = true)
    {
        var antiguedad = referencia - p.ObtenidaEn;
        if (antiguedad < TimeSpan.Zero)
            antiguedad = TimeSpan.Zero;
        return new PosicionVista(p.Latitud, p.Longitud, p.PrecisionMetros, p.ObtenidaEn, p.RecibidaEn, antiguedad,
            !puedeSerActual || antiguedad > Umbral);
    }
}
