// Compositor — Document model (port of Compositor/Document core).
// Non-destructive layers: pixels stay untouched; position/scale applied at compose time.
using SkiaSharp;

namespace Compositor;

public class Layer
{
    public Guid Id = Guid.NewGuid();
    public Guid? ParentId;             // group folder, null = root (Mac parentID)
    public bool IsGroup;               // folder placeholder: Bitmap null, skipped in render
    public string Name = "Layer";
    public SKBitmap Bitmap;          // source pixels — copy-on-write: destructive paint clones first (see Document.EnsureUniqueBitmap)
    public bool Visible = true;
    public float Opacity = 1f;       // 0..1
    public SKPoint Position = new(0, 0);  // non-destructive offset in canvas px
    public float ScaleX = 1f;             // non-destructive scale (Mac size.width / pixels)
    public float ScaleY = 1f;             // non-destructive scale (Mac size.height / pixels)
    /// <summary>Legacy uniform scale (kept for existing callers/tests): get = X, set = both.</summary>
    public float Scale { get => ScaleX; set { ScaleX = value; ScaleY = value; } }
    public LayerSampling Sampling = LayerSampling.High;   // Mac LayerSampling (draw-time interpolation)
    public bool Locked;
    public SKBlendMode Blend = SKBlendMode.SrcOver;
    // --- transform (non-destructive, Mac LayerTransform subset) ---
    public float Rotation;           // degrees, clockwise (Mac rotation)
    public bool FlipH;
    public bool FlipV;
    // --- adjustments (non-destructive, Mac ImageAdjustments subset) ---
    // LEGACY (deprecated for new adjustments): per-layer Brightness/Contrast/
    // Saturation/Blur/Invert. Kept for compatibility; new adjustments should use
    // adjustment layers (IsAdjustmentLayer + Adjustment, Mac LayerAdjustment
    // subset) — see Adjustments.cs migration note.
    public float Brightness;         // -100..100 (0 = off)
    public float Contrast;           // -100..100 (0 = off)
    public float Saturation = 100f;  // 0 = gray, 100 = identity, up to 200 boost
    public float Blur;               // Gaussian radius in layer px, 0 = off
    public bool Invert;              // PixelInvert equivalent
    // --- adjustment layer (Mac LayerAdjustment/AdjustmentEditing subset, preferred) ---
    // When true, Bitmap is ignored and Adjustment applies to the composite of the
    // layers below at draw/compose time (non-destructive, Undo-safe metadata).
    public bool IsAdjustmentLayer;
    public LayerAdjustment Adjustment;
    // --- mask / group / clip / shape (Mac LayerMask/LayerGroups/LiveLayerMask/ShapeTool subset) ---
    // Mask covers the layer pixel grid (white = reveal). Offset in layer px (Mac placement subset).
    public byte[] Mask; public int MaskW, MaskH;
    public bool MaskEnabled = true;
    public bool MaskLinked = true;     // linked = rides the layer; unlinked = fixed offset
    public SKPoint MaskOffset;
    public bool MaskSelected;          // true = paint targets the mask (Mac isMaskSelected)
    public Guid? ClipSourceId;         // live/clipping mask base (Mac maskSourceID)
    public ShapeInfo Shape;            // shape style metadata (raster layer; Mac LayerShape subset)
    public float FolderW, FolderH;     // group transform size in doc px (Mac group transform.size; 0 = doc size at creation)
    // P1 ベクター線 (CLIP STUDIO ベクター層の考え): ラスタ焼成後も編集用データを保持。
    public VectorStroke Vector;
    public bool IsVectorLayer;
    // P2 blend range (Affinityの考え): 下地輝度での層の効き具合。
    public bool UseBlendRange;
    public double BlendLo, BlendHi = 1, BlendFeather;

    public SKRect Bounds => Bitmap == null ? SKRect.Empty : SKRect.Create(Position.X, Position.Y, Bitmap.Width * ScaleX, Bitmap.Height * ScaleY);
    public bool HitTest(SKPoint p) => Bitmap != null && Bounds.Contains(p.X, p.Y);
    public float DrawW => Bitmap == null ? 0 : Bitmap.Width * ScaleX;
    public float DrawH => Bitmap == null ? 0 : Bitmap.Height * ScaleY;
}

/// <summary>Lightweight memento of layer stack state. Bitmaps are shared by reference
/// (they are never modified destructively), so snapshots are cheap.</summary>
public class LayerSnapshot
{
    public class Item
    {
        public Layer Layer;
        public SKBitmap BitmapRef;   // copy-on-write anchor: restore reassigns so pre-paint pixels survive Undo
        public Guid Id; public Guid? ParentId; public bool IsGroup;
        public byte[] MaskRef; public int MaskW, MaskH;   // copy-on-write anchor (see EnsureUniqueMask)
        public bool MaskEnabled, MaskLinked, MaskSelected; public SKPoint MaskOffset;
        public Guid? ClipSourceId; public ShapeInfo Shape;
        public float FolderW, FolderH;
        public VectorStroke Vector; public bool IsVectorLayer;   // P1 ベクター線
        public string Name; public bool Visible; public bool Locked;
        public float Opacity, ScaleX, ScaleY; public SKPoint Position; public SKBlendMode Blend;
        public LayerSampling Sampling;
        public float Rotation; public bool FlipH, FlipV;
        public float Brightness, Contrast, Saturation, Blur; public bool Invert;
        public bool IsAdjustmentLayer; public LayerAdjustment Adjustment;
        public bool UseBlendRange; public double BlendLo, BlendHi, BlendFeather;   // P2
    }
    public List<Item> Items = new();
    public int Width, Height;
    public string DocName = "Untitled";
    public Guid DocumentId = Guid.NewGuid();
    public double Resolution = 72;
    public Guid? ActiveLayerId;
    public bool IsModified;

