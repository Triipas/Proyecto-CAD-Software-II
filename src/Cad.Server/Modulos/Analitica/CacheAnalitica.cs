using Microsoft.Extensions.Caching.Memory;

namespace Cad.Server.Modulos.Analitica;

// Dashboard (27/10/2026): los cálculos de un periodo se guardan unos minutos, para que la columna de preguntas pueda mostrar el
// dato de cada una sin recalcularlo todo en cada página. Lo del momento («Ahora») no pasa por aquí: siempre es en vivo.
public sealed class CacheAnalitica(IMemoryCache cache, TimeProvider reloj)
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(2);

    public async Task<T> ObtenerAsync<T>(string clave, Func<Task<T>> calcular)
    {
        if (cache.TryGetValue(clave, out T? guardado) && guardado is not null)
            return guardado;
        var valor = await calcular();
        cache.Set(clave, valor, reloj.GetUtcNow() + Duracion);
        return valor;
    }

    // Solo si ya está calculado (p. ej. la cobertura, cara de calcular, en la columna de preguntas).
    public T? SiExiste<T>(string clave) => cache.TryGetValue(clave, out T? guardado) ? guardado : default;
}
