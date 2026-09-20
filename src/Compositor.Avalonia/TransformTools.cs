// Compositor — Transform system (port of Mac LayerTransform.swift / Distort.swift /
// CanvasSize.swift / Crop.swift subset + TransformInspector / CropControls /
// CanvasSizeSheet / ImageSizeSheet subset).
//
// Layer placement model (Mac LayerTransform subset):
//   Position = Origin (doc px, top-left of the unrotated box)
//   Size     = Bitmap.Size * (ScaleX, ScaleY)  (non-destructive, Mac size)
//   Rotation = degrees clockwise about the box center (Mac rotation)
//   FlipH/V  = Mac flipX/flipY
//   Sampling = Mac LayerSampling (draw-time interpolation)
using SkiaSharp;

namespace Compositor;

/// <summary>Draw-time interpolation (Mac LayerSampling). High is the Mac default.</summary>
public enum LayerSampling { Nearest, Smooth, High }

public static class SamplingUtil
{
    public static SKFilterQuality ToQuality(LayerSampling s) => s switch
    {
        LayerSampling.Nearest => SKFilterQuality.None,
        LayerSampling.Smooth => SKFilterQuality.Low,
        _ => SKFilterQuality.High,
    };
}

/// <summary>Axis-aligned placement of a layer (Mac LayerTransform subset).</summary>
public readonly struct LayerPlacement
{
    public readonly SKPoint Origin;   // doc px, top-left of unrotated box
    public readonly SKSize Size;      // doc px
    public readonly float Rotation;   // degrees clockwise
    public readonly bool FlipH, FlipV;

    public LayerPlacement(SKPoint origin, SKSize size, float rotation = 0, bool flipH = false, bool flipV = false)
    { Origin = origin; Size = size; Rotation = rotation; FlipH = flipH; FlipV = flipV; }

    public SKPoint Center => new(Origin.X + Size.Width / 2, Origin.Y + Size.Height / 2);
    public float Radians => (Rotation % 360) * MathF.PI / 180f;

    public bool IsValid =>
        float.IsFinite(Origin.X) && float.IsFinite(Origin.Y) &&
        float.IsFinite(Size.Width) && float.IsFinite(Size.Height) && float.IsFinite(Rotation) &&
        Size.Width >= 1 && Size.Width <= 300_000 && Size.Height >= 1 && Size.Height <= 300_000 &&
        Math.Abs(Origin.X) <= 1_000_000 && Math.Abs(Origin.Y) <= 1_000_000;

    /// <summary>Unit-square point (0..1, y down) mapped onto the document (Mac point(_:)).</summary>
    public SKPoint Point(SKPoint unit)
    {
        float x = (unit.X - 0.5f) * Size.Width, y = (unit.Y - 0.5f) * Size.Height;
        float c = MathF.Cos(Radians), s = MathF.Sin(Radians);
        var ctr = Center;
        return new SKPoint(ctr.X + x * c - y * s, ctr.Y + x * s + y * c);
    }

    public static SKPoint[] Handles => new[]
    {
        new SKPoint(0, 0), new SKPoint(0.5f, 0), new SKPoint(1, 0), new SKPoint(1, 0.5f),
        new SKPoint(1, 1), new SKPoint(0.5f, 1), new SKPoint(0, 1), new SKPoint(0, 0.5f),
    };

    /// <summary>Width as % of the pixel size it places (Mac scalePercent).</summary>
    public float ScalePercent(SKSize pixelSize) => Size.Width / Math.Max(1, pixelSize.Width) * 100;

    /// <summary>Both sides set to percent of pixelSize, keeping center (Mac scaled(toPercent:)).</summary>
    public LayerPlacement ScaledToPercent(float percent, SKSize pixelSize)
    {
        var size = new SKSize(pixelSize.Width * percent / 100, pixelSize.Height * percent / 100);
        var ctr = Center;
        return new LayerPlacement(new SKPoint(ctr.X - size.Width / 2, ctr.Y - size.Height / 2), size, Rotation, FlipH, FlipV);
    }

    /// <summary>Whole pixels / whole degrees (Mac rounded()).</summary>
    public LayerPlacement Rounded() => new(
        new SKPoint(MathF.Round(Origin.X), MathF.Round(Origin.Y)),
        new SKSize(Math.Max(1, MathF.Round(Size.Width)), Math.Max(1, MathF.Round(Size.Height))),
        MathF.Round(Rotation), FlipH, FlipV);

    /// <summary>Mirrored across the canvas middle (Mac mirrored(horizontally:across:)).</summary>
    public LayerPlacement Mirrored(bool horizontally, float axis)
    {
        var ctr = Center;
        if (horizontally)
            return new LayerPlacement(
                new SKPoint(2 * axis - ctr.X - Size.Width / 2, Origin.Y),
                Size, -Rotation, !FlipH, FlipV);
        return new LayerPlacement(
            new SKPoint(Origin.X, 2 * axis - ctr.Y - Size.Height / 2),
            Size, -Rotation, FlipH, !FlipV);
    }
}

