using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class JpegPaletteTests
{
    static Document CheckerDoc(int w = 64, int h = 48)
    {
        var doc = new Document { Width = w, Height = h };
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool even = ((x / 4) + (y / 4)) % 2 == 0;
                bmp.SetPixel(x, y, even ? new SKColor(200, 30, 30) : new SKColor(30, 30, 200));
            }
        doc.Layers.Add(new Layer { Name = "checker", Bitmap = bmp });
        return doc;
    }

    [Fact]
    public void Flatten_TransparentOverMatte()
    {
        var doc = new Document { Width = 4, Height = 4 };   // empty = transparent
        using var white = JpegExport.Flatten(doc, new JpegOptions { MatteR = 255, MatteG = 255, MatteB = 255 });
        Assert.Equal(SKColors.White, white.GetPixel(1, 1));
        using var black = JpegExport.Flatten(doc, new JpegOptions { MatteR = 0, MatteG = 0, MatteB = 0 });
        Assert.Equal(SKColors.Black, black.GetPixel(1, 1));
    }

    [Fact]
    public void Encode_QualityMonotonicOnTexture()
    {
        var doc = CheckerDoc();
        using var flat = JpegExport.Flatten(doc, new JpegOptions());
        var hi = JpegExport.Encode(flat, 0.95);
        var lo = JpegExport.Encode(flat, 0.1);
        Assert.True(hi.Length >= lo.Length, $"hi={hi.Length} lo={lo.Length}");
        Assert.True(hi.Length > 0 && lo.Length > 0);
    }

    [Fact]
    public void Export_ThenDecodePreview_Bounded()
    {
        var doc = CheckerDoc(1600, 1200);
        var bytes = JpegExport.Export(doc, new JpegOptions { Quality = 0.85 });
        using var pv = JpegExport.DecodePreview(bytes);
        Assert.True(Math.Max(pv.Width, pv.Height) <= JpegExport.PreviewMax);
    }

    [Fact]
    public void CopyMergedPng_DecodesToDocSize()
    {
        var doc = CheckerDoc();
        var bytes = JpegExport.CopyMergedPng(doc);
        using var bmp = SKBitmap.Decode(bytes);
        Assert.NotNull(bmp);
        Assert.Equal(64, bmp.Width);
        Assert.Equal(48, bmp.Height);
    }

    [Fact]
    public void Palette_HexRoundtrip()
    {
        Assert.Equal("FF0000", new PaletteColor(1, 0, 0).Hex);
        Assert.Equal(new PaletteColor(1, 0, 0), PaletteColor.FromHex("FF0000"));
        Assert.Equal(new PaletteColor(1, 0, 0), PaletteColor.FromHex("#F00"));
        Assert.Equal(new PaletteColor(1, 0, 0), PaletteColor.FromHex("f00"));
        Assert.Null(PaletteColor.FromHex("xyz"));
        Assert.Null(PaletteColor.FromHex("12345"));
    }

    [Fact]
    public void PickerHSB_HueSurvivesGray_AndSatSurvivesBlack()
    {
        var hsb = new PickerHSB(new PaletteColor(1, 0, 0));
        double hue = hsb.Hue;
        hsb.SetRGB(new PaletteColor(0.5, 0.5, 0.5));   // gray: hue kept
        Assert.Equal(hue, hsb.Hue, precision: 9);
        var hsb2 = new PickerHSB(new PaletteColor(1, 0, 0));
        hsb2.SetRGB(new PaletteColor(0, 0, 0));        // black: saturation kept
        Assert.Equal(1, hsb2.Saturation, precision: 9);
    }

    [Fact]
    public void Palette_SwapResetMaskMode()
    {
        var p = new PaletteState();
        p.Foreground = new PaletteColor(1, 0, 0);
        p.Background = new PaletteColor(0, 0, 1);
        p.Swap();
        Assert.Equal(new PaletteColor(0, 0, 1), p.Foreground);
        p.Reset();
        Assert.Equal(PaletteColor.Black, p.Foreground);
        Assert.Equal(PaletteColor.White, p.Background);
        // Mask mode: FG/BG choose reveal vs hide.
        p.MaskSelected = true;
        p.MaskPaintWhite = true;
        Assert.Equal(PaletteColor.White, p.PaintColor(false));
        Assert.Equal(PaletteColor.Black, p.PaintColor(true));
        p.Set(PaletteColor.Black, background: false);
        Assert.False(p.MaskPaintWhite);
    }

    [Fact]
    public void Palette_AddSwatchDedupes_AndCaps()
    {
        var p = new PaletteState();
        int before = p.Swatches.Count;
        Assert.True(p.AddSwatch(new PaletteColor(0.1, 0.2, 0.3)));
        Assert.Equal(before + 1, p.Swatches.Count);
        Assert.False(p.AddSwatch(new PaletteColor(0.1, 0.2, 0.3)));  // duplicate
        Assert.True(p.RemoveSwatchAt(p.Swatches.Count - 1));
        Assert.False(p.RemoveSwatchAt(999));
    }

    [Fact]
    public void SampleComposite_InsideOutside()
    {
        var doc = CheckerDoc(16, 16);
        var inside = PaletteState.SampleComposite(doc, 2, 2);
        Assert.NotNull(inside);
        Assert.Null(PaletteState.SampleComposite(doc, -1, 0));
        Assert.Null(PaletteState.SampleComposite(doc, 99, 99));
        var empty = new Document { Width = 8, Height = 8 };
        Assert.Null(PaletteState.SampleComposite(empty, 2, 2));
    }

    [Fact]
    public void ImageImport_HeicTiff_ReportedUnsupported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "comp-heic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Minimal ftyp box claiming HEIC.
            var heic = new byte[16];
            heic[4] = (byte)'f'; heic[5] = (byte)'t'; heic[6] = (byte)'y'; heic[7] = (byte)'p';
            string hp = Path.Combine(dir, "a.heic");
            File.WriteAllBytes(hp, heic);
            var r1 = ImageImport.DecodeFile(hp);
            Assert.True(r1.Unsupported);
            Assert.NotNull(r1.Error);

            var tiff = new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0 };
            string tp = Path.Combine(dir, "b.tiff");
            File.WriteAllBytes(tp, tiff);
            var r2 = ImageImport.DecodeFile(tp);
            Assert.True(r2.Unsupported);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ImageImport_BudgetExceeded_ReportsError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "comp-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var bmp = new SKBitmap(10, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(SKColors.Red);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            string p = Path.Combine(dir, "a.png");
            File.WriteAllBytes(p, data.ToArray());
            var r = ImageImport.DecodeFile(p, remainingPixels: 10);
            Assert.Null(r.Bitmap);
            Assert.NotNull(r.Error);
        }
        finally { Directory.Delete(dir, true); }
    }
}
