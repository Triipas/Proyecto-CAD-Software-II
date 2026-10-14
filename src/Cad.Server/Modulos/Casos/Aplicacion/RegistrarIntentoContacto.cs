using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Casos.Aplicacion;

public sealed record DatosIntentoContacto(Guid IdSolicitud, string? Resultado, string? InformacionHecho, string? ReferenciaUbicacion);

public abstract record ResultadoIntentoContacto
{
    public sealed record Registrado(IntentoContacto Intento) : ResultadoIntentoContacto;
    // Reenvío técnico del mismo formulario: no se crea un segundo intento.
    public sealed record YaRegistrado(IntentoContacto Intento) : ResultadoIntentoContacto;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoIntentoContacto;
    public sealed record CasoNoEncontrado : ResultadoIntentoContacto;
}

// HU12 — Contactar al ciudadano y registrar la verificación del SOS (S2-T01).
// La autorización (solo operador, CA08) la aplica la política de la página que llama a este caso de uso.
public sealed class RegistrarIntentoContacto(CadDbContext db, TimeProvider reloj, ILogger<RegistrarIntentoContacto> registro)
{
    // CA02/CA03: solo estos tres valores, escritos exactamente así. No se usa Enum.TryParse,
    // que aceptaría números u otras grafías.
    private static readonly Dictionary<string, ResultadoContacto> Resultados = new(StringComparer.Ordinal)
    {
        [nameof(ResultadoContacto.Contactado)] = ResultadoContacto.Contactado,
        [nameof(ResultadoContacto.SinRespuesta)] = ResultadoContacto.SinRespuesta,
        [nameof(ResultadoContacto.NoFuePosibleComunicar)] = ResultadoContacto.NoFuePosibleComunicar,
    };

    public async Task<ResultadoIntentoContacto> EjecutarAsync(Guid casoId, Guid operadorId, DatosIntentoContacto datos, CancellationToken ct)
    {
        var errores = Validar(datos, out var resultado);
        if (errores.Count > 0)
            return new ResultadoIntentoContacto.Invalido(errores);

        var caso = await db.Casos.AsNoTracking().SingleOrDefaultAsync(c => c.Id == casoId && c.Origen == OrigenCaso.Sos, ct);
        if (caso is null)
            return new ResultadoIntentoContacto.CasoNoEncontrado();

        if (await BuscarAsync(casoId, datos.IdSolicitud, ct) is { } existente)
            return new ResultadoIntentoContacto.YaRegistrado(existente);

        var ahora = reloj.GetUtcNow();
        var intento = IntentoContacto.Registrar(caso, operadorId, datos.IdSolicitud, resultado,
            datos.InformacionHecho, datos.ReferenciaUbicacion, ahora);

        // Intento y evento en una sola transacción. El estado del SOS no cambia: un intento sin respuesta
        // no lo cancela y el SOS sigue pudiendo asignarse (CA06).
        db.IntentosContacto.Add(intento);
        db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.IntentoContactoRegistrado, operadorId, VisibilidadEvento.Interna, ahora));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            db.ChangeTracker.Clear();
            return new ResultadoIntentoContacto.YaRegistrado((await BuscarAsync(casoId, datos.IdSolicitud, ct))!);
        }

        // Sin la información ni la referencia en el registro: pueden contener datos personales.
        registro.LogInformation("Intento de contacto {Resultado} registrado en el caso {CasoId}.", resultado, casoId);
        return new ResultadoIntentoContacto.Registrado(intento);
    }

    private static Dictionary<string, string[]> Validar(DatosIntentoContacto datos, out ResultadoContacto resultado)
    {
        var errores = new Dictionary<string, string[]>();
        resultado = default;
        if (datos.IdSolicitud == Guid.Empty)
            errores["idSolicitud"] = ["Falta el identificador de la solicitud. Recarga la página e inténtalo de nuevo."];
        if (datos.Resultado is null || !Resultados.TryGetValue(datos.Resultado, out resultado))
            errores["resultado"] = ["Elige el resultado del intento: Contactado, Sin respuesta o No fue posible comunicar."];
        if (datos.InformacionHecho?.Trim().Length > IntentoContacto.MaximoInformacion)
            errores["informacionHecho"] = [$"Escribe como máximo {IntentoContacto.MaximoInformacion} caracteres."];
        if (datos.ReferenciaUbicacion?.Trim().Length > IntentoContacto.MaximoReferencia)
            errores["referenciaUbicacion"] = [$"Escribe como máximo {IntentoContacto.MaximoReferencia} caracteres."];
        return errores;
    }

    private Task<IntentoContacto?> BuscarAsync(Guid casoId, Guid idSolicitud, CancellationToken ct) =>
        db.IntentosContacto.AsNoTracking().SingleOrDefaultAsync(i => i.CasoId == casoId && i.IdSolicitud == idSolicitud, ct);
}
