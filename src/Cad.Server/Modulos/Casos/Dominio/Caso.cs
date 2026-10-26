namespace Cad.Server.Modulos.Casos.Dominio;

// Caso común a SOS y reportes (Plan de Arquitectura, 9.1). El Sprint 3 añade el origen «Reporte» (HU10). El Sprint 5 añade
// «Acompañamiento»: el caso urgente que crea un operador desde un aviso del acompañamiento (HU43-CA05, DP19).
public enum OrigenCaso { Sos, Reporte, Acompanamiento }

public static class OrigenesCaso
{
    public static string Texto(OrigenCaso origen) => origen switch
    {
        OrigenCaso.Sos => "SOS",
        OrigenCaso.Reporte => "Reporte",
        OrigenCaso.Acompanamiento => "Acompañamiento",
        _ => origen.ToString(),
    };

    // DP19: un caso urgente creado desde un aviso se atiende como un SOS (verificación, clasificación, asignación con HU20).
    // En las consultas se escribe «Origen != OrigenCaso.Reporte» para que EF Core lo traduzca.
    public static bool SeAtiendeComoSos(OrigenCaso origen) => origen != OrigenCaso.Reporte;
}

// Estados del caso (Plan de Arquitectura, máquina de estados). El Sprint 2 añade «Asignada» (HU20), «En camino»
// (HU22), «En atención» (HU23) y «Cerrada» (HU24). El Sprint 3 añade «Recibido» (HU10), el estado inicial de un reporte,
// «En revisión» (HU14), «Derivada» (HU16) y «Descartada» (HU17). Un reporte admitido pasa a «Pendiente» (HU15). El Sprint 5
// añade «Cancelada» (HU09), un estado final: no es asignable (HU20).
public enum EstadoCaso { Pendiente, Asignada, EnCamino, EnAtencion, Cerrada, Recibido, EnRevision, Derivada, Descartada, Cancelada }

public static class EstadosCaso
{
    // Texto que ven el panel y la app.
    public static string Texto(EstadoCaso estado) => estado switch
    {
        EstadoCaso.Pendiente => "Pendiente",
        EstadoCaso.Asignada => "Asignada",
        EstadoCaso.EnCamino => "En camino",
        EstadoCaso.EnAtencion => "En atención",
        EstadoCaso.Cerrada => "Cerrada",
        EstadoCaso.Recibido => "Recibido",
        EstadoCaso.EnRevision => "En revisión",
        EstadoCaso.Derivada => "Derivada",
        EstadoCaso.Descartada => "Descartada",
        EstadoCaso.Cancelada => "Cancelada",
        _ => estado.ToString(),
    };

    // Lo que ve el panel (24/10/2026): un reporte «Pendiente» ya fue admitido y espera sereno, así que se muestra «Admitido»,
    // como en sus pasos de progreso y en la app del ciudadano. El estado del dominio y los textos de la API no cambian.
    public const string ReporteAdmitido = "Admitido";
    public static string Texto(OrigenCaso origen, EstadoCaso estado) =>
        origen == OrigenCaso.Reporte && estado == EstadoCaso.Pendiente ? ReporteAdmitido : Texto(estado);
}

public sealed class Caso
{
    public Guid Id { get; private set; }
    public OrigenCaso Origen { get; private set; }
    public EstadoCaso Estado { get; private set; }
    // Nulo en un reporte que registró la central (23/10/2026), p. ej. por una llamada: no pertenece a ningún ciudadano de la app.
    public Guid? CiudadanoId { get; private set; }

    // Idempotencia: único por ciudadano (HU07-CA07, DP14); en un reporte de la central, único entre los de la central.
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

    // HU22-CA01/CA02: solo un caso «Asignada» pasa a «En camino».
    public void ConfirmarDesplazamiento()
    {
        if (Estado != EstadoCaso.Asignada)
            throw new InvalidOperationException("Solo un caso asignado puede pasar a «En camino».");
        Estado = EstadoCaso.EnCamino;
    }

