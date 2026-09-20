using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>Adjustment + filter engine tests (Mac Curves / Levels(+Automatic) /
/// GradientMap / Exposure / HueSaturation / Grain / Noise / Lens / Gaussian+Motion
/// blur-margin / LayerAdjustment parity). Every test checks pixel values.</summary>
public class AdjustmentTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    static Layer PixelLayer(SKColor c, int w = 8, int h = 8) =>
        new() { Name = "t", Bitmap = Solid(w, h, c), Position = new SKPoint(0, 0) };

    // ---------- Curves ----------
    [Fact]
    public void Curves_Default_IsIdentity()
    {
        var s = new CurvesSettings();
        Assert.True(s.IsValid);
        var l = PixelLayer(new SKColor(10, 200, 90, 255));
        LayerFilters.ApplyCurves(l, s);
        Assert.Equal(new SKColor(10, 200, 90, 255), l.Bitmap.GetPixel(3, 3));
    }

    [Fact]
    public void Curves_DarkenMidtones_Maps128Down()
    {
        var s = new CurvesSettings();
        // Per-channel curve only (channel 0 = master stays identity: Mac applies
        // channel first, then master, so setting all four would double-apply).
        for (int ch = 1; ch <= 3; ch++) { s.Channels[ch].Clear(); s.Channels[ch].Add(new(0, 0)); s.Channels[ch].Add(new(128, 64)); s.Channels[ch].Add(new(255, 255)); }
        Assert.True(s.IsValid);
        Assert.Equal(64, s.Value(128, 1), 1);   // Hermite through the handle
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 4, 4);
        LayerFilters.ApplyCurves(l, s);
        var p = l.Bitmap.GetPixel(1, 1);
        Assert.InRange(p.Red, 60, 70);
        Assert.Equal(p.Red, p.Green);
        Assert.Equal(p.Red, p.Blue);
    }

    [Fact]
    public void Curves_Invalid_Throws()
    {
        var s = new CurvesSettings();
        s.Channels[0].Clear(); s.Channels[0].Add(new(0, 0));   // single point
        Assert.False(s.IsValid);
        Assert.Throws<InvalidOperationException>(() => LayerFilters.ApplyCurves(PixelLayer(SKColors.White), s));
    }

    [Fact]
    public void Curves_RedOnly_LeavesOtherChannels()
    {
        var s = new CurvesSettings();
        s.Channels[1].Clear(); s.Channels[1].Add(new(0, 0)); s.Channels[1].Add(new(255, 0)); // red -> 0
        var l = PixelLayer(new SKColor(200, 100, 50, 255), 4, 4);
        LayerFilters.ApplyCurves(l, s);
        var p = l.Bitmap.GetPixel(0, 0);
        Assert.Equal(0, p.Red);
        Assert.Equal(100, p.Green);
        Assert.Equal(50, p.Blue);
    }

    // ---------- Levels ----------
    [Fact]
    public void Levels_Identity_LeavesPixels()
    {
        var l = PixelLayer(new SKColor(11, 22, 33, 255));
        LayerFilters.ApplyLevels(l, new LevelsSettings());
        Assert.Equal(new SKColor(11, 22, 33, 255), l.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Levels_Stretch_MidpointMapsHalf()
    {
        var s = new LevelsSettings();
        s.Ranges[0] = new LevelRange(100, 200);   // RGB master
        var l = PixelLayer(new SKColor(150, 150, 150, 255), 4, 4);
        LayerFilters.ApplyLevels(l, s);
        var p = l.Bitmap.GetPixel(0, 0);   // (150-100)/100 = 0.5 -> ~128
        Assert.InRange(p.Red, 124, 132);
        var lo = PixelLayer(new SKColor(100, 100, 100, 255), 2, 2);
        LayerFilters.ApplyLevels(lo, s);
        Assert.Equal(0, lo.Bitmap.GetPixel(0, 0).Red);
        var hi = PixelLayer(new SKColor(200, 200, 200, 255), 2, 2);
        LayerFilters.ApplyLevels(hi, s);
        Assert.Equal(255, hi.Bitmap.GetPixel(0, 0).Red);
    }

    [Fact]
    public void Levels_Gamma2_BrightensMidtones()
    {
        var s = new LevelsSettings();
        var r = s.Ranges[0]; r.Gamma = 2; s.Ranges[0] = r;
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 2, 2);  // sqrt(0.5) ~= 0.707 -> ~180
        LayerFilters.ApplyLevels(l, s);
        Assert.InRange(l.Bitmap.GetPixel(0, 0).Red, 175, 186);
    }

    [Fact]
    public void Levels_PerChannel_RedCutOnly()
    {
        var s = new LevelsSettings();
        s.Ranges[1] = new LevelRange(128, 255);   // red channel
        var dark = PixelLayer(new SKColor(64, 32, 16, 255), 2, 2);
        LayerFilters.ApplyLevels(dark, s);
        var p = dark.Bitmap.GetPixel(0, 0);
        Assert.Equal(0, p.Red);
        Assert.Equal(32, p.Green);
        Assert.Equal(16, p.Blue);
    }

    [Fact]
    public void Levels_AutoContrast_FindsEndpoints()
    {
        var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                bmp.SetPixel(x, y, new SKColor((byte)(50 + x * 18), (byte)(50 + x * 18), (byte)(50 + x * 18), 255));
        var hist = LevelsOps.Histogram4(bmp);
        var auto = LevelsOps.AutoSettings(hist, LevelsAutoMode.Contrast);
        Assert.InRange(auto.Ranges[0].Black, 40, 60);
        Assert.InRange(auto.Ranges[0].White, 165, 185);
    }

    [Fact]
    public void Levels_SampleBlack_SetsBlackPoint()
    {
        var s = LevelsOps.Sample(new LevelsSettings(), new[] { 0.5, 0.25, 0.75 }, LevelsSampleMode.Black);
        Assert.InRange(s.Ranges[1].Black, 126, 129);
        Assert.Equal(255, s.Ranges[1].White);
    }

    [Fact]
    public void Levels_Histogram_SolidRed_Counts()
    {
        using var bmp = Solid(2, 2, new SKColor(255, 0, 0, 255));
        var h = LevelsOps.Histogram4(bmp);
        Assert.Equal(4, h[1][255], 3);   // red channel bin
        Assert.Equal(4, h[2][0], 3);     // green at 0
        Assert.True(LevelsOps.DisplayScale(h[1]) > 0);
    }

    // ---------- Gradient Map ----------
    [Fact]
    public void GradientMap_BlackWhite_MidStaysMid()
    {
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 4, 4);
        LayerFilters.ApplyGradientMap(l, new GradientMapSettings());
        var p = l.Bitmap.GetPixel(0, 0);
        Assert.InRange(p.Red, 124, 132);
        Assert.Equal(p.Red, p.Green);
        Assert.Equal(p.Red, p.Blue);
    }

    [Fact]
    public void GradientMap_RedBlue_EndsMap()
    {
        var s = new GradientMapSettings
        { Shadows = new AdjustmentColor(1, 0, 0), Highlights = new AdjustmentColor(0, 0, 1) };
        var black = PixelLayer(SKColors.Black, 2, 2);
        LayerFilters.ApplyGradientMap(black, s);
        Assert.Equal(new SKColor(255, 0, 0, 255), black.Bitmap.GetPixel(0, 0));
        var white = PixelLayer(SKColors.White, 2, 2);
        LayerFilters.ApplyGradientMap(white, s);
        Assert.Equal(new SKColor(0, 0, 255, 255), white.Bitmap.GetPixel(0, 0));
        var rev = new GradientMapSettings
        { Shadows = new AdjustmentColor(1, 0, 0), Highlights = new AdjustmentColor(0, 0, 1), Reversed = true };
        var black2 = PixelLayer(SKColors.Black, 2, 2);
        LayerFilters.ApplyGradientMap(black2, rev);
        Assert.Equal(new SKColor(0, 0, 255, 255), black2.Bitmap.GetPixel(0, 0));
    }

    // ---------- Exposure ----------
    [Fact]
    public void Exposure_Identity_LeavesPixels()
    {
        var l = PixelLayer(new SKColor(11, 22, 33, 255));
        LayerFilters.ApplyExposure(l, new ExposureSettings());
        Assert.Equal(new SKColor(11, 22, 33, 255), l.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Exposure_PlusOneStop_Brightens()
    {
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 2, 2);
        LayerFilters.ApplyExposure(l, new ExposureSettings { Exposure = 1 });
        Assert.True(l.Bitmap.GetPixel(0, 0).Red > 150);
    }

    // ---------- Hue/Saturation ----------
    [Fact]
    public void Hue_MasterDesaturate_GraysPixel()
    {
        var s = new HueSaturationSettings(0, -100, 0);
        var l = PixelLayer(new SKColor(200, 50, 50, 255), 4, 4);
        LayerFilters.ApplyHueSaturation(l, s);
        var p = l.Bitmap.GetPixel(0, 0);
        Assert.Equal(p.Red, p.Green);
        Assert.Equal(p.Red, p.Blue);
    }

    [Fact]
    public void Hue_Shift180_RedBecomesCyan()
    {
        var s = new HueSaturationSettings(180, 0, 0);
        var l = PixelLayer(new SKColor(255, 0, 0, 255), 2, 2);
        LayerFilters.ApplyHueSaturation(l, s);
        var p = l.Bitmap.GetPixel(0, 0);
        Assert.InRange(p.Red, 0, 30);
        Assert.InRange(p.Green, 225, 255);
        Assert.InRange(p.Blue, 225, 255);
    }

    [Fact]
    public void Hue_RedsBand_WeightCenterVsOpposite()
    {
        var s = new HueSaturationSettings();
        Assert.Equal(1, s.Weight(ColorRange.Reds, 0), 3);
        Assert.Equal(0, s.Weight(ColorRange.Reds, 180), 3);
        Assert.Equal(1, s.Weight(ColorRange.Master, 180), 3);
    }

    [Fact]
    public void Hue_BandOps_CenterIncludeExclude()
    {
        var band = ColorRange.Reds.DefaultBand();
        var centered = band.Centered(180);
        Assert.Equal(1, centered.Weight(180), 3);
        var wider = band.Including(90);
        Assert.True(wider.Weight(90) > band.Weight(90));
        var narrower = band.Excluding(0);
        Assert.Equal(0, narrower.Weight(0), 3);
    }

    [Fact]
    public void Hue_SampledHue_RedGreenGray()
    {
        using var bmp = Solid(3, 1, SKColors.Black);
        bmp.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bmp.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
        bmp.SetPixel(2, 0, new SKColor(128, 128, 128, 255));
        Assert.Equal(0, HueOps.SampledHue(bmp, 0, 0)!.Value, 1);
        Assert.Equal(120, HueOps.SampledHue(bmp, 1, 0)!.Value, 1);
        Assert.Null(HueOps.SampledHue(bmp, 2, 0));   // near-neutral: no hue
    }

    [Fact]
    public void Hue_Colorize_TintsGray()
    {
        var s = HueSaturationSettings.ColorizeStart(240);
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 2, 2);
        LayerFilters.ApplyHueSaturation(l, s);
        var p = l.Bitmap.GetPixel(0, 0);
        Assert.True(p.Blue > p.Red);   // blue hue dominates
    }

    // ---------- Grain ----------
    [Fact]
    public void Grain_ZeroAmount_LeavesPixels()
    {
        var l = PixelLayer(new SKColor(128, 128, 128, 255));
        LayerFilters.ApplyGrain(l, new GrainSettings { Amount = 0 });
        Assert.Equal(new SKColor(128, 128, 128, 255), l.Bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void Grain_SameSeed_Deterministic_DifferentSeed_Differs()
    {
        var a = PixelLayer(new SKColor(128, 128, 128, 255), 8, 8);
        var b = PixelLayer(new SKColor(128, 128, 128, 255), 8, 8);
        var g = new GrainSettings { Amount = 100, Size = 1.5, Roughness = 50, Seed = 42 };
        LayerFilters.ApplyGrain(a, g, seed: 42);
        LayerFilters.ApplyGrain(b, g, seed: 42);
        bool anyChanged = false, allEqual = true;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                if (a.Bitmap.GetPixel(x, y).Red != 128) anyChanged = true;
                if (a.Bitmap.GetPixel(x, y) != b.Bitmap.GetPixel(x, y)) allEqual = false;
            }
        Assert.True(anyChanged);
        Assert.True(allEqual);
        var c = PixelLayer(new SKColor(128, 128, 128, 255), 8, 8);
        LayerFilters.ApplyGrain(c, g, seed: 43);
        bool differs = false;
        for (int y = 0; y < 8 && !differs; y++)
            for (int x = 0; x < 8 && !differs; x++)
                if (c.Bitmap.GetPixel(x, y) != a.Bitmap.GetPixel(x, y)) differs = true;
        Assert.True(differs);
    }

    // ---------- Noise ----------
    [Fact]
    public void Noise_Uniform_ChangesPixels_Mono_KeepsDeltas()
    {
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 8, 8);
        LayerFilters.ApplyNoise(l, new NoiseSettings { Amount = 50, Seed = 7 });
        bool changed = false;
        for (int y = 0; y < 8 && !changed; y++)
            for (int x = 0; x < 8 && !changed; x++)
                if (l.Bitmap.GetPixel(x, y).Red != 128) changed = true;
        Assert.True(changed);
        var m = PixelLayer(new SKColor(110, 120, 130, 255), 8, 8);
        LayerFilters.ApplyNoise(m, new NoiseSettings { Amount = 20, Monochromatic = true, Seed = 7 });
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var p = m.Bitmap.GetPixel(x, y);
                Assert.Equal(p.Red - 110, p.Green - 120);   // same shift every channel
                Assert.Equal(p.Red - 110, p.Blue - 130);
            }
    }

    [Fact]
    public void Noise_Gaussian_ChangesPixels()
    {
        var l = PixelLayer(new SKColor(128, 128, 128, 255), 8, 8);
        LayerFilters.ApplyNoise(l, new NoiseSettings { Amount = 50, Gaussian = true, Seed = 9 });
        bool changed = false;
        for (int y = 0; y < 8 && !changed; y++)
            for (int x = 0; x < 8 && !changed; x++)
                if (l.Bitmap.GetPixel(x, y).Red != 128) changed = true;
        Assert.True(changed);
    }

    // ---------- Lens ----------
    [Fact]
    public void Lens_Zero_IsNoOp_NonZero_BendsEdgesKeepsCenter()
    {
        var l = PixelLayer(SKColors.White, 16, 16);
        l.Bitmap.SetPixel(8, 8, SKColors.Black);
        LayerFilters.ApplyLens(l, 0);
        Assert.Equal(SKColors.Black, l.Bitmap.GetPixel(8, 8));
        var bent = PixelLayer(SKColors.White, 16, 16);
        bent.Bitmap.SetPixel(0, 0, SKColors.Black);
        LayerFilters.ApplyLens(bent, 100);
        Assert.Equal(SKColors.White, bent.Bitmap.GetPixel(0, 0));   // corner pushed out of frame
        Assert.Equal(SKColors.White, bent.Bitmap.GetPixel(8, 8));  // center untouched
    }

    // ---------- Blur edge expansion ----------
    [Fact]
    public void GaussianExpand_GrowsCanvas_AndSpreads()
    {
        var l = new Layer { Name = "t", Bitmap = Solid(8, 8, SKColor.Empty), Position = new SKPoint(10, 10) };
        l.Bitmap.SetPixel(4, 4, SKColors.White);
        int m = LayerFilters.ApplyGaussianExpand(l, 2);
        Assert.Equal(BlurOps.GaussianMargin(2), m);
        Assert.Equal(8 + 2 * m, l.Bitmap.Width);
        Assert.Equal(10 - m, l.Position.X, 3);   // content stays in place
        Assert.True(l.Bitmap.GetPixel(m + 4 + 2, m + 4).Red > 0);   // spread past the dot
        Assert.Equal(0, l.Bitmap.GetPixel(0, 0).Red);               // far corner stays clear
    }

    [Fact]
    public void MotionExpand_GrowsCanvas_AndStreaksHorizontally()
    {
        var l = new Layer { Name = "t", Bitmap = Solid(16, 16, SKColor.Empty), Position = new SKPoint(0, 0) };
        l.Bitmap.SetPixel(8, 8, SKColors.White);
        int m = LayerFilters.ApplyMotionExpand(l, 0, 10);
        Assert.Equal(BlurOps.MotionMargin(10), m);
        Assert.Equal(16 + 2 * m, l.Bitmap.Width);
        Assert.True(l.Bitmap.GetPixel(m + 8 + 4, m + 8).Red > 0);   // streak along X
        Assert.Equal(0, l.Bitmap.GetPixel(m + 8, m + 8 - 5).Red);   // nothing vertical
    }

    [Fact]
    public void TrimTransparent_CropsToContent()
    {
        using var bmp = Solid(8, 8, SKColor.Empty);
        bmp.SetPixel(2, 3, SKColors.White);
        using var t = BlurOps.TrimTransparent(bmp);
        Assert.Equal(1, t.Width);
        Assert.Equal(1, t.Height);
    }

    // ---------- Adjustment layers ----------
    [Fact]
    public void AdjustmentLayer_Levels_AppliesToLayersBelow_NotAbove()
    {
        var doc = new Document { Width = 8, Height = 8 };
        doc.Layers.Add(PixelLayer(new SKColor(150, 150, 150, 255)));
        var adj = LayerFilters.NewAdjustmentLayer(AdjustmentKind.Levels, "Levels", 8, 8);
        adj.Adjustment.Levels.Ranges[0] = new LevelRange(100, 200);
        doc.Layers.Add(adj);
        using var flat = doc.Compose();
        Assert.InRange(flat.GetPixel(0, 0).Red, 124, 132);   // stretched below
        doc.Layers.Add(PixelLayer(new SKColor(150, 150, 150, 255)));   // above: untouched
        using var flat2 = doc.Compose();
        Assert.Equal(150, flat2.GetPixel(0, 0).Red);
    }

    [Fact]
    public void AdjustmentLayer_Undo_RestoresSettings()
    {
        var doc = new Document { Width = 4, Height = 4 };
        doc.Layers.Add(PixelLayer(SKColors.Gray));
        var undo = new UndoStack();
        undo.Push(doc);
        var adj = LayerFilters.NewAdjustmentLayer(AdjustmentKind.Exposure, "Exposure", 4, 4);
        adj.Adjustment.Exposure.Exposure = 1;
        doc.Layers.Add(adj);
        Assert.True(doc.HasAdjustmentLayers);
        undo.Undo(doc);
        Assert.False(doc.HasAdjustmentLayers);
    }

    [Fact]
    public void AdjustmentLayer_GradientMap_FactoryDefaults_BlackWhite()
    {
        var adj = LayerFilters.NewAdjustmentLayer(AdjustmentKind.GradientMap, "GM", 4, 4);
        Assert.True(adj.IsAdjustmentLayer);
        Assert.True(adj.Adjustment.IsValid);
    }
}
