namespace Cad.Server.Modulos.Serenos.Dominio;

// ADR-07 — dispositivo del sereno que recibe la notificación push del despacho (HU21-CA01). Uno por sereno: el
// último registrado. El token es un dato sensible: nunca se escribe en los registros.
public sealed class DispositivoSereno
{
    // Límite técnico de la columna; los tokens de FCM rondan los 160–200 caracteres.
    public const int MaximoToken = 4096;

    public Guid SerenoId { get; private set; }
    public string Token { get; private set; } = "";
    public DateTimeOffset ActualizadoEn { get; private set; }

    private DispositivoSereno() { } // EF Core

    public static DispositivoSereno Registrar(Guid serenoId, string token, DateTimeOffset ahora) =>
        new() { SerenoId = serenoId, Token = token, ActualizadoEn = ahora };

    public void Actualizar(string token, DateTimeOffset ahora)
    {
        Token = token;
        ActualizadoEn = ahora;
    }
}