/// <summary>Transform drag session (Mac TransformDrag subset: move / resize / rotate).
/// Distort corner moves live in DistortWarp.DragCorners.</summary>
public class TransformDrag
{
    public enum Mode { Move, Resize, Rotate }
    public LayerPlacement Original;
    public SKPoint Start;
    public Mode DragMode;
    public int HandleIndex;   // for Resize (LayerPlacement.Handles order)

    public TransformDrag(LayerPlacement original, SKPoint start, Mode mode, int handleIndex = 0)
    { Original = original; Start = start; DragMode = mode; HandleIndex = handleIndex; }

    /// <summary>Dragged placement (Mac updated(to:lockRatio:shift:option:) subset).
    /// lockRatio evens W/H; shift snaps rotation to 15deg / single-axis move.</summary>
    public LayerPlacement Updated(SKPoint point, bool lockRatio, bool shift, bool fromCenter = false)
    {
        var r = Original;
        switch (DragMode)
        {
            case Mode.Move:
            {
                float dx = point.X - Start.X, dy = point.Y - Start.Y;
                if (shift) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
                return new LayerPlacement(new SKPoint(r.Origin.X + dx, r.Origin.Y + dy), r.Size, r.Rotation, r.FlipH, r.FlipV);
            }
            case Mode.Rotate:
            {
                var c = r.Center;
                float delta = MathF.Atan2(point.Y - c.Y, point.X - c.X) - MathF.Atan2(Start.Y - c.Y, Start.X - c.X);
                float rot = r.Rotation + delta * 180 / MathF.PI;
                if (shift) rot = MathF.Round(rot / 15) * 15;
                return new LayerPlacement(r.Origin, r.Size, rot, r.FlipH, r.FlipV);
            }
            default: // Resize
            {
                var handle = LayerPlacement.Handles[HandleIndex];
                var anchorUnit = fromCenter ? new SKPoint(0.5f, 0.5f) : new SKPoint(1 - handle.X, 1 - handle.Y);
                var anchor = r.Point(anchorUnit);
                var initialHandle = r.Point(handle);
                float dx = initialHandle.X + point.X - Start.X - anchor.X;
                float dy = initialHandle.Y + point.Y - Start.Y - anchor.Y;
                float span = fromCenter ? 2 : 1;
                float c = MathF.Cos(r.Radians), s = MathF.Sin(r.Radians);
                float localX = (dx * c + dy * s) * span;
                float localY = (-dx * s + dy * c) * span;
                float sx = handle.X * 2 - 1, sy = handle.Y * 2 - 1;
                float w = sx == 0 ? r.Size.Width : Math.Max(1, localX * sx);
                float h = sy == 0 ? r.Size.Height : Math.Max(1, localY * sy);
                if (lockRatio != shift)
                {
                    float factor;
                    if (sx == 0) factor = h / r.Size.Height;
                    else if (sy == 0) factor = w / r.Size.Width;
                    else
                        factor = Math.Max(1 / Math.Min(r.Size.Width, r.Size.Height),
                            (localX * sx * r.Size.Width + localY * sy * r.Size.Height) /
                            (r.Size.Width * r.Size.Width + r.Size.Height * r.Size.Height));
                    w = r.Size.Width * factor; h = r.Size.Height * factor;
                }
                float offX = (0.5f - anchorUnit.X) * w, offY = (0.5f - anchorUnit.Y) * h;
                var center = new SKPoint(
                    anchor.X + offX * c - offY * s,
                    anchor.Y + offX * s + offY * c);
                return new LayerPlacement(new SKPoint(center.X - w / 2, center.Y - h / 2),
                    new SKSize(w, h), r.Rotation, r.FlipH, r.FlipV);
            }
        }
    }
}

