using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// HU27 — Consultar el historial de un caso (S3-T11). Solo lectura y solo para operadores (CA06, lo aplica la página).
// Reúne, en orden temporal, las actuaciones registradas por las demás historias de casos, cada una con su responsable y
// su marca de tiempo (CA01, CA03). No incluye posiciones ni sesiones de acompañamiento (CA05: son de HU39).
public sealed record HistorialCaso(
    Guid Id,
    string Origen,
    string Estado,
    DateTimeOffset RegistradoEn,
    ResultadoFinal? Resultado,
    IReadOnlyList<EntradaHistorial> Entradas,
    // HU10: fecha y hora aproximadas del hecho que indicó el ciudadano (solo reportes).
    DateTimeOffset? HechoOcurridoEn = null,
    // HU33: la evaluación del ciudadano, si la registró (CA02: sin evaluación no hay puntuación).
    EvaluacionCiudadano? Evaluacion = null);

// HU33-CA01: puntuación de 1 a 5 y comentario, si existe.
public sealed record EvaluacionCiudadano(int Puntuacion, string? Comentario, DateTimeOffset EvaluadaEn);

// CA02: un caso finalizado queda identificado con su resultado.
public sealed record ResultadoFinal(string Estado, string Descripcion, DateTimeOffset En);

// Interna = solo para el personal autorizado (CA04): por ejemplo, las notas internas del cierre.
public sealed record EntradaHistorial(DateTimeOffset En, string Actuacion, string Responsable, string? Detalle, bool Interna = false);

public sealed class ConsultarHistorialCaso(CadDbContext db)
{
    public async Task<HistorialCaso?> EjecutarAsync(Guid casoId, CancellationToken ct)
    {
        var caso = await db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == casoId, ct);
        if (caso is null)
            return null;

        var nombres = new Dictionary<Guid, string>();
        async Task<string> NombreAsync(Guid usuario)
        {
            if (!nombres.TryGetValue(usuario, out var nombre))
                nombres[usuario] = nombre = await db.Users.AsNoTracking().Where(u => u.Id == usuario)
                    // Personal: con su código (22/10/2026), que lo identifica sin ambigüedad en la auditoría.
                    .Select(u => u.CodigoPersonal == null ? u.Nombres + " " + u.Apellidos : u.Nombres + " " + u.Apellidos + " (" + u.CodigoPersonal + ")")
                    .SingleOrDefaultAsync(ct) ?? "Cuenta no disponible";
            return nombre;
        }

