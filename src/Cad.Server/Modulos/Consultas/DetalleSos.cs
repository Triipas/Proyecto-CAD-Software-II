using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU12 — datos que el operador necesita para verificar un SOS. Solo lectura (módulo Consultas).
public sealed record DetalleSos(
    Guid Id,
    string Estado,
    DateTimeOffset RegistradoEn,
    string Ciudadano,
    string? Telefono,
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros,
    DateTimeOffset? UbicacionObtenidaEn,
    IReadOnlyList<IntentoContactoVista> Intentos,
    AsignacionVista? Asignacion,
    ClasificacionVista? Clasificacion,
    IReadOnlyList<AvanceVista>? Avances = null,
    CierreVista? Cierre = null,
    // DP19: un caso urgente creado desde un aviso del acompañamiento se verifica y asigna desde esta misma vista.
    string Origen = "SOS")
{
    public bool EsCasoUrgente => Origen != "SOS";

    public bool EnCamino => Estado == EstadosCaso.Texto(EstadoCaso.EnCamino);
    public bool EnAtencion => Estado == EstadosCaso.Texto(EstadoCaso.EnAtencion);

    public bool UbicacionDisponible => Latitud is not null && Longitud is not null;
}

// HU22/HU23: avances del caso con su responsable, fecha y hora.
// Nota: en el inicio de atención, dónde estaba el sereno respecto del punto del caso (rango de atención, 24/10/2026);
// Alerta si lo inició fuera del rango.
public sealed record AvanceVista(string Descripcion, string Responsable, DateTimeOffset En, string? Nota = null, bool Alerta = false);

// Rango de atención (24/10/2026): cómo se cuenta a la central dónde inició el sereno la atención.
public static class TextoInicioAtencion
{
    public static string? Describir(InicioAtencion inicio)
    {
        var motivo = inicio.Motivo is { } m
            ? $" · Motivo: {MotivosInicioFueraDeRango.Texto(m)}{(inicio.Detalle is { } d ? $" ({d})" : "")}"
            : "";
        return inicio.DentroDelRango switch
        {
            true => $"En el lugar: a {RangoAtencion.TextoDistancia(inicio.DistanciaMetros!.Value)} del punto del caso",
            false when inicio.DistanciaMetros is { } distancia =>
                $"Fuera del rango: a {RangoAtencion.TextoDistancia(distancia)} del punto del caso (rango {inicio.RadioMetros:0} m){motivo}",
            false => $"Sin posición reciente del teléfono del sereno{motivo}",
            _ => null, // el caso no tiene un punto con el que comparar
        };
    }
}

// HU13: categoría asignada, responsable y momento (CA01). Nula = «Tipo por determinar» (CA03).
public sealed record ClasificacionVista(string Categoria, string Operador, DateTimeOffset ClasificadoEn);

// HU20: asignación vigente del caso (sereno y momento).
public sealed record AsignacionVista(Guid SerenoId, string Sereno, DateTimeOffset AsignadaEn, DateTimeOffset? FinalizadaEn = null);

// HU24: cierre registrado. Resultado público y notas internas diferenciados (CA04).
public sealed record CierreVista(string Tipificacion, string ResultadoPublico, string? NotasInternas, string Responsable, DateTimeOffset CerradoEn);

public sealed record IntentoContactoVista(
    ResultadoContacto Resultado,
    string Operador,
    DateTimeOffset RegistradoEn,
    string? InformacionHecho,
    string? ReferenciaUbicacion);

