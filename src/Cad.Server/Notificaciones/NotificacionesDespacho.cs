using Cad.Server.Compartido;

namespace Cad.Server.Notificaciones;

public static class NotificacionesDespacho
{
    public static IServiceCollection AddNotificacionesDespacho(this IServiceCollection services, IConfiguration configuracion)
    {
        var credencial = configuracion.GetSection(OpcionesPush.Seccion).Get<OpcionesPush>()?.CredencialFcm;
        if (string.IsNullOrWhiteSpace(credencial))
            services.AddSingleton<IEnviadorPush, EnviadorPushDesactivado>();
        else
            services.AddSingleton<IEnviadorPush>(sp => new EnviadorPushFcm(credencial, sp.GetRequiredService<ILogger<EnviadorPushFcm>>()));

        services.AddSingleton<ColaAvisosDespacho>();
        services.AddHostedService<ServicioAvisosDespacho>();
        services.AddScoped<AvisarDespacho>();
        return services;
    }
}
