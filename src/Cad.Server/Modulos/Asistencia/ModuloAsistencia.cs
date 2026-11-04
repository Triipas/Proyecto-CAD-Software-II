using Cad.Server.Compartido;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Asistencia;

// Asistencia con IA para reportes (HU44, HU45; plan de asistencia con IA, 5.1). Con Ia:Habilitada = false (por defecto) no hay
// asistente y nada cambia: el registro de un reporte nunca espera a la IA ni depende de ella.
public static class ModuloAsistencia
{
    public static IServiceCollection AddModuloAsistencia(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesIa>(configuracion.GetSection(OpcionesIa.Seccion));
        services.AddSingleton<ControlConsumoIa>();
        services.AddHttpClient<AsistenteGemini>();
        services.AddSingleton<AsistenteReglas>();
        services.AddSingleton<AsistenteNulo>();
        // El proveedor se elige al pedirlo: las pruebas pueden cambiar la configuración o reemplazar el asistente.
        services.AddScoped<IAsistenteReportes>(sp =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesIa>>().Value;
            if (!opciones.Habilitada)
                return sp.GetRequiredService<AsistenteNulo>();
            if (string.Equals(opciones.Proveedor, ProveedoresIa.Reglas, StringComparison.OrdinalIgnoreCase))
                return sp.GetRequiredService<AsistenteReglas>();
            var gemini = sp.GetRequiredService<AsistenteGemini>();
            if (gemini.Disponible)
                return gemini;
            sp.GetRequiredService<ILogger<AsistenteGemini>>().LogWarning(
                "Ia:Habilitada está activa pero falta Ia:Gemini:ClaveApi: no hay asistente.");
            return sp.GetRequiredService<AsistenteNulo>();
        });

        services.AddSingleton<ColaAnalisis>();
        services.AddSingleton<IColaAnalisisReportes>(sp => sp.GetRequiredService<ColaAnalisis>());
        services.AddHostedService<ServicioAnalisis>();
        services.AddScoped<AnalizarReporte>();
        services.AddScoped<BuscarDuplicados>();
        services.AddScoped<CambiarAjusteIa>();
        services.AddScoped<ConsultarAsistencia>();
        return services;
    }
}