    // HU23-CA01/CA04: solo un caso «En camino» pasa a «En atención».
    public void IniciarAtencion()
    {
        if (Estado != EstadoCaso.EnCamino)
            throw new InvalidOperationException("Solo un caso en camino puede pasar a «En atención».");
        Estado = EstadoCaso.EnAtencion;
    }

    // DP03 (16/10/2026): si se desactiva la cuenta del sereno asignado, la asignación se cancela y el caso vuelve a
    // «Pendiente» para asignarlo a otro sereno (HU20). Solo desde un estado con asignación vigente y sin cerrar.
    public void LiberarPorCancelacion()
    {
        if (Estado is not (EstadoCaso.Asignada or EstadoCaso.EnCamino or EstadoCaso.EnAtencion))
            throw new InvalidOperationException("Solo un caso asignado y sin cerrar vuelve a «Pendiente».");
        Estado = EstadoCaso.Pendiente;
    }

    // HU09-CA01/CA03: el titular solo cancela un SOS «Pendiente»; asignado o en un estado posterior, ya no.
    // HU09: un SOS se cancela mientras está pendiente. Decisión del PO del 26/10/2026: un reporte se retira mientras la central
    // no lo haya decidido («Recibido» o «En revisión»); termina igual, como «Cancelada» con el motivo de su titular.
    public bool PuedeCancelarse => Origen == OrigenCaso.Sos ? Estado == EstadoCaso.Pendiente
        : Origen == OrigenCaso.Reporte && Estado is EstadoCaso.Recibido or EstadoCaso.EnRevision;

    public void Cancelar()
    {
        if (!PuedeCancelarse)
            throw new InvalidOperationException("Solo un SOS pendiente o un reporte sin decidir puede cancelarse.");
        Estado = EstadoCaso.Cancelada;
    }

    // Descarte por la central (23/10/2026): un caso falso o erróneo termina como «Descartada» desde cualquier estado abierto de
    // la atención. Un reporte recibido o en revisión se descarta con su decisión (HU17), no por aquí.
    public bool PuedeDescartarse => Estado is EstadoCaso.Pendiente or EstadoCaso.Asignada or EstadoCaso.EnCamino or EstadoCaso.EnAtencion;

    public void Descartar()
    {
        if (!PuedeDescartarse)
            throw new InvalidOperationException("Solo un caso pendiente o en atención puede descartarse.");
        Estado = EstadoCaso.Descartada;
    }

    // Reasignación por la central (24/10/2026): mientras el sereno no está con el ciudadano («Asignada» o «En camino»); en
    // atención, no. El caso vuelve a «Asignada» con el sereno nuevo, que debe confirmar que va en camino.
    public bool PuedeReasignarse => Estado is EstadoCaso.Asignada or EstadoCaso.EnCamino;

    public void Reasignar()
    {
        if (!PuedeReasignarse)
            throw new InvalidOperationException("Solo un caso asignado o en camino puede reasignarse.");
        Estado = EstadoCaso.Asignada;
    }

