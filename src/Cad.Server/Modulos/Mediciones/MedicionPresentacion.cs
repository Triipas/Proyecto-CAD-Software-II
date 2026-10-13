using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Mediciones;

// RNF02 / HU11-CA05 (S1-T18): medición del tiempo desde la aceptación de un SOS por el servidor hasta su
// presentación en el panel de la central. Solo para pruebas (Plan de Arquitectura, 9.1 y 11.5): se activa
// con Medicion:Rnf02:Habilitada y, apagada, no registra nada. NO es el tiempo de respuesta del sereno.
// Ambos instantes los toma el reloj del servidor: no hay desfase entre relojes.
public sealed class MedicionPresentacion
{
    public Guid Id { get; private set; }
    public Guid CasoId { get; private set; }
    public Guid OperadorId { get; private set; }
    public DateTimeOffset AceptadoEn { get; private set; }   // = Caso.RegistradoEn
    public DateTimeOffset PresentadoEn { get; private set; } // llegada del acuse del panel

    private MedicionPresentacion() { } // EF Core

    public static MedicionPresentacion Registrar(Guid casoId, Guid operadorId, DateTimeOffset aceptadoEn, DateTimeOffset presentadoEn) => new()
    {
        Id = Guid.CreateVersion7(),
        CasoId = casoId,
        OperadorId = operadorId,
        AceptadoEn = aceptadoEn,
        PresentadoEn = presentadoEn,
    };
}

public sealed class OpcionesMedicion
{
    public const string Seccion = "Medicion:Rnf02";
    public bool Habilitada { get; set; }
}

public sealed record PresentacionMedida(Guid CasoId, DateTimeOffset AceptadoEn, DateTimeOffset PresentadoEn);

public sealed class RegistrarPresentacion(CadDbContext db, TimeProvider reloj, Microsoft.Extensions.Options.IOptions<OpcionesMedicion> opciones)
{
    // El panel llama aquí cuando el SOS ya se muestra en la sección urgente. Un acuse repetido
    // del mismo operador no crea otra medición (se conserva el primero).
    public async Task EjecutarAsync(Guid casoId, Guid operadorId, CancellationToken ct)
    {
        if (!opciones.Value.Habilitada)
            return;

        var presentadoEn = reloj.GetUtcNow();
        var caso = await db.Casos.AsNoTracking()
            .Where(c => c.Id == casoId && c.Origen == OrigenCaso.Sos)
            .Select(c => new { c.RegistradoEn })
            .SingleOrDefaultAsync(ct);
        if (caso is null || await db.MedicionesPresentacion.AnyAsync(m => m.CasoId == casoId && m.OperadorId == operadorId, ct))
            return;

        db.MedicionesPresentacion.Add(MedicionPresentacion.Registrar(casoId, operadorId, caso.RegistradoEn, presentadoEn));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            // Acuse duplicado simultáneo: ya hay medición para este caso y operador.
        }
    }
}

public sealed class ConsultarPresentaciones(CadDbContext db)
{
    // Primer acuse de cada SOS aceptado desde «desde» (si hubiera varios paneles, cuenta el primero).
    public async Task<IReadOnlyList<PresentacionMedida>> EjecutarAsync(DateTimeOffset desde, CancellationToken ct) =>
        (await db.MedicionesPresentacion.AsNoTracking()
            .Where(m => m.AceptadoEn >= desde)
            .ToListAsync(ct))
        .GroupBy(m => m.CasoId)
        .Select(g => g.OrderBy(m => m.PresentadoEn).First())
        .Select(m => new PresentacionMedida(m.CasoId, m.AceptadoEn, m.PresentadoEn))
        .ToList();
}

public static class ModuloMediciones
{
    public static IServiceCollection AddModuloMediciones(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesMedicion>(configuracion.GetSection(OpcionesMedicion.Seccion));
        services.AddScoped<RegistrarPresentacion>();
        services.AddScoped<ConsultarPresentaciones>();
        return services;
    }

    public static IEndpointRouteBuilder MapModuloMediciones(this IEndpointRouteBuilder app)
    {
        // Resultados para Cad.PruebaCarga. Solo administrador y solo con la medición activada.
        app.MapGet("/api/mediciones/presentacion", async (DateTimeOffset desde, ConsultarPresentaciones consultar,
                Microsoft.Extensions.Options.IOptions<OpcionesMedicion> opciones, CancellationToken ct) =>
                opciones.Value.Habilitada
                    ? Results.Ok(await consultar.EjecutarAsync(desde.ToUniversalTime(), ct))
                    : Results.NotFound())
            .RequireAuthorization(Politicas.Administrador);

        return app;
    }
}
