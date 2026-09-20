// Compositor — P1 非破壊深化の試験 (docs/WORLD_BEST_PAINT.md §4 P1).
// Adjustment Brush / Live層 / 覆面合成 / QuickShape / ベクター＋Correct line＋
// 磁石 / spare channel・Quick mask / Time-lapse / Persona・Simple・Contextual /
// .comp v8 往復。いずれも決定性・headless 可。
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

static class P1Docs
{
    public static Document New(int w = 32, int h = 32)
    {
        var doc = new Document { Width = w, Height = h, Name = "P1" };
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 100, 50, 255));
        doc.Layers.Add(new Layer { Name = "Base", Bitmap = bmp });
        doc.ActiveLayerId = doc.Layers[0].Id;
        return doc;
    }
}

public class P1AdjustmentBrushTests
{
    [Fact]
    public void BrushLayerStartsBlackMask_NoEffectUntilPainted()
    {
        var doc = P1Docs.New();
        var before = doc.Compose();
        var layer = AdjustmentBrushOps.CreateBrushLayer(doc, AdjustmentKind.Hsv, "AB");
        Assert.True(layer.IsAdjustmentLayer);
        Assert.True(MaskOps.HasMask(layer));
        Assert.All(layer.Mask, b => Assert.Equal(0, b));
        using var after = doc.Compose();
        Assert.Equal(before.GetPixel(5, 5), after.GetPixel(5, 5));
        before.Dispose();
    }

    [Fact]
    public void PaintedStrokeRevealsAdjustmentOnlyThere()
    {
        var doc = P1Docs.New();
        var adj = new LayerAdjustment
        {
            Kind = AdjustmentKind.Hsv,
            HueSat = new HueSaturationSettings(0, 0, 100, false),   // lightness +100 → 白へ
        };
        var layer = AdjustmentBrushOps.CreateBrushLayer(doc, AdjustmentKind.Hsv, "AB");
        layer.Adjustment = adj;
        AdjustmentBrushOps.PaintStroke(layer,
            new List<SKPoint> { new(2, 2), new(6, 6) }, 8f);
        Assert.True(layer.Mask.Max() > 0);
        Assert.True(layer.Mask.Min() == 0);   // 塗っていない所は閉じたまま
        using var bmp = doc.Compose();
        var painted = bmp.GetPixel(4, 4);
        var unpainted = bmp.GetPixel(28, 28);
        Assert.True(painted.Red > 200 && painted.Green > 200 && painted.Blue > 200);
        Assert.Equal(new SKColor(200, 100, 50, 255), unpainted);
    }
}

public class P1LiveLayerTests
{
    [Fact]
    public void CreateRetuneReorderToggle()
    {
        var doc = P1Docs.New();
        var live = LiveLayerOps.Create(doc, AdjustmentKind.GaussBlur, "Live Blur");
        Assert.True(MaskOps.IsActive(live));   // filter mask は白開始
        Assert.Equal(AdjustmentKind.GaussBlur, live.Adjustment.Kind);
        LiveLayerOps.Retune(live, a => a.GaussRadius = 3.0);
        Assert.Equal(3.0, live.Adjustment.GaussRadius);
        Assert.Throws<InvalidOperationException>(() => LiveLayerOps.Retune(live, a => a.GaussRadius = -5));
        int at = doc.Layers.IndexOf(live);
        Assert.True(LiveLayerOps.Reorder(doc, live.Id, up: false));
        Assert.Equal(at - 1, doc.Layers.IndexOf(live));
        Assert.True(LiveLayerOps.Reorder(doc, live.Id, up: true));
        Assert.Equal(at, doc.Layers.IndexOf(live));
        live.Visible = false;   // on/off は Visible
        using var bmp = doc.Compose();
        Assert.Equal(new SKColor(200, 100, 50, 255), bmp.GetPixel(4, 4));
    }