    // HU24-CA01/CA03: solo un caso «En atención» se cierra.
    public void Cerrar()
    {
        if (Estado != EstadoCaso.EnAtencion)
            throw new InvalidOperationException("Solo un caso en atención puede cerrarse.");
        Estado = EstadoCaso.Cerrada;
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

    // HU14-CA01/CA02: solo un reporte «Recibido» pasa a «En revisión».
    public void IniciarRevision()
    {
        if (Origen != OrigenCaso.Reporte || Estado != EstadoCaso.Recibido)
            throw new InvalidOperationException("Solo un reporte recibido puede pasar a «En revisión».");
        Estado = EstadoCaso.EnRevision;
    }

    // HU14-CA03/CA04: la categoría vigente se corrige solo durante la revisión, con su responsable y momento.
    // La categoría original del ciudadano queda en el detalle del reporte (HU10-CA07).
    public void CorregirCategoria(Guid categoriaId, Guid operadorId, DateTimeOffset ahora)
    {
        if (Origen != OrigenCaso.Reporte || Estado != EstadoCaso.EnRevision)
            throw new InvalidOperationException("Solo se corrige la categoría de un reporte en revisión.");
        CategoriaId = categoriaId;
        ClasificadoPor = operadorId;
        ClasificadoEn = ahora;
    }

    // HU15, HU16 y HU17: solo un reporte «En revisión» se admite (pasa a «Pendiente», asignable como un SOS: HU20-CA07),
    // se deriva o se descarta. Derivado y descartado son estados finales: no son asignables (HU17-CA04).
    public void Decidir(TipoDecisionReporte decision)
    {
        if (Origen != OrigenCaso.Reporte || Estado != EstadoCaso.EnRevision)
            throw new InvalidOperationException("Solo se decide sobre un reporte en revisión.");
        Estado = decision switch
        {
            TipoDecisionReporte.Admision => EstadoCaso.Pendiente,
            TipoDecisionReporte.Derivacion => EstadoCaso.Derivada,
            TipoDecisionReporte.Descarte => EstadoCaso.Descartada,
            _ => throw new ArgumentOutOfRangeException(nameof(decision)),
        };
    }

    private Caso() { } // EF Core

    // HU10-CA01: un reporte nace «Recibido». La categoría que elige el ciudadano es la del caso desde el inicio (el
    // ciudadano queda como responsable de esa primera clasificación); HU14 podrá corregirla conservando la original.
    // Reporte que registra la central (23/10/2026): sin ciudadano y ya admitido («Pendiente»), porque lo crea el propio operador.
    // La categoría la elige él y queda como su clasificación.
    public static Caso RegistrarReporteDeLaCentral(Guid operadorId, Guid idSolicitud, Guid categoriaId, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        Origen = OrigenCaso.Reporte,
        Estado = EstadoCaso.Pendiente,
        IdSolicitud = idSolicitud,
        RegistradoEn = ahora,
        CategoriaId = categoriaId,
        ClasificadoPor = operadorId,
        ClasificadoEn = ahora,
    };

    public static Caso RegistrarReporte(Guid ciudadanoId, Guid idSolicitud, Guid categoriaId, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        Origen = OrigenCaso.Reporte,
        Estado = EstadoCaso.Recibido,
        CiudadanoId = ciudadanoId,
        IdSolicitud = idSolicitud,
        RegistradoEn = ahora,
        CategoriaId = categoriaId,
        ClasificadoPor = ciudadanoId,
        ClasificadoEn = ahora,
    };

    // HU43-CA05 y DP19 (decisión del equipo): el caso urgente creado por el operador desde un aviso. Origen «Acompañamiento»,
    // estado «Pendiente» (se asigna con HU20, sin despacho automático: CA06), el ciudadano de la sesión como titular y la
    // última posición conocida como ubicación, con su hora real de obtención (no se presenta como actual).
    public static Caso CrearDesdeAviso(Guid ciudadanoId, Guid idSolicitud, (double Latitud, double Longitud, double PrecisionMetros, DateTimeOffset ObtenidaEn)? ultimaPosicion,
        DateTimeOffset ahora)
    {
        var caso = new Caso
        {
            Id = Guid.CreateVersion7(),
            Origen = OrigenCaso.Acompanamiento,
            Estado = EstadoCaso.Pendiente,
            CiudadanoId = ciudadanoId,
            IdSolicitud = idSolicitud,
            RegistradoEn = ahora,
        };
        if (ultimaPosicion is { } p)
            (caso.Latitud, caso.Longitud, caso.PrecisionMetros, caso.UbicacionObtenidaEn) = (p.Latitud, p.Longitud, p.PrecisionMetros, p.ObtenidaEn);
        return caso;
    }

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
