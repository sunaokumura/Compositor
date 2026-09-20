// Compositor — paint tools port (Mac BrushStroke/CloneStamp/SmudgeLiquify/BlurTool subset).
// Covers: soft-round dabs (hardness), spaced dab placement, Clone Stamp (aligned /
// sample-all-layers / stroke Undo by caller), Spot-Healing approximation, Smudge /
// Blur / Liquify-push with strength, and Eyedropper sampling.
// Selection gating is per-dab-center InsideSelection (same rule as the Brush path);
// stroke-level Undo is the caller's job (undoStack.Push before the stroke starts).
using SkiaSharp;

namespace Compositor;

/// <summary>Mac BrushSettings subset (diameter px, hardness 0..1, spacing ratio, opacity 0.01..1).</summary>
public struct BrushSettings
{
    public float Diameter = 40f;
    public float Hardness = 1f;
    public float Spacing = 0.15f;
    public float Opacity = 1f;
    public BrushSettings() { }
    public BrushSettings(float diameter, float hardness, float spacing, float opacity)
    { Diameter = diameter; Hardness = hardness; Spacing = spacing; Opacity = opacity; }
}

/// <summary>Shared soft-brush math (Mac BrushRaster.falloff / WarpStroke.weight equivalent).
/// u = distance/radius in 0..1, h = hardness: full strength inside h, smoothstep to 0 at rim.</summary>
public static class BrushTip
{
    public static float Weight(float u, float hardness)
    {
        if (u >= 1f) return 0f;
        float h = Math.Clamp(hardness, 0f, 0.98f);
        if (u <= h) return 1f;
        float t = (1f - u) / Math.Max(1e-6f, 1f - h);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Spaced dab centers along a polyline (Mac spacing dynamics subset).
    /// spacingPx = max(1, diameter * spacing). Single point yields one dab.</summary>
    public static List<SKPoint> DabPositions(IList<SKPoint> points, float diameter, float spacing)
    {
        var dabs = new List<SKPoint>();
        if (points == null || points.Count == 0 || diameter <= 0) return dabs;
        float step = Math.Max(1f, diameter * Math.Clamp(spacing, 0.01f, 2f));
        dabs.Add(points[0]);
        if (points.Count == 1) return dabs;
        float sinceDab = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            int placed = dabs.Count;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len <= 0) continue;
            float t = (step - sinceDab) / len;
            while (t <= 1f)
            {
                var dab = new SKPoint(a.X + dx * t, a.Y + dy * t);
                dabs.Add(dab);
                a = dab; len = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                dx = b.X - a.X; dy = b.Y - a.Y;
                if (len <= 0) break;
                t = step / len;
            }
            sinceDab = dabs.Count > placed ? len : sinceDab + len;
            if (sinceDab >= step) sinceDab = 0f;
        }
        return dabs;
    }

    /// <summary>One soft-round dab. Color must already carry the stroke opacity in its alpha.</summary>
    public static void DrawDab(SKCanvas canvas, SKPoint center, float diameter, SKColor color, float hardness, bool erasing)
    {
        if (diameter <= 0) return;
        float r = diameter / 2f;
        var blend = erasing ? SKBlendMode.Clear : SKBlendMode.SrcOver;
        if (hardness >= 0.98f)
        {
            using var solid = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true, BlendMode = blend };
            canvas.DrawCircle(center, r, solid);
            return;
        }
        float h = Math.Clamp(hardness, 0f, 0.98f);
        var transparent = color.WithAlpha(0);
        using var soft = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(center, r,
                new[] { color, color, transparent }, new[] { 0f, h, 1f }, SKShaderTileMode.Clamp),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            BlendMode = blend,
        };
        canvas.DrawCircle(center, r, soft);
    }
}

/// <summary>Brush/eraser stroke with hardness + spacing (Mac BrushStroke subset).</summary>
public static class PaintEngine
{
    public static void PaintBrushStroke(Layer layer, IList<SKPoint> points, SKColor color,
        BrushSettings settings, bool erasing, SKRect? clip = null)
    {
        if (layer?.Bitmap == null || points == null || points.Count == 0 || settings.Diameter <= 0) return;
        Document.EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        bool clipped = clip is SKRect cr && cr.Width > 0 && cr.Height > 0;
        if (clipped) { canvas.Save(); canvas.ClipRect(clip.Value); }
        try { PaintDabs(canvas, points, color, settings, erasing); }
        finally { if (clipped) canvas.Restore(); }
    }