    [Fact]
    public void LiveNoiseAndLensApply()
    {
        var doc = P1Docs.New();
        var n = LiveLayerOps.Create(doc, AdjustmentKind.Noise, "Live Noise");
        n.Adjustment.Noise = new NoiseSettings { Amount = 100, Seed = 7 };
        using var bmp = doc.Compose();
        Assert.NotEqual(new SKColor(200, 100, 50, 255), bmp.GetPixel(4, 4));
        doc.Layers.Remove(n);
        var lens = LiveLayerOps.Create(doc, AdjustmentKind.Lens, "Live Lens");
        lens.Adjustment.LensDistortion = 0;   // k=0 は no-op
        using var bmp2 = doc.Compose();
        Assert.Equal(new SKColor(200, 100, 50, 255), bmp2.GetPixel(4, 4));
    }

    [Fact]
    public void FilterMaskModulatesLiveLayer()
    {
        var doc = P1Docs.New();
        var live = LiveLayerOps.Create(doc, AdjustmentKind.Hsv, "Live H");
        live.Adjustment.HueSat = new HueSaturationSettings(0, 0, 100, false);
        MaskOps.Fill(live, 0);
        MaskOps.PaintStroke(live, new List<SKPoint> { new(2, 2) }, 255, 6f);
        using var bmp = doc.Compose();
        Assert.True(bmp.GetPixel(2, 2).Red > 200);   // 開けた所だけ効く
        Assert.Equal(new SKColor(200, 100, 50, 255), bmp.GetPixel(28, 28));
    }
}

public class P1QuickShapeTests
{
    [Fact]
    public void StraightStrokeIsLine()
    {
        var pts = Enumerable.Range(0, 20).Select(i => new SKPoint(i * 5, i * 5)).ToList();
        Assert.Equal(QuickShapeKind.Line, QuickShapeOps.Fit(pts));
    }

    [Fact]
    public void ClosedRectIsRectangle()
    {
        var pts = new List<SKPoint>
        {
            new(0, 0), new(10, 0), new(20, 0), new(20, 10), new(20, 20),
            new(10, 20), new(0, 20), new(0, 10), new(0, 1),
        };
        Assert.Equal(QuickShapeKind.Rectangle, QuickShapeOps.Fit(pts));
    }

    [Fact]
    public void TriangleAndEllipse()
    {
        var tri = new List<SKPoint>
        {
            new(0, 0), new(10, 0), new(20, 0), new(15, 10), new(10, 20), new(5, 10), new(0, 1),
        };
        Assert.Equal(QuickShapeKind.Triangle, QuickShapeOps.Fit(tri));
        var circle = Enumerable.Range(0, 25)
            .Select(i => new SKPoint(16 + 14 * MathF.Cos(i / 24f * 2 * MathF.PI), 16 + 14 * MathF.Sin(i / 24f * 2 * MathF.PI)))
            .ToList();
        Assert.Equal(QuickShapeKind.Ellipse, QuickShapeOps.Fit(circle));
    }

    [Fact]
    public void OpenCurveIsNone()
    {
        var pts = Enumerable.Range(0, 20).Select(i => new SKPoint(i * 2, MathF.Sin(i) * 10 + 16)).ToList();
        Assert.Equal(QuickShapeKind.None, QuickShapeOps.Fit(pts));
    }

    [Fact]
    public void Snap15AndSquare()
    {
        Assert.Equal(15, QuickShapeOps.Snap15(17));
        Assert.Equal(0, QuickShapeOps.Snap15(4));
        var snapped = QuickShapeOps.SnapLine45(new SKPoint(0, 0), new SKPoint(10, 1));
        Assert.True(Math.Abs(snapped.Y) < 0.01);
        var sq = QuickShapeOps.SquareConstrain(SKRect.Create(0, 0, 30, 10));
        Assert.Equal(sq.Width, sq.Height);
    }
}

public class P1VectorTests
{
    static VectorStroke Stroke() => new()
    {
        Points = new List<SKPoint> { new(0, 0), new(5, 1), new(10, 0), new(15, 1), new(20, 0) },
        Width = 4,
    };