/// <summary>Free distortion (Mac DistortWarp subset).
/// Corners in handle order: top-left, top-right, bottom-right, bottom-left (doc px).
/// Apply resamples pixels into the shape; the layer becomes axis-aligned over the bounds.</summary>
public static class DistortWarp
{
    public static SKPoint[] CornersOf(LayerPlacement t) =>
        new[] { new SKPoint(0, 0), new SKPoint(1, 0), new SKPoint(1, 1), new SKPoint(0, 1) }
            .Select(t.Point).ToArray();

    /// <summary>Four finite corners forming a convex, non-degenerate shape (Mac isUsable).</summary>
    public static bool IsUsable(IList<SKPoint> corners)
    {
        if (corners == null || corners.Count != 4) return false;
        if (!corners.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) &&
            Math.Abs(p.X) <= 1_000_000 && Math.Abs(p.Y) <= 1_000_000)) return false;
        float sign = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i]; var b = corners[(i + 1) % 4]; var c = corners[(i + 2) % 4];
            float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) <= 0.01f) return false;
            if (sign == 0) sign = cross < 0 ? -1 : 1;
            else if ((cross < 0) != (sign < 0)) return false;
        }
        return true;
    }

    /// <summary>Forward perspective coefficients mapping the unit square onto corners
    /// (Mac homography: x=(a u+b v+x0)/w, y=(d u+e v+y0)/w, w=g u+h v+1).</summary>
    public static (float a, float b, float x0, float d, float e, float y0, float g, float h) Homography(IList<SKPoint> c)
    {
        float sx = c[0].X - c[1].X + c[2].X - c[3].X, sy = c[0].Y - c[1].Y + c[2].Y - c[3].Y;
        float g = 0, h = 0;
        if (Math.Abs(sx) > 1e-9f || Math.Abs(sy) > 1e-9f)
        {
            float dx1 = c[1].X - c[2].X, dx2 = c[3].X - c[2].X;
            float dy1 = c[1].Y - c[2].Y, dy2 = c[3].Y - c[2].Y;
            float den = dx1 * dy2 - dx2 * dy1;
            if (Math.Abs(den) > 1e-12f)
            {
                g = (sx * dy2 - dx2 * sy) / den;
                h = (dx1 * sy - sx * dy1) / den;
            }
        }
        float a = c[1].X - c[0].X + g * c[1].X, b = c[3].X - c[0].X + h * c[3].X;
        float d = c[1].Y - c[0].Y + g * c[1].Y, e = c[3].Y - c[0].Y + h * c[3].Y;
        return (a, b, c[0].X, d, e, c[0].Y, g, h);
    }

    public static SKPoint Forward((float a, float b, float x0, float d, float e, float y0, float g, float h) H, float u, float v)
    {
        float w = H.g * u + H.h * v + 1;
        return new SKPoint((H.a * u + H.b * v + H.x0) / w, (H.d * u + H.e * v + H.y0) / w);
    }

    /// <summary>Inverse map: doc point -&gt; unit square (u,v). False when degenerate.</summary>
    public static bool Inverse((float a, float b, float x0, float d, float e, float y0, float g, float h) H,
        float x, float y, out float u, out float v)
    {
        float m00 = H.a - x * H.g, m01 = H.b - x * H.h;
        float m10 = H.d - y * H.g, m11 = H.e - y * H.h;
        float det = m00 * m11 - m01 * m10;
        if (Math.Abs(det) < 1e-9f) { u = v = 0; return false; }
        float rx = x - H.x0, ry = y - H.y0;
        u = (rx * m11 - m01 * ry) / det;
        v = (m00 * ry - rx * m10) / det;
        return true;
    }

    /// <summary>Corner/edge/body drag (Mac TransformDrag.corners(to:shift:) subset).
    /// index = handle order (0..7): even = corner, odd = edge midpoint.</summary>
    public static SKPoint[] DragCorners(IList<SKPoint> original, SKPoint start, SKPoint point, int handleIndex, bool shift, bool moveBody = false)
    {
        if (original == null || original.Count != 4) return null;
        var result = original.ToArray();
        float dx = point.X - start.X, dy = point.Y - start.Y;
        if (shift) { if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0; }
        int[] moved = moveBody ? new[] { 0, 1, 2, 3 }
            : handleIndex % 2 == 0 ? new[] { handleIndex / 2 }
            : new[] { handleIndex / 2, (handleIndex / 2 + 1) % 4 };
        foreach (int k in moved) result[k] = new SKPoint(result[k].X + dx, result[k].Y + dy);
        return IsUsable(result) ? result : null;   // twisted/collapsed shapes are refused (Mac parity)
    }

    static SKColor SampleBilinear(SKBitmap src, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        SKColor C(int ix, int iy)
        {
            ix = Math.Clamp(ix, 0, src.Width - 1); iy = Math.Clamp(iy, 0, src.Height - 1);
            return src.GetPixel(ix, iy);
        }
        var c00 = C(x0, y0); var c10 = C(x0 + 1, y0); var c01 = C(x0, y0 + 1); var c11 = C(x0 + 1, y0 + 1);
        float L(float a, float b, float t) => a + (b - a) * t;
        return new SKColor(
            (byte)Math.Clamp(L(L(c00.Red, c10.Red, fx), L(c01.Red, c11.Red, fx), fy), 0, 255),
            (byte)Math.Clamp(L(L(c00.Green, c10.Green, fx), L(c01.Green, c11.Green, fx), fy), 0, 255),
            (byte)Math.Clamp(L(L(c00.Blue, c10.Blue, fx), L(c01.Blue, c11.Blue, fx), fy), 0, 255),
            (byte)Math.Clamp(L(L(c00.Alpha, c10.Alpha, fx), L(c01.Alpha, c11.Alpha, fx), fy), 0, 255));
    }

    /// <summary>Resample src so its (flip-aware) corners land on target corners.
    /// Returns warped pixels over the shape's whole-pixel bounds + its axis-aligned origin.</summary>
    public static (SKBitmap bmp, SKPoint origin)? Warp(SKBitmap src, IList<SKPoint> corners,
        bool flipH, bool flipV, LayerSampling sampling)
    {
        if (src == null || src.Width <= 0 || src.Height <= 0) return null;
        if (!IsUsable(corners)) return null;
        // Flipped layers show mirrored pixels: route image corners to opposite shape corners (Mac imageCorners).
        int U(int x) => flipH ? 1 - x : x;
        int V(int y) => flipV ? 1 - y : y;
        int[] order = { 0, 1, 3, 2 };   // (u,v) grid index -> corners index (TL,TR,BR,BL handle order)
        var grid = new SKPoint[4];
        for (int v = 0; v < 2; v++) for (int u = 0; u < 2; u++)
            grid[v * 2 + u] = corners[order[V(v) * 2 + U(u)]];
        // Homography takes corners as top-left, top-right, bottom-right, bottom-left.
        var mapped = new[] { grid[0], grid[1], grid[3], grid[2] };
        float minX = MathF.Floor(mapped.Min(p => p.X)), minY = MathF.Floor(mapped.Min(p => p.Y));
        float maxX = mapped.Max(p => p.X), maxY = mapped.Max(p => p.Y);
        int w = (int)(MathF.Ceiling(maxX) - minX), h = (int)(MathF.Ceiling(maxY) - minY);
        if (w < 1 || h < 1 || w > 30_000 || h > 30_000 || (long)w * h > 100_000_000) return null;
        var H = Homography(mapped);
        var dst = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Inverse(H, minX + x + 0.5f, minY + y + 0.5f, out float u, out float v)) continue;
                if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                float sx = u * (src.Width - 1), sy = v * (src.Height - 1);
                SKColor c = sampling == LayerSampling.Nearest
                    ? src.GetPixel(Math.Clamp((int)MathF.Round(sx), 0, src.Width - 1),
                                   Math.Clamp((int)MathF.Round(sy), 0, src.Height - 1))
                    : SampleBilinear(src, sx, sy);
                dst.SetPixel(x, y, c);
            }
        return (dst, new SKPoint(minX, minY));
    }
}

