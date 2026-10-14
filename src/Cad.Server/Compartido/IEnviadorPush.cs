namespace Cad.Server.Compartido;

public enum ResultadoPush
{
    Enviada,
    // FCM indica que el token ya no corresponde a una instalación de la app: se borra.
    TokenInvalido,
    // Error transitorio o de configuración: no hay garantía de entrega (ADR-07).
    NoEnviada,
}

// ADR-07 — notificación push al dispositivo de un sereno. La notificación no lleva datos del caso: solo anuncia
// que hay un despacho y abre la app, que consulta los datos con la sesión del sereno (DP06, RNF12).
public interface IEnviadorPush
{
    bool Configurado { get; }

    Task<ResultadoPush> EnviarDespachoAsync(string tokenDispositivo, CancellationToken ct);
}
