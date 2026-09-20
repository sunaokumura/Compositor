// Compositor — Document model (port of Compositor/Document core, MVP subset).
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

    public SKRect Bounds => SKRect.Create(Position.X, Position.Y, Bitmap.Width * Scale, Bitmap.Height * Scale);
    public bool HitTest(SKPoint p) => Bounds.Contains(p.X, p.Y);
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
        paint.BlendMode = SKBlendMode.SrcOver;
        var dst = new SKRect(
            layer.Position.X * zoom, layer.Position.Y * zoom,
            (layer.Position.X + layer.Bitmap.Width * layer.Scale) * zoom,
            (layer.Position.Y + layer.Bitmap.Height * layer.Scale) * zoom);
        canvas.DrawBitmap(layer.Bitmap, dst, paint);
    }
}
