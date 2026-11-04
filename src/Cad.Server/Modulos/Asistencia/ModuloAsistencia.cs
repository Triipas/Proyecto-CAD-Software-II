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
        services.AddHttpClient<AsistenteGroq>();
        services.AddSingleton<AsistenteReglas>();
        services.AddSingleton<AsistenteNulo>();
        // El proveedor se elige al pedirlo: las pruebas pueden cambiar la configuración o reemplazar el asistente.
        services.AddScoped<IAsistenteReportes>(sp =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesIa>>().Value;
            if (!opciones.Habilitada)
                return sp.GetRequiredService<AsistenteNulo>();
            var principal = Proveedor(sp, opciones.Proveedor);
            if (!principal.Disponible || string.IsNullOrWhiteSpace(opciones.ProveedorVerificador)
                || string.Equals(opciones.ProveedorVerificador, opciones.Proveedor, StringComparison.OrdinalIgnoreCase))
                return principal;
            var verificador = Proveedor(sp, opciones.ProveedorVerificador);
            return verificador.Disponible ? new AsistenteConVerificador(principal, verificador) : principal;
        });

        services.AddSingleton<ColaAnalisis>();
        services.AddSingleton<IColaAnalisisReportes>(sp => sp.GetRequiredService<ColaAnalisis>());
        services.AddHostedService<ServicioAnalisis>();
        services.AddScoped<AnalizarReporte>();
        services.AddScoped<BuscarDuplicados>();
        services.AddScoped<IRecuperadorEjemplos, RecuperarEjemplos>();
        services.AddScoped<EvaluarAdmision>();
        services.AddScoped<AdmitirAutomaticamente>();
        services.AddHostedService<ServicioAdmisionAutomatica>();
        services.AddScoped<CambiarAjusteIa>();
        services.AddScoped<ConsultarAsistencia>();
        return services;
    }

    // Un proveedor sin clave no está disponible: se avisa una vez por solicitud y no hay sugerencias (o no hay verificación).
    private static IAsistenteReportes Proveedor(IServiceProvider sp, string nombre)
    {
        IAsistenteReportes asistente = nombre.ToLowerInvariant() switch
        {
            "reglas" => sp.GetRequiredService<AsistenteReglas>(),
            "groq" => sp.GetRequiredService<AsistenteGroq>(),
            _ => sp.GetRequiredService<AsistenteGemini>(),
        };
        if (!asistente.Disponible)
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ModuloAsistencia))
                .LogWarning("Ia: el proveedor {Proveedor} no tiene clave de API configurada (Ia:{Proveedor}:ClaveApi).", nombre, nombre);
        return asistente.Disponible ? asistente : sp.GetRequiredService<AsistenteNulo>();
    }
}

// Análisis con un proveedor y segunda opinión con otro (Ia:ProveedorVerificador).
public sealed class AsistenteConVerificador(IAsistenteReportes principal, IAsistenteReportes verificador) : IAsistenteReportes
{
    public bool Disponible => principal.Disponible;

    public Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct) => principal.AnalizarAsync(entrada, ct);

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) => verificador.VerificarAsync(entrada, ct);
}
