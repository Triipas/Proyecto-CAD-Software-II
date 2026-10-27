namespace Cad.Server.Modulos.Analitica;

// Analítica de la central (27/10/2026): cálculos para el dashboard del analista, sobre los datos que ya registran los demás
// módulos. Solo lectura; no guarda nada. Todos los resultados son agregados: ninguno lleva datos de un caso, un ciudadano o
// un sereno concreto.
public sealed class OpcionesAnalitica
{
    public const string Seccion = "Analitica";

    // Cobertura (bloque B): una zona está cubierta en un momento si hay un sereno disponible a esta distancia o menos de su
    // centro. Es un parámetro del análisis, no una regla del servicio; 800 m es una distancia de llegada razonable en moto.
    public double RadioCoberturaMetros { get; set; } = 800;

    // Cada cuántos minutos se revisa la cobertura en el periodo, y hasta qué antigüedad la última posición de un sereno cuenta
    // como su ubicación (con una más antigua, no se sabe dónde está y no cubre ninguna zona).
    public int MuestreoMinutos { get; set; } = 5;
    public int AntiguedadPosicionMinutos { get; set; } = 10;

    // Privacidad: una zona con menos casos que este mínimo no se muestra sola (se suma al «resto»), para que no se pueda
    // reconocer un caso concreto por su lugar.
    public int MinimoCasosPorZona { get; set; } = 3;

    // Puntos críticos (bloque C): cuántas incidencias y a qué distancia entre ellas forman un punto que se repite.
    public double RadioPuntoCriticoMetros { get; set; } = 100;
    public int MinimoCasosPuntoCritico { get; set; } = 3;

    // Zonas en alza (bloque C): cuántas desviaciones sobre el promedio de las semanas anteriores son una alerta.
    public double UmbralAlza { get; set; } = 2;
}

public static class ModuloAnalitica
{
    public static IServiceCollection AddModuloAnalitica(this IServiceCollection services, IConfiguration configuracion)
    {
        services.Configure<OpcionesAnalitica>(configuracion.GetSection(OpcionesAnalitica.Seccion));
        services.AddScoped<ConsultarDemandaYOferta>();
        services.AddScoped<ConsultarCobertura>();
        services.AddScoped<ConsultarRespuestaPorZona>();
        services.AddScoped<ConsultarPuntosCriticos>();
        services.AddScoped<ConsultarZonasEnAlza>();
        services.AddScoped<ConsultarReportesAnalitica>();
        services.AddScoped<ConsultarPercepcion>();
        return services;
    }
}
