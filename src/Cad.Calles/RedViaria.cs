using System.Text;
using System.Text.Json;

namespace Cad.Calles;

public sealed record PuntoRuta(double Latitud, double Longitud);

public enum Sentido : byte { Ambos = 0, Directo = 1, Contrario = 2 }

// Red de calles de una zona (22/10/2026): nodos con su posición y vías como secuencias de nodos con su sentido de circulación.
// Se prepara una vez desde OpenStreetMap (herramientas/red-viaria) y viaja dentro de la app en un formato binario compacto:
//   «CADRV1» · int32 nodos · por nodo int32 lat×1e6, int32 lon×1e6 · int32 vías · por vía byte sentido, int32 n, n × int32 índice.
// Datos © colaboradores de OpenStreetMap (ODbL).
public sealed class RedViaria
{
    private static readonly byte[] Firma = "CADRV1"u8.ToArray();

    public required IReadOnlyList<PuntoRuta> Nodos { get; init; }
    public required IReadOnlyList<(Sentido Sentido, int[] Nodos)> Vias { get; init; }

    // Grafo para el A*. Con respetarSentido=false, todas las vías son de doble sentido (respaldo si los sentidos no conectan).
    public GrafoViario Grafo(bool respetarSentido = true)
    {
        var grafo = new GrafoViario(Nodos);
        foreach (var (sentido, nodos) in Vias)
            grafo.AgregarVia(nodos, respetarSentido ? sentido : Sentido.Ambos);
        return grafo;
    }

    public void Escribir(Stream destino)
    {
        using var escritor = new BinaryWriter(destino, Encoding.UTF8, leaveOpen: true);
        escritor.Write(Firma);
        escritor.Write(Nodos.Count);
        foreach (var n in Nodos)
        {
            escritor.Write((int)Math.Round(n.Latitud * 1e6));
            escritor.Write((int)Math.Round(n.Longitud * 1e6));
        }
        escritor.Write(Vias.Count);
        foreach (var (sentido, nodos) in Vias)
        {
            escritor.Write((byte)sentido);
            escritor.Write(nodos.Length);
            foreach (var i in nodos) escritor.Write(i);
        }
    }

    public static RedViaria Leer(Stream origen)
    {
        using var lector = new BinaryReader(origen, Encoding.UTF8, leaveOpen: true);
        if (!lector.ReadBytes(Firma.Length).AsSpan().SequenceEqual(Firma))
            throw new InvalidDataException("El archivo no es una red de calles del CAD.");
        var nodos = new PuntoRuta[lector.ReadInt32()];
        for (var i = 0; i < nodos.Length; i++)
            nodos[i] = new PuntoRuta(lector.ReadInt32() / 1e6, lector.ReadInt32() / 1e6);
        var vias = new (Sentido, int[])[lector.ReadInt32()];
        for (var v = 0; v < vias.Length; v++)
        {
            var sentido = (Sentido)lector.ReadByte();
            var ids = new int[lector.ReadInt32()];
            for (var i = 0; i < ids.Length; i++) ids[i] = lector.ReadInt32();
            vias[v] = (sentido, ids);
        }
        return new RedViaria { Nodos = nodos, Vias = vias };
    }

    // Respuesta JSON de Overpass (vías con etiquetas y sus nodos): solo se conservan los nodos que usan las vías.
    public static RedViaria DesdeOverpass(JsonDocument json)
    {
        var posiciones = new Dictionary<long, PuntoRuta>();
        var elementos = json.RootElement.GetProperty("elements");
        foreach (var e in elementos.EnumerateArray())
            if (e.GetProperty("type").GetString() == "node")
                posiciones[e.GetProperty("id").GetInt64()] = new PuntoRuta(e.GetProperty("lat").GetDouble(), e.GetProperty("lon").GetDouble());

        var indices = new Dictionary<long, int>();
        var nodos = new List<PuntoRuta>();
        var vias = new List<(Sentido, int[])>();
        foreach (var e in elementos.EnumerateArray())
        {
            if (e.GetProperty("type").GetString() != "way" || !e.TryGetProperty("nodes", out var ids))
                continue;
            var secuencia = new List<int>();
            foreach (var id in ids.EnumerateArray().Select(n => n.GetInt64()))
            {
                if (!posiciones.TryGetValue(id, out var posicion)) continue;
                if (!indices.TryGetValue(id, out var indice))
                {
                    indices[id] = indice = nodos.Count;
                    nodos.Add(posicion);
                }
                secuencia.Add(indice);
            }
            if (secuencia.Count > 1)
                vias.Add((SentidoDe(e.TryGetProperty("tags", out var t) ? t : default), secuencia.ToArray()));
        }
        return new RedViaria { Nodos = nodos, Vias = vias };
    }