/// <summary>Crop geometry (Mac CropGeometry port: whole-pixel snapped frames).</summary>
public static class CropGeometry
{
    public static SKRect Snapped(SKRect r)
    {
        float l = Math.Min(r.Left, r.Right), t = Math.Min(r.Top, r.Bottom);
        float rr = Math.Max(r.Left, r.Right), b = Math.Max(r.Top, r.Bottom);
        float x = MathF.Round(l), y = MathF.Round(t);
        return new SKRect(x, y, Math.Max(x + 1, MathF.Round(rr)), Math.Max(y + 1, MathF.Round(b)));
    }

    public static bool Valid(SKRect r) =>
        float.IsFinite(r.Left) && float.IsFinite(r.Top) && float.IsFinite(r.Width) && float.IsFinite(r.Height) &&
        r.Width >= 1 && r.Width <= 30_000 && r.Height >= 1 && r.Height <= 30_000 &&
        Math.Abs(r.Left) <= 1_000_000 && Math.Abs(r.Top) <= 1_000_000;

    /// <summary>Frame dragged from start to end; symmetric (Option/Alt) grows about start (Mac create).</summary>
    public static SKRect Create(SKPoint start, SKPoint end, float? ratio, bool symmetric = false)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        if (ratio is float q && q > 0)
        {
            if (Math.Abs(dx) > Math.Abs(dy) * q) dy = (dy < 0 ? -1 : 1) * Math.Abs(dx) / q;
            else dx = (dx < 0 ? -1 : 1) * Math.Abs(dy) * q;
        }
        if (symmetric)
            return Snapped(new SKRect(start.X - Math.Abs(dx), start.Y - Math.Abs(dy),
                                      start.X + Math.Abs(dx), start.Y + Math.Abs(dy)));
        return Snapped(new SKRect(Math.Min(start.X, start.X + dx), Math.Min(start.Y, start.Y + dy),
                                  Math.Max(start.X, start.X + dx), Math.Max(start.Y, start.Y + dy)));
    }
}

