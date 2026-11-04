using System.Globalization;
using System.Text;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Modulos.Asistencia.Adaptadores;

// Plan de asistencia con IA, 5.1 y 9.2 — asistente por palabras clave, sin red. Sirve para tres cosas: demostrar el flujo sin
// clave de API (Pista A), ser la línea base contra la que se mide el modelo y responder de forma determinista en las pruebas.
// Es deliberadamente simple: no confirma duplicados (eso exige comparar redacciones, 6.2) ni ofrece una segunda opinión.
public sealed class AsistenteReglas : IAsistenteReportes
{
    public const string Modelo = "reglas-v1";

    public bool Disponible => true;

    public Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, CancellationToken ct) => Task.FromResult<ResultadoAsistente?>(null);

    public Task<ResultadoAsistente?> AnalizarAsync(EntradaAnalisis entrada, CancellationToken ct) =>
        Task.FromResult<ResultadoAsistente?>(new ResultadoAsistente(Analizar(entrada), Modelo));

    // Raíces sin tildes: el texto se normaliza (minúsculas, sin tildes) antes de buscar.
    private static readonly Dictionary<Guid, string[]> PalabrasPorCategoria = new()
    {
        [Id(0)] = ["mi esposo", "mi pareja", "mi marido", "mi conviviente", "le pega a su", "golpea a su", "maltrat", "violencia familiar", "agrede a su", "pegando a su"],
        [Id(1)] = ["robo", "robar", "robaron", "arrebat", "hurto", "asalt", "quitar", "celular", "telefono", "cartera", "billetera", "carter", "ladron", "choro", "raqueteo", "bolso"],
        [Id(2)] = ["droga", "marihuana", "fumon", "pasta basica", "venden drogas", "vendiendo", "microcomercializ", "fumando", "terokal", "pbc"],
        [Id(4)] = ["acoso", "acosa", "me sigue", "me siguio", "tocamiento", "toco", "manose", "piropo", "exhibicion", "persigu"],
        [Id(5)] = ["extorsi", "cupo", "amenaz", "carta extorsiva", "me cobran", "chalequ", "gota a gota"],
        [Id(6)] = ["tomando", "chupando", "borracho", "ebrio", "cerveza", "licor", "alcohol", "chupe"],
        [Id(7)] = ["pelea", "peleando", "riña", "rina", "trompead", "se agarraron", "golpes", "pandilla", "bronca"],
        [Id(8)] = ["sospechos", "merode", "vehiculo abandonado", "auto abandonado", "rondando", "mirando las casas", "extrano", "desconocido"],
        [Id(9)] = ["vandal", "pintas", "grafiti", "destroz", "rompieron", "lunas rotas", "dañaron", "danaron", "mobiliario"],
        [Id(10)] = ["choque", "chocaron", "accidente", "atropell", "volcadura", "colision", "choco"],
        [Id(11)] = ["ruido", "bulla", "musica alta", "escandalo", "fiesta", "parlante", "gritos toda la noche"],
        [Id(12)] = ["perdido", "perdida", "desorientad", "adulto mayor", "menor solo", "nino solo", "extraviad", "abandonado en la calle"],
    };

    // Hechos fuera del alcance de seguridad ciudadana (CategoriaIncidencia: incendios y limpieza pública; infraestructura vial).
    private static readonly (string Destino, string[] Palabras)[] FueraDeAlcance =
    [
        (DestinosDerivacion.Bomberos, ["incendio", "fuego", "humo", "se quema", "quemandose", "llamas"]),
        (DestinosDerivacion.Municipalidad, ["basura", "desmonte", "bache", "semaforo", "alumbrado", "poste caido", "fuga de agua", "desague", "buzon abierto", "pista rota"]),
    ];

    private static readonly string[] Armas = ["arma", "pistola", "revolver", "cuchillo", "navaja", "machete", "disparo", "balacera", "baleado", "apuñal", "apunal"];
    private static readonly string[] Agresion = ["golpeando", "pegando", "esta golpeando", "le estan pegando", "ayuda", "auxilio", "sangr", "la va a matar", "lo va a matar"];
    private static readonly string[] EnCurso = ["ahora", "ahorita", "en este momento", "esta pasando", "rapido", "urgente", "todavia estan"];
    private static readonly string[] Basura = ["asdf", "qwer", "prueba", "test", "hola", "xxx", "jaja", "nada"];
    private static readonly string[] Insultos = ["idiota", "imbecil", "estupido", "huevon", "conchatumare", "ctm", "mierda", "basura de serenazgo"];

    private static Guid Id(int indice) => CategoriaIncidencia.Iniciales[indice].Id;

    // Cada palabra clave es una raíz: debe empezar una palabra del texto («robo» encuentra «robó», no «protocolo» a «toco»).
    private static Func<string, bool> En(string texto) => palabra => texto.Contains(" " + palabra, StringComparison.Ordinal);

    public static AnalisisReporte Analizar(EntradaAnalisis entrada)
    {
        // Solo la descripción: la referencia del lugar («frente al parque», «Urb. …») no dice qué pasó y confundiría las palabras clave.
        var texto = Normalizar(entrada.Descripcion);
        var nombre = (Guid id) => entrada.Catalogo.FirstOrDefault(c => c.Id == id)?.Nombre ?? "";
        var senales = new List<SenalTriaje>();

        var puntajes = PalabrasPorCategoria
            .Select(p => (Categoria: p.Key, Encontradas: p.Value.Where(En(texto)).ToList()))
            .Where(p => p.Encontradas.Count > 0)
            .OrderByDescending(p => p.Encontradas.Count)
            .ToList();
        var mejor = puntajes.FirstOrDefault();
        var segunda = puntajes.Skip(1).FirstOrDefault();
        var categoria = mejor.Encontradas is null ? entrada.CategoriaCiudadanoId : mejor.Categoria;
        var confianza = mejor.Encontradas is null ? NivelConfianza.Baja
            : mejor.Encontradas.Count >= 2 && (segunda.Encontradas is null || segunda.Encontradas.Count < mejor.Encontradas.Count) ? NivelConfianza.Alta
            : NivelConfianza.Media;
        Guid? alternativa = segunda.Encontradas is null ? null : segunda.Categoria;
        if (categoria != entrada.CategoriaCiudadanoId && confianza != NivelConfianza.Baja)
            senales.Add(SenalTriaje.CategoriaCiudadanoDudosa);

        var conArma = Armas.Any(En(texto));
        var urgencia = conArma || (Agresion.Any(En(texto)) && EnCurso.Any(En(texto)));
        var motivoUrgencia = conArma ? "La descripción menciona un arma." : urgencia ? "Describe una agresión que podría seguir en curso." : null;
        var ofensivo = Insultos.Any(En(texto));
        if (ofensivo)
            senales.Add(SenalTriaje.Ofensivo);
        var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letras = texto.Count(char.IsLetter);
        var basura = !urgencia && mejor.Encontradas is null && (letras < 8 || palabras.All(p => Basura.Any(b => p.StartsWith(b)) || p.Length <= 2));
        var fuera = FueraDeAlcance.FirstOrDefault(f => f.Palabras.Any(En(texto)));

        string Palabras(IEnumerable<string> lista) => string.Join(", ", lista.Take(4).Select(p => $"«{p}»"));

        if (basura || (ofensivo && mejor.Encontradas is null))
        {
            senales.Add(basura ? SenalTriaje.Spam : SenalTriaje.Ofensivo);
            return new AnalisisReporte(entrada.CategoriaCiudadanoId, null, NivelConfianza.Media,
                basura ? "El texto no describe ningún hecho: parece una prueba o un texto sin sentido." : "El texto solo contiene insultos y no describe un hecho.",
                DecisionSugerida.Descartar, "La descripción no permite identificar un hecho de seguridad ciudadana atendible.",
                null, null, MotivosDescarte.FalsoOMalintencionado, null, false, null, null, senales);
        }

        if (fuera.Destino is not null && !urgencia && mejor.Encontradas is null)
        {
            senales.Add(SenalTriaje.FueraDeAlcance);
            var destino = Catalogo.Texto(DestinosDerivacion.Todos, fuera.Destino)!;
            return new AnalisisReporte(entrada.CategoriaCiudadanoId, null, NivelConfianza.Media,
                $"Menciona {Palabras(fuera.Palabras.Where(En(texto)))}: no es un hecho de seguridad ciudadana.",
                DecisionSugerida.Derivar, $"El hecho no es de seguridad ciudadana y corresponde a {destino}.",
                fuera.Destino, $"Se informó del hecho a {destino}.", null, null, false, null, null, senales);
        }

        if (urgencia)
            senales.Add(SenalTriaje.PosibleUrgencia);
        var ubicable = entrada.ConCoordenadas || !string.IsNullOrWhiteSpace(entrada.ReferenciaHecho);
        if (mejor.Encontradas is null || !ubicable || palabras.Length < 4)
        {
            senales.Add(SenalTriaje.InformacionInsuficiente);
            return new AnalisisReporte(categoria, alternativa, NivelConfianza.Baja,
                mejor.Encontradas is null ? "No reconoce palabras de ninguna categoría: conviene leerlo con cuidado." : "Hay pocos datos para ubicar o entender el hecho.",
                DecisionSugerida.RevisarConCuidado, "", null, null, null, null, urgencia, motivoUrgencia, null, senales);
        }

        // Con urgencia nunca «Alta»: la admisión automática exige confianza alta y sin urgencia (7.3).
        var prioridad = urgencia ? PrioridadesCaso.Alta
            : categoria == Id(0) || categoria == Id(1) || categoria == Id(5) || categoria == Id(7) || categoria == Id(12) ? PrioridadesCaso.Media
            : PrioridadesCaso.Baja;
        return new AnalisisReporte(categoria, alternativa, urgencia ? NivelConfianza.Media : confianza,
            $"Palabras de «{nombre(categoria)}»: {Palabras(mejor.Encontradas)}.",
            DecisionSugerida.Admitir, $"Hecho de seguridad ciudadana ({nombre(categoria).ToLowerInvariant()}) con ubicación para atenderlo.",
            null, null, null, prioridad, urgencia, motivoUrgencia, null, senales);
    }

    // Minúsculas y sin tildes («Robó» y «robo» son lo mismo); la «ñ» se conserva.
    public static string Normalizar(string texto)
    {
        var descompuesto = texto.ToLowerInvariant().Replace('ñ', '\u0001').Normalize(NormalizationForm.FormD);
        var salida = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            salida.Append(char.IsLetterOrDigit(c) || c == '\u0001' ? c : ' ');
        }
        return " " + string.Join(' ', salida.ToString().Replace('\u0001', 'ñ').Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
    }
}
