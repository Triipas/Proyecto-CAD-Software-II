using Cad.Contratos.Casos;

namespace Cad.Movil.Servicios;

// HU31 — bandeja de notificaciones del ciudadano (E4: dentro de la app, sin push). Las notificaciones las genera el
// servidor y persisten: se consultan al abrir la app o la bandeja, también tras un tiempo sin conexión (CA02).
public sealed class ServicioBandeja(IApiCad api, IAlmacenSesion sesiones, TimeProvider reloj)
{
    // Sin leer, para el aviso del inicio; null si no hay sesión vigente o respuesta (no se muestra un número falso).
    public async Task<int?> ContarNoLeidasAsync(CancellationToken ct = default)
    {
        var sesion = await sesiones.LeerAsync();
        if (sesion is null || !sesion.Vigente(reloj.GetUtcNow()))
            return null;
        try
        {
            return await api.ConsultarNotificacionesAsync(sesion.Token, ct) is ResultadoApi<BandejaNotificaciones>.Correcto c ? c.Valor.NoLeidas : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