/// <summary>Crop drag session (Mac CropDrag port: create / move / resize).</summary>
public class CropDrag
{
    public enum Mode { Create, Move, Resize }
    public SKPoint Start;
    public SKRect Original;
    public Mode DragMode;
    public int HandleIndex;   // for Resize (LayerPlacement.Handles order)

    public CropDrag(SKPoint start, SKRect original, Mode mode, int handleIndex = 0)
    { Start = start; Original = original; DragMode = mode; HandleIndex = handleIndex; }

    public SKRect Updated(SKPoint point, float? ratio, bool symmetric = false)
    {
        switch (DragMode)
        {
            case Mode.Create: return CropGeometry.Create(Start, point, ratio, symmetric);
            case Mode.Move:
                var moved = Original;
                moved.Offset(point.X - Start.X, point.Y - Start.Y);
                return CropGeometry.Snapped(moved);
            default:
            {
                var t = new LayerPlacement(
                    new SKPoint(Original.Left, Original.Top),
                    new SKSize(Original.Width, Original.Height));
                var drag = new TransformDrag(t, Start, TransformDrag.Mode.Resize, HandleIndex);
                var next = drag.Updated(point, ratio != null, shift: false, fromCenter: symmetric);
                return CropGeometry.Snapped(new SKRect(next.Origin.X, next.Origin.Y,
                    next.Origin.X + next.Size.Width, next.Origin.Y + next.Size.Height));
            }
        }
    }
}