    private static Sentido SentidoDe(JsonElement etiquetas)
    {
        if (etiquetas.ValueKind != JsonValueKind.Object) return Sentido.Ambos;
        string? Etiqueta(string nombre) => etiquetas.TryGetProperty(nombre, out var v) ? v.GetString() : null;
        return Etiqueta("oneway") switch
        {
            "yes" or "true" or "1" => Sentido.Directo,
            "-1" or "reverse" => Sentido.Contrario,
            "no" or "false" or "0" => Sentido.Ambos,
            _ => Etiqueta("junction") is "roundabout" or "circular" ? Sentido.Directo : Sentido.Ambos,
        };
    }
}

// Red de calles como grafo dirigido: tramos entre nodos consecutivos con su longitud en metros.
public sealed class GrafoViario(IReadOnlyList<PuntoRuta> nodos)
{
    private readonly List<(int Hasta, double Metros)>?[] salidas = new List<(int, double)>?[nodos.Count];
    // Nodos a los que se puede llegar (el final de una vía de un solo sentido tiene entrada pero quizá no salida).
    private readonly bool[] conEntrada = new bool[nodos.Count];

    public IReadOnlyList<PuntoRuta> Nodos => nodos;

    // Une los nodos consecutivos de una vía; en una vía de un solo sentido, solo en ese sentido.
    public void AgregarVia(IReadOnlyList<int> ids, Sentido sentido)
    {
        for (var i = 0; i + 1 < ids.Count; i++)
        {
            var metros = Geo.DistanciaMetros(nodos[ids[i]], nodos[ids[i + 1]]);
            if (sentido != Sentido.Contrario) Unir(ids[i], ids[i + 1], metros);
            if (sentido != Sentido.Directo) Unir(ids[i + 1], ids[i], metros);
        }
    }

    private void Unir(int desde, int hasta, double metros)
    {
        (salidas[desde] ??= []).Add((hasta, metros));
        conEntrada[hasta] = true;
    }

    // Nodo de la red más cercano a un punto, para empezar o terminar la ruta sobre una calle: el origen necesita una salida y el
    // destino, una entrada.
    public (int Nodo, double Metros)? MasCercano(PuntoRuta punto, bool comoDestino = false)
    {
        (int, double)? mejor = null;
        for (var i = 0; i < nodos.Count; i++)
        {
            if (comoDestino ? !conEntrada[i] : salidas[i] is null) continue;
            var d = Geo.DistanciaMetros(punto, nodos[i]);
            if (mejor is null || d < mejor.Value.Item2) mejor = (i, d);
        }
        return mejor;
    }

    // A*: camino más corto en metros. La heurística (distancia en línea recta) nunca sobreestima, así que el camino es óptimo.
    public (IReadOnlyList<PuntoRuta> Puntos, double Metros)? CaminoMasCorto(int origen, int destino)
    {
        var meta = nodos[destino];
        var costo = new Dictionary<int, double> { [origen] = 0 };
        var anterior = new Dictionary<int, int>();
        var abiertos = new PriorityQueue<int, double>();
        abiertos.Enqueue(origen, Geo.DistanciaMetros(nodos[origen], meta));
        var cerrados = new HashSet<int>();

        while (abiertos.TryDequeue(out var actual, out _))
        {
            if (actual == destino)
                return (Reconstruir(anterior, destino), costo[destino]);
            if (!cerrados.Add(actual) || salidas[actual] is not { } vecinos)
                continue;
            foreach (var (hasta, metros) in vecinos)
            {
                var nuevo = costo[actual] + metros;
                if (costo.TryGetValue(hasta, out var conocido) && conocido <= nuevo)
                    continue;
                costo[hasta] = nuevo;
                anterior[hasta] = actual;
                abiertos.Enqueue(hasta, nuevo + Geo.DistanciaMetros(nodos[hasta], meta));
            }
        }
        return null;
    }

    private List<PuntoRuta> Reconstruir(Dictionary<int, int> anterior, int destino)
    {
        var camino = new List<PuntoRuta> { nodos[destino] };
        for (var id = destino; anterior.TryGetValue(id, out var previo); id = previo)
            camino.Add(nodos[previo]);
        camino.Reverse();
        return camino;
    }
}

public static class Geo
{
    // Fórmula del haversine (radio medio de la Tierra): suficiente para distancias urbanas.
    public static double DistanciaMetros(PuntoRuta a, PuntoRuta b) => DistanciaMetros(a.Latitud, a.Longitud, b.Latitud, b.Longitud);

    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double Radio = 6_371_000;
        static double Rad(double grados) => grados * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * Radio * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
