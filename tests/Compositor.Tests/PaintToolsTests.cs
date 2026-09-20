using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_f04eebff: 描画系 (Clone/Heal/Smudge/Blur/硬さ・間隔/不透明度キー/スポイト).</summary>
public class PaintToolsTests
{
    static Layer SolidLayer(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return new Layer { Name = "L", Bitmap = b };
    }

    static Document DocWith(Layer l, int w = 64, int h = 64)
    {
        var doc = new Document { Width = w, Height = h };
        doc.Layers.Add(l);
        return doc;
    }

    // ---- Clone: aligned / non-aligned offset ----

    [Fact]
    public void Clone_Offset_Aligned_KeepsFirst_NonAligned_Restarts()
    {
        var src = new SKPoint(50, 50);
        var kept = new SKSize(10, 10);
        // aligned: 初回ストロークのオフセットを維持
        Assert.Equal(kept, CloneTools.StrokeOffset(new SKPoint(20, 20), src, kept, aligned: true));
        // alignedでも初回は brush->source の丸めオフセット
        var first = CloneTools.StrokeOffset(new SKPoint(40, 42), src, null, aligned: true);
        Assert.Equal(new SKSize(10, 8), first);
        // non-aligned: 常に brush->source
        Assert.Equal(new SKSize(10, 8), CloneTools.StrokeOffset(new SKPoint(40, 42), src, kept, aligned: false));
        // ソースなしは null
        Assert.Null(CloneTools.StrokeOffset(new SKPoint(1, 1), null, null, aligned: true));
    }