/// <summary>Canvas size draft (Mac CanvasSizeDraft port: units + relative + aspect lock).</summary>
public class CanvasSizeDraft
{
    public enum Unit { Pixels, Percent, Inches, Centimeters }
    public int OriginalWidth, OriginalHeight;
    public double Resolution = 72;
    public double Width, Height;
    public bool Relative;
    public bool Locked;
    public Unit Units;

    public CanvasSizeDraft(int w, int h, double resolution = 72)
    { OriginalWidth = w; OriginalHeight = h; Width = w; Height = h; Resolution = resolution; }

    public bool Valid => double.IsFinite(Width) && double.IsFinite(Height) &&
        Math.Round(Width) is >= 1 and <= 30_000 && Math.Round(Height) is >= 1 and <= 30_000;

    public double Displayed(bool widthAxis)
    {
        double original = widthAxis ? OriginalWidth : OriginalHeight;
        double pixels = (widthAxis ? Width : Height) - (Relative ? original : 0);
        return Units switch
        {
            Unit.Percent => pixels / original * 100,
            Unit.Inches => pixels / Resolution,
            Unit.Centimeters => pixels / Resolution * 2.54,
            _ => pixels,
        };
    }

    public void Set(double value, bool widthAxis)
    {
        double original = widthAxis ? OriginalWidth : OriginalHeight;
        double pixels = Units switch
        {
            Unit.Percent => value / 100 * original,
            Unit.Inches => value * Resolution,
            Unit.Centimeters => value / 2.54 * Resolution,
            _ => value,
        };
        double final = pixels + (Relative ? original : 0);
        if (widthAxis) { Width = final; if (Locked) Height = final * OriginalHeight / OriginalWidth; }
        else { Height = final; if (Locked) Width = final * OriginalWidth / OriginalHeight; }
    }
}

/// <summary>Canvas resize application (Mac CanvasSizeOptions + CanvasResizer subset).
/// Anchor 0..8 row-major (top-left..bottom-right); content shifts so the anchor stays fixed.</summary>
public class CanvasSizeOptions
{
    public int Width, Height;
    public int Anchor = 4;
    public SKColor? Fill;   // null = transparent extension

    public CanvasSizeOptions(int w, int h, int anchor = 4, SKColor? fill = null)
    { Width = w; Height = h; Anchor = anchor; Fill = fill; }

    public bool Valid => Width is >= 1 and <= 30_000 && Height is >= 1 and <= 30_000 &&
        Anchor is >= 0 and <= 8;

    public SKPoint Offset(int fromWidth, int fromHeight) => new(
        MathF.Floor((Width - fromWidth) * (Anchor % 3) / 2f),
        MathF.Floor((Height - fromHeight) * (Anchor / 3) / 2f));
}

public class ImageSizeOptions
{
    public int Width, Height;
    public double Resolution = 72;
    public LayerSampling Sampling = LayerSampling.High;
    public bool Resample = true;

