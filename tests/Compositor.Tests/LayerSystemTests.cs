using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>Layer system tests: masks, groups, clipping, shapes, gradients, content fill, group merge
/// (Mac LayerMask / LayerGroups / LiveLayerMask / ShapeTool / Gradient / ContentFill / LayerMerge).</summary>
public class LayerSystemTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    static Layer SolidLayer(string name, int w, int h, SKColor c)
        => new() { Name = name, Bitmap = Solid(w, h, c) };

    static Document Doc(int w = 32, int h = 32)
        => new() { Width = w, Height = h };

    // ---------- masks ----------

    [Fact]
    public void Mask_AddRevealAll_CoversEverything()
    {
        var l = SolidLayer("a", 8, 8, SKColors.Red);
        MaskOps.AddMask(l, revealing: true);
        Assert.True(MaskOps.HasMask(l));
        Assert.All(l.Mask, v => Assert.Equal(255, v));
    }

    [Fact]
    public void Mask_AddHideAll_BlocksLayer()
    {
        var doc = Doc(8, 8);
        var l = SolidLayer("a", 8, 8, SKColors.Red);
        doc.Layers.Add(l);
        MaskOps.AddMask(l, revealing: false);
        using var flat = doc.Compose();
        Assert.Equal(0, flat.GetPixel(4, 4).Alpha);
    }

    [Fact]
    public void Mask_DisabledMask_RevealsLayer()
    {
        var doc = Doc(8, 8);
        var l = SolidLayer("a", 8, 8, SKColors.Red);
        doc.Layers.Add(l);
        MaskOps.AddMask(l, revealing: false);
        l.MaskEnabled = false;
        using var flat = doc.Compose();
        Assert.Equal(255, flat.GetPixel(4, 4).Alpha);
    }

    [Fact]
    public void Mask_Invert_FlipsCoverage()
    {
        var l = SolidLayer("a", 4, 4, SKColors.Red);
        MaskOps.AddMask(l, revealing: true);
        MaskOps.Invert(l);
        Assert.All(l.Mask, v => Assert.Equal(0, v));
        MaskOps.Invert(l);
        Assert.All(l.Mask, v => Assert.Equal(255, v));
    }

    [Fact]
    public void Mask_PaintStroke_BlackHidesCenter()
    {
        var doc = Doc(16, 16);
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        doc.Layers.Add(l);
        MaskOps.AddMask(l, revealing: true);
        MaskOps.PaintStroke(l, new List<SKPoint> { new(8, 8) }, 0, diameter: 6, hardness: 1f, opacity: 1f);
        using var flat = doc.Compose();
        Assert.Equal(0, flat.GetPixel(8, 8).Alpha);
        Assert.Equal(255, flat.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Mask_PaintStroke_WhiteRevealsOnHideAll()
    {
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        MaskOps.AddMask(l, revealing: false);
        MaskOps.PaintStroke(l, new List<SKPoint> { new(8, 8) }, 255, diameter: 6, hardness: 1f, opacity: 1f);
        Assert.Equal(255, l.Mask[8 * 16 + 8]);
        Assert.Equal(0, l.Mask[0]);
    }

    [Fact]
    public void Mask_Feather_SoftensHardEdge()
    {
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        MaskOps.AddMask(l, revealing: true);
        for (int y = 0; y < 16; y++)
            for (int x = 8; x < 16; x++) l.Mask[y * 16 + x] = 0;
        MaskOps.Feather(l, 2f);
        byte edge = l.Mask[8 * 16 + 8];
        Assert.True(edge > 0 && edge < 255, $"feathered edge should be gray, got {edge}");
        Assert.Equal(255, l.Mask[8 * 16 + 0]);
        Assert.Equal(0, l.Mask[8 * 16 + 15]);
    }

    [Fact]
    public void Mask_FromSelection_UsesUpSelectionArea()
    {
        var doc = Doc(16, 16);
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        doc.Layers.Add(l);
        SelectionTools.SetRectSelection(doc, new SKRect(4, 4, 12, 12), SelectionMode.Replace);
        MaskOps.AddMaskFromSelection(doc, l, revealing: true);
        // Revealing mask + selection painted black: selected area hidden.
        using var flat = doc.Compose();
        Assert.Equal(0, flat.GetPixel(8, 8).Alpha);
        Assert.Equal(255, flat.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Mask_Undo_RestoresPrePaintBytes()
    {
        var doc = Doc(16, 16);
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        doc.Layers.Add(l);
        MaskOps.AddMask(l, revealing: true);
        var undo = new UndoStack();
        undo.Push(doc);
        MaskOps.PaintStroke(l, new List<SKPoint> { new(8, 8) }, 0, diameter: 6, hardness: 1f, opacity: 1f);
        Assert.Equal(0, l.Mask[8 * 16 + 8]);
        // PaintStroke clones (COW) so the snapshot keeps the pre-paint array.
        undo.Undo(doc);
        Assert.Equal(255, l.Mask[8 * 16 + 8]);
    }

    // ---------- groups ----------

    [Fact]
    public void Group_Validate_RejectsCycle()
    {
        var doc = Doc();
        var g = new Layer { Name = "F", IsGroup = true };
        var c = SolidLayer("c", 4, 4, SKColors.Red); c.ParentId = g.Id;
        doc.Layers.Add(g); doc.Layers.Add(c);
        Assert.True(LayerHierarchy.Validate(doc));
        g.ParentId = c.Id;   // cycle + non-group parent
        Assert.False(LayerHierarchy.Validate(doc));
    }

    [Fact]
    public void Group_HiddenFolder_HidesChildren()
    {
        var doc = Doc(8, 8);
        var g = new Layer { Name = "F", IsGroup = true, Visible = false };
        var c = SolidLayer("c", 8, 8, SKColors.Red); c.ParentId = g.Id;
        doc.Layers.Add(g); doc.Layers.Add(c);
        Assert.False(LayerHierarchy.IsEffectivelyVisible(doc, c));
        using var flat = doc.Compose();
        Assert.Equal(0, flat.GetPixel(4, 4).Alpha);
    }

    [Fact]
    public void Group_GroupAndUngroup_RoundTrips()
    {
        var doc = Doc();
        var a = SolidLayer("a", 4, 4, SKColors.Red);
        var b = SolidLayer("b", 4, 4, SKColors.Blue);
        doc.Layers.Add(a); doc.Layers.Add(b);
        var g = LayerHierarchy.GroupLayers(doc, new[] { a, b });
        Assert.NotNull(g);
        Assert.Equal(g.Id, a.ParentId);
        Assert.Equal(g.Id, b.ParentId);
        Assert.True(LayerHierarchy.Ungroup(doc, g.Id));
        Assert.Null(a.ParentId);
        Assert.DoesNotContain(doc.Layers, l => l.IsGroup);
    }

    // ---------- clipping ----------

    [Fact]
    public void Clip_Toggle_ConstrainsToBaseCoverage()
    {
        var doc = Doc(16, 16);
        var baseLayer = SolidLayer("base", 8, 8, SKColors.Red);   // covers left-top 8x8
        var top = SolidLayer("top", 16, 16, SKColors.Blue);
        doc.Layers.Add(baseLayer); doc.Layers.Add(top);
        Assert.True(ClipOps.Toggle(doc, top.Id));
        using var flat = doc.Compose();
        var inside = flat.GetPixel(2, 2);
        Assert.True(inside.Blue > 200, $"expected blue inside base, got {inside}");
        var outside = flat.GetPixel(12, 12);
        Assert.Equal(0, outside.Alpha);                     // outside base: nothing (only 2 layers)
    }

    [Fact]
    public void Clip_Cycle_Rejected()
    {
        var doc = Doc();
        var a = SolidLayer("a", 4, 4, SKColors.Red);
        var b = SolidLayer("b", 4, 4, SKColors.Blue);
        doc.Layers.Add(a); doc.Layers.Add(b);
        Assert.True(ClipOps.Link(doc, a.Id, b.Id));
        Assert.False(ClipOps.Link(doc, b.Id, a.Id));   // would cycle
    }

    [Fact]
    public void Clip_ReleaseDetached_DropsBrokenLinks()
    {
        var doc = Doc();
        var a = SolidLayer("a", 4, 4, SKColors.Red);
        var b = SolidLayer("b", 4, 4, SKColors.Blue);
        doc.Layers.Add(a); doc.Layers.Add(b);
        Assert.True(ClipOps.Link(doc, a.Id, b.Id));
        // Move b away from a's stack position (simulate reorder breaking contiguity).
        doc.Layers.Clear(); doc.Layers.Add(b); doc.Layers.Add(a);
        ClipOps.ReleaseDetached(doc);
        Assert.Null(b.ClipSourceId);
    }

    // ---------- shapes ----------

    [Fact]
    public void Shape_Rectangle_CreatesFilledLayer()
    {
        var doc = Doc(32, 32);
        var l = ShapeOps.FinishShape(doc, new SKRect(4, 4, 14, 14), ShapeKind.Rectangle, SKColors.Green, 0);
        Assert.NotNull(l);
        Assert.Equal("Rectangle 1", l.Name);
        Assert.Equal(10, l.Bitmap.Width);
        Assert.Equal(new SKPoint(4, 4), l.Position);
        Assert.Equal(255, l.Bitmap.GetPixel(5, 5).Alpha);
        Assert.NotNull(l.Shape);
    }

    [Fact]
    public void Shape_Ellipse_CornersStayTransparent()
    {
        var doc = Doc(32, 32);
        var l = ShapeOps.FinishShape(doc, new SKRect(0, 0, 20, 20), ShapeKind.Ellipse, SKColors.Green, 0);
        Assert.NotNull(l);
        Assert.Equal(0, l.Bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(255, l.Bitmap.GetPixel(10, 10).Alpha);
    }

    [Fact]
    public void Shape_ShiftSquare_EvensSides()
    {
        var r = ShapeOps.DragRect(new SKPoint(0, 0), new SKPoint(10, 4), square: true, fromCenter: false);
        Assert.Equal(r.Width, r.Height);
    }

    [Fact]
    public void Shape_AltCenter_GrowsAroundAnchor()
    {
        var r = ShapeOps.DragRect(new SKPoint(10, 10), new SKPoint(14, 13), square: false, fromCenter: true);
        Assert.Equal(10, r.Left + r.Width / 2, precision: 3);
        Assert.Equal(10, r.Top + r.Height / 2, precision: 3);
    }

    [Fact]
    public void Shape_SubPixelDrag_MakesNothing()
    {
        var doc = Doc(32, 32);
        Assert.Null(ShapeOps.FinishShape(doc, new SKRect(4, 4, 4.5f, 4.5f), ShapeKind.Rectangle, SKColors.Green, 0));
        Assert.Empty(doc.Layers);
    }

    // ---------- gradients ----------

    [Fact]
    public void Gradient_Linear_RunsStartToEnd()
    {
        var draft = new GradientDraft { Start = new SKPoint(0, 8), End = new SKPoint(16, 8),
            Shape = GradientShape.Linear, Style = GradientStyle.ForegroundToBackground };
        Assert.True(draft.HasLine);
        using var bmp = GradientOps.Render(16, 16, draft, SKColors.Black, SKColors.White);
        var left = bmp.GetPixel(0, 8);
        var right = bmp.GetPixel(15, 8);
        Assert.True(left.Red < 40, $"left should be ~black, got {left}");
        Assert.True(right.Red > 200, $"right should be ~white, got {right}");
    }

    [Fact]
    public void Gradient_Snap45_LocksDiagonal()
    {
        var snapped = GradientOps.Snap45(new SKPoint(0, 0), new SKPoint(10, 3));
        float ang = MathF.Atan2(snapped.Y, snapped.X) * 180 / MathF.PI;
        Assert.True(MathF.Abs(ang) < 1 || MathF.Abs(ang - 45) < 1, $"angle should snap, got {ang}");
    }

    [Fact]
    public void Gradient_Commit_PaintsLayerPixels()
    {
        var l = SolidLayer("a", 16, 16, SKColors.White);
        var draft = new GradientDraft { Start = new SKPoint(0, 8), End = new SKPoint(16, 8),
            Shape = GradientShape.Linear, Style = GradientStyle.ForegroundToBackground };
        Assert.True(GradientOps.Commit(l, draft, SKColors.Black, SKColors.White));
        Assert.True(l.Bitmap.GetPixel(0, 8).Red < 40);
    }

    [Fact]
    public void Gradient_CommitToMask_WritesGrayRamp()
    {
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        MaskOps.AddMask(l, revealing: true);
        l.MaskSelected = true;
        var draft = new GradientDraft { Start = new SKPoint(0, 8), End = new SKPoint(16, 8),
            Shape = GradientShape.Linear, Style = GradientStyle.ForegroundToBackground };
        Assert.True(GradientOps.Commit(l, draft, SKColors.White, SKColors.Black));
        Assert.True(l.Mask[8 * 16 + 0] < l.Mask[8 * 16 + 15] ||
                    l.Mask[8 * 16 + 0] != l.Mask[8 * 16 + 15],
            "mask should carry a gradient ramp");
    }

    [Fact]
    public void Gradient_ClickWithoutLine_CommitsNothing()
    {
        var l = SolidLayer("a", 16, 16, SKColors.White);
        var draft = new GradientDraft { Start = new SKPoint(5, 5), End = new SKPoint(5, 5) };
        Assert.False(draft.HasLine);
        Assert.False(GradientOps.Commit(l, draft, SKColors.Black, SKColors.White));
    }

    [Fact]
    public void Gradient_FgToTransparent_AppDefault_PaintsStartKeepsEnd()
    {
        // アプリ既定 (BeginGradient): Linear + ForegroundToTransparent。FG=黒を白層へ引張。
        var l = SolidLayer("a", 16, 16, SKColors.White);
        var draft = new GradientDraft { Start = new SKPoint(0, 8), End = new SKPoint(16, 8),
            Shape = GradientShape.Linear, Style = GradientStyle.ForegroundToTransparent };
        Assert.True(draft.HasLine);
        Assert.True(GradientOps.Commit(l, draft, SKColors.Black, SKColors.White));
        var near = l.Bitmap.GetPixel(0, 8);
        Assert.True(near.Alpha > 200 && near.Red < 40, $"start should be ~opaque black, got {near}");
        var far = l.Bitmap.GetPixel(15, 8);
        Assert.True(far.Red > 200, $"far end should stay white, got {far}");
    }

    [Fact]
    public void UndoStack_DiscardLast_DropsTopWithoutRestoreOrRedo()
    {
        // Gradient click-cancel用: press時Pushを復元なし・redo汚染なしで消す。
        var doc = Doc(8, 8);
        var st = new UndoStack();
        st.Push(doc);
        Assert.True(st.CanUndo);
        st.DiscardLast();
        Assert.False(st.CanUndo);
        Assert.False(st.CanRedo);
    }

    [Fact]
    public void UndoStack_DiscardLast_Empty_NoThrow()
    {
        var st = new UndoStack();
        st.DiscardLast();
        Assert.False(st.CanUndo);
    }

    [Fact]
    public void UndoStack_UndoDepth_TracksPushes()
    {
        var doc = Doc(8, 8);
        var st = new UndoStack();
        Assert.Equal(0, st.UndoDepth);
        st.Push(doc);
        Assert.Equal(1, st.UndoDepth);
        st.DiscardLast();
        Assert.Equal(0, st.UndoDepth);
    }

    // ---------- content fill ----------

    [Fact]
    public void ContentFill_SmallHole_FillsFromSurround()
    {
        var doc = Doc(16, 16);
        var l = SolidLayer("a", 16, 16, SKColors.Red);
        // Punch a transparent 4x4 hole.
        using (var c = new SKCanvas(l.Bitmap))
        using (var p = new SKPaint { BlendMode = SKBlendMode.Clear })
            c.DrawRect(new SKRect(6, 6, 10, 10), p);
        doc.Layers.Add(l);
        SelectionTools.SetRectSelection(doc, new SKRect(6, 6, 10, 10), SelectionMode.Replace);
        int n = ContentFillOps.FillSelection(doc, l);
        Assert.True(n > 0, "should fill hole pixels");
        Assert.Equal(255, l.Bitmap.GetPixel(8, 8).Alpha);
    }

    [Fact]
    public void ContentFill_NoSurround_Throws()
    {
        var doc = Doc(16, 16);
        var l = new Layer { Name = "empty", Bitmap = Solid(16, 16, SKColor.Empty) };
        doc.Layers.Add(l);
        SelectionTools.SetRectSelection(doc, new SKRect(0, 0, 16, 16), SelectionMode.Replace);
        Assert.Throws<ContentFillOps.Failure>(() => ContentFillOps.FillSelection(doc, l));
    }

    [Fact]
    public void ExpandPaste_GrowsLayerBitmap()
    {
        var doc = Doc(32, 32);
        var l = SolidLayer("a", 8, 8, SKColors.Red);
        doc.Layers.Add(l);
        using var paste = Solid(8, 8, SKColors.Blue);
        ContentFillOps.ExpandPaste(l, paste, new SKPoint(20, 20), doc);
        Assert.True(l.Bitmap.Width >= 28 && l.Bitmap.Height >= 28);
    }

    // ---------- merge ----------

    [Fact]
    public void MergeDown_BakesMaskAndKeepsName()
    {
        var doc = Doc(16, 16);
        var below = SolidLayer("below", 16, 16, SKColors.Blue);
        var top = SolidLayer("top", 16, 16, SKColors.Red);
        doc.Layers.Add(below); doc.Layers.Add(top);
        MaskOps.AddMask(top, revealing: false);   // top fully hidden
        var plan = MergeOps.MergeDownPlan(doc, top.Id);
        Assert.NotNull(plan);
        var merged = MergeOps.Execute(doc, plan);
        Assert.NotNull(merged);
        Assert.Equal("below", merged.Name);
        Assert.Single(doc.Layers);
        using var flat = doc.Compose();
        var px = flat.GetPixel(8, 8);
        Assert.True(px.Blue > 200, $"hidden top should vanish, got {px}");
    }

    [Fact]
    public void MergeGroup_MergesContentsInPlace()
    {
        var doc = Doc(16, 16);
        var g = new Layer { Name = "F", IsGroup = true };
        var a = SolidLayer("a", 16, 16, SKColors.Red); a.ParentId = g.Id;
        var b = SolidLayer("b", 8, 8, SKColors.Blue); b.ParentId = g.Id;
        doc.Layers.Add(g); doc.Layers.Add(a); doc.Layers.Add(b);
        var plan = MergeOps.MergeGroupPlan(doc, g.Id);
        Assert.NotNull(plan);
        var merged = MergeOps.Execute(doc, plan);
        Assert.NotNull(merged);
        Assert.Equal("F", merged.Name);
        Assert.DoesNotContain(doc.Layers, l => l.IsGroup);
    }

    [Fact]
    public void MergeDown_EmptyResult_ReturnsNull()
    {
        var doc = Doc(16, 16);
        var below = new Layer { Name = "below", Bitmap = Solid(16, 16, SKColor.Empty) };
        var top = new Layer { Name = "top", Bitmap = Solid(16, 16, SKColor.Empty) };
        doc.Layers.Add(below); doc.Layers.Add(top);
        var plan = MergeOps.MergeDownPlan(doc, top.Id);
        Assert.NotNull(plan);
        Assert.Null(MergeOps.Execute(doc, plan));
    }
}
