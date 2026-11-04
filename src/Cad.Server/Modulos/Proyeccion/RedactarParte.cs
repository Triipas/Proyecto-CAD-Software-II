using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cad.Server.Modulos.Asistencia.Puertos;
using Microsoft.Extensions.Caching.Memory;

namespace Cad.Server.Modulos.Proyeccion;

public sealed record AccionParte(string Zona, string Texto, string? Sereno);

// Resumen y acciones sugeridas; Modelo es null si es el parte plantilla.
public sealed record ParteVista(string Resumen, IReadOnlyList<AccionParte> Acciones, string? Modelo);

// HU46 (S6-T21; plan, 8.6) — el parte de patrullaje: un modelo de lenguaje redacta, con las cifras ya calculadas, un resumen y acciones
// sugeridas. Solo redacta: recibe las 10 zonas (con alias, nivel, casos esperados, explicación) y la distancia del sereno más cercano a
// cada una, nunca casos concretos ni datos personales. La salida se valida: cada zona y sereno debe estar en la entrada y cada cifra
// del texto también (CA09); si algo falla, o la IA está apagada (CA10), se muestra el parte plantilla con las mismas cifras. Se guarda
// en caché por ventana, cuarto de hora y huella de la entrada. No ejecuta nada: reposicionar serenos sigue siendo de la central.
public sealed partial class RedactarParte(IModeloLenguaje modelo, IMemoryCache cache, ILogger<RedactarParte> registro)
{
    public const int MaximoResumen = 400;
    private static readonly CultureInfo Peru = CultureInfo.GetCultureInfo("es-PE");

    private sealed record ZonaEntrada(string Id, string Zona, string Nivel, string CasosEsperados, string Explicacion, string? SerenoId, string? DistanciaSereno);