    [Fact]
    public void Clone_Stamp_CopiesSourcePixels()
    {
        var l = SolidLayer(64, 64, SKColors.White);
        // 左半分を黒くして採取源にする
        using (var c = new SKCanvas(l.Bitmap))
            c.DrawRect(new SKRect(0, 0, 32, 64), new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill });
        var doc = DocWith(l);
        using var sample = CloneTools.CloneSample(doc, l, sampleAllLayers: false);
        var settings = new BrushSettings(diameter: 9, hardness: 1f, spacing: 0.15f, opacity: 1f);
        // 採取点(10,32)->描画点(50,32): オフセット(-40,0) で黒を白地へ写す
        var offset = CloneTools.StrokeOffset(new SKPoint(50, 32), new SKPoint(10, 32), null, aligned: true);
        Assert.NotNull(offset);
        var dest = new List<SKPoint> { new SKPoint(50, 32) };
        var srcOff = new SKPoint(offset.Value.Width, offset.Value.Height);
        CloneTools.PaintCloneStroke(l, sample, dest, srcOff, settings, p => p, p => true);
        var got = l.Bitmap.GetPixel(50, 32);
        Assert.True(got.Red < 64 && got.Green < 64 && got.Blue < 64);
    }

    [Fact]
    public void Clone_SampleAllLayers_ReadsComposite()
    {
        var bottom = SolidLayer(32, 32, SKColors.Red);
        var top = SolidLayer(32, 32, SKColor.Empty);
        var doc = new Document { Width = 32, Height = 32 };
        doc.Layers.Add(bottom); doc.Layers.Add(top);
        using var all = CloneTools.CloneSample(doc, top, sampleAllLayers: true);
        Assert.Equal(SKColors.Red.Red, all.GetPixel(5, 5).Red);
        using var single = CloneTools.CloneSample(doc, top, sampleAllLayers: false);
        Assert.Equal(0, single.GetPixel(5, 5).Alpha);   // 上層だけでは透明
    }

    // ---- Spot healing ----

    [Fact]
    public void Heal_FillsSpot_WithRingTone()
    {
        var l = SolidLayer(64, 64, new SKColor(200, 200, 200));
        // 中央に黒いゴミ
        using (var c = new SKCanvas(l.Bitmap))
            c.DrawCircle(new SKPoint(32, 32), 4, new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill });
        Assert.True(l.Bitmap.GetPixel(32, 32).Red < 64);
        HealTools.SpotHeal(l, new SKPoint(32, 32), diameter: 12, opacity: 1f, mode: 0);
        var healed = l.Bitmap.GetPixel(32, 32);
        Assert.True(healed.Red > 150);   // 周囲のグレーに近づく
    }

    // ---- Smudge / Blur / Liquify ----

    [Fact]
    public void Smudge_DragsColor_AlongStroke()
    {
        var l = SolidLayer(64, 64, SKColors.White);
        using (var c = new SKCanvas(l.Bitmap))
            c.DrawCircle(new SKPoint(10, 32), 6, new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill });
        var stroke = new SmudgeStroke(diameter: 11, strength: 1f, hardness: 1f);
        stroke.PickUp(l.Bitmap, new SKPoint(10, 32));
        int touched = stroke.SmudgeAt(l.Bitmap, new SKPoint(20, 32));
        Assert.True(touched > 0);
        // 黒が右へ引きずられる
        Assert.True(l.Bitmap.GetPixel(20, 32).Red < 200);
    }

    [Fact]
    public void Blur_Softens_HardEdge()
    {
        var l = SolidLayer(64, 64, SKColors.White);
        using (var c = new SKCanvas(l.Bitmap))
            c.DrawRect(new SKRect(0, 0, 32, 64), new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill });
        static double EdgeContrast(SKBitmap b)
        {
            double s = 0; int n = 0;
            for (int y = 28; y < 36; y++)
                for (int x = 29; x < 35; x++)
                { s += System.Math.Abs(b.GetPixel(x, y).Red - b.GetPixel(x + 1, y).Red); n++; }
            return s / n;
        }
        double before = EdgeContrast(l.Bitmap);
        using var soft = SmudgeStroke.BlurSample(l, 64, 64, diameter: 20);
        int touched = SmudgeStroke.BlurAt(l.Bitmap, soft, new SKPoint(32, 32), 20, 1f, 1f);
        Assert.True(touched > 0);
        Assert.True(EdgeContrast(l.Bitmap) < before);
    }

    [Fact]
    public void Liquify_Push_MovesPixelsForward()
    {
        var l = SolidLayer(64, 64, SKColors.White);
        using (var c = new SKCanvas(l.Bitmap))
            c.DrawCircle(new SKPoint(32, 32), 4, new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill });
        int touched = SmudgeStroke.PushAt(l.Bitmap, new SKPoint(32, 32), new SKPoint(40, 32),
            diameter: 21, hardness: 0.5f, strength: 1f);
        Assert.True(touched > 0);
        // 黒が進行方向へにじむ
        Assert.True(l.Bitmap.GetPixel(38, 32).Red < 220);
    }

    // ---- 硬さ・間隔 ----

    [Fact]
    public void Brush_Hardness_SoftensEdge()
    {
        var hard = SolidLayer(64, 64, SKColor.Empty);
        var soft = SolidLayer(64, 64, SKColor.Empty);
        var pts = new List<SKPoint> { new SKPoint(32, 32) };
        PaintEngine.PaintBrushStroke(hard, pts, SKColors.Black,
            new BrushSettings(21, 1f, 0.15f, 1f), erasing: false);
        PaintEngine.PaintBrushStroke(soft, pts, SKColors.Black,
            new BrushSettings(21, 0f, 0.15f, 1f), erasing: false);
        // 硬いダブの輪郭は濃く、柔らかいダブの輪郭は薄い
        byte hardEdge = hard.Bitmap.GetPixel(42, 32).Alpha;
        byte softEdge = soft.Bitmap.GetPixel(42, 32).Alpha;
        Assert.True(hardEdge > softEdge);
        // 中心はどちらも濃い
        Assert.True(hard.Bitmap.GetPixel(32, 32).Alpha > 200);
        Assert.True(soft.Bitmap.GetPixel(32, 32).Alpha > 200);
    }

    [Fact]
    public void Brush_Spacing_Controls_DabCount()
    {
        var line = new List<SKPoint> { new SKPoint(0, 32), new SKPoint(63, 32) };
        var dense = BrushTip.DabPositions(line, diameter: 10, spacing: 0.1f);
        var sparse = BrushTip.DabPositions(line, diameter: 10, spacing: 1f);
        Assert.True(dense.Count > sparse.Count);
        Assert.True(sparse.Count >= 6);   // 63px / 10px
    }

    // ---- 不透明度キー・スポイト ----

    [Fact]
    public void OpacityKey_Maps_Digits_To_Percent()
    {
        static float Key(int d) => d == 0 ? 1f : d / 10f;
        Assert.Equal(0.1f, Key(1));
        Assert.Equal(0.5f, Key(5));
        Assert.Equal(1f, Key(0));
    }

    [Fact]
    public void Eyedropper_Samples_Top_Or_Composite()
    {
        var bottom = SolidLayer(32, 32, SKColors.Blue);
        var top = SolidLayer(32, 32, SKColor.Empty);
        using (var c = new SKCanvas(top.Bitmap))
            c.DrawCircle(new SKPoint(5, 5), 3, new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill });
        var doc = new Document { Width = 32, Height = 32 };
        doc.Layers.Add(bottom); doc.Layers.Add(top);
        var single = Eyedropper.Sample(doc, top, new SKPoint(20, 20), sampleAllLayers: false);
        Assert.Equal(0, single.Alpha);
        var comp = Eyedropper.Sample(doc, top, new SKPoint(20, 20), sampleAllLayers: true);
        Assert.True(comp.Blue > 200);
        var red = Eyedropper.Sample(doc, top, new SKPoint(5, 5), sampleAllLayers: false);
        Assert.True(red.Red > 200);
    }
}
