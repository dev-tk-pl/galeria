using Windows.Graphics.Imaging;
using Windows.Storage;

namespace GaleriaDesktop;

public static class Obrazy
{
    // Dekoduje zdjęcie z uwzględnieniem orientacji EXIF; opcjonalnie obraca o wielokrotność 90°
    // i zmniejsza tak, by dłuższy bok miał najwyżej maksBok pikseli (0 = pełna rozdzielczość).
    public static async Task<SoftwareBitmap> Dekoduj(string sciezka, BitmapRotation obrot = BitmapRotation.None, uint maksBok = 0)
    {
        var plik = await StorageFile.GetFileFromPathAsync(sciezka);
        using var strumien = await plik.OpenReadAsync();
        var dekoder = await BitmapDecoder.CreateAsync(strumien);
        var przeksztalcenie = new BitmapTransform { Rotation = obrot };
        var dluzszyBok = Math.Max(dekoder.PixelWidth, dekoder.PixelHeight);
        if (maksBok > 0 && dluzszyBok > maksBok)
        {
            przeksztalcenie.ScaledWidth = (uint)Math.Round(dekoder.PixelWidth * (double)maksBok / dluzszyBok);
            przeksztalcenie.ScaledHeight = (uint)Math.Round(dekoder.PixelHeight * (double)maksBok / dluzszyBok);
            przeksztalcenie.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        return await dekoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            przeksztalcenie, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
    }
}
