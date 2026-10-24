using System.Globalization;
using Cad.Calles;
using Cad.Contratos.Serenos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public enum Avance
{
    // HU22: el sereno asignado confirma que se dirige al caso. Solo el sereno (CA04).
    ConfirmarDesplazamiento,
    // HU23: inicio de la atención. El sereno asignado o un operador (CA01, CA02).
    IniciarAtencion,
}

public enum ActorAvance { Sereno, Operador }

public abstract record ResultadoAvance
{
    public sealed record Registrado(EstadoCaso Estado) : ResultadoAvance;
    // La misma solicitud reenviada por el mismo responsable: no se duplica nada (HU22-CA05, HU23-CA06).
    public sealed record YaRegistrado(EstadoCaso Estado) : ResultadoAvance;
    public sealed record Rechazado(string Motivo) : ResultadoAvance;
    // Rango de atención (24/10/2026): el sereno está lejos del punto del caso (o sin posición reciente) y no indicó por qué.
    public sealed record FueraDeRango(double? DistanciaMetros, double RadioMetros) : ResultadoAvance
    {
        public string Mensaje => DistanciaMetros is { } d
            ? $"Estás a {RangoAtencion.TextoDistancia(d)} del punto del caso (el rango es de {RadioMetros.ToString("0", CultureInfo.InvariantCulture)} m). Indica por qué inicias la atención aquí."
            : "No hay una posición reciente de tu teléfono para comprobar que estás en el lugar. Indica por qué inicias la atención.";
    }
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoAvance;
    // El caso no existe o, para un sereno, no está asignado a él (HU22-CA03, HU23-CA05): no se distingue.
    public sealed record NoEncontrado : ResultadoAvance;
}

// HU22 y HU23 (S2-T10, S2-T11). El responsable, la fecha y la hora quedan en el historial del caso (evento de
// solo inserción). Un solo cambio aunque lleguen solicitudes a la vez: token xmin del caso y nueva evaluación.
public sealed class AvanzarCaso(CadDbContext db, TimeProvider reloj, INotificador notificador, ILogger<AvanzarCaso> registro)
{
    public const string MotivoNoAsignada = "El caso no está «Asignada»: no se puede confirmar el desplazamiento.";
    public const string MotivoNoEnCamino = "El caso no está «En camino»: no se puede registrar el inicio de atención.";
    public const string MotivoSoloSereno = "Solo el sereno asignado puede confirmar el desplazamiento.";
    public const string MotivoCambioConcurrente = "El caso cambió mientras se registraba. Revisa su estado e inténtalo de nuevo.";

    private const int Intentos = 3;

    public static string MotivoYaRegistrado(Avance avance) => avance == Avance.ConfirmarDesplazamiento
        ? "El desplazamiento ya fue confirmado."
        : "El inicio de atención ya fue registrado por otro responsable.";

