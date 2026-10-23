namespace Cad.Calles;

public abstract record ResultadoRuta
{
    public sealed record Encontrada(IReadOnlyList<PuntoRuta> Puntos, double Metros) : ResultadoRuta;
    // Motivo legible: fuera de la zona con calles cargadas, sin camino…
    public sealed record NoDisponible(string Motivo) : ResultadoRuta;
}

// Ruta del sereno hasta el caso por las calles que puede recorrer un vehículo del serenazgo (moto o camioneta), respetando
// los sentidos de circulación; si así no hay camino (red recortada en el borde de la zona), se repite sin sentidos.
// La misma búsqueda guía al sereno en la app y dibuja su recorrido en el mapa de la central.
public static class BuscadorRutas
{
    // Un punto a más de esta distancia de la calle más cercana está fuera de la zona con calles cargadas.
    public const double DistanciaMaximaACalleMetros = 300;
    public const string MensajeFueraDeZona = "El caso está fuera de la zona con calles cargadas.";

    public static ResultadoRuta Calcular(GrafoViario conSentidos, GrafoViario sinSentidos, PuntoRuta origen, PuntoRuta destino)
    {
        if (conSentidos.MasCercano(origen) is not { } a || conSentidos.MasCercano(destino) is not { } b
            || a.Metros > DistanciaMaximaACalleMetros || b.Metros > DistanciaMaximaACalleMetros)
            return new ResultadoRuta.NoDisponible(MensajeFueraDeZona);

        foreach (var grafo in new[] { conSentidos, sinSentidos })
        {
            if (grafo.CaminoMasCorto(a.Nodo, b.Nodo) is not { } camino)
                continue;
            // La ruta empieza en el sereno y termina en el punto del caso, no solo en las esquinas más cercanas.
            var puntos = new List<PuntoRuta>(camino.Puntos.Count + 2) { origen };
            puntos.AddRange(camino.Puntos);
            puntos.Add(destino);
            return new ResultadoRuta.Encontrada(puntos, camino.Metros + a.Metros + b.Metros);
        }
        return new ResultadoRuta.NoDisponible("No se encontró un camino por las calles de la zona.");
    }
}
