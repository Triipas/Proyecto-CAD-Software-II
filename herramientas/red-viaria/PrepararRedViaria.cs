#:project ../../src/Cad.Calles/Cad.Calles.csproj

// Prepara la red de calles que viaja dentro de la app para la ruta del sereno (22/10/2026). Desde el 23/10/2026, la ciudad
// de Arequipa (demostración): Cercado, Cayma, Yanahuara, Cerro Colorado, Paucarpata, Socabaya, J. L. Bustamante y alrededores.
//
// Uso (una vez, o al cambiar la zona):
//   1. Descargar de Overpass las vías de la zona (Arequipa metropolitana, ~19 × 19 km):
//        curl -A "CadMovil-preparacion/1.0" --data-urlencode "data=<consulta>" https://overpass-api.de/api/interpreter -o red.json
//      con la consulta:
//        [out:json][timeout:180];
//        way["highway"~"^(motorway|trunk|primary|secondary|tertiary|unclassified|residential|living_street|service|road|
//                          motorway_link|trunk_link|primary_link|secondary_link|tertiary_link)$"](-16.4900,-71.6300,-16.3200,-71.4500);
//        out body qt;>;out skel qt;
//   2. Convertir:  dotnet run herramientas/red-viaria/PrepararRedViaria.cs -- red.json src/Cad.Movil/Resources/Raw/mapa/red-viaria.bin
//
// Datos © colaboradores de OpenStreetMap (ODbL): la app muestra la atribución en el mapa.

using System.Text.Json;
using Cad.Calles;

if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: dotnet run PrepararRedViaria.cs -- <overpass.json> <red-viaria.bin>");
    return 1;
}

using var json = JsonDocument.Parse(File.ReadAllBytes(args[0]));
var red = RedViaria.DesdeOverpass(json);
await using (var salida = File.Create(args[1]))
    red.Escribir(salida);

var latitudes = red.Nodos.Select(n => n.Latitud).ToList();
var longitudes = red.Nodos.Select(n => n.Longitud).ToList();
Console.WriteLine($"{red.Nodos.Count} nodos, {red.Vias.Count} vías, {new FileInfo(args[1]).Length / 1024} KB");
Console.WriteLine($"Zona: {latitudes.Min():0.####},{longitudes.Min():0.####} a {latitudes.Max():0.####},{longitudes.Max():0.####}");
return 0;
