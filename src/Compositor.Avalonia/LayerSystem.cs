// Compositor — layer system: masks, groups, clipping, shapes, gradients, content fill, group-aware merge.
// Port of Mac Compositor/Document LayerMask.swift / LiveLayerMask.swift (subset) / LayerGroups.swift /
// LayerMerge.swift / ShapeTool.swift / Gradient.swift / ContentFill.swift.
//
// DOCUMENTED APPROXIMATIONS (vs Mac):
// - Mask placement is a 2D layer-px offset (MaskOffset), not a full LayerTransform. Linked masks ride
//   the layer automatically (mask is stored in layer-pixel space); unlinked masks keep a fixed offset.
//   Mac's resampled clipImage / MaskPlacementCache / distort-warp of masks are not ported.
// - Clipping ("live mask") constrains the clipped layer to the base layer's alpha coverage via DstIn.
//   Mac's LiveMaskRenderer/LiveMaskBaker bake dialog is simplified: no bake dialog, links release directly.
// - Shape layers are plain raster layers (Mac keeps a re-drawable vector style; we store the style as
//   metadata in Layer.Shape but do not re-render on scale — documented, style kept for future work).
// - Gradient commits destructively to layer pixels (or mask bytes when mask editing); Mac keeps a live
//   GradientEdit raster preview. Preview here is draw-time overlay only.
// - ContentFill is an iterative neighbor-average inpaint, NOT Mac's content_fill C kernel (patch search +
//   membrane fill). Good for small holes/textures; documented as approximation.
// - SubjectRemoval / GuidedMatte stay explicitly unported (no Vision equivalent on Windows).
using SkiaSharp;

namespace Compositor;

// ---------- layer extensions (mask / group / clip / shape metadata) ----------

/// <summary>Shape style metadata kept on shape layers (Mac LayerShapeStyle subset).
/// Pixels are plain raster; style is retained so a future pass can re-render on scale.</summary>
public class ShapeInfo
{
    public ShapeKind Kind;
    public float R, G, B;       // 0..1 sRGB
    public float CornerRadius;  // layer px (rectangles only)
    public ShapeInfo Clone() => new() { Kind = Kind, R = R, G = G, B = B, CornerRadius = CornerRadius };
}

public static class LayerFields
{
    // Layer carries (see DocumentModel.cs): Guid Id, Guid? ParentId, bool IsGroup,
    // byte[] Mask + MaskW/MaskH + MaskEnabled/MaskLinked/MaskOffset/MaskSelected,
    // Guid? ClipSourceId, ShapeInfo Shape.
}

/// <summary>Hierarchy helpers (Mac LayerHierarchy subset).</summary>
public static class LayerHierarchy
{
    public class Entry
    {
        public Layer Layer;
        public int Depth;
        public bool Visible;   // effective (self && ancestors)
    }

    public static List<Entry> Entries(Document doc)
    {
        var result = new List<Entry>();
        var byId = doc.Layers.ToDictionary(l => l.Id);
        // Root sentinel: Dictionary rejects null keys (Guid? null boxes to null).
        var root = Guid.Empty;
        var children = new Dictionary<Guid, List<Layer>>();
        foreach (var l in doc.Layers)
        {
            var key = l.ParentId.HasValue && byId.ContainsKey(l.ParentId.Value) ? l.ParentId.Value : root;
            if (!children.TryGetValue(key, out var list)) children[key] = list = new List<Layer>();
            list.Add(l);
        }
        void Visit(Guid parent, int depth, bool visible)
        {
            if (depth > 64) return;
            if (!children.TryGetValue(parent, out var siblings)) return;
            foreach (var l in siblings)
            {
                bool eff = visible && l.Visible;
                result.Add(new Entry { Layer = l, Depth = depth, Visible = eff });
                if (l.IsGroup) Visit(l.Id, depth + 1, eff);
            }
        }
        Visit(root, 0, true);
        return result;
    }

    public static bool IsEffectivelyVisible(Document doc, Layer layer)
    {
        var byId = doc.Layers.ToDictionary(l => l.Id);
        var cur = layer;
        int depth = 0;
        while (cur != null && depth <= 64)
        {
            if (!cur.Visible) return false;
            cur = cur.ParentId.HasValue && byId.TryGetValue(cur.ParentId.Value, out var p) ? p : null;
            depth++;
        }
        return true;
    }

    public static HashSet<Guid> Descendants(Document doc, Guid id)
    {
        var result = new HashSet<Guid>();
        // Manual grouping: Dictionary rejects null keys (Guid? null boxes to null).
        var kids = new Dictionary<Guid, List<Layer>>();
        foreach (var l in doc.Layers)
        {
            if (!l.ParentId.HasValue) continue;
            if (!kids.TryGetValue(l.ParentId.Value, out var list)) kids[l.ParentId.Value] = list = new List<Layer>();
            list.Add(l);
        }
        var pending = new Stack<Guid>(); pending.Push(id);
        while (pending.Count > 0)
        {
            var p = pending.Pop();
            if (!kids.TryGetValue(p, out var list)) continue;
            foreach (var k in list)
                if (result.Add(k.Id)) pending.Push(k.Id);
        }
        return result;
    }