        var entradas = new List<EntradaHistorial>();
        DateTimeOffset? hechoOcurridoEn = null;
        if (caso.Origen == OrigenCaso.Reporte)
        {
            // HU10: el reporte con la categoría que eligió el ciudadano (CA07) y la ubicación del hecho (CA03).
            var detalle = await db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == casoId, ct);
            hechoOcurridoEn = detalle.OcurridoAproximadamenteEn;
            // Un reporte de la central (23/10/2026) lo registra un operador, con quien avisó si lo dio.
            var deLaCentral = detalle.RegistradoPorOperadorId is { } operadorId;
            entradas.Add(new(caso.RegistradoEn,
                $"Reporte registrado {(deLaCentral ? "por la central " : "")}como «{await CategoriaAsync(detalle.CategoriaOriginalId, ct)}»",
                await NombreAsync(detalle.RegistradoPorOperadorId ?? caso.CiudadanoId!.Value),
                Unir(TextoFuente(detalle.FuenteUbicacion), detalle.ReferenciaHecho is { } r ? $"Referencia: {r}" : null,
                    detalle.ContactoNombre is { } contacto ? $"Avisó: {contacto}{(detalle.ContactoTelefono is { } t ? $" ({t})" : "")}" : null)));
        }
        else if (caso.Origen == OrigenCaso.Acompanamiento)
        {
            // DP19: caso urgente creado por un operador desde un aviso del acompañamiento, con su justificación.
            var origen = await (from aviso in db.AvisosAcompanamiento.AsNoTracking()
                                where aviso.CasoId == casoId && aviso.CasoCreado
                                select new { aviso.VinculadoPor, aviso.JustificacionCaso, aviso.Regla }).SingleOrDefaultAsync(ct);
            entradas.Add(new(caso.RegistradoEn, "Caso urgente creado desde un aviso del acompañamiento",
                origen?.VinculadoPor is { } operador ? await NombreAsync(operador) : "Cuenta no disponible",
                Unir(origen is null ? null : $"Justificación: {origen.JustificacionCaso}",
                    caso.UbicacionDisponible ? "Ubicación: última posición conocida de la sesión" : "Sin posiciones en la sesión")));
        }
        else
            entradas.Add(new(caso.RegistradoEn, "SOS emitido", await NombreAsync(caso.CiudadanoId!.Value),
                caso.UbicacionDisponible ? "Con ubicación del dispositivo" : "Ubicación no disponible"));

        // HU12: intentos de contacto con su resultado.
        foreach (var intento in await db.IntentosContacto.AsNoTracking().Where(i => i.CasoId == casoId).ToListAsync(ct))
            entradas.Add(new(intento.RegistradoEn, $"Intento de contacto: {TextoResultado(intento.Resultado)}", await NombreAsync(intento.OperadorId),
                Unir(intento.InformacionHecho is { } h ? $"Hecho: {h}" : null, intento.ReferenciaUbicacion is { } r ? $"Referencia indicada por el ciudadano: {r}" : null)));

        // HU13: clasificación de un SOS. En un reporte, la categoría del ciudadano figura en su registro y los cambios en
        // las correcciones (HU14).
        if (caso.Origen != OrigenCaso.Reporte && caso.CategoriaId is { } categoria && caso.ClasificadoPor is { } clasificador && caso.ClasificadoEn is { } clasificadoEn)
            entradas.Add(new(clasificadoEn, $"Clasificado como «{await CategoriaAsync(categoria, ct)}»", await NombreAsync(clasificador), null));

        // HU14: inicio de la revisión y correcciones de categoría (anterior → nueva).
        foreach (var evento in await db.EventosCaso.AsNoTracking()
                     .Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.RevisionIniciada).ToListAsync(ct))
            entradas.Add(new(evento.OcurridoEn, "Revisión iniciada", await NombreAsync(evento.ActorId), null));
        foreach (var correccion in await db.CorreccionesCategoria.AsNoTracking().Where(c => c.CasoId == casoId).ToListAsync(ct))
            entradas.Add(new(correccion.CorregidaEn,
                $"Categoría corregida de «{await CategoriaAsync(correccion.CategoriaAnteriorId, ct)}» a «{await CategoriaAsync(correccion.CategoriaNuevaId, ct)}»",
                await NombreAsync(correccion.OperadorId), null));

        // HU15–HU17: decisión sobre el reporte, con su motivo (y destino y acción en una derivación). Derivado y descartado
        // son resultados finales identificables (HU16-CA04, HU17-CA03).
        ResultadoFinal? resultado = null;
        if (await db.DecisionesReporte.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == casoId, ct) is { } decision)
        {
            var responsable = await NombreAsync(decision.OperadorId);
            switch (decision.Tipo)
            {
                case TipoDecisionReporte.Admision:
                    entradas.Add(new(decision.DecididaEn, "Reporte admitido para atención", responsable, $"Motivo: {decision.Motivo}"));
                    break;
                case TipoDecisionReporte.Derivacion:
                    entradas.Add(new(decision.DecididaEn, $"Reporte derivado a {decision.Destino}", responsable,
                        $"Acción realizada: {decision.Accion} · Motivo: {decision.Motivo}"));
                    resultado = new ResultadoFinal(EstadosCaso.Texto(EstadoCaso.Derivada), $"Derivado a {decision.Destino}: {decision.Accion}", decision.DecididaEn);
                    break;
                default:
                    entradas.Add(new(decision.DecididaEn, "Reporte descartado", responsable, $"Motivo: {decision.Motivo}"));
                    resultado = new ResultadoFinal(EstadosCaso.Texto(EstadoCaso.Descartada), $"Motivo: {decision.Motivo}", decision.DecididaEn);
                    break;
            }
        }

        // HU09: cancelación por el titular, con su motivo; «Cancelada» es un resultado final identificable (HU27-CA02).
        if (await db.CancelacionesSos.AsNoTracking().SingleOrDefaultAsync(c => c.CasoId == casoId, ct) is { } cancelacion)
        {
            entradas.Add(new(cancelacion.CanceladaEn, "SOS cancelado por el ciudadano", await NombreAsync(cancelacion.CiudadanoId), $"Motivo: {cancelacion.Motivo}"));
            resultado = new ResultadoFinal(EstadosCaso.Texto(EstadoCaso.Cancelada), $"Cancelado por el ciudadano. Motivo: {cancelacion.Motivo}", cancelacion.CanceladaEn);
        }

        // Descarte por la central (23/10/2026): «Descartada» es un resultado final, con el motivo (solo personal autorizado).
        if (await db.DescartesCaso.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == casoId, ct) is { } descarte)
        {
            entradas.Add(new(descarte.DescartadoEn, "Caso descartado por la central", await NombreAsync(descarte.OperadorId),
                $"Estaba «{EstadosCaso.Texto(descarte.EstadoPrevio)}». Motivo: {descarte.Motivo}"));
            resultado = new ResultadoFinal(EstadosCaso.Texto(EstadoCaso.Descartada), $"Descartado por la central. Motivo: {descarte.Motivo}", descarte.DescartadoEn);
        }

        // HU20: asignaciones (y su fin, al cerrar el caso).
        foreach (var asignacion in await db.Asignaciones.AsNoTracking().Where(a => a.CasoId == casoId).ToListAsync(ct))
        {
            entradas.Add(new(asignacion.AsignadaEn, $"Asignado al sereno {await NombreAsync(asignacion.SerenoId)}", await NombreAsync(asignacion.OperadorId), null));
            // DP03: una asignación cancelada por desactivar la cuenta del sereno aparece con su propio evento.
            if (asignacion.FinalizadaEn is { } fin && !asignacion.Cancelada)
                entradas.Add(new(fin, "Asignación finalizada", await NombreAsync(asignacion.SerenoId), "El sereno queda libre para otro caso."));
        }

        // DP03 (16/10/2026): asignación cancelada al desactivar la cuenta del sereno; el responsable es el administrador.
        foreach (var evento in await db.EventosCaso.AsNoTracking()
                     .Where(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.AsignacionCancelada).ToListAsync(ct))
            entradas.Add(new(evento.OcurridoEn, "Asignación cancelada: la cuenta del sereno se desactivó", await NombreAsync(evento.ActorId),
                "El caso vuelve a «Pendiente» para asignarlo a otro sereno."));

        // HU22 y HU23: avances registrados como eventos. El inicio de atención del sereno lleva dónde estaba (rango de atención).
        var inicios = await db.IniciosAtencion.AsNoTracking().Where(i => i.CasoId == casoId).ToListAsync(ct);
        foreach (var evento in await db.EventosCaso.AsNoTracking()
                     .Where(e => e.CasoId == casoId && (e.Tipo == TipoEventoCaso.DesplazamientoConfirmado || e.Tipo == TipoEventoCaso.AtencionIniciada))
                     .ToListAsync(ct))
            entradas.Add(new(evento.OcurridoEn, evento.Tipo == TipoEventoCaso.DesplazamientoConfirmado ? "En camino" : "Inicio de atención",
                await NombreAsync(evento.ActorId),
                evento.Tipo == TipoEventoCaso.AtencionIniciada
                    && inicios.FirstOrDefault(i => (i.RegistradoPorOperadorId ?? i.SerenoId) == evento.ActorId && i.IniciadaEn == evento.OcurridoEn) is { } inicio
                    ? TextoInicioAtencion.Describir(inicio)
                    : null));

        // HU24: cierre, con el resultado público y, aparte, las notas internas (CA04).
        if (await db.CierresCaso.AsNoTracking().SingleOrDefaultAsync(c => c.CasoId == casoId, ct) is { } cierre)
        {
            var tipificacion = await CategoriaAsync(cierre.CategoriaId, ct);
            var responsable = await NombreAsync(cierre.ResponsableId);
            entradas.Add(new(cierre.CerradoEn, $"Caso cerrado · tipificación «{tipificacion}»", responsable, $"Resultado público: {cierre.ResultadoPublico}"));
            if (cierre.NotasInternas is { } notas)
                entradas.Add(new(cierre.CerradoEn, "Notas internas del cierre", responsable, notas, Interna: true));
            resultado = new ResultadoFinal(EstadosCaso.Texto(EstadoCaso.Cerrada), $"{tipificacion}: {cierre.ResultadoPublico}", cierre.CerradoEn);
        }

        return new HistorialCaso(caso.Id, OrigenesCaso.Texto(caso.Origen), EstadosCaso.Texto(caso.Estado),
            caso.RegistradoEn, resultado,
            // Orden temporal; a igual momento, el orden en que se añadieron (registro, cierre antes que sus notas).
            entradas.Select((e, i) => (e, i)).OrderBy(x => x.e.En).ThenBy(x => x.i).Select(x => x.e).ToList(), hechoOcurridoEn,
            // HU33: solo lectura, en la misma página restringida a operadores (CA03).
            await db.EvaluacionesCaso.AsNoTracking().Where(e => e.CasoId == casoId)
                .Select(e => new EvaluacionCiudadano(e.Puntuacion, e.Comentario, e.EvaluadaEn)).SingleOrDefaultAsync(ct));
    }

    private async Task<string> CategoriaAsync(Guid id, CancellationToken ct) =>
        await db.CategoriasIncidencia.AsNoTracking().Where(c => c.Id == id).Select(c => c.Nombre).SingleOrDefaultAsync(ct) ?? "Categoría no disponible";

    private static string TextoFuente(string fuente) => fuente switch
    {
        Cad.Contratos.Reportes.FuentesUbicacionHecho.Mapa => "Ubicación del hecho: punto en el mapa",
        Cad.Contratos.Reportes.FuentesUbicacionHecho.PosicionActual => "Ubicación del hecho: posición actual elegida por el ciudadano",
        _ => "Ubicación del hecho: referencia textual",
    };

    private static string TextoResultado(ResultadoContacto resultado) => resultado switch
    {
        ResultadoContacto.Contactado => "Contactado",
        ResultadoContacto.SinRespuesta => "Sin respuesta",
        ResultadoContacto.NoFuePosibleComunicar => "No fue posible comunicar",
        _ => resultado.ToString(),
    };

    private static string? Unir(params string?[] partes)
    {
        var presentes = partes.Where(p => p is not null).ToList();
        return presentes.Count == 0 ? null : string.Join(" · ", presentes);
    }
}
