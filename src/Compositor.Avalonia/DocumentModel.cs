// Compositor — Document model (port of Compositor/Document core).
// Non-destructive layers: pixels stay untouched; position/scale applied at compose time.
using SkiaSharp;

namespace Compositor;

public class Layer
{
    public string Name = "Layer";
    public SKBitmap Bitmap;          // source pixels — never destructively modified
    public bool Visible = true;
    public float Opacity = 1f;       // 0..1
    public SKPoint Position = new(0, 0);  // non-destructive offset in canvas px
    public float Scale = 1f;              // non-destructive scale
    public bool Locked;
    public SKBlendMode Blend = SKBlendMode.SrcOver;

    public SKRect Bounds => SKRect.Create(Position.X, Position.Y, Bitmap.Width * Scale, Bitmap.Height * Scale);
    public bool HitTest(SKPoint p) => Bounds.Contains(p.X, p.Y);
}

/// <summary>Lightweight memento of layer stack state. Bitmaps are shared by reference
/// (they are never modified destructively), so snapshots are cheap.</summary>
public class LayerSnapshot
{
    public class Item
    {
        public Layer Layer;
        public string Name; public bool Visible; public bool Locked;
        public float Opacity, Scale; public SKPoint Position; public SKBlendMode Blend;
    }
    public List<Item> Items = new();
    public int Width, Height;
    public string DocName = "Untitled";

    public static LayerSnapshot Capture(Document doc)
    {
        var s = new LayerSnapshot { Width = doc.Width, Height = doc.Height, DocName = doc.Name };
        foreach (var l in doc.Layers)
            s.Items.Add(new Item { Layer = l, Name = l.Name, Visible = l.Visible, Locked = l.Locked,
                Opacity = l.Opacity, Scale = l.Scale, Position = l.Position, Blend = l.Blend });
        return s;
    }

    public void Restore(Document doc)
    {
        doc.Width = Width; doc.Height = Height; doc.Name = DocName;
        doc.Layers.Clear();
        foreach (var it in Items)
        {
            it.Layer.Name = it.Name; it.Layer.Visible = it.Visible; it.Layer.Locked = it.Locked;
            it.Layer.Opacity = it.Opacity; it.Layer.Scale = it.Scale; it.Layer.Position = it.Position;
            it.Layer.Blend = it.Blend;
            doc.Layers.Add(it.Layer);
        }
    }
}

/// <summary>Snapshot-based undo/redo. Push before each mutating operation.</summary>
public class UndoStack
{
    readonly List<LayerSnapshot> undo = new();
    readonly List<LayerSnapshot> redo = new();
    public const int Limit = 100;
    public event Action Changed;

    public void Push(Document doc)
    {
        undo.Add(LayerSnapshot.Capture(doc));
        if (undo.Count > Limit) undo.RemoveAt(0);
        redo.Clear();
        Changed?.Invoke();
    }

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public LayerSnapshot Undo(Document doc)
    {
        if (!CanUndo) return null;
        var s = undo[^1]; undo.RemoveAt(undo.Count - 1);
        redo.Add(LayerSnapshot.Capture(doc));
        s.Restore(doc);
        Changed?.Invoke();
        return s;
    }

    public void Redo(Document doc)
    {
        if (!CanRedo) return;
        var s = redo[^1]; redo.RemoveAt(redo.Count - 1);
        undo.Add(LayerSnapshot.Capture(doc));
        s.Restore(doc);
        Changed?.Invoke();
    }
}

public class Document
{
    public int Width;
    public int Height;
    public string Name = "Untitled";
    public List<Layer> Layers = new();   // bottom-up order

    public event Action Changed;
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Composite all visible layers bottom-up into a flat bitmap of doc size.</summary>
    public SKBitmap Compose()
    {
        var bmp = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        Draw(canvas, 1f);
        return bmp;
    }

    public void Draw(SKCanvas canvas, float zoom)
    {
        foreach (var layer in Layers)
            DrawLayer(canvas, layer, zoom);
    }

    public static void DrawLayer(SKCanvas canvas, Layer layer, float zoom)
    {
        if (layer is not { Visible: true, Bitmap: not null } || layer.Opacity <= 0f) return;
        using var paint = new SKPaint();
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255));
        paint.BlendMode = layer.Blend;
        var dst = new SKRect(
            layer.Position.X * zoom, layer.Position.Y * zoom,
            (layer.Position.X + layer.Bitmap.Width * layer.Scale) * zoom,
            (layer.Position.Y + layer.Bitmap.Height * layer.Scale) * zoom);
        canvas.DrawBitmap(layer.Bitmap, dst, paint);
    }
}
