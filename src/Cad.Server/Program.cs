using Cad.Server.Modulos.Cuentas;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// La cadena de conexión nunca va en el repositorio: user-secrets en desarrollo
// y variable de entorno ConnectionStrings__Cad en el entorno publicado.
var conexion = builder.Configuration.GetConnectionString("Cad")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Cad'. Consulta la sección «Base de datos local» del README.");

builder.Services.AddDbContext<CadDbContext>(opciones => opciones.UseNpgsql(conexion));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

// /salud indica solo que el proceso responde; no consulta la base de datos
// para no despertar PostgreSQL en cada sondeo (Plan de Arquitectura, ADR-05).
builder.Services.AddHealthChecks();

builder.Services.AddModuloCuentas();

var app = builder.Build();

// Una sola instancia del servidor: las migraciones se aplican al arrancar (Plan de Arquitectura, 4).
if (app.Configuration.GetValue<bool>("BaseDatos:MigrarAlIniciar"))
{
    using var alcance = app.Services.CreateScope();
    await alcance.ServiceProvider.GetRequiredService<CadDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/salud");
app.MapModuloCuentas();

app.Run();
