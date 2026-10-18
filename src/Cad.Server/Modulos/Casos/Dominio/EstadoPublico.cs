using Cad.Contratos.Casos;

namespace Cad.Server.Modulos.Casos.Dominio;

// DP05 (decisión del PO del 14/10/2026): el estado que ve el titular del caso. Lo usan «Mis casos» (HU30) y las
// notificaciones (HU31): un cambio de estado interno solo se notifica si cambia este estado público.
public static class EstadoPublicoCaso
{
    // Un SOS pendiente aún no tiene sereno: se muestra «Recibido» (DP05 no nombra otro estado para él). Un reporte
    // pendiente fue admitido. Asignada, En camino y En atención se agrupan en «En atención».
    public static string De(OrigenCaso origen, EstadoCaso estado) => estado switch
    {
        EstadoCaso.Recibido => EstadosPublicos.Recibido,
        EstadoCaso.EnRevision => EstadosPublicos.EnRevision,
        EstadoCaso.Pendiente => origen == OrigenCaso.Reporte ? EstadosPublicos.Admitido : EstadosPublicos.Recibido,
        EstadoCaso.Asignada or EstadoCaso.EnCamino or EstadoCaso.EnAtencion => EstadosPublicos.EnAtencion,
        EstadoCaso.Cerrada => EstadosPublicos.Cerrado,
        EstadoCaso.Derivada => EstadosPublicos.Derivado,
        EstadoCaso.Descartada => EstadosPublicos.Descartado,
        EstadoCaso.Cancelada => EstadosPublicos.Cancelado,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };
}
