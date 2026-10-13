// Cad.PruebaCarga — medición de RNF02 / HU11-CA05 (S1-T18). Ver docs/pruebas.md.
//
// Requisitos previos:
//   1. Servidor con Medicion__Rnf02__Habilitada=true.
//   2. Un panel real de operador abierto en /panel/central («Conectado en tiempo real»): es quien envía
//      el acuse de presentación. La herramienta no lo simula.
//   3. Credenciales del administrador en CAD_PRUEBA_ADMIN_CORREO y CAD_PRUEBA_ADMIN_CLAVE (para leer resultados).
//
// Uso: dotnet run --project tests/Cad.PruebaCarga -- --url https://servidor --entorno "validacion-render" [opciones]
//   --ciudadanos 19   sesiones de ciudadano concurrentes que emiten SOS
//   --paneles 1       paneles de operador abiertos a mano (se suman a las sesiones declaradas)
//   --sos 100         SOS totales (mínimo de RNF02)
//   --pausa-min-ms 200 --pausa-max-ms 1500   pausa aleatoria entre SOS de una misma sesión
//   --espera-acuses-s 20   espera máxima de acuses tras el último SOS
//   --salida docs/mediciones
//
// Todos los datos son ficticios (correos @correo.test, coordenadas aleatorias en Lima). Los SOS generados
// quedan como pendientes en la base del entorno. NO mide el tiempo de respuesta del sereno.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.PruebaCarga;

var opciones = Opciones.Leer(args);
var ejecucion = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
Console.WriteLine($"RNF02 · {opciones.Entorno} · {opciones.Url} · ejecución {ejecucion}");

// 1. Administrador (solo para leer los resultados de la medición).
var cookies = new CookieContainer();
using var admin = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = opciones.Url };
await IngresarAlPanelAsync(admin, Requerida("CAD_PRUEBA_ADMIN_CORREO"), Requerida("CAD_PRUEBA_ADMIN_CLAVE"));
var comprobacion = await admin.GetAsync($"/api/mediciones/presentacion?desde={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}");
if (comprobacion.StatusCode == HttpStatusCode.NotFound)
    Salir("La medición está desactivada en el servidor: define Medicion__Rnf02__Habilitada=true.");
if (!comprobacion.IsSuccessStatusCode)
    Salir($"No se pueden leer los resultados ({(int)comprobacion.StatusCode}). ¿La cuenta es de administrador?");

// 2. Sesiones de ciudadano (cuentas ficticias nuevas en cada ejecución).
using var http = new HttpClient { BaseAddress = opciones.Url, Timeout = TimeSpan.FromSeconds(30) };
Console.WriteLine($"Creando {opciones.Ciudadanos} sesiones de ciudadano…");
var sesiones = await Task.WhenAll(Enumerable.Range(1, opciones.Ciudadanos).Select(i => CrearCiudadanoAsync(http, ejecucion, i)));

// 3. Emisión concurrente de SOS.
var inicio = DateTimeOffset.UtcNow.AddSeconds(-1);
var emitidos = new System.Collections.Concurrent.ConcurrentBag<(Guid Id, HttpStatusCode Estado)>();
var reparto = Enumerable.Range(0, opciones.Sos).GroupBy(n => n % sesiones.Length).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"Emitiendo {opciones.Sos} SOS desde {sesiones.Length} sesiones concurrentes…");
await Task.WhenAll(sesiones.Select((cliente, indice) => Task.Run(async () =>
{
    var azar = new Random(indice);
    for (var n = 0; n < reparto.GetValueOrDefault(indice); n++)
    {
        await Task.Delay(azar.Next(opciones.PausaMinMs, opciones.PausaMaxMs + 1));
        var activado = DateTimeOffset.UtcNow;
        UbicacionSos? ubicacion = azar.NextDouble() < 0.5
            ? null // la mitad sin ubicación (HU11-CA04)
            : new UbicacionSos(-12.20 + azar.NextDouble() * 0.25, -77.10 + azar.NextDouble() * 0.15, 5 + azar.NextDouble() * 40, activado.AddMilliseconds(500));
        var respuesta = await cliente.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), activado, ubicacion));
        var cuerpo = respuesta.IsSuccessStatusCode ? await respuesta.Content.ReadFromJsonAsync<RespuestaSos>() : null;
        emitidos.Add((cuerpo?.Id ?? Guid.Empty, respuesta.StatusCode));
    }
})));