    public static LayerSnapshot Capture(Document doc)
    {
        var s = new LayerSnapshot { Width = doc.Width, Height = doc.Height, DocName = doc.Name,
            DocumentId = doc.DocumentId, Resolution = doc.Resolution,
            ActiveLayerId = doc.ActiveLayerId, IsModified = doc.IsModified };
        foreach (var l in doc.Layers)
            s.Items.Add(new Item { Layer = l, BitmapRef = l.Bitmap, Name = l.Name, Visible = l.Visible, Locked = l.Locked,
                Id = l.Id, ParentId = l.ParentId, IsGroup = l.IsGroup,
                MaskRef = l.Mask, MaskW = l.MaskW, MaskH = l.MaskH,
                MaskEnabled = l.MaskEnabled, MaskLinked = l.MaskLinked, MaskSelected = l.MaskSelected,
                MaskOffset = l.MaskOffset, ClipSourceId = l.ClipSourceId, Shape = l.Shape?.Clone(),
                FolderW = l.FolderW, FolderH = l.FolderH,
                Vector = l.Vector?.Clone(), IsVectorLayer = l.IsVectorLayer,
                Opacity = l.Opacity, ScaleX = l.ScaleX, ScaleY = l.ScaleY, Position = l.Position, Blend = l.Blend,
                Sampling = l.Sampling,
                Rotation = l.Rotation, FlipH = l.FlipH, FlipV = l.FlipV,
                Brightness = l.Brightness, Contrast = l.Contrast, Saturation = l.Saturation,
                Blur = l.Blur, Invert = l.Invert,
                IsAdjustmentLayer = l.IsAdjustmentLayer, Adjustment = l.Adjustment?.Clone(),
                UseBlendRange = l.UseBlendRange, BlendLo = l.BlendLo, BlendHi = l.BlendHi,
                BlendFeather = l.BlendFeather });
        return s;
    }

    public void Restore(Document doc)
    {
        doc.Width = Width; doc.Height = Height; doc.Name = DocName;
        doc.DocumentId = DocumentId; doc.Resolution = Resolution;
        doc.ActiveLayerId = ActiveLayerId; doc.IsModified = IsModified;
        doc.Layers.Clear();
        foreach (var it in Items)
        {
            it.Layer.Bitmap = it.BitmapRef;
            it.Layer.Id = it.Id; it.Layer.ParentId = it.ParentId; it.Layer.IsGroup = it.IsGroup;
            it.Layer.Mask = it.MaskRef; it.Layer.MaskW = it.MaskW; it.Layer.MaskH = it.MaskH;
            it.Layer.MaskEnabled = it.MaskEnabled; it.Layer.MaskLinked = it.MaskLinked;
            it.Layer.MaskSelected = it.MaskSelected; it.Layer.MaskOffset = it.MaskOffset;
            it.Layer.ClipSourceId = it.ClipSourceId; it.Layer.Shape = it.Shape?.Clone();
            it.Layer.Vector = it.Vector?.Clone(); it.Layer.IsVectorLayer = it.IsVectorLayer;
            it.Layer.FolderW = it.FolderW; it.Layer.FolderH = it.FolderH;
            it.Layer.Name = it.Name; it.Layer.Visible = it.Visible; it.Layer.Locked = it.Locked;
            it.Layer.Opacity = it.Opacity; it.Layer.ScaleX = it.ScaleX; it.Layer.ScaleY = it.ScaleY; it.Layer.Position = it.Position;
            it.Layer.Blend = it.Blend; it.Layer.Sampling = it.Sampling;
            it.Layer.Rotation = it.Rotation; it.Layer.FlipH = it.FlipH; it.Layer.FlipV = it.FlipV;
            it.Layer.Brightness = it.Brightness; it.Layer.Contrast = it.Contrast;
            it.Layer.Saturation = it.Saturation; it.Layer.Blur = it.Blur; it.Layer.Invert = it.Invert;
            it.Layer.IsAdjustmentLayer = it.IsAdjustmentLayer; it.Layer.Adjustment = it.Adjustment?.Clone();
            it.Layer.UseBlendRange = it.UseBlendRange; it.Layer.BlendLo = it.BlendLo;
            it.Layer.BlendHi = it.BlendHi; it.Layer.BlendFeather = it.BlendFeather;
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
        if (doc != null) doc.IsModified = true;
        Changed?.Invoke();
    }

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    /// <summary>Depth of the undo stack (for draft owners to tell whether their entry is still on top).</summary>
    public int UndoDepth => undo.Count;

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

    /// <summary>Drop the newest undo entry WITHOUT restoring it and WITHOUT touching redo.
    /// For cancelling a non-destructive draft whose press-time Push must vanish (click-without-drag).
    /// Only the owner that pushed it may call this, and only while its entry is still on top.</summary>
    public void DiscardLast()
    {
        if (!CanUndo) return;
        undo.RemoveAt(undo.Count - 1);
        Changed?.Invoke();
    }
}

public class Document
{
    public int Width;
    public int Height;
    public string Name = "Untitled";
    public Guid DocumentId = Guid.NewGuid();
    public double Resolution = 72;      // pixels/inch, 1..9600 (Mac CanvasDocument.resolution; default 72)
    public string ProjectPath;          // .comp package directory path; null = unsaved
    public Guid? ActiveLayerId;         // active layer (Mac activeLayerID; round-trips in manifest)
    public bool IsModified;             // set by UndoStack.Push; cleared by save/load (Mac history.isModified)
    public Guid? SoloLayerId;           // P0 Solo 表示: 非null時は対象層のみ表示 (view 状態・Undo/保存対象外)
    public float CanvasAngle;           // P0 canvas 回転: 表示のみの回転角 (-180,180]・画素不変)
    // P1 session 状態 (Undo/保存対象外。spare channel のみ .comp v8 で保存):
    public List<SpareChannel> SpareChannels = new();   // spare channel (Affinity/GIMP の考え)
    public byte[] QuickMask; public int QuickMaskW, QuickMaskH;
    public bool QuickMaskEnabled;
    public List<TimelapseEntry> Timelapse = new();      // 過程記録 (session-only)
    public bool TimelapseRecording;
    public PersonaKind Persona = PersonaKind.Paint;     // Persona弱移植 (描く/整える/出す)
    public bool SimpleMode;                             // Simple preset (初心者縮小)
    // P2 表現拡張の文書状態 (.comp v9で永続するのは History・Macro・Slices・
    // blendRange・IccProfile・HdrEv のみ。他は session-only で肥大防止):
    public SymmetryMode Symmetry;                       // 対称描画 (view 状態・保存対象外)
    public bool WrapEnabled;                            // Wrap-Around preview (view 状態・保存対象外)
    public SKPoint VanishingPoint;                      // 透視消失点 (session-only)
    public bool HasVanishingPoint;                      // session-only
    public List<HistoryEntry> History = new();          // P2 v9 永続 (上限200・画素なし)
    public List<MacroStep> Macro = new();               // P2 v9 永続 (上限64)
    public List<Slice> Slices = new();                  // P2 v9 永続 (上限64)
    public IccProfileKind IccProfile;                   // P2 v9 永続
    public double HdrEv;                                // P2 v9 永続 (-8..8・0=off)
    public GamutKind Gamut = GamutKind.Full;            // session-only
    public List<SKBitmap> VideoFrames = new();          // 動画層 frame列 (session-only)
    /// <summary>Mark the document saved (Mac history.markSaved subset).</summary>
    public void MarkSaved() => IsModified = false;
    /// <summary>Geometry limit shared with NewCanvas (Mac CanvasDocument.validDimension).</summary>
    public static int? ValidDimension(string value)
    {
        if (!int.TryParse((value ?? "").Trim(), out int n)) return null;
        return (n >= 1 && n <= 30_000) ? n : (int?)null;
    }
    public List<Layer> Layers = new();   // bottom-up order
    /// <summary>Marquee selection bounds in document px (Mac DocumentSelection subset: rect + ellipse bbox).</summary>
    /// null = no selection (whole canvas). The outline kind and vector/mask detail live in
    /// SelKind / SelectionPolygon / SelectionMask below.
    public SKRect? Selection;
    /// <summary>Outline kind of the current selection (Mac LassoKind subset: rectangle/ellipse/freehand/polygonal/wand).</summary>
    public SelectionKind SelKind = SelectionKind.Rectangle;
    /// <summary>Outline vertices in document px for Freehand/Polygon/Wand (closed loop, may be null).</summary>
    public List<SKPoint> SelectionPolygon;
    /// <summary>Rasterized selection mask at document resolution (1 = selected). Null = derive from Selection/SelKind.</summary>
    public byte[] SelectionMask;
    public int SelectionMaskW, SelectionMaskH;