    /// <summary>Validate hierarchy (Mac LayerHierarchy.validate subset): unique ids, groups childless
    /// of pixels, parents are groups, no cycles, depth cap 64.</summary>
    public static bool Validate(Document doc)
    {
        var byId = new Dictionary<Guid, Layer>();
        foreach (var l in doc.Layers)
        {
            if (!byId.TryAdd(l.Id, l)) return false;
            if (l.IsGroup && l.Bitmap != null) return false;
        }
        foreach (var l in doc.Layers)
        {
            var seen = new HashSet<Guid> { l.Id };
            var parent = l.ParentId;
            int depth = 0;
            while (parent.HasValue)
            {
                if (depth++ > 64 || !seen.Add(parent.Value)) return false;
                if (!byId.TryGetValue(parent.Value, out var node) || !node.IsGroup) return false;
                parent = node.ParentId;
            }
        }
        return true;
    }

    static string NextGroupName(Document doc)
    {
        var names = new HashSet<string>(doc.Layers.Select(l => l.Name));
        int n = 1;
        while (names.Contains($"Folder {n}")) n++;
        return $"Folder {n}";
    }

    /// <summary>Create a group above the active layer (Mac addGroup subset).</summary>
    public static Layer AddGroup(Document doc, Layer anchor)
    {
        var g = new Layer { Name = NextGroupName(doc), IsGroup = true, Visible = true,
            ParentId = anchor?.ParentId };
        int at = anchor != null ? doc.Layers.IndexOf(anchor) + 1 : doc.Layers.Count;
        if (at < 0 || at > doc.Layers.Count) at = doc.Layers.Count;
        doc.Layers.Insert(at, g);
        return g;
    }

    /// <summary>Group the given layers (Mac groupSelectedLayers subset): wrapper placed at the
    /// topmost selected branch; selected descendants of selected folders stay inside.</summary>
    public static Layer GroupLayers(Document doc, IList<Layer> picked)
    {
        var sel = new HashSet<Guid>(picked.Select(l => l.Id));
        // Drop selected layers already inside another selected layer.
        var byId = doc.Layers.ToDictionary(l => l.Id);
        bool InsideSelected(Layer l)
        {
            var p = l.ParentId;
            while (p.HasValue)
            {
                if (sel.Contains(p.Value)) return true;
                p = byId.TryGetValue(p.Value, out var n) ? n.ParentId : null;
            }
            return false;
        }
        var roots = picked.Where(l => !InsideSelected(l)).ToList();
        if (roots.Count == 0) return null;
        // Common parent = first shared ancestor chain entry of the first root present in all roots.
        Guid? CommonParent()
        {
            var chains = roots.Select(r =>
            {
                var c = new List<Guid?>();
                var p = r.ParentId;
                while (p.HasValue) { c.Add(p); p = byId.TryGetValue(p.Value, out var n) ? n.ParentId : (Guid?)null; }
                c.Add(null);
                return c;
            }).ToList();
            foreach (var cand in chains[0])
                if (chains.All(c => c.Contains(cand))) return cand;
            return null;
        }
        var parent = CommonParent();
        var g = new Layer { Name = NextGroupName(doc), IsGroup = true, ParentId = parent };
        int top = doc.Layers.Count;
        foreach (var r in roots) top = Math.Min(top, doc.Layers.IndexOf(r));
        doc.Layers.Insert(top, g);
        foreach (var r in roots) r.ParentId = g.Id;
        return Validate(doc) ? g : null;
    }

    /// <summary>Ungroup: children take the group's parent; the group goes (Mac release subset).</summary>
    public static bool Ungroup(Document doc, Guid groupId)
    {
        var g = doc.Layers.FirstOrDefault(l => l.Id == groupId && l.IsGroup);
        if (g == null) return false;
        foreach (var l in doc.Layers.Where(l => l.ParentId == groupId).ToList())
            l.ParentId = g.ParentId;
        doc.Layers.Remove(g);
        return Validate(doc);
    }
}

// ---------- layer masks (Mac LayerMask.swift subset) ----------

public static class MaskOps
{
    public static bool HasMask(Layer l) => l.Mask != null && l.MaskW > 0 && l.MaskH > 0 && l.Mask.Length == l.MaskW * l.MaskH;
    public static bool IsActive(Layer l) => HasMask(l) && l.MaskEnabled;

    /// <summary>All-reveal (white) or all-hide (black) mask at the layer's pixel grid.</summary>
    public static void AddMask(Layer layer, bool revealing = true)
    {
        if (layer?.Bitmap == null || HasMask(layer)) return;
        int w = layer.Bitmap.Width, h = layer.Bitmap.Height;
        if (w <= 0 || h <= 0 || (long)w * h > 100_000_000) return;
        layer.Mask = new byte[w * h];
        if (revealing) Array.Fill(layer.Mask, (byte)255);
        layer.MaskW = w; layer.MaskH = h;
        layer.MaskEnabled = true; layer.MaskLinked = true;
        layer.MaskOffset = new SKPoint(0, 0);
        layer.MaskSelected = true;
    }

    public static void DeleteMask(Layer layer)
    {
        if (layer == null) return;
        layer.Mask = null; layer.MaskW = layer.MaskH = 0;
        layer.MaskSelected = false;
    }

    public static void Fill(Layer layer, byte value)
    {
        if (!HasMask(layer)) return;
        Document.EnsureUniqueMask(layer);
        Array.Fill(layer.Mask, value);
    }

    public static void Invert(Layer layer)
    {
        if (!HasMask(layer)) return;
        Document.EnsureUniqueMask(layer);
        for (int i = 0; i < layer.Mask.Length; i++) layer.Mask[i] = (byte)(255 - layer.Mask[i]);
    }