var aceptados = emitidos.Where(e => e.Estado == HttpStatusCode.Created).Select(e => e.Id).ToHashSet();
Console.WriteLine($"Aceptados por el servidor: {aceptados.Count}/{opciones.Sos}. Esperando acuses del panel…");

// 4. Acuses de presentación (primer acuse por SOS), con espera máxima.
Dictionary<Guid, (DateTimeOffset Aceptado, DateTimeOffset Presentado)> acuses = [];
var limiteEspera = DateTime.UtcNow.AddSeconds(opciones.EsperaAcusesSegundos);
do
{
    var filas = await admin.GetFromJsonAsync<List<Fila>>($"/api/mediciones/presentacion?desde={Uri.EscapeDataString(inicio.ToString("O"))}") ?? [];
    acuses = filas.Where(f => aceptados.Contains(f.CasoId)).ToDictionary(f => f.CasoId, f => (f.AceptadoEn, f.PresentadoEn));
    if (acuses.Count == aceptados.Count) break;
    await Task.Delay(1000);
} while (DateTime.UtcNow < limiteEspera);

// 5. Evaluación e informe.
var latencias = aceptados.Select(id => acuses.TryGetValue(id, out var a) ? (TimeSpan?)(a.Presentado - a.Aceptado) : null).ToList();
var resultado = Estadisticas.Evaluar(latencias, opciones.Ciudadanos + opciones.Paneles);
var rechazos = emitidos.Where(e => e.Estado != HttpStatusCode.Created).GroupBy(e => (int)e.Estado).ToDictionary(g => g.Key, g => g.Count());
var informe = Informe.Markdown(opciones, ejecucion, resultado, rechazos);
Directory.CreateDirectory(opciones.Salida);
var archivo = Path.Combine(opciones.Salida, $"rnf02-{ejecucion}-{opciones.Entorno}.md");
await File.WriteAllTextAsync(archivo, informe, new UTF8Encoding(false));
await File.WriteAllTextAsync(Path.ChangeExtension(archivo, ".json"), JsonSerializer.Serialize(new { opciones.Entorno, ejecucion, resultado, rechazos }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(informe);
Console.WriteLine($"Informe: {archivo}");
return resultado.Cumple ? 0 : 1;

static async Task<HttpClient> CrearCiudadanoAsync(HttpClient http, string ejecucion, int i)
{
    var correo = $"carga.{ejecucion}.{i}@correo.test".ToLowerInvariant();
    var clave = $"Carga-{Guid.NewGuid():N}";
    var registro = await http.PostAsJsonAsync("/api/cuentas/registro",
        new SolicitudRegistroCiudadano("Ciudadano", $"Carga {i}", correo, $"9{Random.Shared.Next(10_000_000, 99_999_999)}", clave));
    if (registro.StatusCode != HttpStatusCode.Created) Salir($"No se pudo registrar la sesión {i}: {(int)registro.StatusCode}");
    var token = await (await http.PostAsJsonAsync("/api/auth/token", new SolicitudInicioSesion(correo, clave))).Content.ReadFromJsonAsync<RespuestaToken>();
    var cliente = new HttpClient { BaseAddress = http.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
    cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);
    return cliente;
}

static async Task IngresarAlPanelAsync(HttpClient cliente, string correo, string clave)
{
    var pagina = await cliente.GetStringAsync("/panel/ingresar");
    var token = Regex.Match(pagina, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    var respuesta = await cliente.PostAsync("/panel/ingresar", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["Formulario.Correo"] = correo,
        ["Formulario.Contrasena"] = clave,
        ["__RequestVerificationToken"] = token,
    }));
    if (respuesta.StatusCode != HttpStatusCode.Redirect) Salir("No se pudo ingresar al panel con la cuenta de administrador.");
}

static string Requerida(string variable)
{
    var valor = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrEmpty(valor)) Salir($"Falta la variable de entorno {variable}.");
    return valor!;
}

static void Salir(string mensaje)
{
    Console.Error.WriteLine(mensaje);
    Environment.Exit(2);
}

sealed record Fila(Guid CasoId, DateTimeOffset AceptadoEn, DateTimeOffset PresentadoEn);