    /// <summary>Draw spaced dabs onto an existing canvas (caller owns COW/clip).
    /// Prefix-stable: DabPositions on a growing polyline only appends, so the caller can
    /// draw Skip(drawn) dabs per mouse-move and keep spacing continuous.</summary>
    public static int PaintDabs(SKCanvas canvas, IList<SKPoint> points, SKColor color,
        BrushSettings settings, bool erasing, int skipDabs = 0)
    {
        if (canvas == null || points == null || points.Count == 0 || settings.Diameter <= 0) return 0;
        var dabs = BrushTip.DabPositions(points, settings.Diameter, settings.Spacing);
        var c = erasing ? SKColors.Transparent : color.WithAlpha((byte)Math.Round(Math.Clamp(settings.Opacity, 0.01f, 1f) * 255));
        int drawn = 0;
        for (int i = Math.Min(skipDabs, dabs.Count); i < dabs.Count; i++)
        { BrushTip.DrawDab(canvas, dabs[i], settings.Diameter, c, settings.Hardness, erasing); drawn++; }
        return drawn;
    }

    public static int DabCount(IList<SKPoint> points, float diameter, float spacing) =>
        BrushTip.DabPositions(points, diameter, spacing).Count;
}

/// <summary>Clone Stamp state + sampling (Mac CloneStamp.swift port).
/// Source/offset live in document px; the caller maps dab centers with InsideSelection gating.</summary>
public static class CloneTools
{
    /// <summary>Whole-pixel offset a stroke at `point` copies with (Mac cloneStrokeOffset).
    /// Aligned strokes keep the first stroke's offset; otherwise every stroke restarts at the source.</summary>
    public static SKSize? StrokeOffset(SKPoint point, SKPoint? source, SKSize? keptOffset, bool aligned)
    {
        if (source == null) return null;
        if (aligned && keptOffset != null) return keptOffset;
        return new SKSize(MathF.Round(source.Value.X - point.X), MathF.Round(source.Value.Y - point.Y));
    }

    /// <summary>Where the source sits for a brush at `point` (Mac cloneSamplePoint, for the crosshair).</summary>
    public static SKPoint? SamplePoint(SKPoint point, SKPoint? source, SKSize? keptOffset, bool aligned, bool strokeActive)
    {
        if (source == null) return null;
        if (keptOffset == null || (!aligned && !strokeActive)) return source;
        return new SKPoint(point.X + keptOffset.Value.Width, point.Y + keptOffset.Value.Height);
    }

