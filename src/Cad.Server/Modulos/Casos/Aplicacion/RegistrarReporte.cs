using Cad.Contratos.Reportes;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public abstract record ResultadoRegistroReporte
{
    public sealed record Registrado(Caso Caso) : ResultadoRegistroReporte;
    // Reiteración técnica de la misma solicitud: se devuelve el reporte ya registrado (CA08).
    public sealed record YaRegistrado(Caso Caso) : ResultadoRegistroReporte;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoRegistroReporte;
}

// HU10 — Registrar un reporte de incidencia (parte de servidor de S3-T01 y S3-T02). Mismo mecanismo de idempotencia
// que el SOS (S1-T14). Un reporte no es un SOS: no avisa a la sección de atención urgente (CA06).
public sealed class RegistrarReporte(CadDbContext db, TimeProvider reloj, Compartido.INotificador notificador, ILogger<RegistrarReporte> registro)
{
    // DP08: la fecha y hora del hecho no pueden ser futuras. Margen técnico para la diferencia entre el reloj del
    // teléfono y el del servidor (no es una regla de negocio): sin él, «ahora» en un teléfono adelantado se rechazaría.
    public static readonly TimeSpan MargenReloj = TimeSpan.FromMinutes(5);

    public async Task<ResultadoRegistroReporte> EjecutarAsync(Guid ciudadanoId, SolicitudRegistroReporte solicitud, CancellationToken ct)
    {
        // Sin IdSolicitud no se puede garantizar que un reintento no duplique el reporte.
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoRegistroReporte.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });

        if (await BuscarAsync(ciudadanoId, solicitud.IdSolicitud, ct) is { } existente)
            return Reiteracion(existente);

        var ahora = reloj.GetUtcNow();
        var (errores, descripcion, ubicacion) = await ValidarAsync(solicitud, ahora, ct);
        var fotos = ValidarFotos(solicitud.Fotos, errores);
        if (errores.Count > 0)
            return new ResultadoRegistroReporte.Invalido(errores);

        var caso = Caso.RegistrarReporte(ciudadanoId, solicitud.IdSolicitud, solicitud.CategoriaId!.Value, ahora);
        var u = ubicacion!;

        // Caso, detalle y evento en un solo SaveChanges: una sola transacción, sin reporte parcial (CA08).
        db.Casos.Add(caso);
        db.DetallesReporte.Add(DetalleReporte.Registrar(caso, caso.CategoriaId!.Value, descripcion!,
            solicitud.OcurridoAproximadamenteEn!.Value.ToUniversalTime(), u.Fuente, u.Latitud, u.Longitud, u.Precision, u.Referencia));
        for (var n = 0; n < fotos.Count; n++)
            db.FotosReporte.Add(FotoReporte.Registrar(caso, n + 1, fotos[n], ahora));
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.ReporteRegistrado, ciudadanoId, VisibilidadEvento.Publica, ahora));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Dos reintentos simultáneos: el índice único deja pasar solo uno.
            db.ChangeTracker.Clear();
            return Reiteracion((await BuscarAsync(ciudadanoId, solicitud.IdSolicitud, ct))!);
        }

        // Sin descripción, referencia, coordenadas ni fotos en el registro (RNF07): solo el identificador, la fuente y cuántas fotos.
        registro.LogInformation("Reporte {CasoId} registrado (ubicación del hecho: {Fuente}; fotos: {Fotos}).", caso.Id, u.Fuente, fotos.Count);
        await AvisarAsync(caso.Id);
        return new ResultadoRegistroReporte.Registrado(caso);
    }

    // Reporte registrado por la central (23/10/2026), p. ej. por una llamada: sin ciudadano, con quién avisó y su teléfono si los
    // dio (opcionales), y ya admitido para asignarlo. La admisión queda como decisión del operador, como la de un reporte revisado,
    // para que figure en «Reportes admitidos» y en sus tiempos. No genera avisos a ningún ciudadano.
    public const string MotivoAdmisionCentral = "Registrado por la central: no requiere revisión.";

    public async Task<ResultadoRegistroReporte> RegistrarDeLaCentralAsync(Guid operadorId, SolicitudRegistroReporte solicitud,
        string? contactoNombre, string? contactoTelefono, CancellationToken ct)
    {
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoRegistroReporte.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });
        if (await BuscarDeLaCentralAsync(solicitud.IdSolicitud, ct) is { } existente)
            return new ResultadoRegistroReporte.YaRegistrado(existente);

        var ahora = reloj.GetUtcNow();
        var (errores, descripcion, ubicacion) = await ValidarAsync(solicitud, ahora, ct);
        var nombre = string.IsNullOrWhiteSpace(contactoNombre) ? null : contactoNombre.Trim();
        var telefono = string.IsNullOrWhiteSpace(contactoTelefono) ? null : contactoTelefono.Trim();
        if (nombre is { Length: > DetalleReporte.MaximoContactoNombre })
            errores["contactoNombre"] = [$"El nombre admite hasta {DetalleReporte.MaximoContactoNombre} caracteres."];
        if (telefono is not null && (telefono.Length > DetalleReporte.MaximoContactoTelefono || !telefono.All(c => char.IsDigit(c) || c is ' ' or '+' or '-')))
            errores["contactoTelefono"] = ["Escribe el teléfono solo con números (puede llevar +, espacios o guiones)."];
        if (errores.Count > 0)
            return new ResultadoRegistroReporte.Invalido(errores);

        var caso = Caso.RegistrarReporteDeLaCentral(operadorId, solicitud.IdSolicitud, solicitud.CategoriaId!.Value, ahora);
        var u = ubicacion!;
        db.Casos.Add(caso);
        db.DetallesReporte.Add(DetalleReporte.Registrar(caso, caso.CategoriaId!.Value, descripcion!,
            solicitud.OcurridoAproximadamenteEn!.Value.ToUniversalTime(), u.Fuente, u.Latitud, u.Longitud, u.Precision, u.Referencia,
            operadorId, nombre, telefono));
        db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Admision, MotivoAdmisionCentral, null, null, operadorId, ahora));
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.ReporteRegistrado, operadorId, VisibilidadEvento.Interna, ahora));
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.ReporteAdmitido, operadorId, VisibilidadEvento.Interna, ahora));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
            return new ResultadoRegistroReporte.YaRegistrado((await BuscarDeLaCentralAsync(solicitud.IdSolicitud, ct))!);
        }

        // Sin descripción, contacto, referencia ni coordenadas en el registro (RNF07).
        registro.LogInformation("Reporte {CasoId} registrado por la central (ubicación del hecho: {Fuente}).", caso.Id, u.Fuente);
        await AvisarAsync(caso.Id);
        return new ResultadoRegistroReporte.Registrado(caso);
    }

    // Validación común a la app (HU10) y a la central: tipo del catálogo, descripción, fecha y hora del hecho y su ubicación.
    private async Task<(Dictionary<string, string[]> Errores, string? Descripcion, UbicacionValida? Ubicacion)> ValidarAsync(
        SolicitudRegistroReporte solicitud, DateTimeOffset ahora, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        void Error(string campo, string mensaje) => errores[campo] = [mensaje];

        // CA01, CA07 y DP07: tipo del catálogo compartido.
        var categoriaValida = solicitud.CategoriaId is { } categoriaId
            && await db.CategoriasIncidencia.AsNoTracking().AnyAsync(c => c.Id == categoriaId, ct);
        if (!categoriaValida)
            Error("categoriaId", "Elige un tipo de incidencia del catálogo.");

        // CA02 y DP08: descripción obligatoria con límite técnico.
        var descripcion = solicitud.Descripcion?.Trim();
        if (string.IsNullOrEmpty(descripcion))
            Error("descripcion", "Describe lo ocurrido.");
        else if (descripcion.Length > DetalleReporte.MaximoDescripcion)
            Error("descripcion", $"La descripción admite hasta {DetalleReporte.MaximoDescripcion} caracteres.");

        // CA02 y DP08: fecha y hora aproximadas obligatorias y no futuras.
        if (solicitud.OcurridoAproximadamenteEn is not { } ocurrido || ocurrido == default)
            Error("ocurridoAproximadamenteEn", "Indica la fecha y hora aproximadas del hecho.");
        else if (ocurrido > ahora + MargenReloj)
            Error("ocurridoAproximadamenteEn", "La fecha y hora del hecho no pueden ser futuras.");

        // CA03 y CA04: ubicación del hecho por un punto (del mapa o la posición actual, si el ciudadano la eligió) o por
        // una referencia textual. Con «Texto» no se admiten coordenadas: no debe llegar ninguna posición del dispositivo.
        var ubicacion = ValidarUbicacion(solicitud.Ubicacion, Error);

        return (errores, descripcion, ubicacion);
    }

    // DP08 ampliada (26/10/2026): hasta tres fotos JPEG de hasta 1 MB. Se guardan sin metadatos (EXIF con la ubicación GPS,
    // XMP, comentarios) aunque la app ya los quite: la app no es la única que puede llamar a la API.
    private static List<byte[]> ValidarFotos(IReadOnlyList<byte[]>? fotos, Dictionary<string, string[]> errores)
    {
        var limpias = new List<byte[]>();
        if (fotos is null or { Count: 0 })
            return limpias;
        if (fotos.Count > LimitesFotosReporte.Maximo)
        {
            errores["fotos"] = [$"Puedes adjuntar hasta {LimitesFotosReporte.Maximo} fotos."];
            return limpias;
        }
        for (var n = 0; n < fotos.Count; n++)
        {
            var foto = fotos[n];
            if (foto is not { Length: > 0 })
                errores[$"fotos[{n}]"] = ["La foto está vacía."];
            else if (foto.Length > LimitesFotosReporte.MaximoBytes)
                errores[$"fotos[{n}]"] = ["Cada foto admite hasta 1 MB."];
            else if (LimpiezaJpeg.QuitarMetadatos(foto) is { } limpia)
                limpias.Add(limpia);
            else
                errores[$"fotos[{n}]"] = ["La foto debe ser una imagen JPEG."];
        }
        return limpias;
    }

    // HU25-CA05: la central incorpora el marcador sin recargar (sin el aviso sonoro de un SOS: no es urgente). Después de
    // confirmar; si falla, el caso ya está guardado y aparece al resincronizar.
    private async Task AvisarAsync(Guid casoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central del reporte {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }

    private sealed record UbicacionValida(string Fuente, double? Latitud, double? Longitud, double? Precision, string? Referencia);

    private static UbicacionValida? ValidarUbicacion(UbicacionHecho? ubicacion, Action<string, string> error)
    {
        if (ubicacion is null)
        {
            error("ubicacion", "Indica la ubicación del hecho con un punto en el mapa o una referencia.");
            return null;
        }

        var referencia = string.IsNullOrWhiteSpace(ubicacion.Referencia) ? null : ubicacion.Referencia.Trim();
        if (referencia is { Length: > DetalleReporte.MaximoReferencia })
        {
            error("ubicacion.referencia", $"La referencia admite hasta {DetalleReporte.MaximoReferencia} caracteres.");
            return null;
        }

        var conCoordenadas = ubicacion.Latitud is not null || ubicacion.Longitud is not null || ubicacion.PrecisionMetros is not null;
        switch (ubicacion.Fuente)
        {
            case FuentesUbicacionHecho.Texto:
                if (conCoordenadas)
                {
                    error("ubicacion", "Una ubicación por referencia no lleva coordenadas.");
                    return null;
                }
                if (referencia is null)
                {
                    error("ubicacion.referencia", "Escribe una referencia del lugar del hecho.");
                    return null;
                }
                return new UbicacionValida(FuentesUbicacionHecho.Texto, null, null, null, referencia);

            case FuentesUbicacionHecho.Mapa:
            case FuentesUbicacionHecho.PosicionActual:
                if (ubicacion.Latitud is not { } latitud || !double.IsFinite(latitud) || latitud is < -90 or > 90
                    || ubicacion.Longitud is not { } longitud || !double.IsFinite(longitud) || longitud is < -180 or > 180)
                {
                    error("ubicacion", "El punto del hecho no es válido.");
                    return null;
                }
                if (ubicacion.PrecisionMetros is { } precision && (!double.IsFinite(precision) || precision <= 0))
                {
                    error("ubicacion", "La precisión del punto no es válida.");
                    return null;
                }
                return new UbicacionValida(ubicacion.Fuente, latitud, longitud, ubicacion.PrecisionMetros, referencia);

            default:
                error("ubicacion.fuente", "Indica si la ubicación del hecho es un punto en el mapa, tu posición actual o una referencia.");
                return null;
        }
    }

    // El identificador de solicitud es único por ciudadano para todos sus casos: si ya se usó en un SOS, no se devuelve
    // ese SOS como si fuera el reporte.
    private static ResultadoRegistroReporte Reiteracion(Caso existente) => existente.Origen == OrigenCaso.Reporte
        ? new ResultadoRegistroReporte.YaRegistrado(existente)
        : new ResultadoRegistroReporte.Invalido(new() { ["idSolicitud"] = ["El identificador de la solicitud ya se usó en otro caso."] });

    private Task<Caso?> BuscarDeLaCentralAsync(Guid idSolicitud, CancellationToken ct) =>
        db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.CiudadanoId == null && c.IdSolicitud == idSolicitud, ct);

    private Task<Caso?> BuscarAsync(Guid ciudadanoId, Guid idSolicitud, CancellationToken ct) =>
        db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.CiudadanoId == ciudadanoId && c.IdSolicitud == idSolicitud, ct);
}