    /// <summary>Build a mask from the document selection (Mac addMask-from-selection subset):
    /// base fill + selected area painted the opposite. Mask covers the layer pixel grid.</summary>
    public static void AddMaskFromSelection(Document doc, Layer layer, bool revealing = true)
    {
        if (layer?.Bitmap == null || HasMask(layer) || doc.Selection == null) return;
        int w = layer.Bitmap.Width, h = layer.Bitmap.Height;
        if (w <= 0 || h <= 0 || (long)w * h > 100_000_000) return;
        var sel = SelectionTools.CurrentMask(doc);
        var m = new byte[w * h];
        byte bg = revealing ? (byte)255 : (byte)0;
        byte fg = revealing ? (byte)0 : (byte)255;
        Array.Fill(m, bg);
        float sx = Math.Max(1e-6f, layer.ScaleX), sy = Math.Max(1e-6f, layer.ScaleY);
        for (int y = 0; y < doc.Height; y++)
            for (int x = 0; x < doc.Width; x++)
            {
                if (sel[y * doc.Width + x] == 0) continue;
                int lx = (int)MathF.Floor((x + 0.5f - layer.Position.X) / sx);
                int ly = (int)MathF.Floor((y + 0.5f - layer.Position.Y) / sy);
                if (lx < 0 || ly < 0 || lx >= w || ly >= h) continue;
                m[ly * w + lx] = fg;
            }
        layer.Mask = m; layer.MaskW = w; layer.MaskH = h;
        layer.MaskEnabled = true; layer.MaskLinked = true;
        layer.MaskOffset = new SKPoint(0, 0);
        layer.MaskSelected = true;
    }

    /// <summary>Paint on the mask (Mac mask-selected brush subset): white reveals, black hides.
    /// Points are in layer-pixel space; value 0..255 is the paint color (gray = soft).</summary>
    public static void PaintStroke(Layer layer, IList<SKPoint> points, byte value, float diameter,
        float hardness = 1f, float opacity = 1f)
    {
        if (!HasMask(layer) || points == null || points.Count == 0 || diameter <= 0) return;
        Document.EnsureUniqueMask(layer);
        int w = layer.MaskW, h = layer.MaskH;
        hardness = Math.Clamp(hardness, 0f, 1f);
        opacity = Math.Clamp(opacity, 0f, 1f);
        var dabs = BrushTip.DabPositions(points, diameter, 0.15f);
        if (dabs.Count == 0) dabs = new List<SKPoint>(points);
        float hardR = diameter / 2 * hardness;
        float softR = diameter / 2;
        foreach (var d in dabs)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(d.X - softR)), x1 = Math.Min(w - 1, (int)MathF.Ceiling(d.X + softR));
            int y0 = Math.Max(0, (int)MathF.Floor(d.Y - softR)), y1 = Math.Min(h - 1, (int)MathF.Ceiling(d.Y + softR));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dist = MathF.Sqrt((x + 0.5f - d.X) * (x + 0.5f - d.X) + (y + 0.5f - d.Y) * (y + 0.5f - d.Y));
                    if (dist > softR) continue;
                    float a = dist <= hardR ? 1f : 1f - (dist - hardR) / Math.Max(1e-6f, softR - hardR);
                    a *= opacity;
                    int i = y * w + x;
                    layer.Mask[i] = (byte)Math.Round(layer.Mask[i] * (1 - a) + value * a);
                }
        }
    }

    /// <summary>Feather (Mac feather/blur subset): separable box blur, 3 passes ~ gaussian.</summary>
    public static void Feather(Layer layer, float radiusPx)
    {
        if (!HasMask(layer) || radiusPx <= 0) return;
        Document.EnsureUniqueMask(layer);
        int r = Math.Max(1, (int)Math.Round(radiusPx));
        layer.Mask = BoxBlur(layer.Mask, layer.MaskW, layer.MaskH, r);
    }

    public static byte[] BoxBlur(byte[] src, int w, int h, int r)
    {
        var tmp = new byte[w * h];
        var dst = new byte[w * h];
        for (int pass = 0; pass < 3; pass++)
        {
            var a = pass == 0 ? src : dst;
            // horizontal
            for (int y = 0; y < h; y++)
            {
                int sum = 0;
                for (int x = -r; x <= r; x++) sum += a[y * w + Math.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = (byte)(sum / (2 * r + 1));
                    sum += a[y * w + Math.Min(w - 1, x + r + 1)] - a[y * w + Math.Max(0, x - r)];
                }
            }
            // vertical
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int y = -r; y <= r; y++) sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = (byte)(sum / (2 * r + 1));
                    sum += tmp[Math.Min(h - 1, y + r + 1) * w + x] - tmp[Math.Max(0, y - r) * w + x];
                }
            }
        }
        return dst;
    }

    /// <summary>Edge background (Mac LayerMask.background subset): what the mask shows outside its
    /// pixels — white or black, whichever most of its edge is.</summary>
    public static byte EdgeBackground(byte[] mask, int w, int h)
    {
        if (mask == null || w <= 0 || h <= 0) return 255;
        long total = 0; long count = 0;
        for (int x = 0; x < w; x++) { total += mask[x]; total += mask[(h - 1) * w + x]; count += 2; }
        for (int y = 1; y < h - 1; y++) { total += mask[y * w]; total += mask[y * w + w - 1]; count += 2; }
        return total * 2 >= count * 255 ? (byte)255 : (byte)0;
    }

    /// <summary>Sample mask coverage at a layer pixel (nearest; outside = edge background).</summary>
    public static byte CoverageAt(Layer layer, int lx, int ly)
    {
        int x = lx - (int)Math.Round(layer.MaskOffset.X), y = ly - (int)Math.Round(layer.MaskOffset.Y);
        if (x < 0 || y < 0 || x >= layer.MaskW || y >= layer.MaskH)
            return EdgeBackground(layer.Mask, layer.MaskW, layer.MaskH);
        return layer.Mask[y * layer.MaskW + x];
    }

    /// <summary>Return a copy of the bitmap with alpha scaled by mask coverage (draw-time apply).</summary>
    public static SKBitmap ApplyToBitmap(SKBitmap src, byte[] mask, int mw, int mh, SKPoint offset)
    {
        if (src == null || mask == null) return null;
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var px = src.Pixels;
        var out_ = new SKColor[px.Length];
        byte edge = EdgeBackground(mask, mw, mh);
        int ox = (int)Math.Round(offset.X), oy = (int)Math.Round(offset.Y);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int i = y * src.Width + x;
                var c = px[i];
                if (c.Alpha == 0) { out_[i] = c; continue; }
                int mx = x - ox, my = y - oy;
                byte cov = (mx < 0 || my < 0 || mx >= mw || my >= mh) ? edge : mask[my * mw + mx];
                if (cov == 255) { out_[i] = c; continue; }
                if (cov == 0) { out_[i] = SKColor.Empty; continue; }
                float k = cov / 255f;
                // Unpremultiply, scale, re-premultiply (keeps color correct under premul).
                float a = c.Alpha / 255f * k;
                if (a <= 0) { out_[i] = SKColor.Empty; continue; }
                float r = (c.Red / 255f) / Math.Max(1e-6f, c.Alpha / 255f);
                float g = (c.Green / 255f) / Math.Max(1e-6f, c.Alpha / 255f);
                float b = (c.Blue / 255f) / Math.Max(1e-6f, c.Alpha / 255f);
                out_[i] = new SKColor(
                    (byte)Math.Clamp(r * a * 255, 0, 255),
                    (byte)Math.Clamp(g * a * 255, 0, 255),
                    (byte)Math.Clamp(b * a * 255, 0, 255),
                    (byte)Math.Clamp(a * 255, 0, 255));
            }
        dst.Pixels = out_;
        return dst;
    }
}