    /// <summary>Doc-sized sample: full composite for sampleAllLayers, else the active layer as drawn.</summary>
    public static SKBitmap CloneSample(Document doc, Layer activeLayer, bool sampleAllLayers)
    {
        if (sampleAllLayers) return doc.Compose();
        var bmp = new SKBitmap(doc.Width, doc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        if (activeLayer?.Bitmap != null)
            using (var canvas = new SKCanvas(bmp))
                Document.DrawLayer(canvas, activeLayer, 1f);
        return bmp;
    }

    /// <summary>Stamp the sample through a soft tip (Mac BrushStroke.clone path subset).
    /// Dest dabs and the source offset live in document px; docToLayer maps every touched
    /// doc pixel to layer px (Position/Scale; rotation is out of scope, documented).</summary>
    public static void PaintCloneStroke(Layer layer, SKBitmap sample, IList<SKPoint> destDocPoints,
        SKPoint srcOffsetDoc, BrushSettings settings, Func<SKPoint, SKPoint> docToLayer, Func<SKPoint, bool> accept = null)
    {
        if (layer?.Bitmap == null || sample == null || destDocPoints == null || destDocPoints.Count == 0) return;
        if (docToLayer == null) return;
        Document.EnsureUniqueBitmap(layer);
        var dabs = BrushTip.DabPositions(destDocPoints, settings.Diameter, settings.Spacing);
        float r = settings.Diameter / 2f;
        float opacity = Math.Clamp(settings.Opacity, 0.01f, 1f);
        int ir = (int)MathF.Ceiling(r);
        var bmp = layer.Bitmap;
        foreach (var d in dabs)
        {
            if (accept != null && !accept(d)) continue;
            for (int dy = -ir; dy <= ir; dy++)
                for (int dx = -ir; dx <= ir; dx++)
                {
                    float w = BrushTip.Weight(MathF.Sqrt(dx * dx + dy * dy) / Math.Max(1e-6f, r), settings.Hardness);
                    if (w <= 0) continue;
                    var docP = new SKPoint(d.X + dx, d.Y + dy);
                    var lp = docToLayer(docP);
                    int x = (int)MathF.Round(lp.X), y = (int)MathF.Round(lp.Y);
                    if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                    int sx = (int)MathF.Round(docP.X + srcOffsetDoc.X), sy = (int)MathF.Round(docP.Y + srcOffsetDoc.Y);
                    if (sx < 0 || sy < 0 || sx >= sample.Width || sy >= sample.Height) continue;
                    var src = sample.GetPixel(sx, sy);
                    var dst = bmp.GetPixel(x, y);
                    float a = w * opacity;
                    bmp.SetPixel(x, y, Lerp(dst, src, a));
                }
        }
    }

    internal static SKColor Lerp(SKColor dst, SKColor src, float a)
    {
        a = Math.Clamp(a, 0f, 1f);
        return new SKColor(
            (byte)Math.Round(dst.Red + (src.Red - dst.Red) * a),
            (byte)Math.Round(dst.Green + (src.Green - dst.Green) * a),
            (byte)Math.Round(dst.Blue + (src.Blue - dst.Blue) * a),
            (byte)Math.Round(dst.Alpha + (src.Alpha - dst.Alpha) * a));
    }
}

/// <summary>Spot-healing approximation (Mac HealPixels.c content-aware subset, documented).
/// Full port is heavy (patch search + membrane solve); this fills the spot from its ring's
/// mean tone with edge feathering and ring-matched grain, which reads as healing on small
/// blemishes. Modes: 0 Content-Aware ≈ mean+membrane feather, 1 Create Texture ≈ +grain,
/// 2 Proximity Match ≈ nearest-ring-tone fill (same fill, tighter ring).</summary>
public static class HealTools
{
    public static void SpotHeal(Layer layer, SKPoint centerLayerPx, float diameter, float opacity, int mode = 0, Func<SKPoint, bool> accept = null)
    {
        if (layer?.Bitmap == null || diameter < 2) return;
        if (accept != null && !accept(centerLayerPx)) return;
        Document.EnsureUniqueBitmap(layer);
        var bmp = layer.Bitmap;
        float r = diameter / 2f;
        int ring = Math.Clamp((int)(diameter / 8), 2, 16);
        if (mode == 2) ring = Math.Max(1, ring / 2);
        int cx = (int)MathF.Round(centerLayerPx.X), cy = (int)MathF.Round(centerLayerPx.Y);
        int ir = (int)MathF.Ceiling(r) + ring;
        // Ring statistics (pixels just outside the spot).
        long[] sum = new long[4]; long[] sum2 = new long[4]; long n = 0;
        for (int dy = -ir; dy <= ir; dy++)
            for (int dx = -ir; dx <= ir; dx++)
            {
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= r || dist > r + ring) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                var c = bmp.GetPixel(x, y);
                sum[0] += c.Red; sum[1] += c.Green; sum[2] += c.Blue; sum[3] += c.Alpha;
                sum2[0] += (long)c.Red * c.Red; sum2[1] += (long)c.Green * c.Green;
                sum2[2] += (long)c.Blue * c.Blue; sum2[3] += (long)c.Alpha * c.Alpha;
                n++;
            }
        if (n == 0) return;
        float[] mean = new float[4], std = new float[4];
        for (int k = 0; k < 4; k++)
        {
            mean[k] = (float)sum[k] / n;
            float v = (float)sum2[k] / n - mean[k] * mean[k];
            std[k] = MathF.Sqrt(Math.Max(0, v));
        }
        float op = Math.Clamp(opacity, 0.01f, 1f);
        int rr = (int)MathF.Ceiling(r);
        for (int dy = -rr; dy <= rr; dy++)
            for (int dx = -rr; dx <= rr; dx++)
            {
                float u = MathF.Sqrt(dx * dx + dy * dy) / Math.Max(1e-6f, r);
                if (u >= 1f) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                // Feather at the rim so the patch meets the surrounding tone (membrane-lite).
                float w = BrushTip.Weight(u, 0.35f) * op;
                var dst = bmp.GetPixel(x, y);
                float[] fill = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    float g = 0f;
                    if (mode == 1) g = (Hash01((uint)(x * 73856093 ^ y * 19349663)) - 0.5f) * std[k];
                    fill[k] = mean[k] + g;
                }
                var src = new SKColor(ClampB(fill[0]), ClampB(fill[1]), ClampB(fill[2]), ClampB(fill[3]));
                bmp.SetPixel(x, y, CloneTools.Lerp(dst, src, w));
            }
    }

    static byte ClampB(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
        return (x >> 8) / 16777216f;
    }
}

