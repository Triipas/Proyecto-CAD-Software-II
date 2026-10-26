using Android.Graphics;
using Cad.Contratos.Reportes;
using Cad.Movil.Servicios;
using AExif = Android.Media.ExifInterface;

namespace Cad.Movil.Platforms.Android;

// DP08 ampliada (26/10/2026): fotos del reporte con la cámara o la galería (MediaPicker). Cada foto se vuelve a codificar aquí
// como JPEG con el lado mayor de hasta 1600 px: así llega como JPEG aunque sea HEIC o PNG, pesa unos cientos de KB y, al
// escribirse desde un Bitmap, sale sin EXIF (ubicación GPS, modelo de la cámara, fecha). La orientación se aplica antes.
public sealed class FotosAndroid : IFotosReporte
{
    public bool PuedeTomar => MediaPicker.Default.IsCaptureSupported;

    public async Task<byte[]?> TomarAsync()
    {
        var foto = await MediaPicker.Default.CapturePhotoAsync();
        return foto is null ? null : await PrepararAsync(foto);
    }

    public async Task<IReadOnlyList<byte[]>> ElegirAsync(int maximo)
    {
        if (maximo <= 0)
            return [];
        var elegidas = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = maximo, Title = "Fotos del reporte" });
        var preparadas = new List<byte[]>();
        foreach (var foto in elegidas ?? [])
        {
            if (foto is null) continue;
            preparadas.Add(await PrepararAsync(foto));
        }
        return preparadas;
    }

    private static async Task<byte[]> PrepararAsync(FileResult foto)
    {
        using var copia = new MemoryStream();
        await using (var origen = await foto.OpenReadAsync())
            await origen.CopyToAsync(copia);
        var datos = copia.ToArray();
        return await Task.Run(() => Preparar(datos));
    }

    internal static byte[] Preparar(byte[] datos)
    {
        // Primero solo las medidas, para decodificar ya reducida (una foto de 12 MP no cabe cómoda en memoria).
        var medidas = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(datos, 0, datos.Length, medidas);
        if (medidas.OutWidth <= 0 || medidas.OutHeight <= 0)
            throw new FotoNoUsableException("Ese archivo no es una foto que se pueda adjuntar.");
        var muestra = 1;
        while (Math.Max(medidas.OutWidth, medidas.OutHeight) / (muestra * 2) >= LimitesFotosReporte.LadoMayorPixeles)
            muestra *= 2;

        using var leida = BitmapFactory.DecodeByteArray(datos, 0, datos.Length, new BitmapFactory.Options { InSampleSize = muestra })
            ?? throw new FotoNoUsableException("Ese archivo no es una foto que se pueda adjuntar.");
        using var orientada = Orientar(leida, datos);
        var escala = Math.Min(1d, (double)LimitesFotosReporte.LadoMayorPixeles / Math.Max(orientada.Width, orientada.Height));
        using var final = escala < 1
            ? Bitmap.CreateScaledBitmap(orientada, (int)Math.Round(orientada.Width * escala), (int)Math.Round(orientada.Height * escala), true)!
            : orientada.Copy(orientada.GetConfig() ?? Bitmap.Config.Argb8888!, false)!;

        // Si una foto con mucho detalle pasa de 1 MB, se baja la calidad.
        foreach (var calidad in new[] { LimitesFotosReporte.CalidadJpeg, 60, 45 })
        {
            using var salida = new MemoryStream();
            final.Compress(Bitmap.CompressFormat.Jpeg!, calidad, salida);
            if (salida.Length <= LimitesFotosReporte.MaximoBytes)
                return salida.ToArray();
        }
        throw new FotoNoUsableException("La foto es demasiado pesada aun reducida. Prueba con otra.");
    }

    // La cámara guarda la rotación en el EXIF; como el EXIF no se conserva, se aplica a los píxeles.
    private static Bitmap Orientar(Bitmap bitmap, byte[] datos)
    {
        int orientacion;
        try
        {
            using var flujo = new MemoryStream(datos);
            orientacion = new AExif(flujo).GetAttributeInt(AExif.TagOrientation, (int)global::Android.Media.Orientation.Normal);
        }
        catch (Exception)
        {
            orientacion = (int)global::Android.Media.Orientation.Normal;
        }
        var grados = (global::Android.Media.Orientation)orientacion switch
        {
            global::Android.Media.Orientation.Rotate90 => 90,
            global::Android.Media.Orientation.Rotate180 => 180,
            global::Android.Media.Orientation.Rotate270 => 270,
            _ => 0,
        };
        if (grados == 0)
            return bitmap.Copy(bitmap.GetConfig() ?? Bitmap.Config.Argb8888!, false)!;
        using var matriz = new Matrix();
        matriz.PostRotate(grados);
        return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matriz, true)!;
    }
}
