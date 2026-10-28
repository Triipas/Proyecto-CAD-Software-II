using System.Text.Json;

namespace Cad.CargadorDataset;

// Lectura de los archivos de salida/ del generador (formato en el README del dataset). Los identificadores del dataset
// (CIU-0001, SER-01, OPE-1, CASO-0001, SES-001) solo sirven para relacionar los archivos: en la base de datos todo recibe un
// Guid nuevo.
public sealed record Dataset(Personas Personas, List<CasoDataset> Casos, List<SesionDataset> Sesiones, List<TurnoDataset> Turnos,
    Dictionary<string, List<DateOnly>> Actividad, DateTimeOffset FinDelPeriodo, List<ViaDataset> Vias)
{
    private static readonly JsonSerializerOptions Opciones = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static Dataset Leer(string carpeta)
    {
        T Archivo<T>(string ruta) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(carpeta, ruta)), Opciones)
            ?? throw new InvalidDataException($"No se pudo leer {ruta}.");
        var meta = Archivo<JsonElement>("salida/meta.json");
        return new(Archivo<Personas>("salida/personas.json"), Archivo<List<CasoDataset>>("salida/casos.json"),
            Archivo<List<SesionDataset>>("salida/acompanamientos.json"), Archivo<List<TurnoDataset>>("salida/turnos.json"),
            Archivo<Dictionary<string, List<DateOnly>>>("salida/actividad.json"),
            meta.GetProperty("fin_del_periodo").GetDateTimeOffset(), Archivo<List<ViaDataset>>("fuentes/vias.json"));
    }
}

public sealed record Personas(List<CiudadanoDataset> Ciudadanos, List<SerenoDataset> Serenos, List<OperadorDataset> Operadores);

public sealed record CiudadanoDataset(string Id, string Nombres, string Apellidos, string Telefono, DateTimeOffset RegistradoEn, PerfilDataset? Perfil);

public sealed record PerfilDataset(string? TipoDocumento, string? NumeroDocumento, DateOnly? FechaNacimiento, string? Direccion,
    string? ContactoNombre, string? ContactoParentesco, string? ContactoTelefono);

public sealed record SerenoDataset(string Id, string Nombres, string Apellidos, string Correo, string Telefono, string Turno, int Sector);

public sealed record OperadorDataset(string Id, string Nombres, string Apellidos, string Correo, string Turno);

public sealed record TurnoDataset(string Sereno, DateOnly Dia, string Turno, int Sector);

public sealed record ViaDataset(string Nombre, List<double[]> Puntos);

public sealed record CasoDataset(string Id, string Origen, Guid CategoriaInicial, string? Ciudadano, DateTimeOffset RegistradoEn, int Sector,
    UbicacionDataset? Ubicacion, string? Acompanamiento, DateTimeOffset? PresentadoEnCentral, DateTimeOffset? AbiertoEnCentral,
    List<IntentoDataset>? IntentosContacto, ClasificacionDataset? Clasificacion, ReporteDataset? Reporte, List<EventoDataset> Eventos,
    string Estado, EvaluacionDataset? Evaluacion);

public sealed record UbicacionDataset(double Latitud, double Longitud, double PrecisionM);

public sealed record IntentoDataset(DateTimeOffset En, string Operador, string Resultado, string? Referencia);

public sealed record ClasificacionDataset(Guid Categoria, string Prioridad, string Heridos);

public sealed record ReporteDataset(string Descripcion, DateTimeOffset OcurridoAproximadamenteEn, string Canal, string? RegistradoPorOperador,
    ContactoDataset? Contacto, UbicacionHechoDataset Ubicacion, int Fotos);

public sealed record ContactoDataset(string? Nombre, string? Telefono);

public sealed record UbicacionHechoDataset(string Fuente, double? Latitud, double? Longitud, double? PrecisionM, string? Referencia);

public sealed record EventoDataset(string Tipo, DateTimeOffset En, string Actor, string? Sereno, string? Motivo, string? Resultado,
    InicioDataset? Inicio, CierreDataset? Cierre, DecisionDataset? Decision, CorreccionDataset? Correccion, DescarteDataset? Descarte,
    CancelacionDataset? Cancelacion);

public sealed record InicioDataset(double? DistanciaM, bool? DentroDelRango, bool RegistradoPorCentral);

public sealed record CierreDataset(string Resultado, string ResultadoPublico, Guid Categoria, string? Destino);

public sealed record DecisionDataset(string Tipo, string Texto, string? Motivo, string? Destino, string? Accion);

public sealed record CorreccionDataset(Guid Anterior, Guid Nueva);

public sealed record DescarteDataset(string Motivo, string? Detalle);

public sealed record CancelacionDataset(string Motivo);

public sealed record EvaluacionDataset(int Puntuacion, List<string> Motivos, DateTimeOffset EvaluadaEn, string? Comentario);

public sealed record SesionDataset(string Id, string Ciudadano, DateTimeOffset IniciadaEn, DateTimeOffset VenceEn, DateTimeOffset CerradaEn,
    string Estado, List<PosicionDataset> Posiciones, List<DateTimeOffset> ConfirmacionesBienestar, List<AvisoDataset> Avisos);

public sealed record PosicionDataset(DateTimeOffset En, double Latitud, double Longitud, double PrecisionM);

public sealed record AvisoDataset(string Regla, DateTimeOffset GeneradoEn, string Estado, DateTimeOffset ResueltoEn, string Actuacion, string? Caso);
