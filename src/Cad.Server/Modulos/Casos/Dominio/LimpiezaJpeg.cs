namespace Cad.Server.Modulos.Casos.Dominio;

// DP08 ampliada — segunda defensa de la privacidad de las fotos del reporte: aunque la app ya las vuelve a codificar sin
// metadatos, el servidor quita de cada JPEG los segmentos APP1–APP15 (EXIF con la ubicación GPS, XMP, ICC, datos del
// fabricante) y los comentarios (COM). Conserva APP0 (JFIF) y los segmentos de la imagen. Sin librerías externas.
public static class LimpiezaJpeg
{
    private const byte Marca = 0xFF;
    private const byte Soi = 0xD8; // inicio de la imagen
    private const byte Sos = 0xDA; // inicio de los datos comprimidos: desde aquí se copia todo tal cual
    private const byte Eoi = 0xD9;
    private const byte App0 = 0xE0;
    private const byte App15 = 0xEF;
    private const byte Com = 0xFE;

    public static bool EsJpeg(ReadOnlySpan<byte> datos) =>
        datos.Length >= 4 && datos[0] == Marca && datos[1] == Soi && datos[2] == Marca;

    // null si no es un JPEG bien formado hasta el inicio de los datos comprimidos.
    public static byte[]? QuitarMetadatos(byte[] datos)
    {
        if (!EsJpeg(datos))
            return null;

        using var salida = new MemoryStream(datos.Length);
        salida.Write(datos, 0, 2);
        var i = 2;
        while (i + 4 <= datos.Length)
        {
            if (datos[i] != Marca)
                return null;
            var marcador = datos[i + 1];
            if (marcador == Marca)
            {
                i++; // relleno entre segmentos
                continue;
            }
            if (marcador == Eoi)
                return null; // sin imagen
            var largo = (datos[i + 2] << 8) | datos[i + 3];
            if (largo < 2 || i + 2 + largo > datos.Length)
                return null;

            if (marcador == Sos)
            {
                salida.Write(datos, i, datos.Length - i);
                return salida.ToArray();
            }
            var esMetadato = marcador is > App0 and <= App15 or Com;
            if (!esMetadato)
                salida.Write(datos, i, 2 + largo);
            i += 2 + largo;
        }
        return null;
    }
}
