// Compositor — JPEG output + Copy Merged (Mac ImageExporter.jpeg subset).
// Flatten over a matte color, encode at quality 0..1, decode a <=1000px preview.
using SkiaSharp;

namespace Compositor;

public class JpegOptions
{
    public double Quality = 0.85;   // 0..1
    public byte MatteR = 255, MatteG = 255, MatteB = 255;
    public JpegOptions Clone() => new() { Quality = Quality, MatteR = MatteR, MatteG = MatteG, MatteB = MatteB };
}

public static class JpegExport
{
    public const int PreviewMax = 1000;

    /// <summary>Flatten the composite over the matte (JPEG has no alpha).</summary>
    public static SKBitmap Flatten(Document doc, JpegOptions opts)
    {
        if (doc == null) throw new ArgumentException("No document.");
        if (doc.Width < 1 || doc.Height < 1) throw new ArgumentException("Bad canvas.");
        var flat = new SKBitmap(doc.Width, doc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(flat))
        {
            c.Clear(new SKColor(opts.MatteR, opts.MatteG, opts.MatteB));
            using var comp = doc.Compose();
            c.DrawBitmap(comp, 0, 0);
        }
        return flat;
    }

    public static byte[] Encode(SKBitmap flat, double quality)
    {
        quality = Math.Clamp(quality, 0, 1);
        using var img = SKImage.FromBitmap(flat);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, (int)Math.Round(quality * 100));
        return data.ToArray();
    }

    public static byte[] Export(Document doc, JpegOptions opts) => Encode(Flatten(doc, opts), opts.Quality);

    /// <summary>Decode a fitted preview (long side <= PreviewMax), like the Mac sheet's encoded preview.</summary>
    public static SKBitmap DecodePreview(byte[] jpeg)
    {
        var src = SKBitmap.Decode(jpeg) ?? throw new InvalidOperationException("JPEG decode failed.");
        int w = src.Width, h = src.Height;
        int m = Math.Max(w, h);
        if (m <= PreviewMax) return src;
        double s = (double)PreviewMax / m;
        var small = src.Resize(new SKImageInfo((int)(w * s), (int)(h * s)), SKFilterQuality.Medium);
        src.Dispose();
        return small ?? throw new InvalidOperationException("JPEG preview failed.");
    }

    /// <summary>Copy Merged: full-canvas composite as PNG bytes for the clipboard (Mac Copy Merged).</summary>
    public static byte[] CopyMergedPng(Document doc)
    {
        using var flat = doc.Compose();
        using var img = SKImage.FromBitmap(flat);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