public sealed class ConsultarDetalleSos(CadDbContext db)
{
    // cualquierOrigen: el detalle de un reporte en curso usa la misma asignación, avances y cierre (decisión del PO del
    // 16/10/2026: el operador registra el inicio de atención y cierra un reporte como un SOS).
    public async Task<DetalleSos?> EjecutarAsync(Guid casoId, CancellationToken ct, bool cualquierOrigen = false)
    {
        var sos = await (from caso in db.Casos.AsNoTracking()
                         from ciudadano in db.Users.AsNoTracking().Where(u => u.Id == caso.CiudadanoId).DefaultIfEmpty()
                         where caso.Id == casoId && (cualquierOrigen || caso.Origen != OrigenCaso.Reporte)
                         select new
                         {
                             caso.Id, caso.Estado, caso.RegistradoEn, caso.Origen,
                             Ciudadano = ciudadano == null ? AutorReporte.DeLaCentral : ciudadano.Nombres + " " + ciudadano.Apellidos,
                             PhoneNumber = ciudadano == null ? null : ciudadano.PhoneNumber,
                             caso.Latitud, caso.Longitud, caso.PrecisionMetros, caso.UbicacionObtenidaEn,
                         })
                        .SingleOrDefaultAsync(ct);
        if (sos is null)
            return null;

        // CA07: todos los intentos, del más reciente al más antiguo.
        var intentos = await (from intento in db.IntentosContacto.AsNoTracking()
                              join operador in db.Users.AsNoTracking() on intento.OperadorId equals operador.Id
                              where intento.CasoId == casoId
                              orderby intento.RegistradoEn descending, intento.Id descending
                              select new IntentoContactoVista(intento.Resultado, operador.Nombres + " " + operador.Apellidos,
                                  intento.RegistradoEn, intento.InformacionHecho, intento.ReferenciaUbicacion))
                             .ToListAsync(ct);

        var asignacion = await (from a in db.Asignaciones.AsNoTracking()
                                join sereno in db.Users.AsNoTracking() on a.SerenoId equals sereno.Id
                                where a.CasoId == casoId
                                // La vigente o, si el caso se cerró, la que finalizó con el cierre (HU24).
                                orderby a.Vigente descending, a.AsignadaEn descending
                                select new AsignacionVista(a.SerenoId, sereno.Nombres + " " + sereno.Apellidos, a.AsignadaEn, a.FinalizadaEn))
                               .FirstOrDefaultAsync(ct);

        var cierre = await (from c in db.CierresCaso.AsNoTracking()
                            join categoria in db.CategoriasIncidencia.AsNoTracking() on c.CategoriaId equals categoria.Id
                            join responsable in db.Users.AsNoTracking() on c.ResponsableId equals responsable.Id
                            where c.CasoId == casoId
                            select new CierreVista(categoria.Nombre, c.ResultadoPublico, c.NotasInternas,
                                responsable.Nombres + " " + responsable.Apellidos, c.CerradoEn))
                           .SingleOrDefaultAsync(ct);

        var clasificacion = await (from caso in db.Casos.AsNoTracking()
                                   join categoria in db.CategoriasIncidencia.AsNoTracking() on caso.CategoriaId equals (Guid?)categoria.Id
                                   join operador in db.Users.AsNoTracking() on caso.ClasificadoPor equals (Guid?)operador.Id
                                   where caso.Id == casoId
                                   select new ClasificacionVista(categoria.Nombre, operador.Nombres + " " + operador.Apellidos,
                                       caso.ClasificadoEn!.Value))
                                  .SingleOrDefaultAsync(ct);

        var avances = (await (from evento in db.EventosCaso.AsNoTracking()
                              join actor in db.Users.AsNoTracking() on evento.ActorId equals actor.Id
                              where evento.CasoId == casoId
                                  && (evento.Tipo == TipoEventoCaso.DesplazamientoConfirmado || evento.Tipo == TipoEventoCaso.AtencionIniciada)
                              orderby evento.OcurridoEn
                              select new { evento.Tipo, Responsable = actor.Nombres + " " + actor.Apellidos, evento.OcurridoEn })
                             .ToListAsync(ct))
            .Select(a => new AvanceVista(a.Tipo == TipoEventoCaso.DesplazamientoConfirmado ? "En camino" : "En atención", a.Responsable, a.OcurridoEn))
            .ToList();
        // El último inicio de atención del sereno: dónde estaba al iniciarla.
        if (await db.IniciosAtencion.AsNoTracking().Where(i => i.CasoId == casoId).OrderByDescending(i => i.IniciadaEn).FirstOrDefaultAsync(ct) is { } inicio
            && avances.FindLastIndex(a => a.Descripcion == "En atención") is var indice and >= 0)
            avances[indice] = avances[indice] with { Nota = TextoInicioAtencion.Describir(inicio), Alerta = inicio.FueraDeRango };

        return new DetalleSos(sos.Id, EstadosCaso.Texto(sos.Estado), sos.RegistradoEn, sos.Ciudadano, sos.PhoneNumber,
            sos.Latitud, sos.Longitud, sos.PrecisionMetros, sos.UbicacionObtenidaEn, intentos, asignacion, clasificacion, avances, cierre,
            sos.Origen == OrigenCaso.Sos ? "SOS" : $"Caso urgente · {OrigenesCaso.Texto(sos.Origen)}");
    }
}