    [Fact]
    public void MovePointSetWidthSimplify()
    {
        var s = Stroke();
        VectorStrokeOps.MovePoint(s, 2, new SKPoint(10, 10));
        Assert.Equal(new SKPoint(10, 10), s.Points[2]);
        VectorStrokeOps.SetWidth(s, 12);
        Assert.Equal(12, s.Width);
        VectorStrokeOps.SetWidth(s, 9999);
        Assert.Equal(VectorStrokeOps.MaxWidth, s.Width);
        int removed = VectorStrokeOps.Simplify(s, 3.0);
        Assert.True(removed >= 1);
        Assert.Throws<InvalidOperationException>(() => VectorStrokeOps.MovePoint(s, 99, new SKPoint(0, 0)));
    }

    [Fact]
    public void JoinAndMagnet()
    {
        var a = Stroke();
        var b = new VectorStroke { Points = new List<SKPoint> { new(21, 1), new(30, 0) }, Width = 6 };
        var joined = VectorStrokeOps.Join(a, b);
        Assert.Equal(7, joined.Points.Count);
        Assert.Equal(5, joined.Width);
        var target = new VectorStroke { Points = new List<SKPoint> { new(0, 0), new(10, 10), new(22, 2) }, Width = 4 };
        int snapped = VectorStrokeOps.MagnetSnap(target, new[] { b }, 8f);
        Assert.Equal(1, snapped);
        Assert.Equal(new SKPoint(21, 1), target.Points[2]);
        Assert.Equal(new SKPoint(10, 10), target.Points[1]);   // 中央は吸着しない
    }

    [Fact]
    public void RasterizeKeepsVector_UndoSafe()
    {
        var doc = P1Docs.New();
        var layer = new Layer { Name = "V" };
        doc.Layers.Add(layer);
        var s = new VectorStroke
        {
            Points = new List<SKPoint> { new(2, 2), new(28, 2), new(28, 28) }, Width = 3,
        };
        VectorStrokeOps.Rasterize(layer, s, SKColors.Black, doc.Width, doc.Height);
        Assert.True(layer.IsVectorLayer);
        Assert.Equal(3, layer.Vector.Points.Count);
        var snap = LayerSnapshot.Capture(doc);
        VectorStrokeOps.SetWidth(layer.Vector, 9);
        snap.Restore(doc);
        var restored = doc.Layers.First(l => l.Name == "V");
        Assert.Equal(3, restored.Vector.Width);
        Assert.True(restored.IsVectorLayer);
    }
}

public class P1ChannelQuickMaskTests
{
    [Fact]
    public void SpareChannelRoundTrip()
    {
        var doc = P1Docs.New();
        var mask = new byte[32 * 32];
        mask[0] = 255; mask[100] = 128;
        var ch = SpareChannelOps.Save(doc, "Sel1", mask, 32, 32);
        Assert.Equal("Sel1", ch.Name);
        var back = SpareChannelOps.Load(doc, 0);
        Assert.Equal(mask, back);
        back[0] = 0;
        Assert.Equal(255, doc.SpareChannels[0].Mask[0]);   // 複写で返る
        Assert.True(SpareChannelOps.Delete(doc, 0));
        Assert.Empty(doc.SpareChannels);
        Assert.False(SpareChannelOps.Delete(doc, 0));
        Assert.Throws<InvalidOperationException>(() => SpareChannelOps.Save(doc, "x", new byte[4], 4, 4));
    }

    [Fact]
    public void QuickMaskPaintCommit()
    {
        var doc = P1Docs.New();
        SelectionTools.SetRectSelection(doc, SKRect.Create(4, 4, 8, 8), SelectionMode.Replace);
        QuickMaskOps.Enable(doc);
        Assert.True(doc.QuickMaskEnabled);
        QuickMaskOps.Paint(doc, new List<SKPoint> { new(20, 20) }, 6f, 255);
        Assert.Equal(255, doc.QuickMask[20 * 32 + 20]);
        using var overlay = QuickMaskOps.RenderOverlay(doc);
        Assert.Equal(new SKColor(255, 0, 0, 90), overlay.GetPixel(0, 0));   // 非選択は赤
        Assert.Equal(SKColor.Empty, overlay.GetPixel(6, 6));
        QuickMaskOps.Commit(doc);
        Assert.False(doc.QuickMaskEnabled);
        Assert.NotNull(doc.SelectionMask);
    }
}

