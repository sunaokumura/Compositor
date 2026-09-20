// Compositor — image import incl. HEIC/TIFF probe (Mac ImageImporter.decode subset).
// SkiaSharp 2.88.9 ships PNG/JPEG/BMP decoders; HEIC and TIFF have no bundled decoder,
// so those two are detected by extension/signature and reported unsupported (documented
// in README/FEATURE_GAP instead of silently failing).
using SkiaSharp;

namespace Compositor;

public static class ImageImport
{
    public static readonly string[] PixelExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    public static readonly string[] ProbedExtensions = { ".heic", ".heif", ".tiff", ".tif" };
    public const long MaxPixels = 100_000_000;
    public const int MaxSide = 30_000;

    public class Result
    {
        public SKBitmap Bitmap;
        public string Name;
        public bool Unsupported;   // HEIC/TIFF without a decoder
        public string Error;
    }

    static bool IsHeicSig(byte[] b) =>
        b.Length > 12 && b[4] == (byte)'f' && b[5] == (byte)'t' && b[6] == (byte)'y' && b[7] == (byte)'p';
    static bool IsTiffSig(byte[] b) =>
        b.Length > 4 && ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 42) || (b[0] == 0x4D && b[1] == 0x4D && b[3] == 42));

    /// <summary>Decode a file within the 100MP/30k budget. HEIC/TIFF return Unsupported=true.</summary>
    public static Result DecodeFile(string path, long remainingPixels = MaxPixels)
    {
        var r = new Result { Name = Path.GetFileNameWithoutExtension(path) };
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) { r.Error = ex.Message; return r; }
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ProbedExtensions.Contains(ext) || IsHeicSig(bytes) || IsTiffSig(bytes))
        {
            // Give the bundled decoders one chance (a future SkiaSharp may add them),
            // then report unsupported so the UI can say so instead of "damaged file".
            // (SKBitmap.Decode throws on garbage instead of returning null.)
            SKBitmap probe = null;
            try { probe = SKBitmap.Decode(bytes); } catch { probe = null; }
            if (probe != null)
            {
                r.Bitmap = probe;
                return r;
            }
            r.Unsupported = true;
            r.Error = "HEIC/TIFF need a system decoder that SkiaSharp 2.88.9 does not bundle. Convert to PNG/JPEG first.";
            return r;
        }
        SKBitmap bmp;
        try { bmp = SKBitmap.Decode(bytes); }
        catch (Exception ex) { r.Error = ex.Message; return r; }
        if (bmp == null) { r.Error = "Could not decode. Choose PNG, JPEG, BMP or WebP."; return r; }
        if (bmp.Width < 1 || bmp.Height < 1 || bmp.Width > MaxSide || bmp.Height > MaxSide
            || (long)bmp.Width * bmp.Height > remainingPixels)
        { bmp.Dispose(); r.Error = "Exceeds the 100-megapixel / 30000px import budget."; return r; }
        r.Bitmap = bmp;
        return r;
    }

    public static long DocumentPixels(Document doc)
    {
        long n = 0;
        foreach (var l in doc.Layers)
            if (l.Bitmap != null) n += (long)l.Bitmap.Width * l.Bitmap.Height;
        return n;
    }
}
