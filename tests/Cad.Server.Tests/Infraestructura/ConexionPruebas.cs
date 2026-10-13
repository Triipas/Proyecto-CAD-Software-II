using Npgsql;

namespace Cad.Server.Tests.Infraestructura;

// Cadena de conexión de la base de pruebas: la variable CAD_PRUEBAS_CONEXION (por ejemplo, en CI)
// o, en local, las credenciales del .env de la raíz del repositorio (el mismo que usa docker compose).
public static class ConexionPruebas
{
    public const string BaseDatos = "cad_pruebas";

    public static string Obtener()
    {
        var variable = Environment.GetEnvironmentVariable("CAD_PRUEBAS_CONEXION");
        if (!string.IsNullOrWhiteSpace(variable))
            return variable;

        var archivo = Path.Combine(BuscarRaizRepositorio(), ".env");
        if (!File.Exists(archivo))
            throw new InvalidOperationException(
                "No se encontró .env en la raíz del repositorio. Copia .env.example a .env y levanta PostgreSQL con `docker compose up -d`.");

        var valores = File.ReadAllLines(archivo)
            .Select(linea => linea.Trim())
            .Where(linea => linea.Length > 0 && !linea.StartsWith('#') && linea.Contains('='))
            .Select(linea => linea.Split('=', 2))
            .ToDictionary(partes => partes[0].Trim(), partes => partes[1].Trim());

        var contrasena = valores.GetValueOrDefault("POSTGRES_PASSWORD");
        if (string.IsNullOrEmpty(contrasena))
            throw new InvalidOperationException("Define POSTGRES_PASSWORD en .env.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = int.Parse(valores.GetValueOrDefault("POSTGRES_PORT") ?? "5432"),
            Database = BaseDatos,
            Username = valores.GetValueOrDefault("POSTGRES_USER") ?? "cad",
            Password = contrasena,
        }.ConnectionString;
    }

    private static string BuscarRaizRepositorio()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cad.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio (Cad.slnx).");
    }
}