public class P1TimelapsePersonaTests
{
    [Fact]
    public void RecordOnlyWhenStarted_ExportManifest()
    {
        var doc = P1Docs.New();
        TimelapseOps.Record(doc, "brush");   // 開始前は無視
        Assert.Empty(doc.Timelapse);
        TimelapseOps.Start(doc);
        TimelapseOps.Record(doc, "brush");
        TimelapseOps.Record(doc, "  ");
        TimelapseOps.Record(doc, "export");
        Assert.Equal(2, doc.Timelapse.Count);
        TimelapseOps.Stop(doc);
        TimelapseOps.Record(doc, "ignored");
        Assert.Equal(2, doc.Timelapse.Count);
        string manifest = TimelapseOps.ExportManifest(doc);
        Assert.Contains("brush", manifest);
        Assert.Contains("export", manifest);
        TimelapseOps.Clear(doc);
        Assert.Empty(doc.Timelapse);
    }

    [Fact]
    public void PersonaToolsAndSimple()
    {
        Assert.Contains("Brush", PersonaOps.ToolsFor(PersonaKind.Paint));
        Assert.Contains("Clone", PersonaOps.ToolsFor(PersonaKind.Retouch));
        Assert.Contains("Crop", PersonaOps.ToolsFor(PersonaKind.Export));
        Assert.Equal("描く", PersonaOps.Label(PersonaKind.Paint));
        Assert.True(PersonaOps.IsSimpleVisible("Brush"));
        Assert.False(PersonaOps.IsSimpleVisible("QuickShape"));
        Assert.Equal("塗って調整: 塗った所だけ効く (覆面つき調整層)", ContextualHint.For("AdjustmentBrush"));
        Assert.Equal("", ContextualHint.For("NoSuchTool"));
    }
}

