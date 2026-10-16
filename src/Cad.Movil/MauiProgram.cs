using Cad.Movil.Servicios;
using Cad.Movil.Servicios.Diagnostico;
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
        builder.Services.AddSingleton<IAlmacenLocal, AlmacenLocalMaui>();

        // Lógica de la app.
        builder.Services.AddSingleton<EmisorSos>();
        builder.Services.AddSingleton<PreparacionUbicacion>();
        builder.Services.AddTransient<RegistradorReporte>(); // un envío pendiente por formulario

        // Pantallas y modelos de vista.
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<IngresoVistaModelo>();
        builder.Services.AddTransient<RegistroVistaModelo>();
        builder.Services.AddSingleton<ServicioAcompanamiento>(); // la sesión y la transmisión sobreviven al cambio de pantalla
        builder.Services.AddTransient(s => new InicioCiudadanoVistaModelo(
            s.GetRequiredService<EmisorSos>(), s.GetRequiredService<PreparacionUbicacion>(),
            s.GetRequiredService<IAlmacenSesion>(), s.GetRequiredService<INavegacion>(), AppInfo.Current.ShowSettingsUI,
            s.GetRequiredService<ServicioBandeja>(), s.GetRequiredService<ServicioAcompanamiento>()));
        builder.Services.AddTransient(s => new AcompanamientoVistaModelo(s.GetRequiredService<ServicioAcompanamiento>(),
            s.GetRequiredService<IServicioUbicacion>(), s.GetRequiredService<INavegacion>(), AppInfo.Current.ShowSettingsUI,
            s.GetRequiredService<TimeProvider>()));
        builder.Services.AddTransient<AcompanamientoPagina>();
        builder.Services.AddTransient(s => new ReporteVistaModelo(
            s.GetRequiredService<IApiCad>(), s.GetRequiredService<RegistradorReporte>(), s.GetRequiredService<IServicioUbicacion>(),
            s.GetRequiredService<IAlmacenSesion>(), s.GetRequiredService<INavegacion>(), s.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ServicioTurnoSereno>();
        builder.Services.AddSingleton<ICanalSereno, CanalSerenoSignalR>();
#if ANDROID
        builder.Services.AddSingleton<IServicioPush, Platforms.Android.ServicioPushAndroid>();
#else
        builder.Services.AddSingleton<IServicioPush, ServicioPushNoDisponible>();
#endif
        builder.Services.AddSingleton<ServicioNotificaciones>();
        builder.Services.AddSingleton<ServicioDespacho>();
        builder.Services.AddTransient(s => new InicioSerenoVistaModelo(
            s.GetRequiredService<ServicioTurnoSereno>(), s.GetRequiredService<ServicioDespacho>(), s.GetRequiredService<ServicioNotificaciones>(),
            s.GetRequiredService<IAlmacenSesion>(),
            s.GetRequiredService<INavegacion>(), AppInfo.Current.ShowSettingsUI));
        builder.Services.AddTransient<IngresoPagina>();
        builder.Services.AddTransient<RegistroPagina>();
        builder.Services.AddTransient<InicioCiudadanoPagina>();
        builder.Services.AddTransient<ReportePagina>();
        builder.Services.AddTransient(s => new MisCasosVistaModelo(s.GetRequiredService<IApiCad>(), s.GetRequiredService<IAlmacenSesion>(),
            s.GetRequiredService<INavegacion>(), s.GetRequiredService<TimeProvider>()));
        builder.Services.AddTransient(s => new MiCasoVistaModelo(s.GetRequiredService<IApiCad>(), s.GetRequiredService<IAlmacenSesion>(),
            s.GetRequiredService<INavegacion>(), s.GetRequiredService<TimeProvider>()));
        builder.Services.AddTransient<MisCasosPagina>();
        builder.Services.AddSingleton<ServicioBandeja>();
        builder.Services.AddTransient(s => new NotificacionesVistaModelo(s.GetRequiredService<IApiCad>(), s.GetRequiredService<IAlmacenSesion>(),
            s.GetRequiredService<INavegacion>(), s.GetRequiredService<TimeProvider>()));
        builder.Services.AddTransient<NotificacionesPagina>();
        builder.Services.AddTransient<MiCasoPagina>();
        builder.Services.AddTransient<InicioSerenoPagina>();

        builder.Services.AddSingleton<EstadoAppMaui>();
#if DEBUG
        builder.Logging.AddDebug();

        // SPIKE01 (S1-T03): sonda de diagnóstico de ubicación, solo en Debug.
        builder.Services.AddSingleton<IEstadoApp>(s => s.GetRequiredService<EstadoAppMaui>());
        builder.Services.AddSingleton<IEstadoRed, EstadoRedMaui>();
        builder.Services.AddSingleton<IComprobadorServidor, ComprobadorServidorHttp>();
        builder.Services.AddSingleton<IRegistroSonda, RegistroSondaArchivo>();
        builder.Services.AddSingleton<SondaUbicacion>();
        builder.Services.AddSingleton<DiagnosticoVistaModelo>();
        builder.Services.AddTransient<DiagnosticoPagina>();
#endif
        return builder.Build();
    }
}