    public event Action Changed;
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Composite all visible layers bottom-up into a flat bitmap of doc size.
    /// Adjustment layers apply to the composite of the layers below (Mac LayerAdjustment).
    /// P2 blend range layers also route through the CPU path.</summary>
    public SKBitmap Compose()
    {
        if (Layers.Any(l => l is { Visible: true, IsAdjustmentLayer: true, Adjustment: not null })
            || Layers.Any(l => l is { Visible: true, UseBlendRange: true } && !l.IsGroup && !l.IsAdjustmentLayer))
            return ComposeWithAdjustments();
        var bmp = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        Draw(canvas, 1f);
        return bmp;
    }

    public bool HasAdjustmentLayers =>
        Layers.Any(l => l is { Visible: true, IsAdjustmentLayer: true, Adjustment: not null });

    /// <summary>CPU composite honoring adjustment layers (non-destructive, Undo-safe).
    /// P2 blend range layers modulate by underlying luminance here too.</summary>
    public SKBitmap ComposeWithAdjustments()
    {
        var acc = new SKBitmap(Math.Max(1, Width), Math.Max(1, Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        acc.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(acc);
        var byId = Layers.ToDictionary(l => l.Id);
        foreach (var layer in Layers)
        {
            if (layer.IsGroup || !LayerHierarchy.IsEffectivelyVisible(this, layer)) continue;
            if (layer is { IsAdjustmentLayer: true, Adjustment: not null })
            {
                // P1: 覆面つき調整層 (Adjustment Brush/filter mask) は覆面で変調する。
                if (MaskOps.IsActive(layer))
                {
                    using var before = new SKBitmap(acc.Width, acc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using (var c2 = new SKCanvas(before))
                        c2.DrawBitmap(acc, 0, 0);
                    layer.Adjustment.ApplyToComposite(acc);
                    P1Compose.BlendMasked(acc, before, layer, Width, Height);
                }
                else layer.Adjustment.ApplyToComposite(acc);
            }
            else if (layer.ClipSourceId is Guid s && byId.TryGetValue(s, out var b))
                DrawClipped(canvas, this, layer, b, 1f);
            else if (layer is { Visible: true, UseBlendRange: true })
            {
                // P2: blend range (Affinityの考え) は下地輝度で変調する。
                var range = new BlendRange { Lo = layer.BlendLo, Hi = layer.BlendHi, Feather = layer.BlendFeather };
                if (!range.IsValid) { DrawLayer(canvas, layer, 1f); continue; }
                using var before = new SKBitmap(acc.Width, acc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var c2 = new SKCanvas(before))
                    c2.DrawBitmap(acc, 0, 0);
                DrawLayer(canvas, layer, 1f);
                P2Blend.BlendRangeModulate(acc, before, range, Width, Height);
            }
            else
                DrawLayer(canvas, layer, 1f);
        }
        return acc;
    }

    public void Draw(SKCanvas canvas, float zoom, SKRect? viewportDocRect = null)
    {
        if (HasAdjustmentLayers
            || Layers.Any(l => l is { Visible: true, UseBlendRange: true } && !l.IsGroup && !l.IsAdjustmentLayer))
        {
            // 調整層・P2 blend range層は文書解像度で先に合成してから blit (preview路・Composeと一致)。
            using var acc = ComposeWithAdjustments();
            canvas.DrawBitmap(acc, new SKRect(0, 0, Width * zoom, Height * zoom));
            return;
        }
        var byId = Layers.ToDictionary(l => l.Id);
        foreach (var layer in Layers)
        {
            if (layer.IsGroup || !LayerHierarchy.IsEffectivelyVisible(this, layer)) continue;
            if (viewportDocRect is SKRect vp && IsOutsideViewport(layer, vp))
                continue;
            if (layer.ClipSourceId is Guid s && byId.TryGetValue(s, out var b))
                DrawClipped(canvas, this, layer, b, zoom);
            else
                DrawLayer(canvas, layer, zoom);
        }
    }

    /// <summary>Draw a clipped layer constrained to its base's alpha coverage (Mac live-mask
    /// subset: DstIn approximation — base transform/rotation honored via DrawLayer).</summary>
    public static void DrawClipped(SKCanvas canvas, Document doc, Layer layer, Layer baseLayer, float zoom)
    {
        int w = Math.Max(1, (int)(doc.Width * zoom)), h = Math.Max(1, (int)(doc.Height * zoom));
        using var content = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        content.Erase(SKColor.Empty);
        using (var c = new SKCanvas(content))
        {
            // Neutral proxy: opacity/blend apply once at final composite below.
            var proxy = new Layer { Bitmap = layer.Bitmap, Visible = true, Opacity = 1f,
                Position = layer.Position, ScaleX = layer.ScaleX, ScaleY = layer.ScaleY,
                Sampling = layer.Sampling, Blend = SKBlendMode.SrcOver,
                Rotation = layer.Rotation, FlipH = layer.FlipH, FlipV = layer.FlipV,
                Brightness = layer.Brightness, Contrast = layer.Contrast, Saturation = layer.Saturation,
                Blur = layer.Blur, Invert = layer.Invert,
                Mask = layer.Mask, MaskW = layer.MaskW, MaskH = layer.MaskH, MaskEnabled = layer.MaskEnabled,
                MaskOffset = layer.MaskOffset, MaskLinked = layer.MaskLinked };
            DrawLayer(c, proxy, zoom);
        }
        using var coverage = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        coverage.Erase(SKColor.Empty);
        using (var c = new SKCanvas(coverage))
        {
            var proxy = new Layer { Bitmap = baseLayer.Bitmap, Visible = true, Opacity = 1f,
                Position = baseLayer.Position, ScaleX = baseLayer.ScaleX, ScaleY = baseLayer.ScaleY,
                Sampling = baseLayer.Sampling, Blend = SKBlendMode.SrcOver,
                Rotation = baseLayer.Rotation, FlipH = baseLayer.FlipH, FlipV = baseLayer.FlipV };
            if (proxy.Bitmap != null) DrawLayer(c, proxy, zoom);
            else if (MaskOps.IsActive(baseLayer))
            {
                // Mask-only base (e.g. fill layer): coverage from the mask itself.
                using var m = MaskOps.ApplyToBitmap(
                    SolidWhite(baseLayer.Bitmap?.Width ?? doc.Width, baseLayer.Bitmap?.Height ?? doc.Height),
                    baseLayer.Mask, baseLayer.MaskW, baseLayer.MaskH, baseLayer.MaskOffset);
                if (m != null) DrawLayer(c, new Layer { Bitmap = m, Visible = true }, zoom);
            }
        }
        using (var c = new SKCanvas(content))
        using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn })
            c.DrawBitmap(coverage, 0, 0, p);
        using var paint = new SKPaint { BlendMode = layer.Blend,
            Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)) };
        paint.FilterQuality = SamplingUtil.ToQuality(layer.Sampling);
        canvas.DrawBitmap(content, 0, 0, paint);
    }