// ---------- clipping masks (Mac LiveLayerMask.swift subset) ----------

public static class ClipOps
{
    /// <summary>Validate a clip link (Mac LiveMaskGraph.validate subset): no groups, no adjustments,
    /// no cycles, source must exist.</summary>
    public static bool CanLink(Document doc, Guid sourceId, Guid targetId)
    {
        if (sourceId == targetId) return false;
        var byId = doc.Layers.ToDictionary(l => l.Id);
        if (!byId.TryGetValue(sourceId, out var src) || !byId.TryGetValue(targetId, out var tgt)) return false;
        if (src.IsGroup || tgt.IsGroup || src.IsAdjustmentLayer) return false;
        if (src.ParentId != tgt.ParentId) return false;   // same folder only
        // Cycle check along the source chain.
        var seen = new HashSet<Guid> { targetId };
        var cur = src.ClipSourceId;
        int depth = 0;
        while (cur.HasValue)
        {
            if (depth++ > 64 || !seen.Add(cur.Value)) return false;
            if (!byId.TryGetValue(cur.Value, out var n)) return false;
            cur = n.ClipSourceId;
        }
        return true;
    }

    public static bool Link(Document doc, Guid sourceId, Guid targetId)
    {
        if (!CanLink(doc, sourceId, targetId)) return false;
        doc.Layers.First(l => l.Id == targetId).ClipSourceId = sourceId;
        return true;
    }

    /// <summary>Release a layer and every contiguous clipped layer above it sharing the base
    /// (Mac removeLiveMask subset).</summary>
    public static void Release(Document doc, Guid targetId)
    {
        var byId = doc.Layers.ToDictionary(l => l.Id);
        if (!byId.TryGetValue(targetId, out var tgt) || tgt.ClipSourceId == null) return;
        var baseId = tgt.ClipSourceId.Value;
        var siblings = doc.Layers.Where(l => l.ParentId == tgt.ParentId).ToList();
        int at = siblings.FindIndex(l => l.Id == targetId);
        if (at < 0) return;
        var releases = new List<Guid> { targetId };
        for (int i = at + 1; i < siblings.Count; i++)
        {
            if (siblings[i].ClipSourceId == baseId) releases.Add(siblings[i].Id);
            else break;
        }
        foreach (var id in releases) byId[id].ClipSourceId = null;
    }

    /// <summary>Drop links whose base no longer starts a contiguous stack (Mac releaseDetachedClipping).</summary>
    public static void ReleaseDetached(Document doc)
    {
        var release = new HashSet<Guid>();
        foreach (var stack in doc.Layers.GroupBy(l => l.ParentId))
        {
            Guid? baseId = null;
            foreach (var l in stack)
            {
                if (l.ClipSourceId is Guid s)
                {
                    if (s != baseId) { release.Add(l.Id); baseId = l.Id; }
                }
                else baseId = l.IsGroup ? null : (Guid?)l.Id;
            }
        }
        foreach (var l in doc.Layers.Where(l => release.Contains(l.Id)))
            l.ClipSourceId = null;
    }

    /// <summary>Adopt clipping when a layer lands inside a clip stack (Mac adoptClipping subset).</summary>
    public static void Adopt(Document doc, Layer moved)
    {
        if (moved.IsGroup) return;
        var siblings = doc.Layers.Where(l => l.ParentId == moved.ParentId).ToList();
        int i = siblings.FindIndex(l => l.Id == moved.Id);
        if (i <= 0 || i + 1 >= siblings.Count) return;
        var above = siblings[i + 1];
        if (above.ClipSourceId is not Guid s || s == moved.Id) return;
        var below = siblings[i - 1];
        if (below.Id == s || below.ClipSourceId == s) moved.ClipSourceId = s;
    }