public class P1CompV8Tests
{
    [Fact]
    public void LiveFilterVectorChannelRoundTrip()
    {
        var doc = P1Docs.New();
        var live = LiveLayerOps.Create(doc, AdjustmentKind.Noise, "Live N");
        live.Adjustment.Noise = new NoiseSettings { Amount = 50, Seed = 3 };
        var vlayer = new Layer { Name = "V" };
        doc.Layers.Add(vlayer);
        VectorStrokeOps.Rasterize(vlayer,
            new VectorStroke { Points = new List<SKPoint> { new(1, 1), new(10, 10) }, Width = 5 },
            SKColors.Black, doc.Width, doc.Height);
        var mask = new byte[doc.Width * doc.Height];
        mask[7] = 200;
        SpareChannelOps.Save(doc, "Keep", mask, doc.Width, doc.Height);
        string dir = Path.Combine(Path.GetTempPath(), "p1v8-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            ProjectFormat.Save(doc, dir);
            var loaded = ProjectFormat.Load(dir);
            var loadedLive = loaded.Layers.First(l => l.Name == "Live N");
            Assert.True(loadedLive.IsAdjustmentLayer);
            Assert.Equal(AdjustmentKind.Noise, loadedLive.Adjustment.Kind);
            Assert.Equal(50, loadedLive.Adjustment.Noise.Amount);
            var loadedV = loaded.Layers.First(l => l.Name == "V");
            Assert.True(loadedV.IsVectorLayer);
            Assert.Equal(2, loadedV.Vector.Points.Count);
            Assert.Equal(5, loadedV.Vector.Width);
            Assert.Single(loaded.SpareChannels);
            Assert.Equal("Keep", loaded.SpareChannels[0].Name);
            Assert.Equal(200, loaded.SpareChannels[0].Mask[7]);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void V7FileStillLoads_NewSaveIsV8()
    {
        var doc = P1Docs.New();
        string dir = Path.Combine(Path.GetTempPath(), "p1v8b-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            ProjectFormat.Save(doc, dir);
            string json = File.ReadAllText(Path.Combine(dir, "manifest.json"));
            Assert.Contains("\"version\": 8", json);
            var loaded = ProjectFormat.Load(dir);
            Assert.Single(loaded.Layers);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void E2E_Import_P1Edit_Export_Headless()
    {
        // Import: PNG bytes -> DecodeFile (実機 Import の engine 路)
        using var src = new SKBitmap(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(120, 140, 160, 255));
        string tmp = Path.Combine(Path.GetTempPath(), $"p1e2e_{Guid.NewGuid():N}.png");
        string dir = Path.Combine(Path.GetTempPath(), "p1e2e-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            using (var img = SKImage.FromBitmap(src))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(tmp, data.ToArray());
            var dec = ImageImport.DecodeFile(tmp);
            Assert.False(dec.Unsupported, dec.Error);
            Assert.NotNull(dec.Bitmap);
            // 編集 (P1一巡り): 取込層 -> AdjBrush 塗り -> Live層 -> QuickShape -> Vector -> channel/QuickMask -> Timelapse
            var doc = new Document { Width = 48, Height = 48, Name = "P1E2E" };
            doc.Layers.Add(new Layer { Name = "imported", Bitmap = dec.Bitmap });
            doc.ActiveLayerId = doc.Layers[0].Id;
            TimelapseOps.Start(doc);
            var brush = AdjustmentBrushOps.CreateBrushLayer(doc, AdjustmentKind.Levels, "E2E Brush");
            brush.Adjustment.Levels = new LevelsSettings();
            AdjustmentBrushOps.PaintStroke(brush, new List<SKPoint> { new(4, 4), new(20, 20) }, 10f);
            TimelapseOps.Record(doc, "adjbrush");
            var live = LiveLayerOps.Create(doc, AdjustmentKind.GaussBlur, "E2E Live");
            LiveLayerOps.Retune(live, a => a.GaussRadius = 1.5);
            Assert.True(LiveLayerOps.Reorder(doc, live.Id, up: false));
            TimelapseOps.Record(doc, "live");
            var qsPts = Enumerable.Range(0, 20).Select(i => new SKPoint(i * 2, i * 2)).ToList();
            Assert.Equal(QuickShapeKind.Line, QuickShapeOps.Fit(qsPts));
            var vlayer = new Layer { Name = "E2E Vector" };
            doc.Layers.Add(vlayer);
            VectorStrokeOps.Rasterize(vlayer,
                new VectorStroke { Points = new List<SKPoint> { new(2, 44), new(44, 44) }, Width = 3 },
                SKColors.Black, doc.Width, doc.Height);
            Assert.True(vlayer.IsVectorLayer);
            TimelapseOps.Record(doc, "vector");
            var mask = new byte[48 * 48];
            mask[0] = 255;
            SpareChannelOps.Save(doc, "E2E", mask, 48, 48);
            QuickMaskOps.Enable(doc);
            QuickMaskOps.Paint(doc, new List<SKPoint> { new(40, 40) }, 6f, 255);
            QuickMaskOps.Commit(doc);
            Assert.NotNull(doc.SelectionMask);
            TimelapseOps.Stop(doc);
            Assert.Equal(3, doc.Timelapse.Count);
            // Export: 合成 PNG が復号でき、過程 manifest が出ること
            using var composed = doc.Compose();
            Assert.Equal(48, composed.Width);
            using (var img = SKImage.FromBitmap(composed))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                Assert.True(data.ToArray().Length > 100);
            byte[] jpeg = JpegExport.Export(doc, new JpegOptions { Quality = 0.85 });
            Assert.True(jpeg.Length > 100);
            string manifest = TimelapseOps.ExportManifest(doc);
            Assert.Contains("adjbrush", manifest);
            // .comp v8 往復: Live・Vector・channel が残ること
            ProjectFormat.Save(doc, dir);
            var loaded = ProjectFormat.Load(dir);
            Assert.True(loaded.Layers.Any(l => l.IsAdjustmentLayer && l.Adjustment.Kind == AdjustmentKind.Levels));
            Assert.True(loaded.Layers.Any(l => l.IsAdjustmentLayer && l.Adjustment.Kind == AdjustmentKind.GaussBlur));
            Assert.True(loaded.Layers.Any(l => l.IsVectorLayer));
            Assert.Single(loaded.SpareChannels);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
