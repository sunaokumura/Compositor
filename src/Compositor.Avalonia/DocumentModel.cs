// Compositor — Document model (port of Compositor/Document core).
// Non-destructive layers: pixels stay untouched; position/scale applied at compose time.
using SkiaSharp;

namespace Compositor;

public class Layer
{
    public string Name = "Layer";
    public SKBitmap Bitmap;          // source pixels — copy-on-write: destructive paint clones first (see Document.EnsureUniqueBitmap)
    public bool Visible = true;
    public float Opacity = 1f;       // 0..1
    public SKPoint Position = new(0, 0);  // non-destructive offset in canvas px
    public float Scale = 1f;              // non-destructive scale
    public bool Locked;
    public SKBlendMode Blend = SKBlendMode.SrcOver;
    // --- transform (non-destructive, Mac LayerTransform subset) ---
    public float Rotation;           // degrees, clockwise (Mac rotation)
    public bool FlipH;
    public bool FlipV;
    // --- adjustments (non-destructive, Mac ImageAdjustments subset) ---
    public float Brightness;         // -100..100 (0 = off)
    public float Contrast;           // -100..100 (0 = off)
    public float Saturation = 100f;  // 0 = gray, 100 = identity, up to 200 boost
    public float Blur;               // Gaussian radius in layer px, 0 = off
    public bool Invert;              // PixelInvert equivalent

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
        public SKBitmap BitmapRef;   // copy-on-write anchor: restore reassigns so pre-paint pixels survive Undo
        public string Name; public bool Visible; public bool Locked;
        public float Opacity, Scale; public SKPoint Position; public SKBlendMode Blend;
        public float Rotation; public bool FlipH, FlipV;
        public float Brightness, Contrast, Saturation, Blur; public bool Invert;
    }
    public List<Item> Items = new();
    public int Width, Height;
    public string DocName = "Untitled";

    public static LayerSnapshot Capture(Document doc)
    {
        var s = new LayerSnapshot { Width = doc.Width, Height = doc.Height, DocName = doc.Name };
        foreach (var l in doc.Layers)
            s.Items.Add(new Item { Layer = l, BitmapRef = l.Bitmap, Name = l.Name, Visible = l.Visible, Locked = l.Locked,
                Opacity = l.Opacity, Scale = l.Scale, Position = l.Position, Blend = l.Blend,
                Rotation = l.Rotation, FlipH = l.FlipH, FlipV = l.FlipV,
                Brightness = l.Brightness, Contrast = l.Contrast, Saturation = l.Saturation,
                Blur = l.Blur, Invert = l.Invert });
        return s;
    }

    public void Restore(Document doc)
    {
        doc.Width = Width; doc.Height = Height; doc.Name = DocName;
        doc.Layers.Clear();
        foreach (var it in Items)
        {
            it.Layer.Bitmap = it.BitmapRef;
            it.Layer.Name = it.Name; it.Layer.Visible = it.Visible; it.Layer.Locked = it.Locked;
            it.Layer.Opacity = it.Opacity; it.Layer.Scale = it.Scale; it.Layer.Position = it.Position;
            it.Layer.Blend = it.Blend;
            it.Layer.Rotation = it.Rotation; it.Layer.FlipH = it.FlipH; it.Layer.FlipV = it.FlipV;
            it.Layer.Brightness = it.Brightness; it.Layer.Contrast = it.Contrast;
            it.Layer.Saturation = it.Saturation; it.Layer.Blur = it.Blur; it.Layer.Invert = it.Invert;
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
    /// <summary>Marquee selection in document px (Mac DocumentSelection subset: rect only).
    /// null = no selection (whole canvas). Lasso/MagicWand are explicitly unsupported.</summary>
    public SKRect? Selection;

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

    /// <summary>Copy-on-write: clone pixels before any destructive paint so Undo snapshots keep pre-paint pixels.</summary>
    public static void EnsureUniqueBitmap(Layer layer)
    {
        if (layer?.Bitmap == null) return;
        var src = layer.Bitmap;
        var copy = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(copy))
            canvas.DrawBitmap(src, 0, 0);
        layer.Bitmap = copy;
    }

    /// <summary>Brush/eraser dab polyline in layer-pixel space (Mac BrushStroke subset: round dabs, no spacing dynamics).</summary>
    public static void PaintStroke(Layer layer, IList<SKPoint> points, SKColor color, float diameter, bool erasing, float opacity = 1f)
    {
        if (layer?.Bitmap == null || points == null || points.Count == 0 || diameter <= 0) return;
        EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        using var paint = new SKPaint
        {
            Color = erasing ? SKColors.Transparent : color.WithAlpha((byte)Math.Round(opacity * 255)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = diameter,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            BlendMode = erasing ? SKBlendMode.Clear : SKBlendMode.SrcOver,
        };
        if (points.Count == 1)
            canvas.DrawCircle(points[0], diameter / 2, new SKPaint
            {
                Color = paint.Color, Style = SKPaintStyle.Fill, IsAntialias = true,
                BlendMode = paint.BlendMode,
            });
        else
            for (int i = 1; i < points.Count; i++)
                canvas.DrawLine(points[i - 1], points[i], paint);
    }

    /// <summary>Destructive invert (Mac PixelInvert equivalent). Push Undo first; COW keeps pre-invert pixels.</summary>
    public static void ApplyInvert(Layer layer)
    {
        if (layer?.Bitmap == null) return;
        EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                -1, 0, 0, 0, 1,
                0, -1, 0, 0, 1,
                0, 0, -1, 0, 1,
                0, 0, 0, 1, 0,
            }),
        };
        canvas.DrawBitmap(layer.Bitmap.Copy(), 0, 0, paint);
    }

    /// <summary>Combined Brightness/Contrast/Saturation/Invert matrix (non-destructive, applied at draw time).</summary>
    public static SKColorFilter BuildAdjustmentFilter(Layer layer)
    {
        float f = 1f + layer.Contrast / 100f;              // contrast gain
        float b = layer.Brightness / 100f + 0.5f * (1f - f);
        float fs = Math.Clamp(layer.Saturation / 100f, 0f, 4f);
        const float lr = 0.213f, lg = 0.715f, lb = 0.072f;
        float s00 = lr * (1 - fs) + fs, s01 = lg * (1 - fs), s02 = lb * (1 - fs);
        float s10 = lr * (1 - fs), s11 = lg * (1 - fs) + fs, s12 = lb * (1 - fs);
        float s20 = lr * (1 - fs), s21 = lg * (1 - fs), s22 = lb * (1 - fs) + fs;
        float sign = layer.Invert ? -1f : 1f;
        float off = layer.Invert ? 1f - b : b;
        // M = sign * f * S, offset applied on RGB
        return SKColorFilter.CreateColorMatrix(new float[]
        {
            sign*f*s00, sign*f*s01, sign*f*s02, 0, off,
            sign*f*s10, sign*f*s11, sign*f*s12, 0, off,
            sign*f*s20, sign*f*s21, sign*f*s22, 0, off,
            0, 0, 0, 1, 0,
        });
    }

    public static bool NeedsAdjustmentFilter(Layer layer) =>
        layer.Brightness != 0 || layer.Contrast != 0 || layer.Saturation != 100f || layer.Invert;

    public static void DrawLayer(SKCanvas canvas, Layer layer, float zoom)
    {
        if (layer is not { Visible: true, Bitmap: not null } || layer.Opacity <= 0f) return;
        using var paint = new SKPaint();
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255));
        paint.BlendMode = layer.Blend;
        if (NeedsAdjustmentFilter(layer))
            paint.ColorFilter = BuildAdjustmentFilter(layer);
        if (layer.Blur > 0)
            paint.ImageFilter = SKImageFilter.CreateBlur(layer.Blur * zoom, layer.Blur * zoom);
        float w = layer.Bitmap.Width * layer.Scale, h = layer.Bitmap.Height * layer.Scale;
        bool hasTransform = layer.Rotation != 0 || layer.FlipH || layer.FlipV;
        if (!hasTransform)
        {
            var dst = new SKRect(
                layer.Position.X * zoom, layer.Position.Y * zoom,
                (layer.Position.X + layer.Bitmap.Width * layer.Scale) * zoom,
                (layer.Position.Y + layer.Bitmap.Height * layer.Scale) * zoom);
            canvas.DrawBitmap(layer.Bitmap, dst, paint);
            return;
        }
        float cx = (layer.Position.X + w / 2) * zoom, cy = (layer.Position.Y + h / 2) * zoom;
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(layer.Rotation);
        canvas.Scale(layer.FlipH ? -1 : 1, layer.FlipV ? -1 : 1);
        canvas.Translate(-cx, -cy);
        try
        {
            var dst = new SKRect(
                layer.Position.X * zoom, layer.Position.Y * zoom,
                (layer.Position.X + w) * zoom, (layer.Position.Y + h) * zoom);
            canvas.DrawBitmap(layer.Bitmap, dst, paint);
        }
        finally { canvas.Restore(); }
    }
}
