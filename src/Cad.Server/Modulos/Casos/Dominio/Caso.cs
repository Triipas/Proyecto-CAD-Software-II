namespace Cad.Server.Modulos.Casos.Dominio;

// Caso común a SOS y reportes (Plan de Arquitectura, 9.1). En el Sprint 1 solo existe el origen SOS.
public enum OrigenCaso { Sos }

// Estados del caso (Plan de Arquitectura, máquina de estados). El Sprint 2 añade «Asignada» (HU20); los demás
// estados del ciclo (En camino, En atención, Cerrada) llegan con HU22–HU24.
public enum EstadoCaso { Pendiente, Asignada }

public sealed class Caso
{
    public Guid Id { get; private set; }
    public OrigenCaso Origen { get; private set; }
    public EstadoCaso Estado { get; private set; }
    public Guid CiudadanoId { get; private set; }

    // Idempotencia: único por ciudadano (HU07-CA07, DP14).
    public Guid IdSolicitud { get; private set; }

    // Momento en que el servidor aceptó y registró el SOS (HU07-CA02; base de la medición de RNF02).
    public DateTimeOffset RegistradoEn { get; private set; }

    // Ubicación actual al emitir (HU08). Todos los campos nulos = «Ubicación no disponible».
    public double? Latitud { get; private set; }
    public double? Longitud { get; private set; }
    public double? PrecisionMetros { get; private set; }
    public DateTimeOffset? UbicacionObtenidaEn { get; private set; }

    public bool UbicacionDisponible => Latitud is not null;

    // Token de concurrencia optimista (columna del sistema xmin de PostgreSQL): si dos operadores deciden a la
    // vez sobre el mismo caso, la segunda transacción no encuentra la versión leída y se rechaza (HU20-CA04).
    public uint Version { get; private set; }

    // HU20-CA01/CA02: solo un caso «Pendiente» puede asignarse.
    public bool PuedeAsignarse => Estado == EstadoCaso.Pendiente;

    public void Asignar()
    {
        if (!PuedeAsignarse)
            throw new InvalidOperationException("Solo un caso pendiente puede asignarse.");
        Estado = EstadoCaso.Asignada;
    }

    // HU13 — clasificación posterior al registro. Sin categoría = «Tipo por determinar» (CA03). La emisión,
    // la presentación y la asignación no la requieren (CA02).
    public Guid? CategoriaId { get; private set; }
    public Guid? ClasificadoPor { get; private set; }
    public DateTimeOffset? ClasificadoEn { get; private set; }

    public bool Clasificado => CategoriaId is not null;

    // CA01: un SOS sin tipo se clasifica con responsable, fecha y hora. Reclasificar no está definido en HU13:
    // se rechaza hasta que el PO lo decida (no se añade una regla que la HU no tiene).
    public void Clasificar(Guid categoriaId, Guid operadorId, DateTimeOffset ahora)
    {
        if (Clasificado)
            throw new InvalidOperationException("El caso ya está clasificado.");
        CategoriaId = categoriaId;
        ClasificadoPor = operadorId;
        ClasificadoEn = ahora;
    }

    private Caso() { } // EF Core

    public static Caso EmitirSos(Guid ciudadanoId, Guid idSolicitud, Posicion? ubicacionActual, DateTimeOffset ahora)
    {
        var caso = new Caso
        {
            Id = Guid.CreateVersion7(),
            Origen = OrigenCaso.Sos,
            Estado = EstadoCaso.Pendiente,
            CiudadanoId = ciudadanoId,
            IdSolicitud = idSolicitud,
            RegistradoEn = ahora,
        };

        if (ubicacionActual is { } p)
        {
            caso.Latitud = p.Latitud;
            caso.Longitud = p.Longitud;
            caso.PrecisionMetros = p.PrecisionMetros;
            caso.UbicacionObtenidaEn = p.ObtenidaEn;
        }

        return caso;
    }
}

// Posición válida y actual. Solo se puede construir con Posicion.ComoActual.
public sealed record Posicion
{
    public double Latitud { get; }
    public double Longitud { get; }
    public double PrecisionMetros { get; }
    public DateTimeOffset ObtenidaEn { get; }

    private Posicion(double latitud, double longitud, double precisionMetros, DateTimeOffset obtenidaEn) =>
        (Latitud, Longitud, PrecisionMetros, ObtenidaEn) = (latitud, longitud, precisionMetros, obtenidaEn);

    // DP11 (delegada, 12/10/2026): solo cuenta como actual una posición obtenida en el momento de la
    // activación o después (HU08-CA03: una posición anterior no se presenta como actual). Sin umbral de
    // precisión: se guarda la que reporta el dispositivo. Una posición inválida o anterior se descarta,
    // y el SOS se registra igualmente como «Ubicación no disponible» (HU07-CA06).
    public static Posicion? ComoActual(double latitud, double longitud, double precisionMetros,
        DateTimeOffset obtenidaEn, DateTimeOffset activadoEn)
    {
        var valida = double.IsFinite(latitud) && latitud is >= -90 and <= 90
            && double.IsFinite(longitud) && longitud is >= -180 and <= 180
            && double.IsFinite(precisionMetros) && precisionMetros > 0;
        var actual = activadoEn != default && obtenidaEn >= activadoEn;

        // Se guarda en UTC: es el mismo instante, y PostgreSQL (timestamptz) solo admite UTC desde Npgsql.
        return valida && actual ? new Posicion(latitud, longitud, precisionMetros, obtenidaEn.ToUniversalTime()) : null;
    }
}
