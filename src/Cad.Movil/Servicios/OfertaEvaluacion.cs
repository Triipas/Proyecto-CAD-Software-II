using Cad.Contratos.Casos;

namespace Cad.Movil.Servicios;

// HU32 — la evaluación de la atención se ofrece sola, una única vez, cuando el caso del ciudadano pasa a «Cerrado» (29/10/2026,
// pedido del ciudadano). Es opcional: omitirla no cambia el caso ni lo restringe, y después sigue disponible en «Mi caso».
// La app no recibe avisos push en el modo ciudadano: se entera al consultar sus casos (al abrir el inicio, cada pocos
// segundos mientras lo ve y al refrescar «Mi caso»). Que el caso ya se ofreció se recuerda en el teléfono, por caso, sin
// importar si el ciudadano calificó, omitió o cerró la app: no se vuelve a abrir sola.
public sealed class OfertaEvaluacion(IApiCad api, IAlmacenLocal almacen, TimeProvider reloj)
{
    public const string PrefijoClave = "cad.evaluacion.ofrecida.";
    // Un cierre más antiguo ya no se ofrece sin que el ciudadano lo pida: así, con la primera versión que trae esto, no se
    // abren de golpe los casos cerrados de hace meses.
    public static readonly TimeSpan Vigencia = TimeSpan.FromDays(7);
    // Cuántos casos cerrados sin revisar se miran en cada consulta (el resto se revisa en las siguientes).
    private const int MaximoPorRevision = 5;

    public bool YaOfrecida(Guid casoId) => almacen.Leer(PrefijoClave + casoId.ToString("N")) is not null;

    public void Marcar(Guid casoId) => almacen.Guardar(PrefijoClave + casoId.ToString("N"), "1");

    // Un caso cerrado hace poco, sin evaluar y que todavía no se ofreció.
    public bool EsOfrecible(MiCasoDetalle caso) => !YaOfrecida(caso.Id) && PendienteDeEvaluar(caso);

    private bool PendienteDeEvaluar(MiCasoDetalle caso) =>
        caso is { Estado: EstadosPublicos.Cerrado, Evaluacion: null }
        && caso.Avances.Where(a => a.Estado == EstadosPublicos.Cerrado).Select(a => (DateTimeOffset?)a.En).Max() is { } cierre
        && reloj.GetUtcNow() - cierre <= Vigencia;

    // El caso cerrado más reciente que corresponde ofrecer ahora, o null. Ese se marca como ofrecido y los demás cerrados que
    // no se ofrecieron también se dan por vistos: se ofrece un caso por vez, no una cola de evaluaciones.
    public async Task<Guid?> BuscarAsync(string token, IEnumerable<MiCasoResumen> casos, CancellationToken ct = default)
    {
        var pendientes = casos.Where(c => c.Estado == EstadosPublicos.Cerrado && !YaOfrecida(c.Id))
            .OrderByDescending(c => c.RegistradoEn).ToList();
        Guid? elegido = null;
        var revisados = 0;
        foreach (var caso in pendientes)
        {
            if (elegido is not null)
            {
                Marcar(caso.Id);
                continue;
            }
            if (revisados++ >= MaximoPorRevision)
                break;
            try
            {
                if (await api.ConsultarMiCasoAsync(token, caso.Id, ct) is not ResultadoApi<MiCasoDetalle>.Correcto detalle)
                    return null; // sin respuesta o sesión vencida: no se marca nada y se reintenta en la próxima consulta
                Marcar(caso.Id);
                if (PendienteDeEvaluar(detalle.Valor))
                    elegido = caso.Id;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }
        return elegido;
    }
}
