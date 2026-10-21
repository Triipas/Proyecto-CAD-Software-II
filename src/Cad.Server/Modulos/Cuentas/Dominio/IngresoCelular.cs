using System.Security.Cryptography;
using System.Text;

namespace Cad.Server.Modulos.Cuentas.Dominio;

// ADR-08 (DP24): el único teléfono vinculado a la cuenta de un ciudadano. La app guarda la llave en su almacenamiento
// seguro; aquí solo queda su hash. Vincular otro teléfono reemplaza la fila y revoca la sesión del anterior.
public sealed class DispositivoCiudadano
{
    public Guid UsuarioId { get; private set; }
    public string LlaveHash { get; private set; } = "";
    public DateTimeOffset VinculadoEn { get; private set; }

    private DispositivoCiudadano() { } // EF Core

    // Devuelve la llave en claro una sola vez, para entregarla a la app.
    public static (DispositivoCiudadano Dispositivo, string Llave) Vincular(Guid usuarioId, DateTimeOffset ahora)
    {
        var llave = Secretos.Generar();
        return (new DispositivoCiudadano { UsuarioId = usuarioId, LlaveHash = Secretos.Hash(llave), VinculadoEn = ahora }, llave);
    }

    public bool Coincide(string? llave) =>
        !string.IsNullOrEmpty(llave) && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(LlaveHash), Encoding.ASCII.GetBytes(Secretos.Hash(llave)));
}

// ADR-08: un envío de código a un celular y, si se valida, el token de un solo uso que prueba que el celular es de
// quien lo escribió. El código simulado se guarda con hash; el de Twilio lo genera y valida Twilio.
public sealed class VerificacionCelular
{
    public Guid Id { get; private set; }
    public string Celular { get; private set; } = "";
    public bool Simulado { get; private set; }
    public string? CodigoHash { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset ExpiraEn { get; private set; }
    public int Intentos { get; private set; }
    public DateTimeOffset? VerificadaEn { get; private set; }
    public string? TokenHash { get; private set; }
    public DateTimeOffset? TokenExpiraEn { get; private set; }
    public DateTimeOffset? TokenUsadoEn { get; private set; }

    private VerificacionCelular() { } // EF Core

    public static VerificacionCelular Crear(string celular, string? codigoSimulado, DateTimeOffset ahora, TimeSpan vigencia)
    {
        var id = Guid.CreateVersion7();
        return new VerificacionCelular
        {
            Id = id,
            Celular = celular,
            Simulado = codigoSimulado is not null,
            CodigoHash = codigoSimulado is null ? null : Secretos.Hash($"{id:N}:{codigoSimulado}"),
            CreadaEn = ahora,
            ExpiraEn = ahora + vigencia,
        };
    }

    public bool Vigente(DateTimeOffset ahora) => VerificadaEn is null && ahora < ExpiraEn;

    public void RegistrarIntento() => Intentos++;

    public bool CodigoSimuladoCoincide(string codigo) =>
        CodigoHash is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(CodigoHash), Encoding.ASCII.GetBytes(Secretos.Hash($"{Id:N}:{codigo}")));

    // Devuelve el token en claro una sola vez.
    public string MarcarVerificada(DateTimeOffset ahora, TimeSpan vigenciaToken)
    {
        var token = Secretos.Generar();
        VerificadaEn = ahora;
        TokenHash = Secretos.Hash(token);
        TokenExpiraEn = ahora + vigenciaToken;
        return token;
    }

    public bool TokenUtilizable(DateTimeOffset ahora) => TokenUsadoEn is null && TokenExpiraEn is { } expira && ahora < expira;

    public void UsarToken(DateTimeOffset ahora) => TokenUsadoEn = ahora;
}

public static class Secretos
{
    // 256 bits aleatorios en Base64 para URL: llaves de dispositivo y tokens de verificación.
    public static string Generar() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    public static string CodigoNumerico(int digitos) =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, digitos)).ToString($"D{digitos}");
}