/// <summary>Smudge / Blur / Liquify-push (Mac WarpStroke subset, strength = brush opacity).
/// Smudge carries paint and drags it along; Blur paints a pre-softened copy through the
/// tip; Liquify pushes pixels forward with bilinear resampling.</summary>
public class SmudgeStroke
{
    readonly float radius, strength, hardness;
    SKColor[,] carried;   // (2r+1)^2 square picked up at stroke start
    int side;

    public SmudgeStroke(float diameter, float strength, float hardness)
    {
        radius = Math.Max(1f, diameter / 2f);
        this.strength = Math.Clamp(strength, 0.01f, 1f);
        this.hardness = hardness;
        side = 2 * (int)MathF.Ceiling(radius) + 1;
    }

    public void PickUp(SKBitmap bmp, SKPoint center)
    {
        int cx = (int)MathF.Round(center.X), cy = (int)MathF.Round(center.Y);
        int r = side / 2;
        carried = new SKColor[side, side];
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, y = cy + dy;
                carried[dy + r, dx + r] = (x >= 0 && y >= 0 && x < bmp.Width && y < bmp.Height)
                    ? bmp.GetPixel(x, y) : SKColor.Empty;
            }
    }

    /// <summary>One smudge dab; returns pixels actually touched.</summary>
    public int SmudgeAt(SKBitmap bmp, SKPoint center)
    {
        if (carried == null) PickUp(bmp, center);
        int touched = 0;
        int cx = (int)MathF.Round(center.X), cy = (int)MathF.Round(center.Y);
        int r = side / 2;
        float keep = strength;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                float w = BrushTip.Weight(MathF.Sqrt(dx * dx + dy * dy) / radius, hardness);
                if (w <= 0) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                var under = bmp.GetPixel(x, y);
                ref SKColor cell = ref carried[dy + r, dx + r];
                var painted = CloneTools.Lerp(under, cell, w);
                bmp.SetPixel(x, y, painted);
                // Pick up some of what was left (weaker smudge reloads faster).
                cell = CloneTools.Lerp(painted, cell, keep);
                touched++;
            }
        return touched;
    }

    /// <summary>Blur: stamp the pre-blurred sample over the live pixels through the tip.</summary>
    public static int BlurAt(SKBitmap live, SKBitmap blurred, SKPoint center, float diameter, float hardness, float strength)
    {
        float r = Math.Max(1f, diameter / 2f);
        int ir = (int)MathF.Ceiling(r);
        int cx = (int)MathF.Round(center.X), cy = (int)MathF.Round(center.Y);
        int touched = 0;
        for (int dy = -ir; dy <= ir; dy++)
            for (int dx = -ir; dx <= ir; dx++)
            {
                float w = BrushTip.Weight(MathF.Sqrt(dx * dx + dy * dy) / r, hardness) * strength;
                if (w <= 0) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= live.Width || y >= live.Height) continue;
                if (x >= blurred.Width || y >= blurred.Height) continue;
                live.SetPixel(x, y, CloneTools.Lerp(live.GetPixel(x, y), blurred.GetPixel(x, y), w));
                touched++;
            }
        return touched;
    }

    /// <summary>Doc-sized softened copy of the layer (Mac blurSample subset: sigma follows brush size).</summary>
    public static SKBitmap BlurSample(Layer layer, int docW, int docH, float diameter)
    {
        var sharp = new SKBitmap(docW, docH, SKColorType.Bgra8888, SKAlphaType.Premul);
        sharp.Erase(SKColor.Empty);
        if (layer?.Bitmap != null)
            using (var c = new SKCanvas(sharp))
                Document.DrawLayer(c, layer, 1f);
        return BlurBitmap(sharp, diameter);
    }

    /// <summary>Layer-space softened copy (same sigma rule; keeps layer/doc mapping exact).</summary>
    public static SKBitmap BlurSampleLayer(Layer layer, float diameter)
    {
        if (layer?.Bitmap == null) return null;
        var sharp = layer.Bitmap.Copy();
        if (sharp == null) return null;
        return BlurBitmap(sharp, diameter);
    }

    static SKBitmap BlurBitmap(SKBitmap sharp, float diameter)
    {
        float sigma = Math.Clamp(diameter / 10f, 1.5f, 30f);
        var soft = new SKBitmap(sharp.Width, sharp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        soft.Erase(SKColor.Empty);
        using (var c = new SKCanvas(soft))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) })
            c.DrawBitmap(sharp, 0, 0, paint);
        sharp.Dispose();
        return soft;
    }

    /// <summary>Liquify forward-warp: pixels under the brush move with it (Mac WarpStroke.push subset).</summary>
    public static int PushAt(SKBitmap bmp, SKPoint from, SKPoint to, float diameter, float hardness, float strength)
    {
        float r = Math.Max(1f, diameter / 2f);
        int ir = (int)MathF.Ceiling(r);
        float mx = (to.X - from.X) * strength, my = (to.Y - from.Y) * strength;
        if (mx == 0 && my == 0) return 0;
        int cx = (int)MathF.Round(to.X), cy = (int)MathF.Round(to.Y);
        int margin = (int)Math.Ceiling(Math.Max(Math.Abs(mx), Math.Abs(my))) + 2;
        int x0 = Math.Max(0, cx - ir - margin), x1 = Math.Min(bmp.Width - 1, cx + ir + margin);
        int y0 = Math.Max(0, cy - ir - margin), y1 = Math.Min(bmp.Height - 1, cy + ir + margin);
        if (x0 > x1 || y0 > y1) return 0;
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var snap = new SKColor[cw, ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                snap[x, y] = bmp.GetPixel(x + x0, y + y0);
        int touched = 0;
        for (int dy = -ir; dy <= ir; dy++)
            for (int dx = -ir; dx <= ir; dx++)
            {
                float w = BrushTip.Weight(MathF.Sqrt(dx * dx + dy * dy) / r, hardness);
                if (w <= 0) continue;
                int x = cx + dx, y = cy + dy;
                if (x < x0 || x > x1 || y < y0 || y > y1) continue;
                float sx = Math.Clamp(x - x0 - mx * w, 0, cw - 1.001f);
                float sy = Math.Clamp(y - y0 - my * w, 0, ch - 1.001f);
                bmp.SetPixel(x, y, Bilinear(snap, cw, ch, sx, sy));
                touched++;
            }
        return touched;
    }

    static SKColor Bilinear(SKColor[,] s, int cw, int ch, float sx, float sy)
    {
        int ix = Math.Min(cw - 2, (int)sx), iy = Math.Min(ch - 2, (int)sy);
        ix = Math.Max(0, ix); iy = Math.Max(0, iy);
        float fx = Math.Clamp(sx - ix, 0, 1), fy = Math.Clamp(sy - iy, 0, 1);
        float[] acc = new float[4];
        var c00 = s[ix, iy]; var c10 = s[ix + 1, iy]; var c01 = s[ix, iy + 1]; var c11 = s[ix + 1, iy + 1];
        for (int k = 0; k < 4; k++)
        {
            float v00 = Chan(c00, k), v10 = Chan(c10, k), v01 = Chan(c01, k), v11 = Chan(c11, k);
            float top = v00 + (v10 - v00) * fx, bot = v01 + (v11 - v01) * fx;
            acc[k] = top + (bot - top) * fy;
        }
        return new SKColor((byte)Math.Round(acc[0]), (byte)Math.Round(acc[1]),
            (byte)Math.Round(acc[2]), (byte)Math.Round(acc[3]));
    }

    static float Chan(SKColor c, int k) => k == 0 ? c.Red : k == 1 ? c.Green : k == 2 ? c.Blue : c.Alpha;
}

/// <summary>Eyedropper sampling (Mac ColorPalette eyedropper subset).</summary>
public static class Eyedropper
{
    public static SKColor Sample(Document doc, Layer activeLayer, SKPoint docPoint, bool sampleAllLayers)
    {
        int x = (int)MathF.Floor(docPoint.X), y = (int)MathF.Floor(docPoint.Y);
        if (sampleAllLayers)
        {
            using var flat = doc.Compose();
            if (x < 0 || y < 0 || x >= flat.Width || y >= flat.Height) return SKColors.Black;
            return flat.GetPixel(x, y);
        }
        if (activeLayer?.Bitmap == null) return SKColors.Black;
        float sx = Math.Max(1e-6f, activeLayer.ScaleX), sy = Math.Max(1e-6f, activeLayer.ScaleY);
        int lx = (int)MathF.Floor((docPoint.X - activeLayer.Position.X) / sx);
        int ly = (int)MathF.Floor((docPoint.Y - activeLayer.Position.Y) / sy);
        if (lx < 0 || ly < 0 || lx >= activeLayer.Bitmap.Width || ly >= activeLayer.Bitmap.Height) return SKColors.Black;
        return activeLayer.Bitmap.GetPixel(lx, ly);
    }
}
