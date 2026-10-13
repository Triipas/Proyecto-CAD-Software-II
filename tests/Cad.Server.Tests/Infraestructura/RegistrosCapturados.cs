using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Cad.Server.Tests.Infraestructura;

// Captura todo lo que el servidor registra (todos los niveles) para comprobar
// que no aparecen contraseñas ni datos personales (RNF01; HU01-CA06).
public sealed class RegistrosCapturados : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entradas = new();

    public IReadOnlyCollection<string> Entradas => entradas;

    public ILogger CreateLogger(string categoria) => new Registrador(categoria, entradas);

    public void Dispose() { }

    private sealed class Registrador(string categoria, ConcurrentQueue<string> entradas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel nivel) => true;

        public void Log<TState>(LogLevel nivel, EventId evento, TState estado, Exception? excepcion,
            Func<TState, Exception?, string> formatear) =>
            entradas.Enqueue($"{nivel} {categoria}: {formatear(estado, excepcion)} {excepcion}");
    }
}
