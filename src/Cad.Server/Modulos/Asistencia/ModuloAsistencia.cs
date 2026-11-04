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
            return verificador.Disponible
                ? new AsistenteConVerificador(principal, verificador, TimeSpan.FromSeconds(Math.Max(1, opciones.TiempoMaximoSegundos)),
                    sp.GetRequiredService<ILogger<AsistenteConVerificador>>())
                : principal;
        });

        // HU46 (S6-T21): el modelo de lenguaje para redactar (el parte de patrullaje), del proveedor configurado.
        services.AddScoped<IModeloLenguaje>(sp =>
        {
            var opciones = sp.GetRequiredService<IOptions<OpcionesIa>>().Value;
            IModeloLenguaje? modelo = !opciones.Habilitada ? null : opciones.Proveedor.ToLowerInvariant() switch
            {
                "groq" => sp.GetRequiredService<AsistenteGroq>(),
                "gemini" => sp.GetRequiredService<AsistenteGemini>(),
                _ => null,
            };
            return modelo is { Disponible: true } ? modelo : new ModeloLenguajeNulo();
        });

        services.AddSingleton<ColaAnalisis>();
        services.AddSingleton<IColaAnalisisReportes>(sp => sp.GetRequiredService<ColaAnalisis>());
        services.AddHostedService<ServicioAnalisis>();
        services.AddScoped<AnalizarReporte>();
        services.AddScoped<BuscarDuplicados>();
        services.AddScoped<ArmarEntrada>();
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

// Sin IA (apagada, por reglas o sin clave): nadie redacta y se usan las plantillas.
public sealed class ModeloLenguajeNulo : IModeloLenguaje
{
    public bool Disponible => false;

    public Task<RespuestaModelo?> CompletarJsonAsync(string sistema, string usuario, System.Text.Json.Nodes.JsonObject esquema, CancellationToken ct) =>
        Task.FromResult<RespuestaModelo?>(null);
}

// Análisis con un proveedor y segunda opinión con otro (Ia:ProveedorVerificador). Si el otro no responde a tiempo (el 04/11/2026
// Gemini respondió 503 o no respondió en el nivel gratuito), la segunda opinión la da el modelo preciso del proveedor principal con
// el prompt del segundo revisor, como en el diseño original del plan (5.3).
public sealed class AsistenteConVerificador(IAsistenteReportes principal, IAsistenteReportes verificador, TimeSpan limiteVerificador,
    ILogger<AsistenteConVerificador> registro) : IAsistenteReportes
{
    public bool Disponible => principal.Disponible;

    public Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct) => principal.AnalizarAsync(entrada, ct);

    public async Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct)
    {
        using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limite.CancelAfter(limiteVerificador);
            try
            {
                if (await verificador.VerificarAsync(entrada, limite.Token) is { } segunda)
                    return segunda;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registro.LogWarning("Reporte {CasoId}: el proveedor de la segunda opinión no respondió ({Error}); la da el proveedor principal.",
                    entrada.CasoId, ex.GetType().Name);
            }
        }
        return await principal.VerificarAsync(entrada, ct);
    }
}