    public ImageSizeOptions(int w, int h) { Width = w; Height = h; }
    public bool Valid => Width is >= 1 and <= 30_000 && Height is >= 1 and <= 30_000 &&
        double.IsFinite(Resolution) && Resolution is >= 1 and <= 9600 &&
        (!Resample || (long)Width * Height <= 100_000_000);
}

/// <summary>Document-level canvas operations (Mac CanvasResizer / ImageResizer / flipCanvas subset).</summary>
public static class CanvasOps
{
    /// <summary>Resize canvas; artwork shifts by the anchor offset (Mac CanvasResizer).
    /// A solid fill adds a bottom "Canvas Extension" layer (Mac parity, transparent hole kept).</summary>
    public static bool Resize(Document doc, CanvasSizeOptions options)
    {
        if (doc == null || options == null || !options.Valid) return false;
        var off = options.Offset(doc.Width, doc.Height);
        if (options.Width == doc.Width && options.Height == doc.Height && off == SKPoint.Empty)
            return false;
        if (!float.IsFinite(off.X) || !float.IsFinite(off.Y) ||
            Math.Abs(off.X) > 1_000_000 || Math.Abs(off.Y) > 1_000_000) return false;
        foreach (var l in doc.Layers)
        {
            var p = new LayerPlacement(l.Position,
                new SKSize(l.Bitmap == null ? 0 : l.Bitmap.Width * l.ScaleX,
                           l.Bitmap == null ? 0 : l.Bitmap.Height * l.ScaleY),
                l.Rotation, l.FlipH, l.FlipV);
            var moved = new SKPoint(p.Origin.X + off.X, p.Origin.Y + off.Y);
            if (Math.Abs(moved.X) > 1_000_000 || Math.Abs(moved.Y) > 1_000_000) return false;
        }
        foreach (var l in doc.Layers)
            l.Position = new SKPoint(l.Position.X + off.X, l.Position.Y + off.Y);
        int oldW = doc.Width, oldH = doc.Height;
        doc.Width = options.Width; doc.Height = options.Height;
        if (options.Fill is SKColor fill && (options.Width > oldW || options.Height > oldH))
        {
            var bmp = new SKBitmap(options.Width, options.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(fill);
                using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
                canvas.DrawRect(new SKRect(off.X, off.Y, off.X + oldW, off.Y + oldH), clear);
            }
            doc.Layers.Insert(0, new Layer
            {
                Name = "Canvas Extension", Bitmap = bmp,
                Position = new SKPoint(0, 0), ScaleX = 1, ScaleY = 1,
            });
        }
        doc.ClearSelection();
        doc.RaiseChanged();
        return true;
    }

    /// <summary>Crop to rect: layers shift by -rect.origin, canvas shrinks (Mac commitCrop subset).</summary>
    public static bool Crop(Document doc, SKRect rect)
    {
        rect = CropGeometry.Snapped(rect);
        if (doc == null || !CropGeometry.Valid(rect)) return false;
        foreach (var l in doc.Layers)
            l.Position = new SKPoint(l.Position.X - rect.Left, l.Position.Y - rect.Top);
        doc.Width = (int)rect.Width; doc.Height = (int)rect.Height;
        doc.ClearSelection();
        doc.RaiseChanged();
        return true;
    }

