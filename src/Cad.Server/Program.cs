var builder = WebApplication.CreateBuilder(args);

// /salud indica solo que el proceso responde; no consulta la base de datos
// para no despertar PostgreSQL en cada sondeo (Plan de Arquitectura, ADR-05).
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/salud");

app.Run();
