using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento;
using Cad.Server.Modulos.Casos;
using Cad.Server.Modulos.Cuentas;
using Cad.Server.Modulos.Mediciones;
using Cad.Server.Modulos.Serenos;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Notificaciones;
using Cad.Server.Persistencia;
using Cad.Server.TiempoReal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// La cadena de conexión nunca va en el repositorio: user-secrets en desarrollo
// y variable de entorno ConnectionStrings__Cad en el entorno publicado.
var conexion = builder.Configuration.GetConnectionString("Cad")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Cad'. Consulta la sección «Base de datos local» del README.");

// Sin Kerberos: Neon y el PostgreSQL local autentican con usuario y contraseña (TLS en Neon). Sin esto, Npgsql
// intenta cifrado GSSAPI y la imagen (sin libgssapi) escribe errores fuera del registro estructurado.
conexion = new Npgsql.NpgsqlConnectionStringBuilder(conexion) { GssEncryptionMode = Npgsql.GssEncryptionMode.Disable }.ConnectionString;

builder.Services.AddDbContext<CadDbContext>(opciones => opciones.UseNpgsql(conexion));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

// /salud indica solo que el proceso responde; no consulta la base de datos para no despertar
// PostgreSQL en cada sondeo (Plan de Arquitectura, ADR-05). /salud/lista sí la comprueba.
builder.Services.AddHealthChecks().AddDbContextCheck<CadDbContext>("base-de-datos", tags: ["lista"]);

builder.Services.AddDataProtection().PersistKeysToDbContext<CadDbContext>().SetApplicationName("cad");

// Detrás del proxy de Render (que termina el TLS): esquema e IP originales desde X-Forwarded-*.
// Solo se activa con Despliegue:DetrasDeProxy, para no aceptar cabeceras falsificadas fuera de ese entorno.
var detrasDeProxy = builder.Configuration.GetValue<bool>("Despliegue:DetrasDeProxy");
if (detrasDeProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(opciones =>
    {
        opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Las IP del proxy de Render no son fijas; el servicio solo es accesible a través de él.
        opciones.KnownIPNetworks.Clear();
        opciones.KnownProxies.Clear();
    });
}

builder.Services.AddAutenticacionCad(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAutorizacionCad();
builder.Services.AddRazorPages(opciones => opciones.RootDirectory = "/Panel/Pages");
// La cookie antifalsificación de los formularios del panel sigue la misma política que la de sesión:
// Secure fuera de desarrollo (RNF09).
builder.Services.AddAntiforgery(opciones => opciones.Cookie.SecurePolicy =
    builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
// El panel está en español: no convertir tildes ni «ñ» en entidades HTML.
// <, >, & y comillas se siguen codificando, así que la protección frente a XSS no cambia.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(opciones =>
    opciones.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddModuloCuentas(builder.Configuration);
builder.Services.AddModuloCasos();
builder.Services.AddModuloMediciones(builder.Configuration);
builder.Services.AddModuloSerenos(builder.Configuration);
builder.Services.AddModuloAcompanamiento(builder.Configuration);

// Tiempo real (HU11): eventos a la central después de cada confirmación.
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificador, NotificadorSignalR>();

// HU21 (ADR-07): notificación push del despacho. Sin credencial configurada no se envía ninguna push.
builder.Services.AddNotificacionesDespacho(builder.Configuration);

var app = builder.Build();

// Una sola instancia del servidor: las migraciones se aplican al arrancar (Plan de Arquitectura, 4).
if (app.Configuration.GetValue<bool>("BaseDatos:MigrarAlIniciar"))
{
    using var alcance = app.Services.CreateScope();
    await alcance.ServiceProvider.GetRequiredService<CadDbContext>().Database.MigrateAsync();
    await alcance.ServiceProvider.GetRequiredService<SembrarAdministrador>().EjecutarAsync();
}

if (detrasDeProxy)
{
    app.UseForwardedHeaders();
    // RNF09: ninguna solicitud se atiende por HTTP sin cifrar en el entorno publicado. Excepción: los
    // sondeos /salud, que Render hace por HTTP interno y no llevan datos.
    app.UseWhen(contexto => !contexto.Request.Path.StartsWithSegments("/salud"), rama => rama.UseHttpsRedirection());
}
if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Hojas de estilo, scripts e iconos del panel (wwwroot); no requieren sesión.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
// ADR-08: límite por IP de las rutas de ingreso con celular (después de la autenticación, antes de los endpoints).
app.UseRateLimiter();

app.MapHealthChecks("/salud", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/salud/lista", new HealthCheckOptions { Predicate = c => c.Tags.Contains("lista") }).AllowAnonymous();
app.MapModuloCuentas();
app.MapModuloCasos();
app.MapModuloMediciones();
app.MapModuloSerenos();
app.MapModuloAcompanamiento();
app.MapHub<CentralHub>(CentralHub.Ruta);
app.MapHub<SerenoHub>(SerenoHub.Ruta);
app.MapRazorPages();

app.Run();
