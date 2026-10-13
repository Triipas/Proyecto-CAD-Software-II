using Cad.Movil.Servicios;
using Cad.Movil.Vistas;
using Cad.Movil.VistasModelo;
using Microsoft.Extensions.Logging;

namespace Cad.Movil;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        // El campo ya tiene marco propio (estilo MarcoCampo): sin el subrayado nativo de Android.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SinSubrayado", (manejador, _) =>
            manejador.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#endif

        // Servicios de la plataforma.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IConfiguracionServidor, ConfiguracionServidorMaui>();
        builder.Services.AddSingleton<IServicioUbicacion, ServicioUbicacionMaui>();
        builder.Services.AddSingleton<IAlmacenSesion, AlmacenSesionSeguro>();
        builder.Services.AddSingleton<INavegacion, NavegacionShell>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<IApiCad, ApiCad>();

        // Lógica de la app.
        builder.Services.AddSingleton<EmisorSos>();
        builder.Services.AddSingleton<PreparacionUbicacion>();

        // Pantallas y modelos de vista.
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<IngresoVistaModelo>();
        builder.Services.AddTransient<RegistroVistaModelo>();
        builder.Services.AddTransient(s => new InicioCiudadanoVistaModelo(
            s.GetRequiredService<EmisorSos>(), s.GetRequiredService<PreparacionUbicacion>(),
            s.GetRequiredService<IAlmacenSesion>(), s.GetRequiredService<INavegacion>(), AppInfo.Current.ShowSettingsUI));
        builder.Services.AddTransient<InicioSerenoVistaModelo>();
        builder.Services.AddTransient<IngresoPagina>();
        builder.Services.AddTransient<RegistroPagina>();
        builder.Services.AddTransient<InicioCiudadanoPagina>();
        builder.Services.AddTransient<InicioSerenoPagina>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