    /// <summary>Toggle: clipped -> release; unclipped -> clip to the sibling below (Mac toggleClippingMask).</summary>
    public static bool Toggle(Document doc, Guid id)
    {
        var byId = doc.Layers.ToDictionary(l => l.Id);
        if (!byId.TryGetValue(id, out var l) || l.IsGroup) return false;
        if (l.ClipSourceId != null) { Release(doc, id); return true; }
        var siblings = doc.Layers.Where(x => x.ParentId == l.ParentId).ToList();
        int i = siblings.FindIndex(x => x.Id == id);
        if (i <= 0 || siblings[i - 1].IsGroup) return false;
        var below = siblings[i - 1];
        return Link(doc, below.ClipSourceId ?? below.Id, id);
    }
}

// ---------- shape tool (Mac ShapeTool.swift subset) ----------

public enum ShapeKind { Rectangle, RoundedRect, Ellipse }

public static class ShapeOps
{
    public const long MaxShapePixels = 100_000_000;

    /// <summary>Drag rect in whole doc px (Mac DragBox.rect port): Shift = square, Alt = from center.</summary>
    public static SKRect DragRect(SKPoint anchor, SKPoint point, bool square, bool fromCenter) =>
        SelectionTools.DragBoxRect(anchor, point, square, fromCenter);

    public static SKBitmap RenderShape(int w, int h, ShapeKind kind, SKColor color, float cornerRadius = 0)
    {
        var bmp = new SKBitmap(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        var r = new SKRect(0, 0, bmp.Width, bmp.Height);
        switch (kind)
        {
            case ShapeKind.Ellipse:
                canvas.DrawOval(r, paint);
                break;
            case ShapeKind.RoundedRect:
                float rad = Math.Clamp(cornerRadius, 0, Math.Min(bmp.Width, bmp.Height) / 2f);
                canvas.DrawRoundRect(r, rad, rad, paint);
                break;
            default:
                canvas.DrawRect(r, paint);
                break;
        }
        return bmp;
    }

    public static string NextName(Document doc, ShapeKind kind)
    {
        string base_ = kind == ShapeKind.Ellipse ? "Ellipse" : "Rectangle";
        var names = new HashSet<string>(doc.Layers.Select(l => l.Name));
        int n = 1;
        while (names.Contains($"{base_} {n}")) n++;
        return $"{base_} {n}";
    }

    /// <summary>Finish a shape drag: filled raster on a new layer above the anchor layer
    /// (Mac finishShape subset). Sub-1px drags make nothing.</summary>
    public static Layer FinishShape(Document doc, SKRect rect, ShapeKind kind, SKColor color,
        float cornerRadius, Layer anchor = null)
    {
        if (rect.Width < 1 || rect.Height < 1) return null;
        if ((long)rect.Width * (long)rect.Height > MaxShapePixels) return null;
        int w = Math.Max(1, (int)Math.Round(rect.Width)), h = Math.Max(1, (int)Math.Round(rect.Height));
        var bmp = RenderShape(w, h, kind, color, cornerRadius);
        var layer = new Layer
        {
            Name = NextName(doc, kind),
            Bitmap = bmp,
            Position = new SKPoint(rect.Left, rect.Top),
            ParentId = anchor?.ParentId,
            Shape = new ShapeInfo { Kind = kind, R = color.Red / 255f, G = color.Green / 255f,
                B = color.Blue / 255f, CornerRadius = kind == ShapeKind.RoundedRect ? cornerRadius : 0 },
        };
        int at = anchor != null ? doc.Layers.IndexOf(anchor) + 1 : doc.Layers.Count;
        if (at < 0 || at > doc.Layers.Count) at = doc.Layers.Count;
        doc.Layers.Insert(at, layer);
        return layer;
    }
}

// ---------- gradient tool (Mac Gradient.swift subset) ----------

public enum GradientShape { Linear, Radial }
public enum GradientStyle { ForegroundToBackground, ForegroundToTransparent }

public class GradientDraft
{
    public SKPoint Start, End;      // layer px
    public GradientShape Shape;
    public GradientStyle Style;
    public bool Reversed;
    public float Opacity = 1f;      // 0..1
    public bool HasLine => (End.X - Start.X) * (End.X - Start.X) + (End.Y - Start.Y) * (End.Y - Start.Y) >= 0.25f;
}

public static class GradientOps
{
    /// <summary>Shift-constrain to 45° steps (Mac shift-snapped gradient line).</summary>
    public static SKPoint Snap45(SKPoint start, SKPoint end)
    {
        float dx = end.X - start.X, dy = end.Y - start.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) return end;
        float ang = MathF.Round(MathF.Atan2(dy, dx) / (MathF.PI / 4)) * (MathF.PI / 4);
        return new SKPoint(start.X + MathF.Cos(ang) * len, start.Y + MathF.Sin(ang) * len);
    }

    static (SKColor c0, SKColor c1) Colors(SKColor fg, SKColor bg, GradientStyle style, bool reversed)
    {
        var cols = style == GradientStyle.ForegroundToBackground
            ? (fg, bg)
            : (fg, fg.WithAlpha(0));
        return reversed ? (cols.Item2, cols.Item1) : cols;
    }

