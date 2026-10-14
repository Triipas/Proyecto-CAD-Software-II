using System.Collections.Concurrent;
using Cad.Server.Compartido;

namespace Cad.Server.Tests.Infraestructura;

// Doble del envío push (ADR-07): registra los tokens a los que se envió y permite simular respuestas de FCM.
public sealed class PushDePrueba : IEnviadorPush
{
    public ConcurrentQueue<string> Enviadas { get; } = new();

    // Tokens que FCM daría por no registrados (app desinstalada).
    public ConcurrentDictionary<string, bool> TokensInvalidos { get; } = new();

    public bool Configurado => true;

    public Task<ResultadoPush> EnviarDespachoAsync(string tokenDispositivo, CancellationToken ct)
    {
        Enviadas.Enqueue(tokenDispositivo);
        return Task.FromResult(TokensInvalidos.ContainsKey(tokenDispositivo) ? ResultadoPush.TokenInvalido : ResultadoPush.Enviada);
    }
}
