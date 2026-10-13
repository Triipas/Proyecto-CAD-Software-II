using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Hosting;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Pruebas");
        builder.UseSetting("ConnectionStrings:Cad", conexion);
        builder.UseSetting("BaseDatos:MigrarAlIniciar", "true");
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