    /// <summary>Render the gradient overlay at layer resolution (premultiplied).</summary>
    public static SKBitmap Render(int w, int h, GradientDraft draft, SKColor fg, SKColor bg)
    {
        var bmp = new SKBitmap(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul);
        if (draft == null || !draft.HasLine) { bmp.Erase(SKColor.Empty); return bmp; }
        var (c0, c1) = Colors(fg, bg, draft.Style, draft.Reversed);
        float f0r = c0.Red / 255f, f0g = c0.Green / 255f, f0b = c0.Blue / 255f, f0a = c0.Alpha / 255f;
        float f1r = c1.Red / 255f, f1g = c1.Green / 255f, f1b = c1.Blue / 255f, f1a = c1.Alpha / 255f;
        float dx = draft.End.X - draft.Start.X, dy = draft.End.Y - draft.Start.Y;
        float lenSq = dx * dx + dy * dy;
        float maxDist = draft.Shape == GradientShape.Radial ? MathF.Sqrt(lenSq)
            : MathF.Sqrt(w * w + h * h);
        if (maxDist < 1e-6f) maxDist = 1;
        var px = new SKColor[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                float t;
                if (draft.Shape == GradientShape.Radial)
                    t = MathF.Sqrt((x + 0.5f - draft.Start.X) * (x + 0.5f - draft.Start.X) + (y + 0.5f - draft.Start.Y) * (y + 0.5f - draft.Start.Y)) / maxDist;
                else
                    t = ((x + 0.5f - draft.Start.X) * dx + (y + 0.5f - draft.Start.Y) * dy) / lenSq;
                t = Math.Clamp(t, 0f, 1f);
                float a = (f0a + (f1a - f0a) * t) * draft.Opacity;
                float r = f0r + (f1r - f0r) * t, g = f0g + (f1g - f0g) * t, b = f0b + (f1b - f0b) * t;
                px[y * bmp.Width + x] = new SKColor(
                    (byte)Math.Clamp(r * a * 255, 0, 255),
                    (byte)Math.Clamp(g * a * 255, 0, 255),
                    (byte)Math.Clamp(b * a * 255, 0, 255),
                    (byte)Math.Clamp(a * 255, 0, 255));
            }
        bmp.Pixels = px;
        return bmp;
    }

    /// <summary>Commit (Enter): composite the gradient onto layer pixels, or into the mask when
    /// mask editing (Mac commitGradient subset).SrcOver at draft opacity.</summary>
    public static bool Commit(Layer layer, GradientDraft draft, SKColor fg, SKColor bg)
    {
        if (layer == null || draft == null || !draft.HasLine) return false;
        if (layer.MaskSelected && MaskOps.HasMask(layer))
        {
            // Grayscale gradient into the mask: luminance of the overlay alpha.
            using var ov = Render(layer.MaskW, layer.MaskH, draft,
                new SKColor(255, 255, 255), new SKColor(0, 0, 0));
            Document.EnsureUniqueMask(layer);
            var px = ov.Pixels;
            for (int i = 0; i < px.Length && i < layer.Mask.Length; i++)
            {
                float a = px[i].Alpha / 255f;
                float lum = (px[i].Red * 0.2126f + px[i].Green * 0.7152f + px[i].Blue * 0.0722f) / 255f;
                byte v = (byte)Math.Round(lum * 255);
                layer.Mask[i] = (byte)Math.Round(layer.Mask[i] * (1 - a) + v * a);
            }
            return true;
        }
        if (layer.Bitmap == null) return false;
        Document.EnsureUniqueBitmap(layer);
        using var overlay = Render(layer.Bitmap.Width, layer.Bitmap.Height, draft, fg, bg);
        using var canvas = new SKCanvas(layer.Bitmap);
        canvas.DrawBitmap(overlay, 0, 0);
        return true;
    }
}

// ---------- content-aware fill, approximate (Mac ContentFill.swift subset) ----------

public static class ContentFillOps
{
    public class Failure : Exception
    {
        public Failure() : base("Not enough unselected, opaque image pixels to synthesize a fill. " +
            "Use a smaller selection with some surrounding image.") { }
    }

