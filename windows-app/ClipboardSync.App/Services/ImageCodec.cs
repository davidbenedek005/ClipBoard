using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardSync.App.Services;

/// <summary>
/// JPEG for the wire. The encrypted plaintext is the UTF-8 of standard Base64
/// of these bytes, matching protocol-schema.json.
/// </summary>
public static class ImageCodec
{
    public const int MaxEdge = 1600;
    public const int MaxJpegBytes = 5 * 1024 * 1024;

    public static byte[]? ToJpeg(BitmapSource source)
    {
        var scaled = Scale(source);
        for (var quality = 85; quality >= 40; quality -= 15)
        {
            var jpeg = Encode(scaled, quality);
            if (jpeg.Length <= MaxJpegBytes)
            {
                return jpeg;
            }
        }

        return null;
    }

    private static BitmapSource Scale(BitmapSource source)
    {
        var longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest <= MaxEdge)
        {
            return source;
        }

        var ratio = (double)MaxEdge / longest;
        var scaled = new TransformedBitmap(source, new ScaleTransform(ratio, ratio));
        scaled.Freeze();
        return scaled;
    }

    private static byte[] Encode(BitmapSource source, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
