using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cad.Server.Tests.Infraestructura;

// Servidor CAD real en memoria contra PostgreSQL real (base cad_pruebas, recreada en cada ejecución).
// El entorno «Pruebas» evita cargar los user-secrets de desarrollo.
public sealed class FabricaCad : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string conexion = ConexionPruebas.Obtener();

    public RegistrosCapturados Registros { get; } = new();

    // ADR-07: doble de FCM. Las pruebas nunca contactan con Firebase.
    public PushDePrueba Push { get; } = new();

    // Secretos de prueba generados en cada ejecución; nunca se guardan en el repositorio.
    public string ClaveFirmaJwt { get; } = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public string AdminCorreo { get; } = $"admin.{Guid.NewGuid():N}@cad.test";
    public string AdminClave { get; } = $"Admin-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.UseSetting("ConnectionStrings:Cad", conexion);
        builder.UseSetting("BaseDatos:MigrarAlIniciar", "true");
        builder.UseSetting("Jwt:ClaveFirma", ClaveFirmaJwt);
        builder.UseSetting("Admin:Email", AdminCorreo);
        builder.UseSetting("Admin:ClaveInicial", AdminClave);
        // ADR-08: códigos simulados (nunca SMS reales) y sin límite por IP, que todas las pruebas comparten.
        // El re-registro de números queda desactivado, como en la presentación (DP25).
        builder.UseSetting("Codigos:PermitirSimulado", "true");
        builder.UseSetting("Codigos:MostrarCodigoSimulado", "true");
        builder.UseSetting("Codigos:SolicitudesPorMinutoIp", "100000");
        builder.UseSetting("Codigos:Twilio:AccountSid", "");
        builder.UseSetting("Cuentas:PermitirReRegistroCelular", "false");
        builder.ConfigureTestServices(servicios => servicios.AddSingleton<Cad.Server.Compartido.IEnviadorPush>(Push));
        builder.ConfigureLogging(registro =>
        {
            registro.AddProvider(Registros);
            registro.AddFilter<RegistrosCapturados>(null, LogLevel.Trace);
        });
    }

    public async Task InitializeAsync()
    {
        await using (var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).Options))
            await db.Database.EnsureDeletedAsync();

        _ = Server; // arranca el servidor, que aplica las migraciones

        // Protección: las pruebas borran datos, así que nunca deben apuntar a otra base.
        var usada = await ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()));
        if (new NpgsqlConnectionStringBuilder(usada).Database != ConexionPruebas.BaseDatos)
            throw new InvalidOperationException($"El servidor de pruebas no usa la base {ConexionPruebas.BaseDatos}.");
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public async Task<T> ConBaseDatosAsync<T>(Func<CadDbContext, Task<T>> consulta)
    {
        using var alcance = Services.CreateScope();
        return await consulta(alcance.ServiceProvider.GetRequiredService<CadDbContext>());
    }

    public async Task<T> ConServiciosAsync<T>(Func<IServiceProvider, Task<T>> consulta)
    {
        using var alcance = Services.CreateScope();
        return await consulta(alcance.ServiceProvider);
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionServidor : ICollectionFixture<FabricaCad>
{
    public const string Nombre = "Servidor CAD";
}