    static SKBitmap SolidWhite(int w, int h)
    {
        var b = new SKBitmap(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(SKColors.White);
        return b;
    }

    static bool IsOutsideViewport(Layer layer, SKRect vp)
    {
        if (layer is not { Visible: true, Bitmap: not null } || layer.Opacity <= 0f) return true;
        if (layer.Rotation != 0 || layer.FlipH || layer.FlipV) return false; // transformed: conservative, no cull
        if (layer.ScaleX != layer.ScaleY) return false;   // non-uniform: conservative, no cull
        float l = layer.Position.X, t = layer.Position.Y;
        float r = l + layer.Bitmap.Width * layer.ScaleX, b = t + layer.Bitmap.Height * layer.ScaleY;
        return r <= vp.Left || l >= vp.Right || b <= vp.Top || t >= vp.Bottom;
    }

    /// <summary>Mask copy-on-write: clone mask bytes before any destructive mask paint so Undo keeps pre-paint pixels.</summary>
    public static void EnsureUniqueMask(Layer layer)
    {
        if (layer?.Mask == null) return;
        var copy = new byte[layer.Mask.Length];
        Array.Copy(layer.Mask, copy, copy.Length);
        layer.Mask = copy;
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
    public static void PaintStroke(Layer layer, IList<SKPoint> points, SKColor color, float diameter, bool erasing, float opacity = 1f, SKRect? clip = null)
    {
        if (layer?.Bitmap == null || points == null || points.Count == 0 || diameter <= 0) return;
        EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        bool clipped = clip is SKRect c && c.Width > 0 && c.Height > 0;
        if (clipped) { canvas.Save(); canvas.ClipRect(clip.Value); }
        try
        {
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
        finally { if (clipped) canvas.Restore(); }
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
        // Layer mask (Mac LayerMask subset): bake coverage into a temp copy, then draw normally.
        // Mask rides the layer (linked) by construction — pixels live in layer space; unlinked
        // offsets sample shifted coverage (see MaskOps.CoverageAt).
        if (MaskOps.IsActive(layer))
        {
            using var masked = MaskOps.ApplyToBitmap(layer.Bitmap, layer.Mask, layer.MaskW, layer.MaskH, layer.MaskOffset);
            if (masked == null) return;
            var proxy = new Layer
            {
                Bitmap = masked, Visible = true, Opacity = layer.Opacity, Position = layer.Position,
                ScaleX = layer.ScaleX, ScaleY = layer.ScaleY, Sampling = layer.Sampling, Blend = layer.Blend,
                Rotation = layer.Rotation, FlipH = layer.FlipH, FlipV = layer.FlipV,
                Brightness = layer.Brightness, Contrast = layer.Contrast, Saturation = layer.Saturation,
                Blur = layer.Blur, Invert = layer.Invert,
            };
            DrawLayer(canvas, proxy, zoom);
            return;
        }
        using var paint = new SKPaint();
        paint.Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255));
        paint.BlendMode = layer.Blend;
        paint.FilterQuality = SamplingUtil.ToQuality(layer.Sampling);
        if (NeedsAdjustmentFilter(layer))
            paint.ColorFilter = BuildAdjustmentFilter(layer);
        if (layer.Blur > 0)
            paint.ImageFilter = SKImageFilter.CreateBlur(layer.Blur * zoom, layer.Blur * zoom);
        float w = layer.Bitmap.Width * layer.ScaleX, h = layer.Bitmap.Height * layer.ScaleY;
        bool hasTransform = layer.Rotation != 0 || layer.FlipH || layer.FlipV ||
            layer.ScaleX != layer.ScaleY;
        if (!hasTransform)
        {
            var dst = new SKRect(
                layer.Position.X * zoom, layer.Position.Y * zoom,
                (layer.Position.X + layer.Bitmap.Width * layer.ScaleX) * zoom,
                (layer.Position.Y + layer.Bitmap.Height * layer.ScaleY) * zoom);
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
            // Box size already carries ScaleX/ScaleY; the matrix only rotates/flips about its center.
            var dst = new SKRect(
                layer.Position.X * zoom, layer.Position.Y * zoom,
                (layer.Position.X + w) * zoom, (layer.Position.Y + h) * zoom);
            canvas.DrawBitmap(layer.Bitmap, dst, paint);
        }
        finally { canvas.Restore(); }
    }

    /// <summary>Hit test against the current selection outline (Mac Selection.swift subset).
    /// Rect = bounds, Ellipse = ellipse equation, Freehand/Polygon = point-in-polygon,
    /// Wand = stored mask (falls back to bounds when no mask).</summary>
    public bool InsideSelection(SKPoint docPoint)
    {
        if (Selection is not SKRect r) return true;   // no selection = everything
        return SelectionTools.Contains(this, docPoint);
    }

    /// <summary>Clear the selection (Mac deselect). Selection itself is never an Undo step.</summary>
    public void ClearSelection()
    {
        Selection = null;
        SelectionPolygon = null;
        SelectionMask = null;
        SelectionMaskW = SelectionMaskH = 0;
    }
}

/// <summary>Selection outline kind (Mac LassoKind subset).</summary>
public enum SelectionKind { Rectangle, Ellipse, Freehand, Polygon, Wand }

/// <summary>How a new outline combines with the existing selection (Mac SelectionMode).</summary>
public enum SelectionMode { Replace, Add, Subtract }

/// <summary>Lifted pixels being dragged (Mac PixelMove/FloatingSelection subset).
/// Pixels are stored cropped to the selection bounds; Origin is the cut position in doc px.</summary>
public class FloatingSelection
{
    public SKBitmap Pixels;      // cropped raster (transparent outside the outline)
    public SKPoint Origin;       // doc-px top-left where the pixels were cut
    public SKPoint Offset;       // current drag offset in doc px
    public bool Duplicate;       // true = source kept (Alt-drag copy)
}

/// <summary>Selection tools ported from Mac Selection.swift / MagicWand.swift / WandPixels.c
/// (raster/atlas approach: outlines rasterize to a doc-resolution mask for edits).</summary>
public static class SelectionTools
{
    /// <summary>Drag box from anchor to point in whole pixels (Mac DragBox.rect port).
    /// square evens the sides (Shift), fromCenter grows around the anchor (Option/Alt).</summary>
    public static SKRect DragBoxRect(SKPoint anchor, SKPoint point, bool square, bool fromCenter)
    {
        float dx = MathF.Round(point.X) - anchor.X, dy = MathF.Round(point.Y) - anchor.Y;
        if (square)
        {
            float side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = dx < 0 ? -side : side;
            dy = dy < 0 ? -side : side;
        }
        if (fromCenter)
            return new SKRect(anchor.X - Math.Abs(dx), anchor.Y - Math.Abs(dy),
                              anchor.X + Math.Abs(dx), anchor.Y + Math.Abs(dy));
        return new SKRect(Math.Min(anchor.X, anchor.X + dx), Math.Min(anchor.Y, anchor.Y + dy),
                          Math.Max(anchor.X, anchor.X + dx), Math.Max(anchor.Y, anchor.Y + dy));
    }