    // inicio: posición y motivo que envía el sereno al iniciar la atención (rango de atención); el operador no lo usa.
    public async Task<ResultadoAvance> EjecutarAsync(Guid casoId, Avance avance, Guid actorId, ActorAvance actor, CancellationToken ct,
        SolicitudInicioAtencion? inicio = null)
    {
        // HU22-CA04: RF13 define la confirmación solo para el sereno.
        if (avance == Avance.ConfirmarDesplazamiento && actor != ActorAvance.Sereno)
            return new ResultadoAvance.Rechazado(MotivoSoloSereno);

        var tipoEvento = avance == Avance.ConfirmarDesplazamiento ? TipoEventoCaso.DesplazamientoConfirmado : TipoEventoCaso.AtencionIniciada;

        for (var intento = 1; ; intento++)
        {
            var caso = await db.Casos.SingleOrDefaultAsync(c => c.Id == casoId, ct);
            var asignacion = await db.Asignaciones.AsNoTracking().SingleOrDefaultAsync(a => a.CasoId == casoId && a.Vigente, ct);
            if (caso is null || (actor == ActorAvance.Sereno && asignacion?.SerenoId != actorId))
                return new ResultadoAvance.NoEncontrado();

            // Primero lo ya registrado: un reenvío del mismo responsable no es un error.
            var previo = await db.EventosCaso.AsNoTracking()
                .Where(e => e.CasoId == casoId && e.Tipo == tipoEvento)
                .Select(e => (Guid?)e.ActorId)
                .FirstOrDefaultAsync(ct);
            if (previo is { } responsable)
                return responsable == actorId
                    ? new ResultadoAvance.YaRegistrado(caso.Estado)
                    : new ResultadoAvance.Rechazado(MotivoYaRegistrado(avance));

            var origen = avance == Avance.ConfirmarDesplazamiento ? EstadoCaso.Asignada : EstadoCaso.EnCamino;
            if (caso.Estado != origen)
                return new ResultadoAvance.Rechazado(avance == Avance.ConfirmarDesplazamiento ? MotivoNoAsignada : MotivoNoEnCamino);

            var ahora = reloj.GetUtcNow();
            // Rango de atención: solo cuando inicia el propio sereno (el operador registra lo que el sereno le informa).
            InicioAtencion? registroInicio = null;
            if (avance == Avance.IniciarAtencion && actor == ActorAvance.Sereno)
            {
                var (rechazo, evaluado) = await EvaluarRangoAsync(caso, actorId, inicio ?? new(), ahora, ct);
                if (rechazo is not null)
                    return rechazo;
                registroInicio = evaluado;
            }

            if (avance == Avance.ConfirmarDesplazamiento)
                caso.ConfirmarDesplazamiento();
            else
                caso.IniciarAtencion();
            db.EventosCaso.Add(EventoCaso.Registrar(caso, tipoEvento, actorId, VisibilidadEvento.Interna, ahora));
            if (registroInicio is not null)
                db.IniciosAtencion.Add(registroInicio);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (intento == Intentos)
                    return new ResultadoAvance.Rechazado(MotivoCambioConcurrente);
                continue;
            }

            registro.LogInformation("Caso {CasoId}: {Avance} registrado.", casoId, avance);
            await AvisarAsync(casoId, asignacion?.SerenoId, actor);
            return new ResultadoAvance.Registrado(caso.Estado);
        }
    }

    // Rango de atención (24/10/2026). El punto del caso es la ubicación del teléfono (SOS) o el lugar del hecho (reporte); la
    // posición del sereno, la que envía la app si es reciente o, si no, la última recibida (como mucho de hace 2 minutos).
    private async Task<(ResultadoAvance? Rechazo, InicioAtencion? Inicio)> EvaluarRangoAsync(Caso caso, Guid serenoId,
        SolicitudInicioAtencion solicitud, DateTimeOffset ahora, CancellationToken ct)
    {
        (double Latitud, double Longitud, double? Precision)? punto = caso.Latitud is { } lat && caso.Longitud is { } lon
            ? (lat, lon, caso.PrecisionMetros)
            : null;
        if (punto is null && caso.Origen == OrigenCaso.Reporte
            && await db.DetallesReporte.AsNoTracking().Where(d => d.CasoId == caso.Id && d.LatitudHecho != null && d.LongitudHecho != null)
                .Select(d => new { Latitud = d.LatitudHecho!.Value, Longitud = d.LongitudHecho!.Value, d.PrecisionHechoMetros })
                .SingleOrDefaultAsync(ct) is { } hecho)
            punto = (hecho.Latitud, hecho.Longitud, hecho.PrecisionHechoMetros);

        var posicion = PosicionReciente(solicitud.Posicion, ahora) ?? await UltimaPosicionAsync(serenoId, ahora, ct);

        double? distancia = null, radio = null;
        bool? dentro = null; // sin punto del caso no hay con qué comparar
        if (punto is { } p)
        {
            radio = RangoAtencion.Radio(p.Precision, posicion?.Precision);
            if (posicion is { } s)
            {
                distancia = Geo.DistanciaMetros(s.Latitud, s.Longitud, p.Latitud, p.Longitud);
                dentro = distancia <= radio;
            }
            else
                dentro = false;
        }

        string? motivo = null, detalle = null;
        if (dentro == false)
        {
            if (solicitud.Motivo is null)
                return (new ResultadoAvance.FueraDeRango(distancia, radio!.Value), null);
            motivo = solicitud.Motivo;
            detalle = string.IsNullOrWhiteSpace(solicitud.Detalle) ? null : solicitud.Detalle.Trim();
            if (!MotivosInicioFueraDeRango.Todos.Contains(motivo))
                return (new ResultadoAvance.Invalido(new() { ["motivo"] = ["Elige uno de los motivos de la lista."] }), null);
            if (motivo == MotivosInicioFueraDeRango.Otro && detalle is null)
                return (new ResultadoAvance.Invalido(new() { ["detalle"] = ["Cuenta brevemente el motivo."] }), null);
            if (detalle is { Length: > MotivosInicioFueraDeRango.MaximoDetalle })
                return (new ResultadoAvance.Invalido(new() { ["detalle"] = [$"El detalle admite hasta {MotivosInicioFueraDeRango.MaximoDetalle} caracteres."] }), null);
        }
        return (null, InicioAtencion.Registrar(caso, serenoId, posicion, distancia, radio, dentro, motivo, detalle, ahora));
    }

    // Diferencia admitida entre el reloj del teléfono y el del servidor (técnica, no una regla de negocio).
    private static readonly TimeSpan MargenReloj = TimeSpan.FromMinutes(1);

    // La posición enviada con la solicitud, si es válida y reciente (con un margen para el reloj del teléfono).
    private static (double Latitud, double Longitud, double Precision)? PosicionReciente(PosicionSerenoEnviada? enviada, DateTimeOffset ahora) =>
        enviada is { } e && double.IsFinite(e.Latitud) && e.Latitud is >= -90 and <= 90 && double.IsFinite(e.Longitud) && e.Longitud is >= -180 and <= 180
            && double.IsFinite(e.PrecisionMetros) && e.PrecisionMetros > 0
            && e.ObtenidaEn >= ahora - RangoAtencion.AntiguedadMaxima && e.ObtenidaEn <= ahora + MargenReloj
            ? (e.Latitud, e.Longitud, e.PrecisionMetros)
            : null;

    private async Task<(double Latitud, double Longitud, double Precision)?> UltimaPosicionAsync(Guid serenoId, DateTimeOffset ahora, CancellationToken ct)
    {
        // El mismo límite futuro que la posición enviada: una hora adelantada en el teléfono no hace «reciente» una posición vieja.
        var (desde, hasta) = (ahora - RangoAtencion.AntiguedadMaxima, ahora + MargenReloj);
        var ultima = await db.PosicionesSereno.AsNoTracking()
            .Where(p => p.SerenoId == serenoId && p.ObtenidaEn >= desde && p.ObtenidaEn <= hasta)
            .OrderByDescending(p => p.ObtenidaEn)
            .Select(p => new { p.Latitud, p.Longitud, p.PrecisionMetros })
            .FirstOrDefaultAsync(ct);
        return ultima is null ? null : (ultima.Latitud, ultima.Longitud, ultima.PrecisionMetros);
    }

    // Después del COMMIT: la central refleja el estado (HU23-CA03) y, si lo registró un operador, la app del sereno
    // vuelve a consultar su despacho.
    private async Task AvisarAsync(Guid casoId, Guid? serenoId, ActorAvance actor)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            if (serenoId is { } sereno)
            {
                await notificador.SerenoActualizadoAsync(sereno, CancellationToken.None);
                if (actor == ActorAvance.Operador)
                    await notificador.DespachoActualizadoAsync(sereno, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar del cambio de estado del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}
