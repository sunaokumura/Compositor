using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>Transform system tests (Mac LayerTransform / Distort / CanvasSize / Crop /
/// TransformInspector / CanvasSizeSheet / ImageSizeSheet parity).</summary>
public class TransformToolsTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    static Layer MakeLayer(int w = 20, int h = 10, float x = 5, float y = 7)
        => new() { Name = "t", Bitmap = Solid(w, h, SKColors.Red), Position = new SKPoint(x, y) };

    static Document MakeDoc()
    {
        var doc = new Document { Width = 100, Height = 80 };
        doc.Layers.Add(MakeLayer());
        return doc;
    }

    // ---------- LayerPlacement ----------

    [Fact]
    public void Placement_Point_MapsUnitCorners()
    {
        var p = new LayerPlacement(new SKPoint(10, 20), new SKSize(30, 40));
        Assert.Equal(new SKPoint(10, 20), p.Point(new SKPoint(0, 0)));
        Assert.Equal(new SKPoint(40, 60), p.Point(new SKPoint(1, 1)));
        Assert.Equal(new SKPoint(25, 40), p.Center);
    }

    [Fact]
    public void Placement_Valid_RejectsDegenerate()
    {
        var ok = new LayerPlacement(new SKPoint(0, 0), new SKSize(10, 10));
        Assert.True(ok.IsValid);
        Assert.False(new LayerPlacement(new SKPoint(0, 0), new SKSize(0, 10)).IsValid);
        Assert.False(new LayerPlacement(new SKPoint(0, 0), new SKSize(10, 10), float.NaN).IsValid);
    }

    [Fact]
    public void Placement_ScalePercent_RoundTrip()
    {
        var p = new LayerPlacement(new SKPoint(0, 0), new SKSize(20, 10));
        Assert.Equal(200, p.ScalePercent(new SKSize(10, 10)));
        var back = p.ScaledToPercent(100, new SKSize(10, 10));
        Assert.Equal(10, back.Size.Width);
        Assert.Equal(10, back.Size.Height);
        // Center preserved.
        Assert.Equal(p.Center.X, back.Center.X, 3);
    }

    [Fact]
    public void Placement_Mirrored_FlipsAcrossCanvasMiddle()
    {
        var p = new LayerPlacement(new SKPoint(10, 0), new SKSize(20, 10), 30, false, false);
        var m = p.Mirrored(true, axis: 50);
        Assert.True(m.FlipH);
        Assert.Equal(-30, m.Rotation);
        // Center mirrored: old center 20 -> 80.
        Assert.Equal(80, m.Center.X, 3);
    }

    // ---------- TransformDrag ----------

    [Fact]
    public void Drag_Move_ShiftsOrigin()
    {
        var p = new LayerPlacement(new SKPoint(10, 10), new SKSize(20, 20));
        var d = new TransformDrag(p, new SKPoint(0, 0), TransformDrag.Mode.Move);
        var next = d.Updated(new SKPoint(3, -4), lockRatio: false, shift: false);
        Assert.Equal(13, next.Origin.X);
        Assert.Equal(6, next.Origin.Y);
    }

    [Fact]
    public void Drag_Move_Shift_LocksSingleAxis()
    {
        var p = new LayerPlacement(new SKPoint(10, 10), new SKSize(20, 20));
        var d = new TransformDrag(p, new SKPoint(0, 0), TransformDrag.Mode.Move);
        var next = d.Updated(new SKPoint(3, 8), lockRatio: false, shift: true);
        Assert.Equal(10, next.Origin.X);
        Assert.Equal(18, next.Origin.Y);
    }

    [Fact]
    public void Drag_Rotate_Shift_Snaps15deg()
    {
        var p = new LayerPlacement(new SKPoint(0, 0), new SKSize(20, 20));   // center (10,10)
        var d = new TransformDrag(p, new SKPoint(20, 10), TransformDrag.Mode.Rotate);
        var next = d.Updated(new SKPoint(10, 22), lockRatio: false, shift: true);
        Assert.Equal(90, next.Rotation, 0);
    }

    [Fact]
    public void Drag_Resize_LockRatio_KeepsAspect()
    {
        var p = new LayerPlacement(new SKPoint(0, 0), new SKSize(20, 10));
        // Bottom-right corner handle (index 4), drag +10/+10 with ratio lock.
        var d = new TransformDrag(p, new SKPoint(20, 10), TransformDrag.Mode.Resize, 4);
        var next = d.Updated(new SKPoint(30, 20), lockRatio: true, shift: false);
        Assert.Equal(2.0, next.Size.Width / next.Size.Height, 2);
    }

    // ---------- DistortWarp ----------

    [Fact]
    public void Distort_IsUsable_AcceptsQuad_RejectsBowtie()
    {
        var good = new[]
        {
            new SKPoint(0, 0), new SKPoint(30, 5), new SKPoint(28, 25), new SKPoint(2, 22),
        };
        Assert.True(DistortWarp.IsUsable(good));
        var bowtie = new[]
        {
            new SKPoint(0, 0), new SKPoint(10, 10), new SKPoint(0, 10), new SKPoint(10, 0),
        };
        Assert.False(DistortWarp.IsUsable(bowtie));
        var flat = new[]
        {
            new SKPoint(0, 0), new SKPoint(10, 0), new SKPoint(20, 0), new SKPoint(0, 10),
        };
        Assert.False(DistortWarp.IsUsable(flat));
    }

    [Fact]
    public void Distort_Homography_RoundTrip()
    {
        var c = new[]
        {
            new SKPoint(5, 5), new SKPoint(35, 8), new SKPoint(32, 40), new SKPoint(3, 37),
        };
        var H = DistortWarp.Homography(c);
        foreach (var (u, v, expect) in new[] { (0f, 0f, c[0]), (1f, 0f, c[1]), (1f, 1f, c[2]), (0f, 1f, c[3]) })
        {
            var fwd = DistortWarp.Forward(H, u, v);
            Assert.Equal(expect.X, fwd.X, 2);
            Assert.Equal(expect.Y, fwd.Y, 2);
            Assert.True(DistortWarp.Inverse(H, fwd.X, fwd.Y, out float iu, out float iv));
            Assert.Equal(u, iu, 2);
            Assert.Equal(v, iv, 2);
        }
    }

    [Fact]
    public void Distort_DragCorner_Shift_LocksAxis()
    {
        var orig = new[]
        {
            new SKPoint(0, 0), new SKPoint(20, 0), new SKPoint(20, 10), new SKPoint(0, 10),
        };
        // Drag top-right corner (handle 2) diagonally with Shift: dominant axis wins.
        var next = DistortWarp.DragCorners(orig, new SKPoint(20, 0), new SKPoint(26, 4), 2, shift: true);
        Assert.NotNull(next);
        Assert.Equal(26, next[1].X);
        Assert.Equal(0, next[1].Y);   // y locked
    }

    [Fact]
    public void Distort_DragCorner_RefusesTwist()
    {
        var orig = new[]
        {
            new SKPoint(0, 0), new SKPoint(20, 0), new SKPoint(20, 10), new SKPoint(0, 10),
        };
        // Throw the top-left corner past the opposite edge -> bow-tie -> refused (null).
        Assert.Null(DistortWarp.DragCorners(orig, new SKPoint(0, 0), new SKPoint(30, 5), 0, shift: false));
    }

    [Fact]
    public void Distort_Warp_Identity_KeepsPixels()
    {
        using var src = Solid(8, 8, SKColors.Red);
        var corners = new[]
        {
            new SKPoint(0, 0), new SKPoint(8, 0), new SKPoint(8, 8), new SKPoint(0, 8),
        };
        var warped = DistortWarp.Warp(src, corners, false, false, LayerSampling.Nearest);
        Assert.NotNull(warped);
        Assert.Equal(8, warped.Value.bmp.Width);
        Assert.Equal(8, warped.Value.bmp.Height);
        Assert.Equal(255, warped.Value.bmp.GetPixel(4, 4).Red);
    }

    [Fact]
    public void Distort_Warp_Skew_MovesMass()
    {
        using var src = Solid(10, 10, SKColors.Red);
        // Skew right edge down: red mass shifts toward bottom-right.
        var corners = new[]
        {
            new SKPoint(0, 0), new SKPoint(10, 5), new SKPoint(10, 15), new SKPoint(0, 10),
        };
        var warped = DistortWarp.Warp(src, corners, false, false, LayerSampling.Smooth);
        Assert.NotNull(warped);
        Assert.True(warped.Value.bmp.Height >= 14);
    }

    [Fact]
    public void Distort_Warp_FlipH_MirrorsContent()
    {
        using var src = new SKBitmap(4, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.SetPixel(0, 0, SKColors.Red);
        src.SetPixel(3, 0, SKColors.Blue);
        var corners = new[]
        {
            new SKPoint(0, 0), new SKPoint(4, 0), new SKPoint(4, 1), new SKPoint(0, 1),
        };
        var plain = DistortWarp.Warp(src, corners, false, false, LayerSampling.Nearest);
        var flipped = DistortWarp.Warp(src, corners, true, false, LayerSampling.Nearest);
        Assert.NotNull(plain);
        Assert.NotNull(flipped);
        Assert.Equal(plain.Value.bmp.GetPixel(3, 0).Blue, flipped.Value.bmp.GetPixel(0, 0).Blue);
    }

    // ---------- Crop ----------

    [Fact]
    public void Crop_Create_Symmetric_GrowsAboutStart()
    {
        var r = CropGeometry.Create(new SKPoint(50, 50), new SKPoint(60, 55), null, symmetric: true);
        Assert.Equal(40, r.Left);
        Assert.Equal(60, r.Right);
        Assert.Equal(45, r.Top);
        Assert.Equal(55, r.Bottom);
    }

    [Fact]
    public void Crop_Create_Ratio_LocksAspect()
    {
        var r = CropGeometry.Create(new SKPoint(0, 0), new SKPoint(30, 10), 2f);
        Assert.Equal(2.0, r.Width / r.Height, 1);
    }

    [Fact]
    public void Crop_Drag_Move_ShiftsFrame()
    {
        var orig = new SKRect(10, 10, 30, 30);
        var d = new CropDrag(new SKPoint(15, 15), orig, CropDrag.Mode.Move);
        var next = d.Updated(new SKPoint(20, 18), null);
        Assert.Equal(15, next.Left);
        Assert.Equal(35, next.Right);
    }

    [Fact]
    public void CropSnapped_WholePixels()
    {
        var r = CropGeometry.Snapped(new SKRect(1.2f, 2.6f, 10.4f, 9.1f));
        Assert.Equal(1, r.Left);
        Assert.Equal(3, r.Top);
        Assert.Equal(10, r.Right);
        Assert.Equal(9, r.Bottom);
    }

    // ---------- CanvasOps ----------

    [Fact]
    public void CanvasOps_Resize_CenterAnchor_ShiftsLayers()
    {
        var doc = MakeDoc();   // 100x80, layer at (5,7)
        var before = doc.Layers[0].Position;
        Assert.True(CanvasOps.Resize(doc, new CanvasSizeOptions(120, 100, anchor: 4)));
        Assert.Equal(120, doc.Width);
        Assert.Equal(100, doc.Height);
        // Anchor 4 (center): offset = floor(20*1/2, 20*1/2) = (10,10).
        Assert.Equal(before.X + 10, doc.Layers[0].Position.X);
        Assert.Equal(before.Y + 10, doc.Layers[0].Position.Y);
    }

    [Fact]
    public void CanvasOps_Resize_TopLeftAnchor_KeepsOrigin()
    {
        var doc = MakeDoc();
        Assert.True(CanvasOps.Resize(doc, new CanvasSizeOptions(120, 100, anchor: 0)));
        Assert.Equal(5, doc.Layers[0].Position.X);
        Assert.Equal(7, doc.Layers[0].Position.Y);
    }

    [Fact]
    public void CanvasOps_Resize_Invalid_Rejected()
    {
        var doc = MakeDoc();
        Assert.False(CanvasOps.Resize(doc, new CanvasSizeOptions(0, 100)));
        Assert.False(CanvasOps.Resize(doc, new CanvasSizeOptions(100, 40000)));
        Assert.Equal(100, doc.Width);   // untouched
    }

    [Fact]
    public void CanvasOps_Resize_Fill_AddsExtensionLayer()
    {
        var doc = MakeDoc();
        Assert.True(CanvasOps.Resize(doc, new CanvasSizeOptions(120, 100, 4, SKColors.White)));
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal("Canvas Extension", doc.Layers[0].Name);
        using var flat = doc.Compose();
        // Extended corner shows the fill (artwork shifted to (15,17), corner (2,2) is fill).
        Assert.Equal(255, flat.GetPixel(2, 2).Red);
    }

    [Fact]
    public void CanvasOps_Crop_ShrinksAndShifts()
    {
        var doc = MakeDoc();
        Assert.True(CanvasOps.Crop(doc, new SKRect(10, 10, 50, 40)));
        Assert.Equal(40, doc.Width);
        Assert.Equal(30, doc.Height);
        Assert.Equal(-5, doc.Layers[0].Position.X);
        Assert.Equal(-3, doc.Layers[0].Position.Y);
    }

    [Fact]
    public void CanvasOps_FlipCanvasH_MirrorsPositions()
    {
        var doc = MakeDoc();   // width 100, layer 20 wide at x=5 (center 15 -> 85)
        Assert.True(CanvasOps.FlipCanvas(doc, horizontally: true));
        Assert.Equal(75, doc.Layers[0].Position.X, 3);
        Assert.Equal(7, doc.Layers[0].Position.Y, 3);
        Assert.True(doc.Layers[0].FlipH);
        // Flip back restores.
        Assert.True(CanvasOps.FlipCanvas(doc, horizontally: true));
        Assert.Equal(5, doc.Layers[0].Position.X, 3);
        Assert.False(doc.Layers[0].FlipH);
    }

    [Fact]
    public void CanvasOps_FlipCanvasV_MirrorsY()
    {
        var doc = MakeDoc();   // height 80, layer 10 tall at y=7 (center 12 -> 68)
        Assert.True(CanvasOps.FlipCanvas(doc, horizontally: false));
        Assert.Equal(63, doc.Layers[0].Position.Y, 3);
        Assert.True(doc.Layers[0].FlipV);
    }

    [Fact]
    public void CanvasOps_ImageSize_Resample_ScalesPixels()
    {
        var doc = MakeDoc();   // 100x80, 20x10 layer at (5,7)
        Assert.True(CanvasOps.ImageSize(doc, new ImageSizeOptions(200, 160)));
        Assert.Equal(200, doc.Width);
        Assert.Equal(40, doc.Layers[0].Bitmap.Width);
        Assert.Equal(20, doc.Layers[0].Bitmap.Height);
        Assert.Equal(10, doc.Layers[0].Position.X);
        Assert.Equal(14, doc.Layers[0].Position.Y);
    }

    [Fact]
    public void CanvasOps_ImageSize_NoResample_KeepsPixels()
    {
        var doc = MakeDoc();
        var opt = new ImageSizeOptions(200, 160) { Resample = false };
        Assert.True(CanvasOps.ImageSize(doc, opt));
        Assert.Equal(20, doc.Layers[0].Bitmap.Width);   // pixels unchanged
    }

    // ---------- numeric transform + sampling ----------

    [Fact]
    public void ApplyNumeric_SetsPlacement()
    {
        var l = MakeLayer();   // 20x10 bitmap
        Assert.True(CanvasOps.ApplyNumeric(l, 11, 22, 40, 30, 45, lockRatio: false));
        Assert.Equal(11, l.Position.X);
        Assert.Equal(2, l.ScaleX);
        Assert.Equal(3, l.ScaleY);
        Assert.Equal(45, l.Rotation);
    }

    [Fact]
    public void ApplyNumeric_LockRatio_DrivesDominantAxis()
    {
        var l = MakeLayer();   // 20x10
        Assert.True(CanvasOps.ApplyNumeric(l, 0, 0, 40, 21, 0, lockRatio: true));
        Assert.Equal(40, l.Bitmap.Width * l.ScaleX);
        Assert.Equal(20, l.Bitmap.Height * l.ScaleY, 0);
    }

    [Fact]
    public void ApplyNumeric_Invalid_Rejected()
    {
        var l = MakeLayer();
        Assert.False(CanvasOps.ApplyNumeric(l, 0, 0, 0, 10, 0, false));   // w < 1
        Assert.False(CanvasOps.ApplyNumeric(l, 0, 0, 10, 10, float.NaN, false));
        Assert.Equal(1, l.ScaleX);   // untouched
    }

    [Fact]
    public void Sampling_MapsToSkiaQuality()
    {
        Assert.Equal(SKFilterQuality.None, SamplingUtil.ToQuality(LayerSampling.Nearest));
        Assert.Equal(SKFilterQuality.Low, SamplingUtil.ToQuality(LayerSampling.Smooth));
        Assert.Equal(SKFilterQuality.High, SamplingUtil.ToQuality(LayerSampling.High));
    }

    [Fact]
    public void NonUniformScale_DrawsStretched()
    {
        var doc = new Document { Width = 60, Height = 20 };
        var l = MakeLayer(10, 10, 0, 0);
        l.ScaleX = 3; l.ScaleY = 1;
        doc.Layers.Add(l);
        using var flat = doc.Compose();
        Assert.Equal(255, flat.GetPixel(29, 5).Red);   // stretched to 30px wide
        Assert.Equal(0, flat.GetPixel(31, 5).Alpha);   // ...but no further
    }

    [Fact]
    public void Undo_RestoresScaleAndSampling()
    {
        var doc = MakeDoc();
        var stack = new UndoStack();
        stack.Push(doc);
        doc.Layers[0].ScaleX = 3;
        doc.Layers[0].Sampling = LayerSampling.Nearest;
        stack.Undo(doc);
        Assert.Equal(1, doc.Layers[0].ScaleX);
        Assert.Equal(LayerSampling.High, doc.Layers[0].Sampling);
    }

    [Fact]
    public void CanvasSizeDraft_Units_Convert()
    {
        var d = new CanvasSizeDraft(200, 100, resolution: 100);
        d.Units = CanvasSizeDraft.Unit.Inches;
        Assert.Equal(2, d.Displayed(true), 3);
        d.Set(3, widthAxis: true);   // 3 inches = 300px
        Assert.Equal(300, d.Width, 3);
        d.Locked = true;
        d.Units = CanvasSizeDraft.Unit.Pixels;
        d.Set(400, widthAxis: true); // aspect 2:1 -> height 200
        Assert.Equal(200, d.Height, 3);
    }
}
