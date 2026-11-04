using Cad.Server.Modulos.Asistencia.Puertos;

namespace Cad.Server.Modulos.Asistencia.Adaptadores;

// IA apagada (Ia:Habilitada = false) o sin clave: no hay sugerencias y el reporte se trabaja como siempre (HU46-CA03).
public sealed class AsistenteNulo : IAsistenteReportes
{
    public bool Disponible => false;

    public Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct) => Task.FromResult<ResultadoAsistente?>(null);

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) => Task.FromResult<ResultadoAsistente?>(null);
}
