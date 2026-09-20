using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class DocumentModelTests
{
    static SKBitmap MakeBitmap(int w = 10, int h = 10, byte alpha = 255)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(new SKColor(255, 0, 0, alpha));
        return b;
    }

    [Fact]
    public void Compose_FlattensVisibleLayers()
    {
        var doc = new Document { Width = 20, Height = 20 };
        doc.Layers.Add(new Layer { Name = "a", Bitmap = MakeBitmap() });
        using var flat = doc.Compose();
        Assert.Equal(20, flat.Width);
        var px = flat.GetPixel(5, 5);
        Assert.Equal(255, px.Red);
    }

    [Fact]
    public void HiddenLayer_IsNotComposited()
    {
        var doc = new Document { Width = 20, Height = 20 };
        doc.Layers.Add(new Layer { Name = "a", Bitmap = MakeBitmap(), Visible = false });
        using var flat = doc.Compose();
        var px = flat.GetPixel(5, 5);
        Assert.Equal(0, px.Alpha);
    }

    [Fact]
    public void MultiplyBlend_Darkens()
    {
        var doc = new Document { Width = 10, Height = 10 };
        // opaque red layer over white backdrop via Multiply
        var baseLayer = new Layer { Name = "base", Bitmap = MakeBitmap() };
        baseLayer.Bitmap.Erase(SKColors.White);
        var top = new Layer { Name = "top", Bitmap = MakeBitmap(), Blend = SKBlendMode.Multiply };
        doc.Layers.Add(baseLayer);
        doc.Layers.Add(top);
        using var flat = doc.Compose();
        var px = flat.GetPixel(5, 5);
        Assert.True(px.Red > 200, $"expected mostly red, got {px}");
    }

    [Fact]
    public void Undo_RevertsAddAndDelete()
    {
        var doc = new Document { Width = 10, Height = 10 };
        var stack = new UndoStack();
        stack.Push(doc);
        doc.Layers.Add(new Layer { Name = "x", Bitmap = MakeBitmap() });
        Assert.Single(doc.Layers);
        stack.Undo(doc);
        Assert.Empty(doc.Layers);
        stack.Redo(doc);
        Assert.Single(doc.Layers);
    }

    [Fact]
    public void Undo_RevertsPropertyChange()
    {
        var doc = new Document { Width = 10, Height = 10 };
        var l = new Layer { Name = "x", Bitmap = MakeBitmap(), Opacity = 1f };
        doc.Layers.Add(l);
        var stack = new UndoStack();
        stack.Push(doc);
        l.Opacity = 0.5f;
        stack.Undo(doc);
        Assert.Equal(1f, doc.Layers[0].Opacity);
    }

    [Fact]
    public void UndoStack_LimitRespected()
    {
        var doc = new Document { Width = 4, Height = 4 };
        var stack = new UndoStack();
        for (int i = 0; i < UndoStack.Limit + 20; i++) stack.Push(doc);
        int n = 0;
        while (stack.CanUndo) { stack.Undo(doc); n++; }
        Assert.True(n <= UndoStack.Limit, $"undoable entries {n} exceed limit");
    }

    [Fact]
    public void Layer_HitTestUsesBounds()
    {
        var l = new Layer { Bitmap = MakeBitmap(), Position = new SKPoint(5, 5) };
        Assert.True(l.HitTest(new SKPoint(7, 7)));
        Assert.False(l.HitTest(new SKPoint(0, 0)));
    }

    [Fact]
    public void Undo_RevertsDragMove_PushBeforeDrag()
    {
        // UI規約: ドラッグ開始時(Pressed)にPushし、移動後にUndoで元の位置に戻ること。
        var doc = new Document { Width = 100, Height = 100 };
        var l = new Layer { Name = "x", Bitmap = MakeBitmap(20, 20), Position = new SKPoint(0, 0) };
        doc.Layers.Add(l);
        var stack = new UndoStack();
        stack.Push(doc);   // ← OnCanvasPointerPressed相当（ドラッグ前）
        l.Position = new SKPoint(140, 100);   // ← OnCanvasPointerMoved相当
        stack.Undo(doc);
        Assert.Equal(0, doc.Layers[0].Position.X);
        Assert.Equal(0, doc.Layers[0].Position.Y);
        stack.Redo(doc);
        Assert.Equal(140, doc.Layers[0].Position.X);
    }
}
