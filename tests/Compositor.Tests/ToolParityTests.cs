using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_e5d70ed9: 主要ツールparity (brush/transform/adjust/filter/selection) と Undo/Redo 往復.</summary>
public class ToolParityTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    [Fact]
    public void BrushStroke_PaintsPixels_AndUndoRestores()
    {
        var doc = new Document { Width = 40, Height = 40 };
        var l = new Layer { Name = "paint", Bitmap = Solid(40, 40, SKColors.White) };
        doc.Layers.Add(l);
        var stack = new UndoStack();
        stack.Push(doc);
        var before = l.Bitmap.GetPixel(20, 20);
        Document.PaintStroke(l, new[] { new SKPoint(20, 20) }, SKColors.Black, 9, erasing: false);
        var after = l.Bitmap.GetPixel(20, 20);
        Assert.NotEqual(before.Red, after.Red);
        stack.Undo(doc);
        var restored = doc.Layers[0].Bitmap.GetPixel(20, 20);
        Assert.Equal(before.Red, restored.Red);
        stack.Redo(doc);
        var redone = doc.Layers[0].Bitmap.GetPixel(20, 20);
        Assert.Equal(after.Red, redone.Red);
    }

    [Fact]
    public void Eraser_ClearsToTransparent()
    {
        var l = new Layer { Name = "e", Bitmap = Solid(20, 20, SKColors.Red) };
        Document.PaintStroke(l, new[] { new SKPoint(10, 10) }, SKColors.Black, 11, erasing: true);
        Assert.Equal(0, l.Bitmap.GetPixel(10, 10).Alpha);
    }

    [Fact]
    public void Transform_RotateFlip_UndoRoundtrip()
    {
        var doc = new Document { Width = 20, Height = 20 };
        var l = new Layer { Name = "t", Bitmap = Solid(20, 20, SKColors.White) };
        doc.Layers.Add(l);
        var stack = new UndoStack();
        stack.Push(doc);
        l.Rotation = 90; l.FlipH = true;
        using var flat = doc.Compose();   // transform付きでも合成できること
        Assert.Equal(20, flat.Width);
        stack.Undo(doc);
        Assert.Equal(0, doc.Layers[0].Rotation);
        Assert.False(doc.Layers[0].FlipH);
        stack.Redo(doc);
        Assert.Equal(90, doc.Layers[0].Rotation);
    }

    [Fact]
    public void Adjust_BrightnessContrastSaturation_ChangeCompose_AndUndo()
    {
        var doc = new Document { Width = 10, Height = 10 };
        var l = new Layer { Name = "a", Bitmap = Solid(10, 10, new SKColor(100, 100, 100)) };
        doc.Layers.Add(l);
        Assert.False(Document.NeedsAdjustmentFilter(l));
        using var plain = doc.Compose();
        var p0 = plain.GetPixel(5, 5);
        var stack = new UndoStack();
        stack.Push(doc);
        l.Brightness = 60; l.Contrast = 20; l.Saturation = 0;
        Assert.True(Document.NeedsAdjustmentFilter(l));
        using var adjusted = doc.Compose();
        var p1 = adjusted.GetPixel(5, 5);
        Assert.NotEqual(p0.Red, p1.Red);
        stack.Undo(doc);
        Assert.Equal(0, doc.Layers[0].Brightness);
        Assert.Equal(100f, doc.Layers[0].Saturation);
    }

    [Fact]
    public void InvertFlag_ChangesCompose_AndUndo()
    {
        var doc = new Document { Width = 10, Height = 10 };
        var l = new Layer { Name = "i", Bitmap = Solid(10, 10, SKColors.White) };
        doc.Layers.Add(l);
        using var plain = doc.Compose();
        Assert.Equal(255, plain.GetPixel(5, 5).Red);
        var stack = new UndoStack();
        stack.Push(doc);
        l.Invert = true;
        using var inv = doc.Compose();
        Assert.True(inv.GetPixel(5, 5).Red < 128, $"inverted red={inv.GetPixel(5, 5).Red}");
        stack.Undo(doc);
        Assert.False(doc.Layers[0].Invert);
    }

    [Fact]
    public void ApplyInvert_Destructive_WithUndo()
    {
        var doc = new Document { Width = 10, Height = 10 };
        var l = new Layer { Name = "d", Bitmap = Solid(10, 10, SKColors.White) };
        doc.Layers.Add(l);
        var stack = new UndoStack();
        stack.Push(doc);
        Document.ApplyInvert(l);
        Assert.True(l.Bitmap.GetPixel(5, 5).Red < 128);
        stack.Undo(doc);
        Assert.Equal(255, doc.Layers[0].Bitmap.GetPixel(5, 5).Red);
    }

    [Fact]
    public void Blur_ComposesWithoutCrash()
    {
        var doc = new Document { Width = 30, Height = 30 };
        var l = new Layer { Name = "b", Bitmap = Solid(30, 30, SKColors.Red), Blur = 5 };
        doc.Layers.Add(l);
        using var flat = doc.Compose();
        Assert.Equal(30, flat.Width);
    }

    [Fact]
    public void Selection_SetAndClear()
    {
        var doc = new Document { Width = 100, Height = 100 };
        Assert.Null(doc.Selection);
        doc.Selection = new SKRect(10, 10, 50, 50);
        Assert.NotNull(doc.Selection);
        Assert.Equal(40, doc.Selection.Value.Width);
        doc.Selection = null;
        Assert.Null(doc.Selection);
    }
}
