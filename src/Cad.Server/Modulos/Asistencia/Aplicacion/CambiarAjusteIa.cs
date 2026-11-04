using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

public abstract record ResultadoAjusteIa
{
    public sealed record Cambiado(ModoAdmision Modo) : ResultadoAjusteIa;
    // Mismo modo y mismos parámetros: no se registra nada (p. ej. dos operadores pausan a la vez).
    public sealed record SinCambios(ModoAdmision Modo) : ResultadoAjusteIa;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoAjusteIa;
}

// HU47 (S6-T15; plan de asistencia con IA, 7.1 y 7.2) — cambiar el modo de la admisión automática. El administrador cambia el
// modo y los parámetros con un motivo; el operador solo puede pausar (pasar a «Apagada»): detener es siempre seguro y reactivar es
// del administrador. La página aplica la autorización; el operador solo dispone de PausarAsync, que no recibe un modo. Cada
// cambio queda en CambiosAjusteIa (quién, cuándo, de qué modo a cuál y por qué).
public sealed class CambiarAjusteIa(CadDbContext db, TimeProvider reloj, ILogger<CambiarAjusteIa> registro)
{
    public const string MotivoPausaOperador = "Pausada por un operador desde «Reportes».";

    public async Task<ResultadoAjusteIa> AdministrarAsync(ModoAdmision modo, ParametrosAdmision parametros, string? motivo, Guid administradorId,
        CancellationToken ct)
    {
        var errores = Validar(modo, parametros, motivo);
        if (errores.Count > 0)
            return new ResultadoAjusteIa.Invalido(errores);
        return await AplicarAsync(modo, parametros, motivo!.Trim(), administradorId, ct);
    }

    // 7.6 — el freno automático: pasa de «Activa» a «Sombra», con el sistema como responsable y el motivo calculado.
    public async Task<ResultadoAjusteIa> FrenarAsync(string motivo, CancellationToken ct)
    {
        var ajuste = await LeerAsync(ct);
        return await AplicarAsync(ModoAdmision.Sombra, ajuste.Parametros, motivo, Modulos.Cuentas.Dominio.UsuarioSistema.Id, ct);
    }

    public async Task<ResultadoAjusteIa> PausarAsync(Guid operadorId, CancellationToken ct)
    {
        var ajuste = await LeerAsync(ct);
        return await AplicarAsync(ModoAdmision.Apagada, ajuste.Parametros, MotivoPausaOperador, operadorId, ct);
    }

    private async Task<ResultadoAjusteIa> AplicarAsync(ModoAdmision modo, ParametrosAdmision parametros, string motivo, Guid actorId, CancellationToken ct)
    {
        var ajuste = await LeerAsync(ct);
        if (ajuste.Modo == modo && ajuste.Parametros == parametros)
            return new ResultadoAjusteIa.SinCambios(modo);

        var anterior = ajuste.Modo;
        var ahora = reloj.GetUtcNow();
        ajuste.Cambiar(modo, parametros, ahora);
        db.CambiosAjusteIa.Add(CambioAjusteIa.Registrar(anterior, modo, parametros, motivo, actorId, ahora));
        await db.SaveChangesAsync(ct);
        // Sin el motivo en el registro: solo el modo y quién.
        registro.LogInformation("Admisión automática: {Anterior} → {Nuevo} (por {ActorId}).", anterior, modo, actorId);
        return new ResultadoAjusteIa.Cambiado(modo);
    }

    // La fila se siembra en la migración; si faltara (p. ej. una base vaciada a mano), se crea con los valores por defecto. Primero
    // lo que ya sigue este contexto: una fila recién creada todavía no está en la base.
    private async Task<AjusteIa> LeerAsync(CancellationToken ct)
    {
        var ajuste = db.AjustesIa.Local.SingleOrDefault(a => a.Id == AjusteIa.IdUnico)
            ?? await db.AjustesIa.SingleOrDefaultAsync(a => a.Id == AjusteIa.IdUnico, ct);
        if (ajuste is not null)
            return ajuste;
        ajuste = AjusteIa.PorDefecto();
        db.AjustesIa.Add(ajuste);
        return ajuste;
    }

    private static Dictionary<string, string[]> Validar(ModoAdmision modo, ParametrosAdmision p, string? motivo)
    {
        var errores = new Dictionary<string, string[]>();
        void Rango(string campo, int valor, (int Minimo, int Maximo) rango, string unidad)
        {
            if (valor < rango.Minimo || valor > rango.Maximo)
                errores[campo] = [$"Indica un valor entre {rango.Minimo} y {rango.Maximo} {unidad}."];
        }

        if (!Enum.IsDefined(modo))
            errores["modo"] = ["Elige un modo."];
        Rango("retrasoSegundos", p.RetrasoSegundos, ParametrosAdmision.RangoRetraso, "segundos");
        Rango("maximoPorHora", p.MaximoPorHora, ParametrosAdmision.RangoMaximoPorHora, "reportes");
        Rango("longitudMinimaDescripcion", p.LongitudMinimaDescripcion, ParametrosAdmision.RangoLongitud, "caracteres");
        Rango("frenoMinimoAdmisiones", p.FrenoMinimoAdmisiones, ParametrosAdmision.RangoFrenoMinimo, "admisiones");
        Rango("frenoPorcentajeDescartes", p.FrenoPorcentajeDescartes, ParametrosAdmision.RangoFrenoPorcentaje, "%");
        if (string.IsNullOrWhiteSpace(motivo))
            errores["motivo"] = ["Indica el motivo del cambio."];
        else if (motivo.Trim().Length > CambioAjusteIa.MaximoMotivo)
            errores["motivo"] = [$"Admite hasta {CambioAjusteIa.MaximoMotivo} caracteres."];
        return errores;
    }
}