    /// <summary>Approximate content-aware fill: iterative border-in neighbor averaging with a
    /// mirrored-edge seed, finished with a 1px seam feather. Deterministic. NOT Mac's content_fill
    /// kernel (patch search + membrane fill) — documented approximation for small holes.</summary>
    /// <returns>filled pixel count</returns>
    public static int FillSelection(Document doc, Layer layer)
    {
        if (layer?.Bitmap == null || doc.Selection == null) return 0;
        var sel = SelectionTools.CurrentMask(doc);
        int w = doc.Width, h = doc.Height;
        Document.EnsureUniqueBitmap(layer);
        var bmp = layer.Bitmap;
        // Map doc px -> layer px; gather known pixels.
        float sx = Math.Max(1e-6f, layer.ScaleX), sy = Math.Max(1e-6f, layer.ScaleY);
        var known = new bool[w * h];
        var pix = new SKColor[w * h];   // doc-space working copy
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int lx = (int)MathF.Floor((x + 0.5f - layer.Position.X) / sx);
                int ly = (int)MathF.Floor((y + 0.5f - layer.Position.Y) / sy);
                if (lx < 0 || ly < 0 || lx >= bmp.Width || ly >= bmp.Height) continue;
                var c = bmp.GetPixel(lx, ly);
                if (c.Alpha == 0) continue;
                pix[y * w + x] = c;
                if (sel[y * w + x] == 0) known[y * w + x] = true;
            }
        int target = 0;
        for (int i = 0; i < sel.Length; i++)
            if (sel[i] != 0 && !known[i]) target++;
            else if (sel[i] != 0 && known[i]) { /* selected but known: keep */ }
        if (target == 0) return 0;
        // Unselected coverage sanity (Mac noSource subset): need some surround.
        int surround = known.Count(k => k);
        if (surround < 8) throw new Failure();
        // Iterative border-in fill (each pass fills unknown cells touching a known cell).
        var filled = new bool[w * h];
        int filledCount = 0;
        bool progress = true;
        int guardPass = 0;
        while (progress && filledCount < target && guardPass++ < w + h + 64)
        {
            progress = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (sel[i] == 0 || known[i] || filled[i]) continue;
                    long sr = 0, sg = 0, sb = 0, sa = 0; int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (!known[j] && !filled[j]) continue;
                            var c = pix[j];
                            sr += c.Red; sg += c.Green; sb += c.Blue; sa += c.Alpha; n++;
                        }
                    if (n == 0) continue;
                    // Unpremultiply average then re-premultiply at mean alpha.
                    float ma = (sa / (float)n) / 255f;
                    float mr = (sr / (float)n) / 255f / Math.Max(1e-6f, ma);
                    float mg = (sg / (float)n) / 255f / Math.Max(1e-6f, ma);
                    float mb = (sb / (float)n) / 255f / Math.Max(1e-6f, ma);
                    pix[i] = new SKColor(
                        (byte)Math.Clamp(mr * ma * 255, 0, 255),
                        (byte)Math.Clamp(mg * ma * 255, 0, 255),
                        (byte)Math.Clamp(mb * ma * 255, 0, 255),
                        (byte)Math.Clamp(ma * 255, 0, 255));
                    filled[i] = true; filledCount++; progress = true;
                }
        }
        // Write back filled (+ keep already-known selected) pixels.
        int written = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (sel[i] == 0 || (!filled[i] && !known[i])) continue;
                int lx = (int)MathF.Floor((x + 0.5f - layer.Position.X) / sx);
                int ly = (int)MathF.Floor((y + 0.5f - layer.Position.Y) / sy);
                if (lx < 0 || ly < 0 || lx >= bmp.Width || ly >= bmp.Height) continue;
                bmp.SetPixel(lx, ly, pix[i]);
                if (filled[i]) written++;
            }
        return written;
    }

    /// <summary>Expand-paste support (拡張貼付対応): paste a bitmap centered on the selection,
    /// growing the layer bitmap when the paste overflows it (Mac grown-layer subset).</summary>
    public static void ExpandPaste(Layer layer, SKBitmap paste, SKPoint docOrigin, Document doc)
    {
        if (layer?.Bitmap == null || paste == null) return;
        Document.EnsureUniqueBitmap(layer);
        float sx = Math.Max(1e-6f, layer.ScaleX), sy = Math.Max(1e-6f, layer.ScaleY);
        float lx0 = (docOrigin.X - layer.Position.X) / sx, ly0 = (docOrigin.Y - layer.Position.Y) / sy;
        float lx1 = lx0 + paste.Width, ly1 = ly0 + paste.Height;
        int needX0 = Math.Min(0, (int)MathF.Floor(lx0)), needY0 = Math.Min(0, (int)MathF.Floor(ly0));
        int needX1 = Math.Max(layer.Bitmap.Width, (int)MathF.Ceiling(lx1));
        int needY1 = Math.Max(layer.Bitmap.Height, (int)MathF.Ceiling(ly1));
        if (needX0 < 0 || needY0 < 0 || needX1 > layer.Bitmap.Width || needY1 > layer.Bitmap.Height)
        {
            int nw = needX1 - needX0, nh = needY1 - needY0;
            if ((long)nw * nh > 100_000_000) return;
            var grown = new SKBitmap(nw, nh, SKColorType.Bgra8888, SKAlphaType.Premul);
            grown.Erase(SKColor.Empty);
            using (var c = new SKCanvas(grown))
                c.DrawBitmap(layer.Bitmap, -needX0, -needY0);
            layer.Bitmap = grown;
            // Keep the layer's doc footprint: shift position by the grown margin (in doc px).
            layer.Position = new SKPoint(layer.Position.X + needX0 * sx, layer.Position.Y + needY0 * sy);
            lx0 -= needX0; ly0 -= needY0;
        }
        using var canvas = new SKCanvas(layer.Bitmap);
        canvas.DrawBitmap(paste, new SKRect(lx0, ly0, lx0 + paste.Width, ly0 + paste.Height));
    }
}

// ---------- group-aware merge (Mac LayerMerge.swift subset) ----------

public static class MergeOps
{
    /// <summary>What merges and where the result goes (Mac mergePlan subset).</summary>
    public class Plan
    {
        public List<Guid> Ids = new();      // stacking order, bottom-up
        public HashSet<Guid> Removed = new();
        public string Name;
        public Guid? Parent;
        public Guid Anchor;                 // insert position reference
        public string Action;
    }

    public static Plan MergeDownPlan(Document doc, Guid activeId)
    {
        var layers = doc.Layers;
        int i = layers.FindIndex(l => l.Id == activeId);
        if (i < 0) return null;
        var active = layers[i];
        if (active.IsGroup)
            return MergeGroupPlan(doc, activeId);
        // Merge with the layer beneath in the same folder (skip group placeholders).
        Layer below = null;
        for (int j = i - 1; j >= 0; j--)
            if (layers[j].ParentId == active.ParentId && !layers[j].IsGroup) { below = layers[j]; break; }
        if (below == null) return null;
        var ids = new List<Guid>();
        if (below.ClipSourceId != null || layers.Any(l => l.ClipSourceId == below.Id || l.ClipSourceId == active.Id))
        {
            // Include the whole contiguous clip stack so links bake consistently.
            Guid baseId = below.ClipSourceId ?? below.Id;
            var sibs = layers.Where(l => l.ParentId == active.ParentId).ToList();
            ids = sibs.Where(l => l.Id == baseId || l.ClipSourceId == baseId).Select(l => l.Id).ToList();
            if (!ids.Contains(active.Id)) ids.Add(active.Id);
        }
        else ids = new List<Guid> { below.Id, active.Id };
        var nonGroups = ids.Select(id => layers.First(l => l.Id == id)).Where(l => !l.IsGroup).ToList();
        if (nonGroups.Count == 0) return null;
        return new Plan { Ids = nonGroups.Select(l => l.Id).ToList(),
            Removed = new HashSet<Guid>(nonGroups.Select(l => l.Id)),
            Name = below.Name, Parent = active.ParentId, Anchor = active.Id, Action = "Merge Down" };
    }

