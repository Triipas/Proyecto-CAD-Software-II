namespace Cad.Server.Modulos.Acompanamiento.Dominio;

// HU34–HU37 — sesión de acompañamiento temporal (Plan de Arquitectura, 5.4 y 5.5). Separada de los casos: no es un SOS ni
// un reporte (HU34-CA05) y tiene su propio historial (HU39).
public enum EstadoSesion { Activa, Finalizada, Vencida }

public sealed class SesionAcompanamiento
{
    // HU34-CA01 y HU37: 30 minutos desde el inicio, sin renovación.
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(30);

    public Guid Id { get; private set; }
    public Guid CiudadanoId { get; private set; }

    // Idempotencia del inicio: único por ciudadano (RNF08).
    public Guid IdSolicitud { get; private set; }

    public EstadoSesion Estado { get; private set; }
    public DateTimeOffset IniciadaEn { get; private set; }
    public DateTimeOffset VenceEn { get; private set; }

    // Momento del cierre: el de la finalización (HU36) o el del vencimiento (HU37), aunque el servidor lo registre después.
    public DateTimeOffset? CerradaEn { get; private set; }

    // DP20 (16/10/2026): último envío recibido del dispositivo, aunque repita una posición ya registrada. Sin envíos durante
    // más del umbral (DP12, 60 s), la central muestra la sesión «Sin conexión».
    public DateTimeOffset? UltimoContactoEn { get; private set; }

    // HU39-B (DP22): cuándo se eliminaron sus posiciones al cumplirse el plazo de conservación (null si se conservan).
    public DateTimeOffset? PosicionesEliminadasEn { get; private set; }

    // Token de concurrencia optimista (columna del sistema xmin), como en los casos.
    public uint Version { get; private set; }

    private SesionAcompanamiento() { } // EF Core

    public static SesionAcompanamiento Iniciar(Guid ciudadanoId, Guid idSolicitud, DateTimeOffset ahora)
    {
        var inicio = AMicrosegundos(ahora);
        return new()
        {
            Id = Guid.CreateVersion7(),
            CiudadanoId = ciudadanoId,
            IdSolicitud = idSolicitud,
            Estado = EstadoSesion.Activa,
            IniciadaEn = inicio,
            VenceEn = inicio + Duracion,
            UltimoContactoEn = inicio,
        };
    }

    // Regla de cierre compartida (S4-T06, S4-T07): una sesión solo admite posiciones mientras está activa y no ha vencido,
    // según el reloj del servidor. Lo que llega después no se incorpora (HU36-CA04, HU37-CA04).
    public bool AdmitePosiciones(DateTimeOffset ahora) => Estado == EstadoSesion.Activa && ahora < VenceEn;

    // Activa pero ya pasada su hora de vencimiento: el temporizador aún no la cerró.
    public bool VencidaSinCerrar(DateTimeOffset ahora) => Estado == EstadoSesion.Activa && ahora >= VenceEn;

    // HU36-CA02: el ciudadano la finaliza. Si ya había vencido, el cierre es el vencimiento (HU37), no la finalización.
    // Devuelve false si ya estaba cerrada (HU36-CA07: un reenvío no cambia nada).
    public bool Finalizar(DateTimeOffset ahora)
    {
        if (Estado != EstadoSesion.Activa)
            return false;
        if (VencidaSinCerrar(ahora))
            return Vencer(ahora);
        Estado = EstadoSesion.Finalizada;
        CerradaEn = AMicrosegundos(ahora);
        return true;
    }

    // HU37-CA01: el servidor la cierra al cumplirse los 30 minutos; la hora de cierre es la de vencimiento.
    public bool Vencer(DateTimeOffset ahora)
    {
        if (!VencidaSinCerrar(ahora))
            return false;
        Estado = EstadoSesion.Vencida;
        CerradaEn = VenceEn;
        return true;
    }

    // PostgreSQL guarda microsegundos: así la hora devuelta coincide con la que se leerá después.
    private static DateTimeOffset AMicrosegundos(DateTimeOffset momento) => momento.AddTicks(-(momento.Ticks % 10));
}

// HU35 — posición recibida durante una sesión activa. Solo inserción.
public sealed class PosicionAcompanamiento
{
    // Margen técnico entre el reloj del teléfono y el del servidor (el mismo de DP08): una hora de obtención más
    // adelantada que esto no es válida.
    public static readonly TimeSpan MargenReloj = TimeSpan.FromMinutes(5);

    public Guid Id { get; private set; }
    public Guid SesionId { get; private set; }
    public double Latitud { get; private set; }
    public double Longitud { get; private set; }
    public double PrecisionMetros { get; private set; }

    // Hora de obtención en el dispositivo (CA01, CA03: una posición antigua conserva la suya) y de recepción en el servidor.
    public DateTimeOffset ObtenidaEn { get; private set; }
    public DateTimeOffset RecibidaEn { get; private set; }

    private PosicionAcompanamiento() { } // EF Core

    // null si no es válida (coordenadas fuera de rango, precisión no positiva, sin hora o con una hora futura).
    public static PosicionAcompanamiento? Crear(Guid sesionId, double latitud, double longitud, double precisionMetros,
        DateTimeOffset obtenidaEn, DateTimeOffset ahora)
    {
        var valida = double.IsFinite(latitud) && latitud is >= -90 and <= 90
            && double.IsFinite(longitud) && longitud is >= -180 and <= 180
            && double.IsFinite(precisionMetros) && precisionMetros > 0
            && obtenidaEn != default && obtenidaEn <= ahora + MargenReloj;
        if (!valida)
            return null;

        return new PosicionAcompanamiento
        {
            Id = Guid.CreateVersion7(),
            SesionId = sesionId,
            Latitud = latitud,
            Longitud = longitud,
            PrecisionMetros = precisionMetros,
            ObtenidaEn = obtenidaEn.ToUniversalTime(),
            RecibidaEn = ahora,
        };
    }
}
