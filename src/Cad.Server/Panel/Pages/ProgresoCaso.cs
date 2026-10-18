namespace Cad.Server.Panel.Pages;

// Rediseño CAD v2 (fase 3): línea de progreso de un caso en su detalle (SOS y reportes).
public enum EstadoPaso { Hecho, Actual, Omitido, Pendiente }

public sealed record PasoCaso(string Texto, EstadoPaso Estado);

public static class ProgresoCaso
{
    // Un paso está hecho si ocurrió; el actual es el de la siguiente acción (-1 si no hay ninguna);
    // un paso sin hacer anterior al actual o al último hecho se saltó (pasos opcionales).
    public static IReadOnlyList<PasoCaso> Construir(IReadOnlyList<string> textos, IReadOnlyList<bool> hechos, int actual)
    {
        var ultimoHecho = -1;
        for (var i = 0; i < hechos.Count; i++)
            if (hechos[i]) ultimoHecho = i;

        return textos.Select((texto, i) => new PasoCaso(texto,
            hechos[i] ? EstadoPaso.Hecho
            : i == actual ? EstadoPaso.Actual
            : i < Math.Max(actual, ultimoHecho) ? EstadoPaso.Omitido
            : EstadoPaso.Pendiente)).ToList();
    }
}
