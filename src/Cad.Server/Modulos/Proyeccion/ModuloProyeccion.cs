namespace Cad.Server.Modulos.Proyeccion;

// HU48 — proyección de focos de incidencias (S6-T19 a S6-T21). El modelo se carga una vez; la consulta y el parte, por solicitud.
public static class ModuloProyeccion
{
    public static IServiceCollection AddModuloProyeccion(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesProyeccion>(configuracion.GetSection(OpcionesProyeccion.Seccion));
        services.AddMemoryCache();
        services.AddSingleton<ModeloProyeccion>();
        services.AddScoped<ConsultarProyeccion>();
        services.AddScoped<RedactarParte>();
        return services;
    }
}