    /// <summary>Flip whole canvas about its middle: every layer mirrored + selection mirrored
    /// (Mac flipCanvas subset; masks/folders out of scope — no mask model on Windows).</summary>
    public static bool FlipCanvas(Document doc, bool horizontally)
    {
        if (doc == null || doc.Width <= 0 || doc.Height <= 0) return false;
        float axis = horizontally ? doc.Width / 2f : doc.Height / 2f;
        foreach (var l in doc.Layers)
        {
            float w = (l.Bitmap?.Width ?? 0) * l.ScaleX, h = (l.Bitmap?.Height ?? 0) * l.ScaleY;
            var p = new LayerPlacement(l.Position, new SKSize(w, h), l.Rotation, l.FlipH, l.FlipV)
                .Mirrored(horizontally, axis);
            l.Position = p.Origin; l.Rotation = p.Rotation; l.FlipH = p.FlipH; l.FlipV = p.FlipV;
        }
        if (doc.Selection is SKRect r)
        {
            doc.Selection = horizontally
                ? new SKRect(doc.Width - r.Right, r.Top, doc.Width - r.Left, r.Bottom)
                : new SKRect(r.Left, doc.Height - r.Bottom, r.Right, doc.Height - r.Top);
            if (doc.SelectionPolygon != null)
                for (int i = 0; i < doc.SelectionPolygon.Count; i++)
                {
                    var p = doc.SelectionPolygon[i];
                    doc.SelectionPolygon[i] = horizontally
                        ? new SKPoint(doc.Width - p.X, p.Y) : new SKPoint(p.X, doc.Height - p.Y);
                }
            doc.SelectionMask = null; doc.SelectionMaskW = doc.SelectionMaskH = 0;
        }
        doc.RaiseChanged();
        return true;
    }

    /// <summary>Image size: without resample only the print resolution changes;
    /// with resample every layer's pixels scale (Mac ImageResizer subset: transforms baked).</summary>
    public static bool ImageSize(Document doc, ImageSizeOptions options)
    {
        if (doc == null || options == null || !options.Valid) return false;
        if (!options.Resample)
        {
            // Resolution-only change is metadata on Windows (no print pipeline yet): accept as no-op success.
            doc.RaiseChanged();
            return true;
        }
        if (options.Width == doc.Width && options.Height == doc.Height) return false;
        float sx = (float)options.Width / doc.Width, sy = (float)options.Height / doc.Height;
        foreach (var l in doc.Layers)
        {
            if (l.Bitmap != null)
            {
                int nw = Math.Max(1, (int)Math.Round(l.Bitmap.Width * sx));
                int nh = Math.Max(1, (int)Math.Round(l.Bitmap.Height * sy));
                if (nw > 30_000 || nh > 30_000 || (long)nw * nh > 100_000_000) return false;
                var scaled = l.Bitmap.Resize(new SKImageInfo(nw, nh),
                    options.Sampling == LayerSampling.Nearest ? SKFilterQuality.None : SKFilterQuality.High);
                if (scaled == null) return false;
                l.Bitmap = scaled;
            }
            l.Position = new SKPoint(l.Position.X * sx, l.Position.Y * sy);
            l.ScaleX = 1; l.ScaleY = 1;
            l.Sampling = options.Sampling;
        }
        doc.Width = options.Width; doc.Height = options.Height;
        doc.ClearSelection();
        doc.RaiseChanged();
        return true;
    }

    /// <summary>Apply a numeric transform edit (Mac TransformInspector subset).
    /// Returns false + keeps layer untouched when any value is invalid.</summary>
    public static bool ApplyNumeric(Layer layer, float x, float y, float w, float h, float angle, bool lockRatio)
    {
        if (layer == null || layer.Bitmap == null) return false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(w) || !float.IsFinite(h) ||
            !float.IsFinite(angle)) return false;
        if (w < 1 || h < 1 || w > 300_000 || h > 300_000) return false;
        if (Math.Abs(x) > 1_000_000 || Math.Abs(y) > 1_000_000) return false;
        if (lockRatio)
        {
            float curW = layer.Bitmap.Width * layer.ScaleX, curH = layer.Bitmap.Height * layer.ScaleY;
            if (curW > 0 && curH > 0)
            {
                // Drive the dominant axis; keep the other proportional (inspector link behavior).
                if (Math.Abs(w - curW) >= Math.Abs(h - curH)) h = curH * w / curW;
                else w = curW * h / curH;
            }
        }
        layer.Position = new SKPoint(x, y);
        layer.ScaleX = w / layer.Bitmap.Width;
        layer.ScaleY = h / layer.Bitmap.Height;
        layer.Rotation = angle % 360;
        return true;
    }
}
