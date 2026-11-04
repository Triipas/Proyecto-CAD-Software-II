namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Cupo por minuto y presupuesto diario de tokens (5.8: cortacircuitos). Uno por servidor; el día se cuenta en UTC.
public sealed class ControlConsumoIa(TimeProvider reloj)
{
    private readonly Lock cerrojo = new();
    private readonly Queue<DateTimeOffset> llamadas = new();
    private DateOnly dia;
    private long tokensDelDia;

    public long TokensDelDia { get { lock (cerrojo) { return tokensDelDia; } } }

    public bool Reservar(int maximoPorMinuto, long presupuestoDiario)
    {
        var ahora = reloj.GetUtcNow();
        lock (cerrojo)
        {
            ReiniciarSiCambioElDia(ahora);
            if (tokensDelDia >= presupuestoDiario)
                return false;
            while (llamadas.Count > 0 && ahora - llamadas.Peek() >= TimeSpan.FromMinutes(1))
                llamadas.Dequeue();
            if (llamadas.Count >= maximoPorMinuto)
                return false;
            llamadas.Enqueue(ahora);
            return true;
        }
    }

    public void Anotar(int tokens)
    {
        lock (cerrojo)
        {
            ReiniciarSiCambioElDia(reloj.GetUtcNow());
            tokensDelDia += tokens;
        }
    }

    private void ReiniciarSiCambioElDia(DateTimeOffset ahora)
    {
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        if (hoy == dia)
            return;
        dia = hoy;
        tokensDelDia = 0;
    }
}