    public static bool EllipseContains(SKRect bounds, SKPoint p)
    {
        float rx = bounds.Width / 2, ry = bounds.Height / 2;
        if (rx <= 0 || ry <= 0) return false;
        float nx = (p.X - (bounds.Left + rx)) / rx, ny = (p.Y - (bounds.Top + ry)) / ry;
        return nx * nx + ny * ny <= 1f;
    }

    /// <summary>Even-odd point-in-polygon (float, doc px).</summary>
    public static bool PointInPolygon(IList<SKPoint> poly, SKPoint p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static SKRect PolygonBounds(IList<SKPoint> poly)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in poly)
        {
            x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
            x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
        }
        return new SKRect(x0, y0, x1, y1);
    }

    /// <summary>矩形の枠内判定 (inclusive)。SKRect.Contains は右/下端を除外する半開区間のため、
    /// 選択枠の右端・下端の押下が枠外扱いになり層掴み枝へ落ちる (t_04a6dbab)。枠表示と一致させる。</summary>
    public static bool RectContainsInclusive(SKRect r, SKPoint p) =>
        p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    public static bool Contains(Document doc, SKPoint p)
    {
        var r = doc.Selection.Value;
        switch (doc.SelKind)
        {
            case SelectionKind.Ellipse: return EllipseContains(r, p);
            case SelectionKind.Freehand:
            case SelectionKind.Polygon:
                if (doc.SelectionPolygon != null && doc.SelectionPolygon.Count >= 3)
                    return PointInPolygon(doc.SelectionPolygon, p);
                return RectContainsInclusive(r, p);
            case SelectionKind.Wand:
                if (doc.SelectionMask != null && doc.SelectionMaskW == doc.Width && doc.SelectionMaskH == doc.Height)
                {
                    int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y);
                    if (x < 0 || y < 0 || x >= doc.Width || y >= doc.Height) return false;
                    return doc.SelectionMask[y * doc.Width + x] != 0;
                }
                return RectContainsInclusive(r, p);
            default: return RectContainsInclusive(r, p);
        }
    }

    // ---------- rasterization (doc-resolution masks, 1 = selected) ----------

    public static byte[] RasterizeRect(SKRect r, int w, int h)
    {
        var m = new byte[w * h];
        int x0 = Math.Clamp((int)MathF.Floor(r.Left), 0, w), x1 = Math.Clamp((int)MathF.Ceiling(r.Right), 0, w);
        int y0 = Math.Clamp((int)MathF.Floor(r.Top), 0, h), y1 = Math.Clamp((int)MathF.Ceiling(r.Bottom), 0, h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) m[y * w + x] = 1;
        return m;
    }

    public static byte[] RasterizeEllipse(SKRect bounds, int w, int h)
    {
        var m = new byte[w * h];
        int x0 = Math.Clamp((int)MathF.Floor(bounds.Left), 0, w), x1 = Math.Clamp((int)MathF.Ceiling(bounds.Right), 0, w);
        int y0 = Math.Clamp((int)MathF.Floor(bounds.Top), 0, h), y1 = Math.Clamp((int)MathF.Ceiling(bounds.Bottom), 0, h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (EllipseContains(bounds, new SKPoint(x + 0.5f, y + 0.5f))) m[y * w + x] = 1;
        return m;
    }

    public static byte[] RasterizePolygon(IList<SKPoint> poly, int w, int h)
    {
        var m = new byte[w * h];
        if (poly == null || poly.Count < 3) return m;
        var bb = PolygonBounds(poly);
        int x0 = Math.Clamp((int)MathF.Floor(bb.Left), 0, w), x1 = Math.Clamp((int)MathF.Ceiling(bb.Right), 0, w);
        int y0 = Math.Clamp((int)MathF.Floor(bb.Top), 0, h), y1 = Math.Clamp((int)MathF.Ceiling(bb.Bottom), 0, h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (PointInPolygon(poly, new SKPoint(x + 0.5f, y + 0.5f))) m[y * w + x] = 1;
        return m;
    }

    /// <summary>Rasterize the document's current selection at doc resolution.</summary>
    public static byte[] CurrentMask(Document doc)
    {
        int w = doc.Width, h = doc.Height;
        if (doc.Selection is not SKRect r || w <= 0 || h <= 0) return new byte[Math.Max(0, w * h)];
        if (doc.SelKind == SelectionKind.Wand && doc.SelectionMask != null &&
            doc.SelectionMaskW == w && doc.SelectionMaskH == h)
            return (byte[])doc.SelectionMask.Clone();
        return doc.SelKind switch
        {
            SelectionKind.Ellipse => RasterizeEllipse(r, w, h),
            SelectionKind.Freehand or SelectionKind.Polygon =>
                doc.SelectionPolygon != null && doc.SelectionPolygon.Count >= 3
                    ? RasterizePolygon(doc.SelectionPolygon, w, h) : RasterizeRect(r, w, h),
            _ => RasterizeRect(r, w, h),
        };
    }

    static SKRect MaskBounds(byte[] m, int w, int h)
    {
        int x0 = w, y0 = h, x1 = 0, y1 = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (m[y * w + x] != 0) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x + 1); y1 = Math.Max(y1, y + 1); }
        return x1 <= x0 ? SKRect.Empty : new SKRect(x0, y0, x1, y1);
    }

    /// <summary>Combine a new outline mask with the current selection (Mac SelectionMode).
    /// Empty result clears the selection (explicit empty, edits touch nothing).</summary>
    public static void ApplyMask(Document doc, byte[] fresh, SelectionKind kind, List<SKPoint> polygon, SelectionMode mode)
    {
        int w = doc.Width, h = doc.Height;
        if (w <= 0 || h <= 0 || fresh == null || fresh.Length != w * h) return;
        byte[] result;
        if (doc.Selection == null)
        {
            if (mode == SelectionMode.Subtract) return;   // nothing to subtract from
            result = fresh;
        }
        else if (mode == SelectionMode.Replace)
            result = fresh;
        else
        {
            var cur = CurrentMask(doc);
            result = new byte[w * h];
            if (mode == SelectionMode.Add)
                for (int i = 0; i < result.Length; i++) result[i] = (cur[i] != 0 || fresh[i] != 0) ? (byte)1 : (byte)0;
            else
                for (int i = 0; i < result.Length; i++) result[i] = (cur[i] != 0 && fresh[i] == 0) ? (byte)1 : (byte)0;
        }
        var bb = MaskBounds(result, w, h);
        if (bb.IsEmpty) { doc.ClearSelection(); return; }
        doc.Selection = bb;
        doc.SelKind = kind;
        doc.SelectionPolygon = (kind is SelectionKind.Freehand or SelectionKind.Polygon) ? polygon : null;
        if (kind == SelectionKind.Wand) { doc.SelectionMask = result; doc.SelectionMaskW = w; doc.SelectionMaskH = h; }
        else { doc.SelectionMask = null; doc.SelectionMaskW = doc.SelectionMaskH = 0; }
    }

    public static void SetRectSelection(Document doc, SKRect rect, SelectionMode mode, SelectionKind kind = SelectionKind.Rectangle)
    {
        rect.Intersect(new SKRect(0, 0, doc.Width, doc.Height));
        if (rect.Width < 1 || rect.Height < 1)
        {
            if (mode == SelectionMode.Replace) doc.ClearSelection();
            return;
        }
        var fresh = kind == SelectionKind.Ellipse
            ? RasterizeEllipse(rect, doc.Width, doc.Height)
            : RasterizeRect(rect, doc.Width, doc.Height);
        ApplyMask(doc, fresh, kind, null, mode);
        // Keep the dragged box as the bounds for rect/ellipse (pixel-snapped, no raster wobble).
        // Replace-only: Add/Subtract keep the combined mask bounds (incl. cleared=null).
        if (mode == SelectionMode.Replace && doc.Selection != null) { doc.Selection = rect; doc.SelKind = kind; }
    }

    /// <summary>Confirm a freehand/polygon draft (Enter / double-click). Tiny drafts are ignored.</summary>
    public static bool ConfirmPolygon(Document doc, IList<SKPoint> points, SelectionMode mode, SelectionKind kind)
    {
        if (points == null || points.Count < 3) return false;
        var bb = PolygonBounds(points);
        if (bb.Width < 3 || bb.Height < 3) return false;
        var fresh = RasterizePolygon(points, doc.Width, doc.Height);
        ApplyMask(doc, fresh, kind, new List<SKPoint>(points), mode);
        return doc.Selection != null;
    }

    // ---------- Magic Wand (WandPixels.c wand_mask port, C#) ----------

    static bool WandMatches(SKColor p, int[] reference, int tolerance) =>
        Math.Abs(p.Red - reference[0]) <= tolerance &&
        Math.Abs(p.Green - reference[1]) <= tolerance &&
        Math.Abs(p.Blue - reference[2]) <= tolerance &&
        Math.Abs(p.Alpha - reference[3]) <= tolerance;

    /// <summary>Flood-fill / global match on a doc-sized sample (Mac wand_mask port).
    /// radius = sample box radius (Mac WandSampleSize.radius), tolerance 0..255 per channel.</summary>
    public static (byte[] mask, int count) WandMask(SKBitmap sample, int seedX, int seedY, int radius, int tolerance, bool contiguous)
    {
        int w = sample.Width, h = sample.Height;
        var mask = new byte[w * h];
        if (w <= 0 || h <= 0 || seedX < 0 || seedY < 0 || seedX >= w || seedY >= h) return (mask, 0);
        var pixels = sample.Pixels;   // single managed copy (unpremultiplied RGBA)
        int x0 = Math.Max(0, seedX - radius), x1 = Math.Min(w - 1, seedX + radius);
        int y0 = Math.Max(0, seedY - radius), y1 = Math.Min(h - 1, seedY + radius);
        long[] sums = new long[4]; long samples = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = pixels[y * w + x];
                sums[0] += c.Red; sums[1] += c.Green; sums[2] += c.Blue; sums[3] += c.Alpha;
                samples++;
            }
        int[] reference = new int[4];
        for (int c = 0; c < 4; c++) reference[c] = (int)((sums[c] + samples / 2) / Math.Max(1, samples));

        int count = 0;
        if (!contiguous)
        {
            for (int i = 0; i < pixels.Length; i++)
                if (WandMatches(pixels[i], reference, tolerance)) { mask[i] = 1; count++; }
            return (mask, count);
        }
        // Scanline flood fill (same shape as wand_mask: run fill + one seed per run above/below).
        var stack = new Stack<(int x, int y)>();
        stack.Push((seedX, seedY));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            int row = y * w;
            if (mask[row + x] != 0 || !WandMatches(pixels[row + x], reference, tolerance)) continue;
            int left = x, right = x;
            while (left > 0 && mask[row + left - 1] == 0 && WandMatches(pixels[row + left - 1], reference, tolerance)) left--;
            while (right + 1 < w && mask[row + right + 1] == 0 && WandMatches(pixels[row + right + 1], reference, tolerance)) right++;
            for (int i = left; i <= right; i++) mask[row + i] = 1;
            count += right - left + 1;
            for (int side = 0; side < 2; side++)
            {
                if (side == 0 ? y == 0 : y + 1 >= h) continue;
                int ny = side == 0 ? y - 1 : y + 1, nrow = ny * w;
                bool inRun = false;
                for (int nx = left; nx <= right; nx++)
                {
                    bool candidate = mask[nrow + nx] == 0 && WandMatches(pixels[nrow + nx], reference, tolerance);
                    if (candidate && !inRun) stack.Push((nx, ny));
                    inRun = candidate;
                }
            }
        }
        return (mask, count);
    }

    /// <summary>Run the wand at a document point against a doc-sized sample bitmap
    /// (active layer or full composite for sampleAllLayers). Returns matched pixel count.</summary>
    public static int WandSelect(Document doc, SKBitmap sample, SKPoint docPoint, int tolerance, int sampleRadius, bool contiguous, SelectionMode mode)
    {
        if (sample == null) return 0;
        int sx = (int)MathF.Floor(docPoint.X), sy = (int)MathF.Floor(docPoint.Y);
        // Map doc px -> sample px when sizes match; otherwise require doc-sized samples.
        if (sample.Width != doc.Width || sample.Height != doc.Height) return 0;
        tolerance = Math.Clamp(tolerance, 0, 255);
        sampleRadius = Math.Clamp(sampleRadius, 0, 2);
        var (mask, count) = WandMask(sample, sx, sy, sampleRadius, tolerance, contiguous);
        if (count == 0)
        {
            if (mode == SelectionMode.Replace) doc.ClearSelection();
            return 0;
        }
        ApplyMask(doc, mask, SelectionKind.Wand, null, mode);
        return count;
    }

    // ---------- pixel edits inside the selection ----------

    static void DocToLayer(Layer l, float dx, float dy, out float lx, out float ly)
    {
        float sx = Math.Max(1e-6f, l.ScaleX), sy = Math.Max(1e-6f, l.ScaleY);
        lx = (dx - l.Position.X) / sx; ly = (dy - l.Position.Y) / sy;
    }

    /// <summary>Delete: selection pixels become transparent (Mac clearSelectedPixels).
    /// No selection handled by the caller (layer delete). Returns cleared pixel count.</summary>
    public static int DeleteSelection(Document doc, Layer layer)
    {
        if (layer?.Bitmap == null || doc.Selection == null) return 0;
        Document.EnsureUniqueBitmap(layer);
        var bmp = layer.Bitmap;
        int cleared = 0;
        if (doc.SelKind == SelectionKind.Rectangle)
        {
            DocToLayer(layer, doc.Selection.Value.Left, doc.Selection.Value.Top, out float x0, out float y0);
            DocToLayer(layer, doc.Selection.Value.Right, doc.Selection.Value.Bottom, out float x1, out float y1);
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Clear };
            canvas.DrawRect(new SKRect(x0, y0, x1, y1), paint);
            cleared = Math.Max(0, (int)((x1 - x0) * (y1 - y0)));
            return cleared;
        }
        var mask = CurrentMask(doc);
        for (int y = 0; y < doc.Height; y++)
            for (int x = 0; x < doc.Width; x++)
            {
                if (mask[y * doc.Width + x] == 0) continue;
                DocToLayer(layer, x + 0.5f, y + 0.5f, out float lx, out float ly);
                int ix = (int)MathF.Floor(lx), iy = (int)MathF.Floor(ly);
                if (ix < 0 || iy < 0 || ix >= bmp.Width || iy >= bmp.Height) continue;
                bmp.SetPixel(ix, iy, SKColor.Empty);
                cleared++;
            }
        return cleared;
    }

    /// <summary>Copy the selected pixels cropped to the selection bounds (Mac renderSelectedPixels
    /// subset: single layer, hard mask). Returns bitmap + doc-px origin for paste-in-place.</summary>
    public static (SKBitmap bmp, SKPoint origin)? CopySelection(Document doc, Layer layer)
    {
        if (layer?.Bitmap == null || doc.Selection == null) return null;
        var r = doc.Selection.Value;
        int x0 = Math.Clamp((int)MathF.Floor(r.Left), 0, doc.Width), x1 = Math.Clamp((int)MathF.Ceiling(r.Right), 0, doc.Width);
        int y0 = Math.Clamp((int)MathF.Floor(r.Top), 0, doc.Height), y1 = Math.Clamp((int)MathF.Ceiling(r.Bottom), 0, doc.Height);
        if (x1 <= x0 || y1 <= y0) return null;
        var bmp = new SKBitmap(x1 - x0, y1 - y0, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        var mask = doc.SelKind == SelectionKind.Rectangle ? null : CurrentMask(doc);
        var src = layer.Bitmap;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (mask != null && mask[y * doc.Width + x] == 0) continue;
                DocToLayer(layer, x + 0.5f, y + 0.5f, out float lx, out float ly);
                int ix = (int)MathF.Floor(lx), iy = (int)MathF.Floor(ly);
                if (ix < 0 || iy < 0 || ix >= src.Width || iy >= src.Height) continue;
                bmp.SetPixel(x - x0, y - y0, src.GetPixel(ix, iy));
            }
        return (bmp, new SKPoint(x0, y0));
    }

    // ---------- floating pixel move (Mac PixelMove subset, translate only) ----------

    /// <summary>Start moving selected pixels (Mac beginPixelMove subset).
    /// Cuts the outline to a floating raster unless duplicate (Alt-drag copy).</summary>
    public static FloatingSelection BeginPixelMove(Document doc, Layer layer, bool duplicate)
    {
        if (layer?.Bitmap == null || doc.Selection == null) return null;
        var copied = CopySelection(doc, layer);
        if (copied == null) return null;
        Document.EnsureUniqueBitmap(layer);
        if (!duplicate) DeleteSelection(doc, layer);
        return new FloatingSelection { Pixels = copied.Value.bmp, Origin = copied.Value.origin, Offset = new SKPoint(0, 0), Duplicate = duplicate };
    }

    public static void MoveFloating(FloatingSelection f, float dx, float dy)
    {
        if (f == null) return;
        f.Offset = new SKPoint(f.Offset.X + dx, f.Offset.Y + dy);
    }

    /// <summary>Commit: composite the floating raster at origin+offset (Mac mergeFloatingTransform subset).</summary>
    public static void CommitPixelMove(Document doc, Layer layer, FloatingSelection f)
    {
        if (f?.Pixels == null || layer?.Bitmap == null) return;
        Document.EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        float ox = f.Origin.X + f.Offset.X - layer.Position.X, oy = f.Origin.Y + f.Offset.Y - layer.Position.Y;
        float sx = Math.Max(1e-6f, layer.ScaleX), sy = Math.Max(1e-6f, layer.ScaleY);
        canvas.DrawBitmap(f.Pixels, new SKRect(ox / sx, oy / sy, (ox + f.Pixels.Width) / sx, (oy + f.Pixels.Height) / sy));
        // The selection frame travels with the pixels.
        if (doc.Selection is SKRect r)
            doc.Selection = new SKRect(r.Left + f.Offset.X, r.Top + f.Offset.Y, r.Right + f.Offset.X, r.Bottom + f.Offset.Y);
        if (doc.SelectionPolygon != null)
            for (int i = 0; i < doc.SelectionPolygon.Count; i++)
                doc.SelectionPolygon[i] = new SKPoint(doc.SelectionPolygon[i].X + f.Offset.X, doc.SelectionPolygon[i].Y + f.Offset.Y);
        doc.SelectionMask = null; doc.SelectionMaskW = doc.SelectionMaskH = 0;
    }

    /// <summary>Cancel: put cut pixels back (no-op for duplicates). Caller Undos the cut for exact restore.</summary>
    public static void CancelPixelMove(Document doc, Layer layer, FloatingSelection f)
    {
        if (f?.Pixels == null || layer?.Bitmap == null) return;
        if (f.Duplicate) return;
        Document.EnsureUniqueBitmap(layer);
        using var canvas = new SKCanvas(layer.Bitmap);
        float ox = f.Origin.X - layer.Position.X, oy = f.Origin.Y - layer.Position.Y;
        float sx = Math.Max(1e-6f, layer.ScaleX), sy = Math.Max(1e-6f, layer.ScaleY);
        canvas.DrawBitmap(f.Pixels, new SKRect(ox / sx, oy / sy, (ox + f.Pixels.Width) / sx, (oy + f.Pixels.Height) / sy));
    }
}