    public async Task<ParteVista> EjecutarAsync(ResultadoProyeccion proyeccion, int principales, CancellationToken ct)
    {
        var zonas = proyeccion.Principales(principales);
        if (zonas.Count == 0)
            return new ParteVista("No hay zonas para proyectar en esta ventana.", [], null);

        // Alias para los serenos: el modelo no recibe nombres.
        var serenos = zonas.Where(z => z.SerenoCercanoId is not null).Select(z => (z.SerenoCercanoId!.Value, z.SerenoCercano!)).Distinct()
            .Select((s, i) => (Id: $"S{i + 1}", SerenoId: s.Item1, Nombre: s.Item2)).ToList();
        var entrada = zonas.Select((z, i) => new ZonaEntrada($"Z{i + 1}", z.Zona, Nivel(z.Nivel), z.Esperados.ToString("0.0", Peru), z.Explicacion,
            serenos.FirstOrDefault(s => s.SerenoId == z.SerenoCercanoId).Id, z.MetrosSerenoCercano is { } m ? Km(m) : null)).ToList();
        var plantilla = Plantilla(proyeccion, zonas, entrada);
        if (!modelo.Disponible)
            return plantilla;

        var datos = JsonSerializer.Serialize(new { ventana = proyeccion.TextoVentana, umbralCobertura = Km(proyeccion.UmbralCoberturaMetros), zonas = entrada },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var clave = $"parte:{proyeccion.Ventana}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 900}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(datos)))[..16]}";
        if (cache.TryGetValue(clave, out ParteVista? guardado) && guardado is not null)
            return guardado;

        ParteVista parte;
        try
        {
            var respuesta = await modelo.CompletarJsonAsync(Sistema, $"Datos de la proyección (JSON):\n{datos}", Esquema(), ct);
            parte = respuesta is null ? plantilla : Validar(respuesta.Texto, datos, serenos, zonas) is { } valido ? valido with { Modelo = respuesta.Modelo } : plantilla;
            if (respuesta is not null && ReferenceEquals(parte, plantilla))
                registro.LogWarning("Parte de patrullaje: la respuesta de {Modelo} no pasó la validación; se usa el parte plantilla.", respuesta.Modelo);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registro.LogWarning("Parte de patrullaje: el modelo no respondió ({Error}); se usa el parte plantilla.", ex.GetType().Name);
            parte = plantilla;
        }
        cache.Set(clave, parte, TimeSpan.FromMinutes(15));
        return parte;
    }

    private const string Sistema = """
        Redactas el parte de patrullaje de la central de serenazgo a partir de una proyección ya calculada. Escribe en español, formal y breve.
        Reglas: usa solo las zonas y los serenos de los datos, por su id; no inventes cifras: toda cifra que escribas debe estar en los datos,
        escrita igual; no calcules nada nuevo; no afirmes que algo ocurrirá: son casos esperados. El resumen tiene hasta 400 caracteres. Da de 1 a
        5 acciones, empezando por las zonas de nivel Alto, y menciona el sereno más cercano cuando lo haya o que no hay cobertura cercana.
        """;

    private static JsonObject Esquema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["resumen"] = new JsonObject { ["type"] = "string" },
            ["acciones"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["zonaId"] = new JsonObject { ["type"] = "string" },
                        ["texto"] = new JsonObject { ["type"] = "string" },
                        ["serenoId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                    },
                    ["required"] = new JsonArray("zonaId", "texto", "serenoId"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("resumen", "acciones"),
        ["additionalProperties"] = false,
    };

    private sealed record Respuesta(string? Resumen, List<RespuestaAccion>? Acciones);
    private sealed record RespuestaAccion(string? ZonaId, string? Texto, string? SerenoId);

    // CA09: zonas y serenos de la entrada, cifras de la entrada, longitudes acotadas. null si algo no cumple.
    public static ParteVista? Validar(string texto, string datos, IReadOnlyList<(string Id, Guid SerenoId, string Nombre)> serenos,
        IReadOnlyList<ZonaProyectada> zonas)
    {
        Respuesta? r;
        try
        {
            r = JsonSerializer.Deserialize<Respuesta>(texto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
        if (r?.Resumen is not { Length: > 0 and <= MaximoResumen } resumen || r.Acciones is not { Count: > 0 and <= 5 } acciones)
            return null;
        var cifrasPermitidas = Cifras(datos).ToHashSet();
        if (!Cifras(resumen).All(cifrasPermitidas.Contains))
            return null;
        var resultado = new List<AccionParte>();
        foreach (var a in acciones)
        {
            if (a.ZonaId is null || a.Texto is not { Length: > 0 and <= 300 } || !Cifras(a.Texto).All(cifrasPermitidas.Contains))
                return null;
            var indice = int.TryParse(a.ZonaId.TrimStart('Z'), out var n) && a.ZonaId.StartsWith('Z') ? n - 1 : -1;
            if (indice < 0 || indice >= zonas.Count)
                return null;
            string? sereno = null;
            if (a.SerenoId is not null)
            {
                var s = serenos.FirstOrDefault(x => x.Id == a.SerenoId);
                if (s.Id is null)
                    return null;
                sereno = s.Nombre;
            }
            resultado.Add(new AccionParte(zonas[indice].Zona, Reemplazar(a.Texto, zonas, serenos), sereno));
        }
        return new ParteVista(Reemplazar(resumen, zonas, serenos), resultado, null);
    }

    // El modelo escribe «Z3» o «S1»; el operador lee la zona y el nombre del sereno.
    private static string Reemplazar(string texto, IReadOnlyList<ZonaProyectada> zonas, IReadOnlyList<(string Id, Guid SerenoId, string Nombre)> serenos)
    {
        texto = Alias().Replace(texto, m =>
        {
            var n = int.Parse(m.Groups[2].Value) - 1;
            return m.Groups[1].Value == "Z"
                ? (n >= 0 && n < zonas.Count ? zonas[n].Zona : m.Value)
                : (n >= 0 && n < serenos.Count ? serenos[n].Nombre : m.Value);
        });
        return texto;
    }

    // Toda cifra del texto, normalizada (2,1 y 2.1 son la misma). Los alias Z1 o S2 no cuentan: se reemplazan.
    public static IEnumerable<string> Cifras(string texto) =>
        CifraRegex().Matches(Alias().Replace(texto, "")).Select(m => m.Value.Replace(',', '.').TrimEnd('.'));

    private static ParteVista Plantilla(ResultadoProyeccion p, IReadOnlyList<ZonaProyectada> zonas, IReadOnlyList<ZonaEntrada> entrada)
    {
        var tres = string.Join(", ", zonas.Take(3).Select((z, i) => $"{z.Zona} ({entrada[i].CasosEsperados} casos esperados)"));
        var acciones = zonas.Select((z, i) => (z, e: entrada[i])).Where(x => x.z.Nivel == NivelFoco.Alto).Take(5)
            .Select(x => new AccionParte(x.z.Zona, x.z.SinCobertura
                ? $"Reforzar {x.z.Zona}: no hay serenos operativos a menos de {Km(p.UmbralCoberturaMetros)}."
                : $"Reforzar {x.z.Zona}: el sereno más cercano está a {x.e.DistanciaSereno}.", x.z.SinCobertura ? null : x.z.SerenoCercano))
            .ToList();
        return new ParteVista($"Para {p.TextoVentana} se esperan más casos en {tres}.", acciones, null);
    }

    private static string Nivel(NivelFoco nivel) => nivel switch { NivelFoco.Alto => "Alto", NivelFoco.Medio => "Medio", _ => "Bajo" };

    private static string Km(int metros) => (metros / 1000.0).ToString("0.0", Peru) + " km";

    [GeneratedRegex(@"\b([ZS])(\d+)\b")]
    private static partial Regex Alias();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex CifraRegex();
}
