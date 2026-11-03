using System.Security.Cryptography;
using System.Text;

namespace Cad.CargadorDataset;

// Contraseña del personal y PIN de los ciudadanos del dataset. Se generan la primera vez y se guardan en un archivo fuera del
// repositorio; las cargas siguientes los reutilizan, así que después de recargar se entra con las mismas cuentas. Nunca se
// escriben en la consola ni en el repositorio.
public sealed record Credenciales(string ContrasenaPersonal, string PinCiudadanos)
{
    private const string ClaveContrasena = "contrasena_personal";
    private const string ClavePin = "pin_ciudadanos";

    public static Credenciales LeerOCrear(string ruta)
    {
        if (File.Exists(ruta))
        {
            var valores = File.ReadAllLines(ruta)
                .Select(l => l.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            if (valores.TryGetValue(ClaveContrasena, out var contrasena) && valores.TryGetValue(ClavePin, out var pin))
                return new(contrasena, pin);
        }
        return new(GenerarContrasena(), GenerarPin());
    }

    // Reescribe el archivo con los valores y la lista de cuentas de esta carga.
    public void Guardar(string ruta, IEnumerable<string> cuentas)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var texto = new StringBuilder()
            .AppendLine("# Credenciales del dataset de demostración (datos ficticios). No subir al repositorio.")
            .AppendLine("# El personal entra al panel o a la app con su correo; los ciudadanos, con su celular y el PIN.")
            .AppendLine($"{ClaveContrasena}={ContrasenaPersonal}")
            .AppendLine($"{ClavePin}={PinCiudadanos}")
            .AppendLine();
        foreach (var cuenta in cuentas)
            texto.AppendLine(cuenta);
        File.WriteAllText(ruta, texto.ToString());
    }

    // Letras y números sin caracteres que se confundan (0/O, 1/l): cumple la regla de 8 a 128 caracteres con letra y número.
    private static string GenerarContrasena()
    {
        const string letras = "abcdefghjkmnpqrstuvwxyz";
        const string digitos = "23456789";
        var partes = Enumerable.Range(0, 3).Select(_ => new string(RandomNumberGenerator.GetItems<char>(letras, 4)));
        return $"{string.Join('-', partes)}-{new string(RandomNumberGenerator.GetItems<char>(digitos, 2))}";
    }

    // Seis dígitos que no sean repetidos ni seguidos (la regla del PIN de la app).
    private static string GenerarPin()
    {
        while (true)
        {
            var pin = string.Concat(Enumerable.Range(0, 6).Select(_ => RandomNumberGenerator.GetInt32(10)));
            var pasos = pin.Zip(pin.Skip(1), (a, b) => b - a).Distinct().ToArray();
            if (pin.Distinct().Count() > 1 && pasos is not ([1] or [-1]))
                return pin;
        }
    }
}
