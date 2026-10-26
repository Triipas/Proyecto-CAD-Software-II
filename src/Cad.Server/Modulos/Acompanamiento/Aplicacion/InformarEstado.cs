using Cad.Contratos.Acompanamiento;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Acompanamiento.Aplicacion;

public abstract record ResultadoEstadoInformado
{
    public sealed record Registrado(EstadoInformado Estado, SesionAcompanamiento Sesion) : ResultadoEstadoInformado;
    // Reenvío de la misma pulsación: se devuelve lo ya registrado (RNF08).
    public sealed record YaRegistrado(EstadoInformado Estado, SesionAcompanamiento Sesion) : ResultadoEstadoInformado;
    // La sesión está cerrada o vencida: solo «Necesito ayuda» se acepta igual.
    public sealed record SesionCerrada(SesionAcompanamiento Sesion) : ResultadoEstadoInformado;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoEstadoInformado;
    public sealed record NoEncontrada : ResultadoEstadoInformado;
}

// 26/10/2026 — el ciudadano informa a la central cómo va durante su acompañamiento, sin que se lo pidan (ver EstadoInformado).
// Solo el titular: la sesión ajena responde igual que una inexistente.
public sealed class InformarEstado(CadDbContext db, ConfirmarBienestar confirmar, GestionarAcompanamiento gestionar, TimeProvider reloj,
    INotificador notificador, ILogger<InformarEstado> registro)
{
    public async Task<ResultadoEstadoInformado> InformarAsync(Guid ciudadanoId, Guid sesionId, SolicitudEstadoAcompanamiento solicitud,
        CancellationToken ct)
    {
        EstadoCiudadano estado;
        switch (solicitud.Estado)
        {
            case EstadosCiudadano.EstoyBien: estado = EstadoCiudadano.EstoyBien; break;
            case EstadosCiudadano.AlgoNoEstaBien: estado = EstadoCiudadano.AlgoNoEstaBien; break;
            case EstadosCiudadano.Llegue: estado = EstadoCiudadano.Llegue; break;
            case EstadosCiudadano.NecesitoAyuda when solicitud.Sos is { } sos && sos.IdSolicitud != Guid.Empty:
                estado = EstadoCiudadano.NecesitoAyuda;
                break;
            case EstadosCiudadano.NecesitoAyuda:
                return new ResultadoEstadoInformado.Invalido(new() { ["sos"] = ["Falta la solicitud del SOS (con su identificador)."] });
            default:
                return new ResultadoEstadoInformado.Invalido(new() { ["estado"] = ["Indica «Estoy bien», «Algo no está bien», «Necesito ayuda» o «Llegué»."] });
        }
        if (solicitud.IdSolicitud == Guid.Empty)
            return new ResultadoEstadoInformado.Invalido(new() { ["idSolicitud"] = ["Falta el identificador de la solicitud."] });

        if (await RepetidoAsync(ciudadanoId, solicitud.IdSolicitud, ct) is { } repetido)
            return repetido;
        if (await gestionar.DelTitularAsync(ciudadanoId, sesionId, ct) is null)
            return new ResultadoEstadoInformado.NoEncontrada();

        // Pedir ayuda nunca se rechaza porque la sesión haya terminado (como HU41): primero el SOS, sin bloquear la sesión.
        Guid? casoId = null;
        if (estado == EstadoCiudadano.NecesitoAyuda)
        {
            var (vigente, errores) = await confirmar.SosVigenteOEmitirAsync(ciudadanoId, solicitud.Sos!, ct);
            if (errores is not null)
                return new ResultadoEstadoInformado.Invalido(errores);
            casoId = vigente;
            db.ChangeTracker.Clear(); // EmitirSos usa este mismo contexto
        }

        // Bloqueo compartido de la sesión: una finalización o un vencimiento simultáneos esperan, así que un estado que exige la
        // sesión activa solo se registra si lo estaba al recibirlo.
        EstadoInformado registrado;
        SesionAcompanamiento sesion;
        await using (var transaccion = await db.Database.BeginTransactionAsync(ct))
        {
            sesion = await db.SesionesAcompanamiento
                .FromSql($"SELECT *, xmin FROM \"SesionesAcompanamiento\" WHERE \"Id\" = {sesionId} FOR SHARE")
                .AsNoTracking().SingleAsync(ct);
            var ahora = reloj.GetUtcNow();
            if (estado != EstadoCiudadano.NecesitoAyuda && !sesion.AdmitePosiciones(ahora))
                return new ResultadoEstadoInformado.SesionCerrada(sesion);
            registrado = EstadoInformado.Registrar(sesion, solicitud.IdSolicitud, estado, casoId, ahora);
            db.EstadosInformados.Add(registrado);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
            {
                // La misma pulsación llegó dos veces a la vez: solo una se registra.
                await transaccion.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return await RepetidoAsync(ciudadanoId, solicitud.IdSolicitud, ct)
                    ?? throw new InvalidOperationException("Violación de unicidad sin un estado repetido.", ex);
            }
        }

        registro.LogInformation("Acompañamiento {SesionId}: el ciudadano informó {Estado}.", sesionId, estado);
        // «Llegué»: llegó a su destino y el acompañamiento termina (HU36; un reenvío no cambia nada, CA07).
        if (estado == EstadoCiudadano.Llegue && await gestionar.FinalizarAsync(ciudadanoId, sesionId, ct) is ResultadoFinalizacion.Cerrada cerrada)
            sesion = cerrada.Sesion;
        else
            await AvisarAsync(sesionId);
        return new ResultadoEstadoInformado.Registrado(registrado, sesion);
    }

    private async Task<ResultadoEstadoInformado?> RepetidoAsync(Guid ciudadanoId, Guid idSolicitud, CancellationToken ct)
    {
        var estado = await db.EstadosInformados.AsNoTracking().SingleOrDefaultAsync(e => e.CiudadanoId == ciudadanoId && e.IdSolicitud == idSolicitud, ct);
        if (estado is null)
            return null;
        var sesion = await db.SesionesAcompanamiento.AsNoTracking().SingleAsync(s => s.Id == estado.SesionId, ct);
        return new ResultadoEstadoInformado.YaRegistrado(estado, sesion);
    }

    // Después del COMMIT, la central vuelve a leer la sesión. Un fallo del canal no afecta al registro.
    private async Task AvisarAsync(Guid sesionId)
    {
        try
        {
            await notificador.AcompanamientoActualizadoAsync(sesionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogWarning(ex, "No se pudo avisar a la central del acompañamiento {SesionId}; se verá al actualizar.", sesionId);
        }
    }
}