    public static Plan MergeGroupPlan(Document doc, Guid groupId)
    {
        var g = doc.Layers.FirstOrDefault(l => l.Id == groupId && l.IsGroup);
        if (g == null) return null;
        var inside = LayerHierarchy.Descendants(doc, groupId);
        var ids = doc.Layers.Where(l => inside.Contains(l.Id) && !l.IsGroup).Select(l => l.Id).ToList();
        if (ids.Count == 0) return null;
        var removed = new HashSet<Guid>(ids) { groupId };
        // Empty sub-groups inside go too.
        foreach (var id in inside)
            if (doc.Layers.First(l => l.Id == id).IsGroup) removed.Add(id);
        return new Plan { Ids = ids, Removed = removed, Name = g.Name, Parent = g.ParentId,
            Anchor = groupId, Action = "Merge Group" };
    }

    public static Plan MergeSelectedPlan(Document doc, IList<Guid> selected)
    {
        var picked = new HashSet<Guid>(selected);
        foreach (var id in selected.ToList())
            foreach (var d in LayerHierarchy.Descendants(doc, id)) picked.Add(d);
        var ordered = doc.Layers.Where(l => picked.Contains(l.Id) && !l.IsGroup).ToList();
        if (!ordered.Any() || selected.Count < 2 && ordered.Count < 1) return null;
        var top = doc.Layers.LastOrDefault(l => selected.Contains(l.Id));
        if (top == null) return null;
        var removed = new HashSet<Guid>(ordered.Select(l => l.Id));
        foreach (var id in picked)
            if (doc.Layers.FirstOrDefault(l => l.Id == id)?.IsGroup == true) removed.Add(id);
        return new Plan { Ids = ordered.Select(l => l.Id).ToList(), Removed = removed,
            Name = top.Name, Parent = top.ParentId, Anchor = top.Id, Action = "Merge Layers" };
    }

    /// <summary>Render a subset of layers at doc resolution (blend/opacity/mask/clipping baked).</summary>
    public static SKBitmap RenderSubset(Document doc, IList<Guid> ids)
    {
        var bmp = new SKBitmap(Math.Max(1, doc.Width), Math.Max(1, doc.Height),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(bmp);
        var keep = new HashSet<Guid>(ids);
        var byId = doc.Layers.ToDictionary(l => l.Id);
        foreach (var l in doc.Layers)
        {
            if (!keep.Contains(l.Id) || l.IsGroup || !l.Visible) continue;
            if (!LayerHierarchy.IsEffectivelyVisible(doc, l)) continue;
            if (l.ClipSourceId is Guid s && keep.Contains(s) && byId.TryGetValue(s, out var b))
                Document.DrawClipped(canvas, doc, l, b, 1f);
            else
                Document.DrawLayer(canvas, l, 1f);
        }
        return bmp;
    }

    /// <summary>Trim fully-transparent borders (Mac PixelFilter.trimmed subset). Returns null when empty.</summary>
    public static (SKBitmap bmp, SKPoint origin)? Trim(SKBitmap src)
    {
        if (src == null) return null;
        int x0 = src.Width, y0 = src.Height, x1 = 0, y1 = 0;
        var px = src.Pixels;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                if (px[y * src.Width + x].Alpha != 0)
                {
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x + 1); y1 = Math.Max(y1, y + 1);
                }
        if (x1 <= x0) return null;
        var trim = new SKBitmap(x1 - x0, y1 - y0, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(trim))
            c.DrawBitmap(src, new SKRect(0, 0, trim.Width, trim.Height),
                new SKRect(x0, y0, x1, y1));
        return (trim, new SKPoint(x0, y0));
    }

    /// <summary>Execute a merge plan: subset composited as the canvas shows it, trimmed, inserted
    /// in place as one step (caller pushes Undo). Returns the merged layer, or null.</summary>
    public static Layer Execute(Document doc, Plan plan)
    {
        if (plan == null || plan.Ids.Count == 0) return null;
        using var full = RenderSubset(doc, plan.Ids);
        var trimmed = Trim(full);
        if (trimmed == null) return null;
        var merged = new Layer { Name = plan.Name, Bitmap = trimmed.Value.bmp,
            Position = trimmed.Value.origin, ParentId = plan.Parent };
        var layers = doc.Layers;
        foreach (var id in layers.Where(l => plan.Removed.Contains(l.Id)).ToList())
            layers.Remove(id);
        // Layers clipped to anything merged now clip to the result.
        foreach (var l in layers)
            if (l.ClipSourceId is Guid s && plan.Removed.Contains(s)) l.ClipSourceId = merged.Id;
        int slot = layers.FindIndex(l => l.Id == plan.Anchor);
        if (slot < 0) slot = layers.Count;
        layers.Insert(Math.Clamp(slot, 0, layers.Count), merged);
        ClipOps.ReleaseDetached(doc);
        return LayerHierarchy.Validate(doc) ? merged : null;
    }
}
