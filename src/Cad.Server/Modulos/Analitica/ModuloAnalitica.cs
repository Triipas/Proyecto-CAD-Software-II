namespace Cad.Server.Modulos.Analitica;

// Analítica de la central (27/10/2026): cálculos para el dashboard del analista, sobre los datos que ya registran los demás
// módulos. Solo lectura; no guarda nada. Los resultados son agregados: ninguno lleva datos de un caso o un ciudadano concreto.
// La única excepción es «Rendimiento de los serenos» (03/11/2026, a pedido del usuario), que nombra a cada sereno.
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

    // Casos atascados (bloque E): minutos que un caso puede llevar en cada etapa antes de marcarse. Parámetros provisionales hasta
    // que el PO fije los tiempos objetivo del servicio.
    public int UmbralSosPendienteMinutos { get; set; } = 3;
    public int UmbralReporteAdmitidoMinutos { get; set; } = 30;
    public int UmbralAsignadaMinutos { get; set; } = 5;
    public int UmbralEnCaminoMinutos { get; set; } = 20;
    public int UmbralEnAtencionMinutos { get; set; } = 60;
    public int UmbralRecibidoMinutos { get; set; } = 15;
    public int UmbralEnRevisionMinutos { get; set; } = 30;

    // Dashboard (27/10/2026): el objetivo de respuesta a un SOS que dibuja la regla, provisional hasta que el PO lo fije, y desde
    // cuántos casos se dan medianas y p90 (con menos, se muestra cada caso: un percentil de tres casos engaña).
    public int ObjetivoRespuestaSosMinutos { get; set; } = 15;
    public int MinimoParaPercentiles { get; set; } = 5;

    // «Reasignaciones» (29/10/2026): desde qué porcentaje de casos asignados reasignados conviene mirarlo. Provisional hasta que
    // el PO fije el objetivo del servicio.
    public double ReferenciaReasignadosPorcentaje { get; set; } = 10;

    // «Rendimiento de los serenos» (03/11/2026): cuándo conviene mirar a un sereno frente al equipo. Provisionales hasta que el PO
    // fije los objetivos del servicio. Llega lento: su mediana de llegada supera la del equipo en este factor. Calificación baja:
    // su promedio queda por debajo del equipo por estos puntos. Inicia lejos: su porcentaje de inicios fuera del rango supera el
    // del equipo en este factor y, además, la referencia (la misma de «Salud del sistema»). Solo la referencia marcaba a la
    // mayoría del equipo con el dataset de demostración (12 % fuera de rango en el equipo): una señal que marca a casi todos no sirve.
    public double ToleranciaLlegadaEquipo { get; set; } = 1.25;
    public double ToleranciaCalificacionEquipo { get; set; } = 0.5;
    public double ToleranciaFueraDeRangoEquipo { get; set; } = 1.5;
    public double ReferenciaFueraDeRangoPorcentaje { get; set; } = 10;
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
        services.AddSingleton<RegistroActividadCiudadano>();
        services.AddScoped<ConsultarEvaluaciones>();
        services.AddScoped<ConsultarAdopcion>();
        services.AddScoped<MarcasCentral>();
        services.AddScoped<ConsultarSaludSistema>();
        services.AddScoped<ConsultarReasignaciones>();
        services.AddScoped<ConsultarRendimientoSerenos>();
        services.AddScoped<ConsultarCasosAtascados>();
        services.AddScoped<ConsultarDemandaEsperada>();
        services.AddScoped<GenerarInformePeriodo>();
        services.AddScoped<ConsultarTiemposPorEtapa>();
        services.AddScoped<ConsultarEmbudoReportes>();
        services.AddScoped<ConsultarIndiceFalsos>();
        services.AddScoped<ConsultarAhora>();
        services.AddMemoryCache();
        services.AddSingleton<CacheAnalitica>();
        return services;
    }
}
