using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Compositor;

public partial class MainWindow : Window
{
    Document doc = new() { Width = 800, Height = 600 };
    float zoom = 1f;
    Layer dragLayer;
    Point dragStart;
    SKPoint dragLayerPos;
    WriteableBitmap renderedBitmap;   // direct-pixel canvas surface (disposed on replace)

    UndoStack undoStack = new();
    readonly Dictionary<Document, UndoStack> docStacks = new();
    readonly List<Document> openDocs = new();
    bool updatingTabs;

    enum Tool { Move, Hand, Brush, Eraser, Clone, Heal, Smudge, Eyedropper, Marquee, Lasso, Polygon, Wand, Crop, Distort, Shape, Gradient }
    Tool currentTool = Tool.Move;
    List<SKPoint> strokePoints;       // active brush stroke (layer-pixel space)
    // --- paint settings (Mac BrushSettings subset) ---
    float brushHardness = 1f;         // 0..1 (柔らか円ダブ)
    float brushSpacing = 0.15f;       // 径比 (間隔配置)
    float brushOpacity = 1f;          // 0.01..1 (Smudge系ではstrength)
    // --- Clone Stamp state (Mac CloneStamp.swift: doc-px source/offset) ---
    SKPoint? cloneSource;             // Alt-click採取点 (doc px)
    SKSize? cloneOffset;              // aligned初回ストロークの whole-pixel オフセット
    bool cloneAligned = true;
    bool cloneSampleAll;
    SKBitmap cloneSampleBmp;          // stroke開始時スナップショット
    List<SKPoint> strokeDocPoints;    // active paint stroke (document-px, Clone/Heal/Smudge用)
    int drawnDabs;                    // Brush増分描画済みダブ数 (spacing連続性用)
    // --- Smudge/Blur/Liquify state (Mac WarpStroke subset) ---
    string smudgeMode = "Smudge";     // Smudge / Blur / Liquify
    SmudgeStroke activeSmudge;
    SKBitmap blurSampleBmp;
    SKPoint? smudgeLastDoc;
    int healMode;                     // 0 Content-Aware / 1 Create Texture / 2 Proximity
    SKColor pickedColor = SKColors.Black;
    bool hasPickedColor;
    SKPoint hoverDoc;                 // brush環表示用ホバー位置 (doc px)
    bool hasHover;
    SKPoint marqueeStart;             // marquee anchor (document px)
    bool marqueeActive;
    bool marqueeSquare, marqueeFromCenter;   // Shift / Option held at drag start
    SelectionKind marqueeShape = SelectionKind.Rectangle;   // Mで Rectangle/Ellipse 切替
    SelectionKind lassoKind = SelectionKind.Freehand;       // Lで Freehand/Polygon 切替
    SelectionMode selModeChoice = SelectionMode.Replace;    // 工具栏Mode (Shift=Add/Option=Subtractで上書き)
    // --- Shape / Gradient state (Mac ShapeTool.swift / Gradient.swift subset) ---
    ShapeKind shapeKind = ShapeKind.Rectangle;
    float shapeCornerRadius = 12f;      // RoundedRect用 (layer px)
    SKPoint shapeAnchor;                // drag開始点 (doc px)
    SKRect? shapeRect;                  // live preview (doc px)
    bool shapeSquare, shapeFromCenter;  // Shift / Alt
    GradientDraft gradientDraft;        // pending (Enter確定・Esc取消)
    Layer gradientLayer;                // draft対象層
    bool gradientDragging;
    SKColor gradientBg = SKColors.White;
    List<SKPoint> lassoDraft;         // freehand draft (document px, Enter/ダブルクリック確定)
    List<SKPoint> polygonDraft;       // polygonal vertices (document px)
    SKPoint polygonCursor;            // polygonal rubber-band end
    bool hasPolygonCursor;
    SelectionMode draftMode = SelectionMode.Replace;   // outline開始時のmode
    FloatingSelection floating;       // 画素移動中 (Enter確定/Esc取消)
    Layer floatingLayer;
    Point floatingLast;             // drag前回位置 (view px)
    bool floatingFrameDrag;           // true=枠のみ移動中
    SKPoint frameDragLast;            // 枠移動の前回doc位置
    SKPoint lastCopyOrigin = new(0, 0);   // 選択コピーの貼付位置

    static readonly List<(string Name, SKColor Color)> BrushColors = new()
    {
        ("Black", SKColors.Black), ("White", SKColors.White), ("Red", SKColors.Red),
        ("Green", SKColors.Green), ("Blue", SKColors.Blue), ("Yellow", SKColors.Yellow),
    };

    bool uiReady;
    bool updatingList;
    bool sliderArmed;   // one undo entry per slider drag gesture
    int refreshDepth;   // RefreshAll再入検出用

    static readonly (string Name, SKBlendMode Mode)[] BlendModes = new[]
    {
        ("Normal", SKBlendMode.SrcOver),
        ("Multiply", SKBlendMode.Multiply),
        ("Screen", SKBlendMode.Screen),
        ("Overlay", SKBlendMode.Overlay),
        ("Darken", SKBlendMode.Darken),
        ("Lighten", SKBlendMode.Lighten),
        ("Color Dodge", SKBlendMode.ColorDodge),
        ("Color Burn", SKBlendMode.ColorBurn),
        ("Hard Light", SKBlendMode.HardLight),
        ("Soft Light", SKBlendMode.SoftLight),
        ("Difference", SKBlendMode.Difference),
        ("Exclusion", SKBlendMode.Exclusion),
        ("Hue", SKBlendMode.Hue),
        ("Saturation", SKBlendMode.Saturation),
        ("Color", SKBlendMode.Color),
        ("Luminosity", SKBlendMode.Luminosity),
    };

    public MainWindow()
    {
        InitializeComponent();
        uiReady = true;

        foreach (var (name, _) in BlendModes) BlendBox.Items.Add(name);
        BlendBox.SelectedIndex = 0;

        foreach (var (name, _) in BrushColors) BrushColorBox.Items.Add(name);
        BrushColorBox.SelectedIndex = 0;

        openDocs.Add(doc);
        docStacks[doc] = undoStack;
        updatingTabs = true;
        try { DocTabs.Items.Add(new TabItem { Header = doc.Name }); DocTabs.SelectedIndex = 0; }
        finally { updatingTabs = false; }

        OpacitySlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        ScaleSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        RotateSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        BrightSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        ContrastSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        SaturSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        BlurSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        OpacitySlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        ScaleSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        RotateSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        BrightSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        ContrastSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        SaturSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        BlurSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        undoStack.Changed += UpdateUndoButtons;

        doc.Changed += RefreshAll;
        SetTool(currentTool);
        RefreshAll();
        UpdateUndoButtons();
    }

    Layer Selected =>
        (!uiReady || LayerList is null || LayerList.SelectedIndex < 0 || LayerList.SelectedIndex >= doc.Layers.Count)
            ? null : doc.Layers[doc.Layers.Count - 1 - LayerList.SelectedIndex];   // list shows top-first

    // ---------- rendering ----------

    void RefreshAll()
    {
        if (refreshDepth > 0) { Log($"RefreshAll reentry suppressed (depth={refreshDepth})"); return; }
        refreshDepth++;
        try { RefreshAllCore(); }
        catch (Exception ex) { Log("RefreshAll ERROR: " + ex); throw; }
        finally { refreshDepth--; }
    }

    void RefreshAllCore()
    {
        Log($"RefreshAllCore layers={doc.Layers.Count}");
        RefreshLayerList();
        if (Selected is { } sel)
        {
            // Programmatic sync must not push Undo entries: guard with updatingList
            // (OnOpacityChanged etc. return early while this is set).
            updatingList = true;
            try
            {
                OpacitySlider.Value = sel.Opacity * 100;
                OpacityLabel.Text = $"{sel.Opacity * 100:F0}%";
                ScaleSlider.Value = Math.Clamp(sel.ScaleX * 100, ScaleSlider.Minimum, ScaleSlider.Maximum);
                VisibleCheck.IsChecked = sel.Visible;
                LockCheck.IsChecked = sel.Locked;
                RenameBox.Text = sel.Name;
                RotateSlider.Value = sel.Rotation;
                RotateLabel.Text = $"{sel.Rotation:F0}°";
                BrightSlider.Value = sel.Brightness;
                BrightLabel.Text = $"{sel.Brightness:F0}";
                ContrastSlider.Value = sel.Contrast;
                ContrastLabel.Text = $"{sel.Contrast:F0}";
                SaturSlider.Value = sel.Saturation;
                SaturLabel.Text = $"{sel.Saturation:F0}";
                BlurSlider.Value = sel.Blur;
                BlurLabel.Text = $"{sel.Blur:F1}";
                int idx = Array.FindIndex(BlendModes, b => b.Mode == sel.Blend);
                BlendBox.SelectedIndex = idx < 0 ? 0 : idx;
                SyncTransformPanel(sel);
            }
            finally { updatingList = false; }
        }
        RenderCanvas();
        UpdateUndoButtons();
        RefreshDocTabs();
    }

    void UpdateUndoButtons()
    {
        UndoBtn.IsEnabled = undoStack.CanUndo;
        RedoBtn.IsEnabled = undoStack.CanRedo;
    }

    void RefreshLayerList()
    {
        updatingList = true;
        try
        {
            int sel = LayerList.SelectedIndex;
            LayerList.Items.Clear();
            var byId = doc.Layers.ToDictionary(l => l.Id);
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                int depth = 0;
                for (var p = l.ParentId; p.HasValue && depth <= 64; depth++)
                    if (!byId.TryGetValue(p.Value, out var n)) break; else p = n.ParentId;
                string indent = new string(' ', Math.Min(depth, 8) * 2).Replace(" ", "· ");
                string kind = l.IsGroup ? "[F] " : "";
                string mask = MaskOps.HasMask(l) ? (l.MaskSelected ? "[M*]" : "[M]") : "";
                string clip = l.ClipSourceId != null ? "[C]" : "";
                string shape = l.Shape != null ? "[S]" : "";
                LayerList.Items.Add($"{indent}{(l.Visible ? "" : "[hidden] ")}{kind}{l.Name} {mask}{clip}{shape}   {l.Opacity:P0}   pos({l.Position.X:F0},{l.Position.Y:F0})");
            }
            if (LayerList.ItemCount > 0)
                LayerList.SelectedIndex = Math.Clamp(sel, 0, LayerList.ItemCount - 1);
        }
        finally { updatingList = false; }
    }

    // 選択変更は必ずこのヘルパー経由。SelectionChanged→RefreshAllが同期再入しないよう
    // Dispatcherで後ろに回す（ドラッグ中・Import直後のクラッシュ防止）。
    void SafeSelect(int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (updatingList || LayerList is null) return;
            int i = Math.Clamp(index, 0, Math.Max(0, LayerList.ItemCount - 1));
            if (LayerList.SelectedIndex != i) LayerList.SelectedIndex = i;
        });
    }

    void RenderCanvas()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int w = (int)Math.Ceiling(doc.Width * zoom);
        int h = (int)Math.Ceiling(doc.Height * zoom);
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul),
                fb.Address, fb.RowBytes);
            var canvas = surface.Canvas;
            DrawCheckerboard(canvas, w, h);
            doc.Draw(canvas, zoom, new SKRect(0, 0, doc.Width, doc.Height));
            DrawSelectionOverlay(canvas);
            DrawDraftOverlay(canvas);
            DrawTransformOverlays(canvas);
            DrawFloatingPreview(canvas);
            DrawBrushOverlay(canvas);
            surface.Flush();
        }
        renderedBitmap?.Dispose();
        renderedBitmap = wb;
        CanvasImage.Source = wb;
        ZoomLabel.Text = $"{zoom:P0}";
        if (StatusZoom != null) StatusZoom.Text = $"{zoom:P0}";
        if (StatusDoc != null) StatusDoc.Text = $"{doc.Width} x {doc.Height} px";
        if (LayerCountLabel != null) LayerCountLabel.Text = $"{doc.Layers.Count}";
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100) Log($"RenderCanvas {w}x{h} took {sw.ElapsedMilliseconds}ms");
    }

    static SKBitmap checkerTile;   // 32x32 pattern (2x2 cells of 16px), built once, tiled via shader
    static SKBitmap CheckerTile()
    {
        if (checkerTile != null) return checkerTile;
        var tile = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(tile))
        {
            canvas.Clear(new SKColor(230, 230, 230));
            using var dark = new SKPaint { Color = new SKColor(200, 200, 204) };
            canvas.DrawRect(16, 0, 16, 16, dark);
            canvas.DrawRect(0, 16, 16, 16, dark);
        }
        checkerTile = tile;
        return tile;
    }

    static void DrawCheckerboard(SKCanvas canvas, int w, int h)
    {
        // 旧来の16pxセル毎DrawRectは4Kで約3.2万回発行しフレームを支配していた。
        // 32x32パターンをRepeatシェーダで一括充填する（1 draw call、メモリ+4KBのみ）。
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateBitmap(CheckerTile(), SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
        };
        canvas.DrawRect(0, 0, w, h, paint);
    }

    SKPath DocPath(IList<SKPoint> pts, bool close)
    {
        var path = new SKPath();
        if (pts == null || pts.Count == 0) return path;
        path.MoveTo(pts[0].X * zoom, pts[0].Y * zoom);
        for (int i = 1; i < pts.Count; i++) path.LineTo(pts[i].X * zoom, pts[i].Y * zoom);
        if (close) path.Close();
        return path;
    }

    void MarchingAnts(SKCanvas canvas, Action<SKPaint> draw)
    {
        using var black = new SKPaint
        {
            Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1,
            IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0),
        };
        draw(black);
        using var white = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1,
            IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 6),
        };
        draw(white);
    }

    /// <summary>確定選択の破線オーバーレイ (矩形/楕円/投繩・多角・Wand).</summary>
    void DrawSelectionOverlay(SKCanvas canvas)
    {
        if (doc.Selection is not SKRect selRect) return;
        var r = new SKRect(selRect.Left * zoom, selRect.Top * zoom, selRect.Right * zoom, selRect.Bottom * zoom);
        if (doc.SelKind == SelectionKind.Ellipse)
        {
            MarchingAnts(canvas, p => canvas.DrawOval(r, p));
            return;
        }
        if ((doc.SelKind is SelectionKind.Freehand or SelectionKind.Polygon or SelectionKind.Wand)
            && doc.SelectionPolygon != null && doc.SelectionPolygon.Count >= 3)
        {
            using var path = DocPath(doc.SelectionPolygon, close: true);
            MarchingAnts(canvas, p => canvas.DrawPath(path, p));
            return;
        }
        MarchingAnts(canvas, p => canvas.DrawRect(r, p));
    }

    /// <summary>未確定ドラフトのrubber-band (Mac lassoDraft相当).</summary>
    void DrawDraftOverlay(SKCanvas canvas)
    {
        if (marqueePreview is SKRect mp && marqueeActive)
        {
            var r = new SKRect(mp.Left * zoom, mp.Top * zoom, mp.Right * zoom, mp.Bottom * zoom);
            if (marqueeShape == SelectionKind.Ellipse) MarchingAnts(canvas, p => canvas.DrawOval(r, p));
            else MarchingAnts(canvas, p => canvas.DrawRect(r, p));
        }
        if (lassoDraft != null && lassoDraft.Count >= 2)
        {
            using var path = DocPath(lassoDraft, close: true);
            using var paint = new SKPaint
            {
                Color = new SKColor(0, 160, 255), Style = SKPaintStyle.Stroke,
                StrokeWidth = 1, IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
            };
            canvas.DrawPath(path, paint);
        }
        if (polygonDraft != null && polygonDraft.Count > 0)
        {
            var pts = new List<SKPoint>(polygonDraft);
            if (hasPolygonCursor) pts.Add(polygonCursor);
            using var path = DocPath(pts, close: false);
            using var paint = new SKPaint
            {
                Color = new SKColor(0, 160, 255), Style = SKPaintStyle.Stroke,
                StrokeWidth = 1, IsAntialias = true,
            };
            canvas.DrawPath(path, paint);
            using var dot = new SKPaint { Color = new SKColor(0, 160, 255), Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var v in polygonDraft) canvas.DrawCircle(v.X * zoom, v.Y * zoom, 3, dot);
        }
        // Shape drag preview (Mac ShapeDraft相当): 楕円はoval、それ以外はrect
        if (shapeRect is SKRect sr2)
        {
            var r = new SKRect(sr2.Left * zoom, sr2.Top * zoom, sr2.Right * zoom, sr2.Bottom * zoom);
            if (shapeKind == ShapeKind.Ellipse) MarchingAnts(canvas, p => canvas.DrawOval(r, p));
            else MarchingAnts(canvas, p => canvas.DrawRect(r, p));
        }
        // Gradient pending line (Mac GradientEdit相当): 線＋端点○
        if (gradientDraft != null && gradientLayer != null && gradientDraft.HasLine)
        {
            var a = LayerToDoc(gradientLayer, gradientDraft.Start);
            var b = LayerToDoc(gradientLayer, gradientDraft.End);
            using var paint = new SKPaint { Color = new SKColor(0, 160, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine(a.X * zoom, a.Y * zoom, b.X * zoom, b.Y * zoom, paint);
            using var dot = new SKPaint { Color = new SKColor(0, 160, 255), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawCircle(a.X * zoom, a.Y * zoom, 4, dot);
            canvas.DrawCircle(b.X * zoom, b.Y * zoom, 4, dot);
        }
    }

    SKPoint LayerToDoc(Layer l, SKPoint lp) =>
        new(l.Position.X + lp.X * l.ScaleX, l.Position.Y + lp.Y * l.ScaleY);

    /// <summary>ブラシ環表示 (Mac BrushCursorOverlay相当): 外環=径・内破線=硬さ・×=Clone採取点.</summary>
    void DrawBrushOverlay(SKCanvas canvas)
    {
        if (!hasHover) return;
        if (currentTool is not (Tool.Brush or Tool.Eraser or Tool.Clone or Tool.Heal or Tool.Smudge)) return;
        if (strokePoints != null) return;   // ストローク中は環を出さない
        float d = BrushDiameter() * zoom;
        if (d < 2) return;
        float cx = hoverDoc.X * zoom, cy = hoverDoc.Y * zoom;
        using var outerW = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        using var outerB = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawCircle(cx, cy, d / 2, outerW);
        canvas.DrawCircle(cx, cy, d / 2, outerB);
        if (brushHardness > 0.01f && brushHardness < 0.99f)
        {
            float inner = d / 2 * brushHardness;
            using var dashW = new SKPaint
            {
                Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
            };
            using var dashB = new SKPaint
            {
                Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 3 }, 0),
            };
            canvas.DrawCircle(cx, cy, inner, dashW);
            canvas.DrawCircle(cx, cy, inner, dashB);
        }
        if (currentTool == Tool.Clone)
        {
            var sample = cloneSource != null
                ? CloneTools.SamplePoint(hoverDoc, cloneSource, cloneAligned ? cloneOffset : null, cloneAligned, strokeActive: false)
                : null;
            if (sample != null)
            {
                float sx = sample.Value.X * zoom, sy = sample.Value.Y * zoom;
                const float reach = 7f;
                using var cross = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
                using var crossB = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
                foreach (var p in new[] { cross, crossB })
                {
                    canvas.DrawLine(sx - reach, sy, sx + reach, sy, p);
                    canvas.DrawLine(sx, sy - reach, sx, sy + reach, p);
                }
            }
        }
    }

    /// <summary>画素移動中のゴースト表示 (切出し画素を移動先に半透明で).</summary>
    void DrawFloatingPreview(SKCanvas canvas)
    {
        if (floating?.Pixels == null) return;
        float x = (floating.Origin.X + floating.Offset.X) * zoom, y = (floating.Origin.Y + floating.Offset.Y) * zoom;
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
        canvas.DrawBitmap(floating.Pixels, new SKRect(x, y, x + floating.Pixels.Width * zoom, y + floating.Pixels.Height * zoom), paint);
        MarchingAnts(canvas, p => canvas.DrawRect(new SKRect(x, y, x + floating.Pixels.Width * zoom, y + floating.Pixels.Height * zoom), p));
    }

    // ---------- tools / zoom ----------

    void SetTool(Tool t)
    {
        if (floating != null) CommitFloating();   // 画素移動中は工具切替で確定
        if (gradientDraft != null && t != Tool.Gradient) CommitGradient();   // 保留グラデは工具切替で適用 (Mac resolveGradient相当)
        if (t != Tool.Shape) shapeRect = null;
        if (t != Tool.Lasso) lassoDraft = null;
        if (t != Tool.Polygon) { polygonDraft = null; hasPolygonCursor = false; }
        if (t != Tool.Distort) CancelDistortSilently();
        if (t != Tool.Crop) cropDrag = null;
        currentTool = t;
        marqueeActive = false;
        strokePoints = null;
        strokeDocPoints = null;
        drawnDabs = 0;
        activeSmudge = null;
        smudgeLastDoc = null;
        var (title, hint) = t switch
        {
            Tool.Move => ("Transform", "選択内Drag=枠移動 · Ctrl+Drag=画素移動 · Alt+Drag=複写"),
            Tool.Hand => ("Pan", "Drag to pan · +/- zoom"),
            Tool.Brush => ("Brush", $"Drag to paint · [ ] size · 1-0 opacity · Alt-click=採取 · Ø{BrushDiameter():F0} H{brushHardness:P0}"),
            Tool.Eraser => ("Eraser", $"Drag to erase · [ ] size · Ø{BrushDiameter():F0} H{brushHardness:P0}"),
            Tool.Clone => ("Clone Stamp", cloneSource == null
                ? "Alt-clickで採取点を設定 (採取後にDragで複写 · Aligned/All-Layers設定)"
                : $"Dragで複写 · Alt-clickで採取点再設定 · Ø{BrushDiameter():F0}"),
            Tool.Heal => ("Spot Healing", "Click/Dragで修復 (採取不要 · モード選択可)"),
            Tool.Smudge => (smudgeMode, $"Dragで{smudgeMode} · Strength=Opacity · [ ] size · Ø{BrushDiameter():F0}"),
            Tool.Eyedropper => ("Eyedropper", "Clickで色採取 (All Layers=合成から)"),
            Tool.Marquee => (marqueeShape == SelectionKind.Ellipse ? "Marquee (Ellipse)" : "Marquee",
                "Dragで選択 · Shift=正方形 · Alt=中心 · M=楕円切替 · Ctrl+D解除"),
            Tool.Lasso => ("Lasso (Freehand)", "Dragで投繩 · Enter/ダブルクリック確定 · Esc取消 · L=多角切替"),
            Tool.Polygon => ("Lasso (Polygonal)", "Clickで頂点 · Enter/ダブルクリック確定 · Esc取消 · L=自由切替"),
            Tool.Wand => ("Magic Wand", "Clickで類似色選択 · Tolerance/Contiguous設定 · Shift=加算 Alt=減算"),
            Tool.Crop => ("Crop", cropRect == null
                ? "Dragで枠作成 · Alt=対称 · Space=移動 · Enter確定 · Esc取消"
                : $"枠 {cropRect.Value.Width:F0}×{cropRect.Value.Height:F0}px · Dragで移動/変形 · Enter確定 · Esc取消"),
            Tool.Distort => ("Distort", distortCorners == null
                ? "T: 層を選択して歪み開始"
                : "角を掴んで歪み · Shift=軸固定 · Enter確定 · Esc取消"),
            Tool.Shape => ("Shape (U)", "Dragで形状を描画 · Shift=正方形 · Alt=中心 · 新規層に作成"),
            Tool.Gradient => ("Gradient (G)", gradientDraft != null
                ? "Enter=確定 · Esc=取消 · Shift=45° · 端点は再ドラッグで調整"
                : "Dragで引張描画 · Shift=45° · Enter確定 · Esc取消"),
            _ => (t.ToString(), ""),
        };
        if (ToolHeaderTitle != null) ToolHeaderTitle.Text = title;
        if (ToolHeaderHint != null) ToolHeaderHint.Text = hint;
        if (StatusHint != null) StatusHint.Text = hint;
        HighlightRail();
        Log($"Tool={t}");
    }

    void HighlightRail()
    {
        // 意匠 t_fec99135: 題材統一 (#094771 選択 / Transparent 非選択)。x:Name・操作子は不変。
        var on = (Avalonia.Media.IBrush)new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(9, 71, 113));
        var off = (Avalonia.Media.IBrush)Avalonia.Media.Brushes.Transparent;
        if (MoveBtn != null) MoveBtn.Background = currentTool == Tool.Move ? on : off;
        if (HandBtn != null) HandBtn.Background = currentTool == Tool.Hand ? on : off;
        if (BrushBtn != null) BrushBtn.Background = currentTool == Tool.Brush ? on : off;
        if (EraserBtn != null) EraserBtn.Background = currentTool == Tool.Eraser ? on : off;
        if (MarqueeBtn != null) MarqueeBtn.Background = currentTool == Tool.Marquee ? on : off;
        if (LassoBtn != null) LassoBtn.Background = (currentTool == Tool.Lasso || currentTool == Tool.Polygon) ? on : off;
        if (WandBtn != null) WandBtn.Background = currentTool == Tool.Wand ? on : off;
        if (CloneBtn != null) CloneBtn.Background = currentTool == Tool.Clone ? on : off;
        if (HealBtn != null) HealBtn.Background = currentTool == Tool.Heal ? on : off;
        if (SmudgeBtn != null) SmudgeBtn.Background = currentTool == Tool.Smudge ? on : off;
        if (EyedropperBtn != null) EyedropperBtn.Background = currentTool == Tool.Eyedropper ? on : off;
        if (CropBtn != null) CropBtn.Background = currentTool == Tool.Crop ? on : off;
        if (DistortBtn != null) DistortBtn.Background = currentTool == Tool.Distort ? on : off;
        if (ShapeBtn != null) ShapeBtn.Background = currentTool == Tool.Shape ? on : off;
        if (GradientBtn != null) GradientBtn.Background = currentTool == Tool.Gradient ? on : off;
    }

    float BrushDiameter() => (float)(BrushSizeSlider?.Value ?? 24);

    BrushSettings CurrentBrushSettings() => new(BrushDiameter(), brushHardness, brushSpacing, brushOpacity);

    void RefreshToolHeader() => SetTool(currentTool);

    bool IsPaintTool() => currentTool is Tool.Brush or Tool.Eraser or Tool.Clone or Tool.Heal or Tool.Smudge;

    void SetPaintOpacity(float v)
    {
        if (strokePoints != null) return;   // ストローク中は変更しない (Mac同様)
        brushOpacity = Math.Clamp(v, 0.01f, 1f);
        SyncBrushPanel();
        RefreshToolHeader();
        Log($"Brush opacity={brushOpacity:P0}");
    }

    /// <summary>BrushパネルUIをフィールド値へ同期 (キー操作からの反映用).</summary>
    void SyncBrushPanel()
    {
        if (HardnessSlider != null) HardnessSlider.Value = brushHardness * 100;
        if (HardnessLabel != null) HardnessLabel.Text = $"{brushHardness:P0}";
        if (SpacingSlider != null) SpacingSlider.Value = brushSpacing * 100;
        if (SpacingLabel != null) SpacingLabel.Text = $"{brushSpacing:P0}";
        if (BrushOpacitySlider != null) BrushOpacitySlider.Value = brushOpacity * 100;
        if (BrushOpacityLabel != null) BrushOpacityLabel.Text = $"{brushOpacity:P0}";
    }

    void OnHardnessChanged(object s, RoutedEventArgs e)
    {
        if (!uiReady || HardnessSlider == null) return;
        brushHardness = (float)(HardnessSlider.Value / 100);
        if (HardnessLabel != null) HardnessLabel.Text = $"{brushHardness:P0}";
        RefreshToolHeader();
    }

    void OnSpacingChanged(object s, RoutedEventArgs e)
    {
        if (!uiReady || SpacingSlider == null) return;
        brushSpacing = (float)(SpacingSlider.Value / 100);
        if (SpacingLabel != null) SpacingLabel.Text = $"{brushSpacing:P0}";
    }

    void OnBrushOpacityChanged(object s, RoutedEventArgs e)
    {
        if (!uiReady || BrushOpacitySlider == null) return;
        brushOpacity = Math.Clamp((float)(BrushOpacitySlider.Value / 100), 0.01f, 1f);
        if (BrushOpacityLabel != null) BrushOpacityLabel.Text = $"{brushOpacity:P0}";
        RefreshToolHeader();
    }

    void OnCloneAlignedChanged(object s, RoutedEventArgs e)
    {
        cloneAligned = CloneAlignedCheck?.IsChecked == true;
    }

    void OnCloneSampleChanged(object s, RoutedEventArgs e)
    {
        cloneSampleAll = (CloneSampleBox?.SelectedIndex ?? 0) == 1;
    }

    void OnSmudgeModeChanged(object s, RoutedEventArgs e)
    {
        int i = SmudgeModeBox?.SelectedIndex ?? 0;
        smudgeMode = i == 1 ? "Blur" : i == 2 ? "Liquify" : "Smudge";
        RefreshToolHeader();
    }

    void OnHealModeChanged(object s, RoutedEventArgs e)
    {
        healMode = HealModeBox?.SelectedIndex ?? 0;
    }

    void OnToolMove(object s, RoutedEventArgs e) => SetTool(Tool.Move);
    void OnToolHand(object s, RoutedEventArgs e) => SetTool(Tool.Hand);
    void OnToolBrush(object s, RoutedEventArgs e) => SetTool(Tool.Brush);
    void OnToolEraser(object s, RoutedEventArgs e) => SetTool(Tool.Eraser);
    void OnToolMarquee(object s, RoutedEventArgs e) => SetTool(Tool.Marquee);
    void OnToolLasso(object s, RoutedEventArgs e) => SetTool(lassoKind == SelectionKind.Polygon ? Tool.Polygon : Tool.Lasso);
    void OnToolWand(object s, RoutedEventArgs e) => SetTool(Tool.Wand);
    void OnToolClone(object s, RoutedEventArgs e) => SetTool(Tool.Clone);
    void OnToolHeal(object s, RoutedEventArgs e) => SetTool(Tool.Heal);
    void OnToolSmudge(object s, RoutedEventArgs e) => SetTool(Tool.Smudge);
    void OnToolEyedropper(object s, RoutedEventArgs e) => SetTool(Tool.Eyedropper);
    void OnToolCrop(object s, RoutedEventArgs e) => SetTool(Tool.Crop);
    void OnToolDistort(object s, RoutedEventArgs e) => BeginDistortTool();
    void OnToolShape(object s, RoutedEventArgs e) => SetTool(Tool.Shape);
    void OnToolGradient(object s, RoutedEventArgs e) => SetTool(Tool.Gradient);
    void OnShapeKindCycle(object s, RoutedEventArgs e)
    {
        shapeKind = shapeKind == ShapeKind.Rectangle ? ShapeKind.RoundedRect
            : shapeKind == ShapeKind.RoundedRect ? ShapeKind.Ellipse : ShapeKind.Rectangle;
        if (ShapeKindBtn != null) ShapeKindBtn.Content = shapeKind.ToString();
        Log($"ShapeKind={shapeKind}");
        RefreshToolHeader();
    }

    // ---------- layer system: mask / group / clip / merge / fill (Mac 層系 subset) ----------

    bool strokeOnMask;   // brush stroke targets the mask (Mac isMaskSelected paint)

    void OnMaskAddWhite(object s, RoutedEventArgs e) => AddMaskForSelected(true);
    void OnMaskAddBlack(object s, RoutedEventArgs e) => AddMaskForSelected(false);

    void AddMaskForSelected(bool revealing)
    {
        if (Selected is not { } l || l.Bitmap == null || l.IsGroup || MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        if (doc.Selection != null)
        {
            MaskOps.AddMaskFromSelection(doc, l, revealing);
            doc.ClearSelection();   // 選択は使い切り (Mac addMask相当)
        }
        else MaskOps.AddMask(l, revealing);
        Log($"AddMask revealing={revealing} on '{l.Name}'");
        RefreshAll();
    }

    void OnMaskDelete(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        MaskOps.DeleteMask(l);
        Log($"DeleteMask '{l.Name}'");
        RefreshAll();
    }

    void OnMaskToggleEnable(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        l.MaskEnabled = !l.MaskEnabled;
        Log($"MaskEnabled={l.MaskEnabled} '{l.Name}'");
        RefreshAll();
    }

    void OnMaskSelectTarget(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        l.MaskSelected = !l.MaskSelected;   // 描画対象の切替のみ (Undo対象外・Mac同様選択扱い)
        Log($"MaskSelected={l.MaskSelected} '{l.Name}'");
        RefreshAll();
    }

    void OnMaskInvert(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        MaskOps.Invert(l);
        Log($"MaskInvert '{l.Name}'");
        RefreshAll();
    }

    void OnMaskFeather(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        MaskOps.Feather(l, 2f);
        Log($"MaskFeather r=2 '{l.Name}'");
        RefreshAll();
    }

    void OnMaskLinkToggle(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !MaskOps.HasMask(l)) return;
        undoStack.Push(doc);
        l.MaskLinked = !l.MaskLinked;
        Log($"MaskLinked={l.MaskLinked} '{l.Name}'");
        RefreshAll();
    }

    void OnGroupAdd(object s, RoutedEventArgs e)
    {
        undoStack.Push(doc);
        var g = LayerHierarchy.AddGroup(doc, Selected);
        if (g == null) return;
        Log($"AddGroup '{g.Name}'");
        RefreshAll();
        SafeSelect(doc.Layers.Count - 1 - doc.Layers.IndexOf(g));
    }

    void OnGroupSelected(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || l.IsGroup) return;
        undoStack.Push(doc);
        var g = LayerHierarchy.GroupLayers(doc, new[] { l });
        Log(g != null ? $"Group '{g.Name}'" : "Group failed");
        RefreshAll();
    }

    void OnUngroup(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !l.IsGroup) return;
        undoStack.Push(doc);
        bool ok = LayerHierarchy.Ungroup(doc, l.Id);
        Log($"Ungroup '{l.Name}' ok={ok}");
        RefreshAll();
    }

    void OnToggleClip(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        bool ok = ClipOps.Toggle(doc, l.Id);
        Log($"ToggleClip '{l.Name}' ok={ok}");
        RefreshAll();
    }

    void OnMergeGroup(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !l.IsGroup) return;
        var plan = MergeOps.MergeGroupPlan(doc, l.Id);
        if (plan == null) { Log("MergeGroup: nothing to merge"); return; }
        undoStack.Push(doc);
        var merged = MergeOps.Execute(doc, plan);
        Log(merged != null ? $"MergeGroup -> '{merged.Name}'" : "MergeGroup failed");
        RefreshAll();
    }

    void OnContentFill(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || l.Bitmap == null || doc.Selection == null) return;
        undoStack.Push(doc);
        try
        {
            int n = ContentFillOps.FillSelection(doc, l);
            Log($"ContentFill '{l.Name}' filled={n} (近似・周囲平均)");
        }
        catch (ContentFillOps.Failure ex)
        {
            Log("ContentFill failed: " + ex.Message);
        }
        RefreshAll();
    }

    // --- gradient draft (Mac GradientEdit subset: 引張描画・端点調整・Shift45°・Enter確定) ---

    SKPoint ToLayerPixelClamped(Layer l, SKPoint docPoint)
    {
        var lp = ToLayerPixel(l, docPoint);
        return new SKPoint(Math.Clamp(lp.X, 0, Math.Max(0, l.Bitmap.Width - 1)),
            Math.Clamp(lp.Y, 0, Math.Max(0, l.Bitmap.Height - 1)));
    }

    void BeginGradient(SKPoint docPoint)
    {
        if (Selected is not { } l || l.Bitmap == null || l.IsGroup || l.Locked) return;
        // 同一対象への再ドラッグは端点を置き換え (Mac beginGradient相当)
        if (gradientDraft != null && gradientLayer == l)
        {
            gradientDraft.Start = ToLayerPixelClamped(l, docPoint);
            gradientDraft.End = gradientDraft.Start;
            RenderCanvas();
            return;
        }
        CommitGradient();   // 別層の保留は先に適用
        undoStack.Push(doc);   // 確定は同一Undoに (draft自体は非破壊)
        gradientDraft = new GradientDraft { Start = ToLayerPixelClamped(l, docPoint) };
        gradientDraft.End = gradientDraft.Start;
        gradientDraft.Shape = GradientShape.Linear;
        gradientDraft.Style = GradientStyle.ForegroundToTransparent;
        gradientLayer = l;
    }

    void MoveGradient(SKPoint docPoint, bool snap45)
    {
        if (gradientDraft == null || gradientLayer == null) return;
        var end = ToLayerPixelClamped(gradientLayer, docPoint);
        gradientDraft.End = snap45 ? GradientOps.Snap45(gradientDraft.Start, end) : end;
        RenderCanvas();
        RefreshToolHeader();
    }

    void CommitGradient()
    {
        if (gradientDraft == null || gradientLayer == null) { gradientDraft = null; gradientLayer = null; return; }
        if (gradientDraft.HasLine && doc.Layers.Contains(gradientLayer))
        {
            GradientOps.Commit(gradientLayer, gradientDraft, BrushPaintColor(), gradientBg);
            Log($"Gradient commit on '{gradientLayer.Name}'");
        }
        gradientDraft = null; gradientLayer = null;
        RefreshAll();
    }

    void CancelGradient()
    {
        gradientDraft = null; gradientLayer = null;
        // Press時にpushしたUndoエントリを取り消し (draftは非破壊のため)
        if (undoStack.CanUndo) undoStack.Undo(doc);
        Log("Gradient cancel");
        RefreshAll();
    }

    /// <summary>Shift=加算・Option/Alt=減算、なければ工具栏Mode (Mac selectionMode相当).</summary>
    SelectionMode EffectiveSelMode(KeyModifiers mods)
    {
        if (mods.HasFlag(KeyModifiers.Alt)) return SelectionMode.Subtract;
        if (mods.HasFlag(KeyModifiers.Shift)) return SelectionMode.Add;
        return selModeChoice;
    }
    void OnZoomIn(object s, RoutedEventArgs e) { zoom = Math.Min(4f, zoom * 1.5f); RenderCanvas(); }
    void OnZoomOut(object s, RoutedEventArgs e) { zoom = Math.Max(0.1f, zoom / 1.5f); RenderCanvas(); }
    void OnZoom100(object s, RoutedEventArgs e) { zoom = 1f; RenderCanvas(); }

    void OnFit(object s, RoutedEventArgs e)
    {
        if (doc.Width <= 0 || doc.Height <= 0) return;
        double vw = 900, vh = 600;
        if (CanvasScroll != null && CanvasScroll.Viewport.Width > 0 && CanvasScroll.Viewport.Height > 0)
        { vw = CanvasScroll.Viewport.Width; vh = CanvasScroll.Viewport.Height; }
        zoom = Math.Clamp((float)Math.Min(vw / doc.Width, vh / doc.Height), 0.1f, 4f);
        RenderCanvas();
        Log($"Fit zoom={zoom}");
    }

    // ---------- layer ops ----------

    void OnNew(object s, RoutedEventArgs e)
    {
        undoStack.Push(doc);
        var fresh = new Document { Width = 800, Height = 600 };
        int idx = openDocs.IndexOf(doc);
        if (idx >= 0)
        {
            docStacks.Remove(doc);
            openDocs[idx] = fresh;
            docStacks[fresh] = undoStack;
            updatingTabs = true;
            try { ((TabItem)DocTabs.Items[idx]).Header = fresh.Name; } finally { updatingTabs = false; }
        }
        doc = fresh;
        doc.Changed += RefreshAll;
        RefreshAll();
    }

    void OnImport(object s, RoutedEventArgs e)
    {
        _ = ImportAsync();
    }

    async System.Threading.Tasks.Task ImportAsync()
    {
        var dlg = new OpenFileDialog { Filters = { new FileDialogFilter { Name = "Images", Extensions = { "png", "jpg", "jpeg", "bmp", "webp", "heic", "heif", "tiff", "tif" } } } };
        var files = await dlg.ShowAsync(this);
        if (files is not { Length: > 0 }) return;
        var res = ImageImport.DecodeFile(files[0], ImageImport.MaxPixels - ImageImport.DocumentPixels(doc));
        if (res.Bitmap == null)
        {
            await new MessageWindow((res.Unsupported ? "未対応形式: " : "読み込み失敗: ") + (res.Error ?? files[0])).ShowDialog(this);
            res.Bitmap?.Dispose();
            return;
        }
        ImportDecoded(res.Bitmap, res.Name);
    }

    void AddLayerBitmap(SKBitmap src, string name, SKPoint? position = null)
    {
        undoStack.Push(doc);
        doc.Layers.Add(new Layer { Name = name, Bitmap = src, Position = position ?? new SKPoint(0, 0) });
        RefreshAll();
        SafeSelect(LayerList.ItemCount - 1);
    }

    public static void Log(string msg)
    {
        try { File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "compositor.log"),
            $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
    }

    void OnAddLayer(object s, RoutedEventArgs e)
    {
        Log("OnAddLayer called");
        AddLayerBitmap(EmptyLayer(), $"Layer {doc.Layers.Count + 1}");
    }

    SKBitmap EmptyLayer()
    {
        var b = new SKBitmap(doc.Width, doc.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(SKColor.Empty);
        return b;
    }

    void OnDeleteLayer(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        var removed = LayerHierarchy.Descendants(doc, l.Id);
        removed.Add(l.Id);
        doc.Layers.RemoveAll(x => removed.Contains(x.Id));
        foreach (var x in doc.Layers.Where(x => x.ClipSourceId is Guid src && removed.Contains(src)))
            x.ClipSourceId = null;   // 供給層の削除はリンク解除 (Mac bakeダイアログの簡略版)
        Log($"DeleteLayer '{l.Name}' +{removed.Count - 1} descendants");
        RefreshAll();
    }

    void OnLockChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || updatingList) return;
        bool locked = LockCheck.IsChecked == true;
        if (l.Locked == locked) return;
        undoStack.Push(doc);
        l.Locked = locked;
        RefreshLayerList();
    }

    void OnRename(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        var name = (RenameBox.Text ?? "").Trim();
        if (name.Length == 0 || name == l.Name) return;
        undoStack.Push(doc);
        l.Name = name;
        RefreshLayerList();
    }

    void OnDuplicateLayer(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || l.Bitmap == null) return;
        undoStack.Push(doc);
        var copy = new SKBitmap(l.Bitmap.Width, l.Bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(copy))
            canvas.DrawBitmap(l.Bitmap, 0, 0);
        int i = doc.Layers.IndexOf(l);
        doc.Layers.Insert(i + 1, new Layer
        {
            Name = l.Name + " copy", Bitmap = copy, Visible = l.Visible, Opacity = l.Opacity,
            Position = new SKPoint(l.Position.X + 10, l.Position.Y + 10),
            ScaleX = l.ScaleX, ScaleY = l.ScaleY, Sampling = l.Sampling,
            Blend = l.Blend, Rotation = l.Rotation, FlipH = l.FlipH, FlipV = l.FlipV,
            Brightness = l.Brightness, Contrast = l.Contrast, Saturation = l.Saturation,
            Blur = l.Blur, Invert = l.Invert, ParentId = l.ParentId,
            Mask = l.Mask != null ? (byte[])l.Mask.Clone() : null, MaskW = l.MaskW, MaskH = l.MaskH,
            MaskEnabled = l.MaskEnabled, MaskLinked = l.MaskLinked, MaskOffset = l.MaskOffset,
            Shape = l.Shape?.Clone(),
            // Clip link is NOT copied: a duplicate starts unclipped (Mac release-on-duplicate相当).
        });
        Log($"Duplicate '{l.Name}'");
        RefreshAll();
    }

    void OnMergeDown(object s, RoutedEventArgs e)
    {
        if (Selected is not { } top) return;
        var plan = MergeOps.MergeDownPlan(doc, top.Id);
        if (plan == null) { Log("MergeDown: nothing below to merge with"); return; }
        undoStack.Push(doc);
        var merged = MergeOps.Execute(doc, plan);
        Log(merged != null ? $"MergeDown ({plan.Action}) -> '{merged.Name}'" : "MergeDown failed");
        RefreshAll();
    }

    // ---------- document tabs ----------

    void OnNewDocTab(object s, RoutedEventArgs e)
    {
        var d = new Document { Width = 800, Height = 600, Name = $"Untitled {openDocs.Count + 1}" };
        d.Changed += RefreshAll;
        openDocs.Add(d);
        var st = new UndoStack();
        st.Changed += UpdateUndoButtons;
        docStacks[d] = st;
        updatingTabs = true;
        try
        {
            DocTabs.Items.Add(new TabItem { Header = d.Name });
            DocTabs.SelectedIndex = DocTabs.Items.Count - 1;
        }
        finally { updatingTabs = false; }
        doc = d;
        undoStack = st;
        Log($"NewDocTab '{d.Name}'");
        RefreshAll();
    }

    void OnDocTabChanged(object s, SelectionChangedEventArgs e)
    {
        if (updatingTabs || DocTabs.SelectedIndex < 0 || DocTabs.SelectedIndex >= openDocs.Count) return;
        doc = openDocs[DocTabs.SelectedIndex];
        undoStack = docStacks[doc];
        Log($"DocTab -> '{doc.Name}'");
        RefreshAll();
    }

    void OnUp(object s, RoutedEventArgs e)   // move toward top of stack
    {
        int li = LayerList.SelectedIndex;
        int i = doc.Layers.Count - 1 - li;
        int k = i + 1;                          // higher = closer to top
        if (li < 0 || k >= doc.Layers.Count) return;
        undoStack.Push(doc);
        (doc.Layers[i], doc.Layers[k]) = (doc.Layers[k], doc.Layers[i]);
        ClipOps.Adopt(doc, doc.Layers[k]);
        ClipOps.ReleaseDetached(doc);
        RefreshAll();
        SafeSelect(doc.Layers.Count - 1 - k);
    }

    void OnDown(object s, RoutedEventArgs e)
    {
        int li = LayerList.SelectedIndex;
        int i = doc.Layers.Count - 1 - li;
        int k = i - 1;
        if (li < 0 || k < 0) return;
        undoStack.Push(doc);
        (doc.Layers[i], doc.Layers[k]) = (doc.Layers[k], doc.Layers[i]);
        ClipOps.Adopt(doc, doc.Layers[k]);
        ClipOps.ReleaseDetached(doc);
        RefreshAll();
        SafeSelect(doc.Layers.Count - 1 - k);
    }

    void OnLayerSelected(object s, SelectionChangedEventArgs e)
    {
        if (updatingList) return;
        // SelectionChanged処理中にRefreshLayerList(Items.Clear)やSlider更新を同期的に
        // 実行するとAvaloniaの選択機構が再入してクラッシュするため、後ろへ逃がす。
        Dispatcher.UIThread.Post(RefreshAll);
    }

    void OnOpacityChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Opacity = (float)(OpacitySlider.Value / 100);
        OpacityLabel.Text = $"{OpacitySlider.Value:F0}%";
        RefreshLayerList();
        RenderCanvas();
    }

    void OnScaleChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Scale = (float)(ScaleSlider.Value / 100);
        RefreshLayerList();
        RenderCanvas();
    }

    void OnVisibleChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || updatingList) return;
        bool vis = VisibleCheck.IsChecked == true;
        if (l.Visible == vis) return;   // RefreshAllによる反映時はundoに載せない
        undoStack.Push(doc);
        l.Visible = vis;
        RefreshLayerList();
        RenderCanvas();
    }

    void OnBlendChanged(object s, SelectionChangedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        undoStack.Push(doc);
        l.Blend = BlendModes[Math.Max(0, BlendBox.SelectedIndex)].Mode;
        RefreshLayerList();
        RenderCanvas();
    }

    // ---------- transform (Mac LayerTransform subset) ----------

    void OnRotateChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Rotation = (float)RotateSlider.Value;
        RotateLabel.Text = $"{l.Rotation:F0}°";
        RenderCanvas();
    }

    void OnRotate90(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        l.Rotation = (l.Rotation + 90) % 360;
        Log($"Rotate90 -> {l.Rotation}°");
        RefreshAll();
    }

    void OnFlipH(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        l.FlipH = !l.FlipH;
        Log($"FlipH={l.FlipH}");
        RefreshAll();
    }

    void OnFlipV(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        l.FlipV = !l.FlipV;
        Log($"FlipV={l.FlipV}");
        RefreshAll();
    }

    // ---------- adjustments (Mac ImageAdjustments subset, non-destructive) ----------

    void OnBrightChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Brightness = (float)BrightSlider.Value;
        BrightLabel.Text = $"{l.Brightness:F0}";
        RenderCanvas();
    }

    void OnContrastChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Contrast = (float)ContrastSlider.Value;
        ContrastLabel.Text = $"{l.Contrast:F0}";
        RenderCanvas();
    }

    void OnSaturChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Saturation = (float)SaturSlider.Value;
        SaturLabel.Text = $"{l.Saturation:F0}";
        RenderCanvas();
    }

    void OnBlurChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Blur = (float)BlurSlider.Value;
        BlurLabel.Text = $"{l.Blur:F1}";
        RenderCanvas();
    }

    void OnInvert(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        undoStack.Push(doc);
        l.Invert = !l.Invert;   // non-destructive flag (destructive path: Document.ApplyInvert)
        Log($"Invert={l.Invert}");
        RefreshAll();
    }

    // ---------- undo / redo ----------

    void OnUndo(object s, RoutedEventArgs e) => DoUndo();
    void OnRedo(object s, RoutedEventArgs e) => DoRedo();

    void DoUndo()
    {
        if (undoStack.Undo(doc) is null) return;
        Log("Undo");
        RefreshAll();
    }

    void DoRedo()
    {
        undoStack.Redo(doc);
        Log("Redo");
        RefreshAll();
    }

    // ---------- selection actions (Mac Selection/ Lasso / MagicWand / FloatingSelection subset) ----------

    int WandTolerance => (int)Math.Clamp(WandToleranceSlider?.Value ?? 32, 0, 255);
    bool WandContiguous => WandContiguousCheck?.IsChecked ?? true;
    bool WandSampleAll => WandSampleAllCheck?.IsChecked ?? false;

    void OnToggleMarqueeShape(object s, RoutedEventArgs e)
    {
        marqueeShape = marqueeShape == SelectionKind.Ellipse ? SelectionKind.Rectangle : SelectionKind.Ellipse;
        if (MarqueeShapeBtn != null) MarqueeShapeBtn.Content = marqueeShape == SelectionKind.Ellipse ? "Ellipse" : "Rect";
        SetTool(Tool.Marquee);
    }

    void OnToggleLassoKind(object s, RoutedEventArgs e)
    {
        lassoKind = lassoKind == SelectionKind.Polygon ? SelectionKind.Freehand : SelectionKind.Polygon;
        if (LassoKindBtn != null) LassoKindBtn.Content = lassoKind == SelectionKind.Polygon ? "Polygonal" : "Freehand";
        SetTool(lassoKind == SelectionKind.Polygon ? Tool.Polygon : Tool.Lasso);
    }

    void OnSelModeChanged(object s, SelectionChangedEventArgs e)
    {
        if (SelModeBox == null) return;
        selModeChoice = (SelModeBox.SelectedIndex switch { 1 => SelectionMode.Add, 2 => SelectionMode.Subtract, _ => SelectionMode.Replace });
        Log($"SelMode={selModeChoice}");
    }

    void OnDeselect(object s, RoutedEventArgs e)
    {
        lassoDraft = null; polygonDraft = null; hasPolygonCursor = false;
        if (floating != null) CommitFloating();
        doc.ClearSelection();
        Log("Deselect");
        RenderCanvas();
    }

    void OnDeleteSelection(object s, RoutedEventArgs e)
    {
        if (Selected is not { Bitmap: not null } l || doc.Selection == null) return;
        if (l.Locked) return;
        undoStack.Push(doc);
        int n = SelectionTools.DeleteSelection(doc, l);
        Log($"DeleteSelection cleared={n}");
        RefreshAll();
    }

    void DoWand(SKPoint docPoint, KeyModifiers mods)
    {
        if (Selected is not { Bitmap: not null } active) { Log("Wand: no active layer"); return; }
        SKBitmap sample;
        bool ownsSample = false;
        if (WandSampleAll)
        {
            sample = doc.Compose();   // 可視合成から読む (Mac sampleAllLayers相当)
            ownsSample = true;
        }
        else sample = active.Bitmap;
        try
        {
            int n = SelectionTools.WandSelect(doc, sample, docPoint, WandTolerance, sampleRadius: 0,
                WandContiguous, EffectiveSelMode(mods));
            Log($"Wand matched={n} tol={WandTolerance} contiguous={WandContiguous} all={WandSampleAll}");
        }
        finally { if (ownsSample) sample.Dispose(); }
        RenderCanvas();
    }

    void ConfirmDrafts()
    {
        if (lassoDraft != null)
        {
            bool ok = SelectionTools.ConfirmPolygon(doc, lassoDraft, draftMode, SelectionKind.Freehand);
            Log($"Lasso confirm ok={ok} pts={lassoDraft.Count}");
            lassoDraft = null;
            RenderCanvas();
        }
        if (polygonDraft != null)
        {
            bool ok = SelectionTools.ConfirmPolygon(doc, polygonDraft, draftMode, SelectionKind.Polygon);
            Log($"Polygon confirm ok={ok} verts={polygonDraft.Count}");
            polygonDraft = null; hasPolygonCursor = false;
            RenderCanvas();
        }
    }

    void CancelDrafts()
    {
        if (lassoDraft == null && polygonDraft == null) return;
        lassoDraft = null; polygonDraft = null; hasPolygonCursor = false;
        Log("Draft cancelled");
        RenderCanvas();
    }

    void CommitFloating()
    {
        if (floating == null || floatingLayer == null) { floating = null; floatingLayer = null; return; }
        // 切出し時にpush済みのため追加push不要。合成して確定 (Mac mergeFloatingTransform相当)。
        SelectionTools.CommitPixelMove(doc, floatingLayer, floating);
        Log($"Floating commit offset=({floating.Offset.X:F0},{floating.Offset.Y:F0})");
        floating = null; floatingLayer = null;
        floatingFrameDrag = false;
        RefreshAll();
    }

    void CancelFloating()
    {
        if (floating == null) return;
        DoUndo();   // 切出し自体を取り消して正確に復元
        Log("Floating cancelled (undo cut)");
        floating = null; floatingLayer = null;
        floatingFrameDrag = false;
        RefreshAll();
    }

    // ---------- clipboard ----------

    const string ClipboardPngFormat = "image/png";

    async void OnCopy(object s, RoutedEventArgs e) => await DoCopy();
    async void OnPaste(object s, RoutedEventArgs e) => await DoPaste();

    async System.Threading.Tasks.Task DoCopy()
    {
        if (Selected is not { Bitmap: not null } l) { Log("Copy: no selected layer"); return; }
        try
        {
            SKBitmap src = l.Bitmap;
            SKPoint origin = new(0, 0);
            bool cropped = false;
            // 選択あり=範囲切抜き (Mac SelectionClipboard相当)。矩形以外はマスクで抜く。
            if (doc.Selection != null)
            {
                var cut = SelectionTools.CopySelection(doc, l);
                if (cut == null) { Log("Copy: empty selection region"); return; }
                src = cut.Value.bmp;
                origin = cut.Value.origin;
                cropped = true;
            }
            lastCopyOrigin = origin;
            using var img = SKImage.FromBitmap(src);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();
            int sw = src.Width, sh = src.Height;
            if (cropped) src.Dispose();
            var cl = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cl == null) return;
            var dObj = new DataObject();
            dObj.Set(ClipboardPngFormat, bytes);
            await cl.SetDataObjectAsync(dObj);
            Log($"Copy: layer '{l.Name}' {(cropped ? "selection" : "full")} {sw}x{sh} @({origin.X:F0},{origin.Y:F0}) -> clipboard PNG ({bytes.Length}B)");
        }
        catch (Exception ex) { Log("Copy ERROR: " + ex); }
    }

    async System.Threading.Tasks.Task DoPaste()
    {
        try
        {
            var cl = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cl == null) return;
            var formats = await cl.GetFormatsAsync();
            byte[] png = null;
            foreach (var fmt in formats)
            {
                if (fmt != ClipboardPngFormat && fmt != "PNG") continue;
                if (await cl.GetDataAsync(fmt) is byte[] b) { png = b; break; }
            }
            if (png == null) { Log("Paste: no image on clipboard"); return; }
            var src = SKBitmap.Decode(png);
            if (src == null) { Log("Paste: decode failed"); return; }
            Log($"Paste: {src.Width}x{src.Height} from clipboard");
            // 選択コピーは元の位置へ (paste-in-place, Mac PixelClipboard.origin相当)
            var at = lastCopyOrigin.X != 0 || lastCopyOrigin.Y != 0 ? lastCopyOrigin : (SKPoint?)null;
            AddLayerBitmap(src, $"Pasted {DateTime.Now:HHmmss}", at);
        }
        catch (Exception ex) { Log("Paste ERROR: " + ex); }
    }

    // ---------- export ----------

    async void OnExport(object s, RoutedEventArgs e)
    {
        try
        {
            Log("OnExport begin");
            var dlg = new SaveFileDialog { DefaultExtension = "png", InitialFileName = doc.Name + ".png" };
            var path = await dlg.ShowAsync(this);
            Log($"OnExport path={path}");
            if (string.IsNullOrEmpty(path)) return;
            using var flat = doc.Compose();
            using var img = SKImage.FromBitmap(flat);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();
            File.WriteAllBytes(path, bytes);
            Log($"OnExport wrote {bytes.Length} bytes");
            await new MessageWindow("書き出しました: " + path).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log("OnExport ERROR: " + ex);
        }
    }

    // ---------- keyboard shortcuts (Photoshop-style) ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var k = e.Key; var mods = e.KeyModifiers;
        Log($"OnKeyDown {mods} {k}");
        switch (k)
        {
            case Key.B when mods == KeyModifiers.None: SetTool(Tool.Brush); e.Handled = true; break;
            case Key.E when mods == KeyModifiers.None: SetTool(Tool.Eraser); e.Handled = true; break;
            case Key.S when mods == KeyModifiers.None: SetTool(Tool.Clone); e.Handled = true; break;
            case Key.J when mods == KeyModifiers.None: SetTool(Tool.Heal); e.Handled = true; break;
            case Key.R when mods == KeyModifiers.None: SetTool(Tool.Smudge); e.Handled = true; break;
            case Key.I when mods == KeyModifiers.None: SetTool(Tool.Eyedropper); e.Handled = true; break;
            case Key.M when mods == KeyModifiers.None:
                if (currentTool == Tool.Marquee) OnToggleMarqueeShape(null, null);   // Mで矩形/楕円切替
                else SetTool(Tool.Marquee);
                e.Handled = true; break;
            case Key.L when mods == KeyModifiers.None:
                // Lで自由投繩/多角投繩を往復 (Mac LassoControls相当)
                if (currentTool == Tool.Lasso) { lassoKind = SelectionKind.Polygon; SetTool(Tool.Polygon); }
                else if (currentTool == Tool.Polygon) { lassoKind = SelectionKind.Freehand; SetTool(Tool.Lasso); }
                else OnToolLasso(null, null);
                if (LassoKindBtn != null) LassoKindBtn.Content = lassoKind == SelectionKind.Polygon ? "Polygonal" : "Freehand";
                e.Handled = true; break;
            case Key.W when mods == KeyModifiers.None: SetTool(Tool.Wand); e.Handled = true; break;
            case Key.V when mods == KeyModifiers.None: SetTool(Tool.Move); e.Handled = true; break;
            case Key.H when mods == KeyModifiers.None: SetTool(Tool.Hand); e.Handled = true; break;
            case Key.C when mods == KeyModifiers.None: SetTool(Tool.Crop); e.Handled = true; break;
            case Key.T when mods == KeyModifiers.None: BeginDistortTool(); e.Handled = true; break;
            case Key.U when mods == KeyModifiers.None: SetTool(Tool.Shape); e.Handled = true; break;
            case Key.G when mods == KeyModifiers.None: SetTool(Tool.Gradient); e.Handled = true; break;
            case Key.Space: cropSpaceHeld = true; e.Handled = true; break;
            case Key.Enter:
                if (currentTool == Tool.Distort && distortCorners != null) ApplyDistort();
                else if (currentTool == Tool.Crop && cropRect != null) ApplyCrop();
                else if (gradientDraft != null) CommitGradient();
                else if (floating != null) CommitFloating();
                else ConfirmDrafts();
                e.Handled = true; break;
            case Key.Escape:
                if (currentTool == Tool.Distort && distortCorners != null) CancelDistort();
                else if (currentTool == Tool.Crop && (cropRect != null || cropDrag != null)) CancelCrop();
                else if (gradientDraft != null) CancelGradient();
                else if (floating != null) CancelFloating();
                else if (lassoDraft != null || polygonDraft != null) CancelDrafts();
                else { doc.ClearSelection(); RenderCanvas(); Log("Deselect"); }
                e.Handled = true; break;
            case Key.OemOpenBrackets when mods == KeyModifiers.None:
                BrushSizeSlider.Value = Math.Max(BrushSizeSlider.Minimum, BrushSizeSlider.Value - 4);
                RefreshToolHeader();
                e.Handled = true; break;
            case Key.OemCloseBrackets when mods == KeyModifiers.None:
                BrushSizeSlider.Value = Math.Min(BrushSizeSlider.Maximum, BrushSizeSlider.Value + 4);
                RefreshToolHeader();
                e.Handled = true; break;
            // Shift-[ / ]: 硬さ 25%刻み (Mac changeBrushHardness相当)
            case Key.OemOpenBrackets when mods.HasFlag(KeyModifiers.Shift):
                brushHardness = Math.Clamp(MathF.Floor(brushHardness * 4 - 0.001f) / 4, 0f, 1f);
                SyncBrushPanel(); RefreshToolHeader(); RenderCanvas();
                e.Handled = true; break;
            case Key.OemCloseBrackets when mods.HasFlag(KeyModifiers.Shift):
                brushHardness = Math.Clamp(MathF.Ceiling(brushHardness * 4 + 0.001f) / 4, 0f, 1f);
                SyncBrushPanel(); RefreshToolHeader(); RenderCanvas();
                e.Handled = true; break;
            case Key.D when mods.HasFlag(KeyModifiers.Control):
                lassoDraft = null; polygonDraft = null; hasPolygonCursor = false;
                if (floating != null) CommitFloating();
                doc.ClearSelection(); RenderCanvas(); Log("Deselect"); e.Handled = true; break;
            // 不透明度キー 1=10% … 9=90% · 0=100% (Mac typeOpacityDigit相当・描画系工具のみ)
            case Key.D1 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.1f); e.Handled = true; break;
            case Key.D2 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.2f); e.Handled = true; break;
            case Key.D3 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.3f); e.Handled = true; break;
            case Key.D4 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.4f); e.Handled = true; break;
            case Key.D5 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.5f); e.Handled = true; break;
            case Key.D6 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.6f); e.Handled = true; break;
            case Key.D7 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.7f); e.Handled = true; break;
            case Key.D8 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.8f); e.Handled = true; break;
            case Key.D9 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(0.9f); e.Handled = true; break;
            case Key.D0 when mods == KeyModifiers.None && IsPaintTool(): SetPaintOpacity(1f); e.Handled = true; break;
            case Key.Left when mods == KeyModifiers.None || mods == KeyModifiers.Shift:
            case Key.Right when mods == KeyModifiers.None || mods == KeyModifiers.Shift:
            case Key.Up when mods == KeyModifiers.None || mods == KeyModifiers.Shift:
            case Key.Down when mods == KeyModifiers.None || mods == KeyModifiers.Shift:
                if (floating != null && currentTool == Tool.Move)
                {
                    // 画素移動中は矢印でゴーストをnudge (Mac Cmd-arrow相当)
                    float d = mods.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                    if (k == Key.Left) SelectionTools.MoveFloating(floating, -d, 0);
                    if (k == Key.Right) SelectionTools.MoveFloating(floating, d, 0);
                    if (k == Key.Up) SelectionTools.MoveFloating(floating, 0, -d);
                    if (k == Key.Down) SelectionTools.MoveFloating(floating, 0, d);
                    RenderCanvas();
                }
                else if (Selected is { } nudge && !nudge.Locked)
                {
                    float d = mods.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                    undoStack.Push(doc);
                    // 覆面編集中の矢印は覆面の単独移動 (Mac unlinked-mask transform相当)
                    if (nudge.MaskSelected && MaskOps.HasMask(nudge) && !nudge.MaskLinked)
                    {
                        var o = nudge.MaskOffset;
                        if (k == Key.Left) o.X -= d;
                        if (k == Key.Right) o.X += d;
                        if (k == Key.Up) o.Y -= d;
                        if (k == Key.Down) o.Y += d;
                        nudge.MaskOffset = o;
                        Log($"MaskOffset=({o.X},{o.Y})");
                        RenderCanvas();
                        RefreshLayerList();
                    }
                    else
                    {
                        var p = nudge.Position;
                        if (k == Key.Left) p.X -= d;
                        if (k == Key.Right) p.X += d;
                        if (k == Key.Up) p.Y -= d;
                        if (k == Key.Down) p.Y += d;
                        nudge.Position = p;
                        RenderCanvas();
                        RefreshLayerList();
                    }
                }
                e.Handled = true; break;
            case Key.Delete:
                if (doc.Selection != null) OnDeleteSelection(null, null);   // 選択あり=範囲画素消去
                else OnDeleteLayer(null, null);                              // 選択なし=層削除(従来)
                e.Handled = true; break;
            case Key.Z when mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Shift):
            case Key.Y when mods.HasFlag(KeyModifiers.Control):
                DoRedo(); e.Handled = true; break;
            case Key.Z when mods.HasFlag(KeyModifiers.Control):
                DoUndo(); e.Handled = true; break;
            case Key.C when mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Shift):
                _ = DoCopyMerged(); e.Handled = true; break;
            case Key.C when mods.HasFlag(KeyModifiers.Control):
                _ = DoCopy(); e.Handled = true; break;
            case Key.V when mods.HasFlag(KeyModifiers.Control):
                _ = DoPaste(); e.Handled = true; break;
            case Key.N when mods.HasFlag(KeyModifiers.Control):
                OnNew(null, null); e.Handled = true; break;
            case Key.O when mods.HasFlag(KeyModifiers.Control):
                OnImport(null, null); e.Handled = true; break;
            case Key.E when mods.HasFlag(KeyModifiers.Control):
                OnExport(null, null); e.Handled = true; break;
            case Key.S when mods.HasFlag(KeyModifiers.Control):
                _ = SaveAsync(asNew: false); e.Handled = true; break;
            case Key.X when mods == KeyModifiers.None:
                palette.Swap(); SyncPalettePanel(); RefreshToolHeader(); e.Handled = true; break;
            case Key.D when mods == KeyModifiers.None:
                palette.Reset(); SyncPalettePanel(); RefreshToolHeader(); e.Handled = true; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) cropSpaceHeld = false;
        base.OnKeyUp(e);
    }

    // ---------- canvas interaction (Move / Brush / Eraser / Marquee) ----------

    SKColor BrushPaintColor()
    {
        // Foreground color (Mac foregroundColor subset); mask mode paints white/black via MaskOps path.
        return palette.PaintColor(false).ToSK();
    }

    void PickColor(SKColor c)
    {
        pickedColor = new SKColor(c.Red, c.Green, c.Blue, 255);
        palette.Set(PaletteColor.FromSK(pickedColor).Quantized, background: false);
        if (!hasPickedColor)
        {
            hasPickedColor = true;
            BrushColors.Add(("Picked", pickedColor));
            BrushColorBox.Items.Add("Picked");
        }
        else
        {
            BrushColors[BrushColors.Count - 1] = ("Picked", pickedColor);
        }
        BrushColorBox.SelectedIndex = BrushColors.Count - 1;
        Log($"Eyedropper picked #{pickedColor.Red:X2}{pickedColor.Green:X2}{pickedColor.Blue:X2}");
    }

    SKPoint ToLayerPixel(Layer l, SKPoint docPoint) =>
        new((docPoint.X - l.Position.X) / Math.Max(1e-6f, l.ScaleX),
            (docPoint.Y - l.Position.Y) / Math.Max(1e-6f, l.ScaleY));

    SKPoint ToDocPixel(Layer l, SKPoint layerPoint) =>
        new(layerPoint.X * l.ScaleX + l.Position.X,
            layerPoint.Y * l.ScaleY + l.Position.Y);

    void DoEyedropper(SKPoint docPoint)
    {
        var layer = Selected;
        if (layer == null || layer.Bitmap == null)
        {
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (l.Visible && l.Bitmap != null && l.HitTest(docPoint)) { layer = l; break; }
            }
        }
        if (layer == null) return;
        bool all = EyedropAllCheck?.IsChecked == true;
        var c = Eyedropper.Sample(doc, layer, docPoint, all);
        PickColor(c);
        RenderCanvas();
    }

    /// <summary>Marquee selection (document px) converted to layer-pixel space for clip.</summary>
    SKRect? SelectionLayerRect(Layer l)
    {
        if (doc.Selection is not SKRect r) return null;
        float sx = Math.Max(1e-6f, l.ScaleX), sy = Math.Max(1e-6f, l.ScaleY);
        return new SKRect((r.Left - l.Position.X) / sx, (r.Top - l.Position.Y) / sy,
                          (r.Right - l.Position.X) / sx, (r.Bottom - l.Position.Y) / sy);
    }

    SKRect? marqueePreview;
    byte[] preDragMask;             // drag前の選択 (Add/Subtract合成用)
    SelectionKind preDragKind = SelectionKind.Rectangle;
    List<SKPoint> preDragPolygon;
    SKRect? preDragSel;

    bool InsideSelection(SKPoint docPoint) => doc.InsideSelection(docPoint);

    void ShiftSelectionFrame(float dx, float dy)
    {
        if (doc.Selection is not SKRect r) return;
        doc.Selection = new SKRect(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
        if (doc.SelectionPolygon != null)
            for (int i = 0; i < doc.SelectionPolygon.Count; i++)
                doc.SelectionPolygon[i] = new SKPoint(doc.SelectionPolygon[i].X + dx, doc.SelectionPolygon[i].Y + dy);
        if (doc.SelectionMask != null && doc.SelectionMaskW == doc.Width && doc.SelectionMaskH == doc.Height)
        {
            var shifted = new byte[doc.SelectionMask.Length];
            int ix = (int)MathF.Round(dx), iy = (int)MathF.Round(dy);
            for (int y = 0; y < doc.Height; y++)
            {
                int sy = y - iy;
                if (sy < 0 || sy >= doc.Height) continue;
                for (int x = 0; x < doc.Width; x++)
                {
                    int sx = x - ix;
                    if (sx < 0 || sx >= doc.Width) continue;
                    shifted[y * doc.Width + x] = doc.SelectionMask[sy * doc.Width + sx];
                }
            }
            doc.SelectionMask = shifted;
        }
    }

    void OnCanvasPointerPressed(object s, PointerPressedEventArgs e)
    {
        Log($"OnCanvasPointerPressed tool={currentTool}");

        var props = e.GetCurrentPoint((Control)s).Properties;
        if (!props.IsLeftButtonPressed) return;
        var mods = e.KeyModifiers;
        var docPoint = CanvasPoint(e, (Control)s);

        // 画素移動中: ゴースト上ならdrag継続、外なら確定して通常処理へ
        if (floating != null && currentTool == Tool.Move)
        {
            var g = new SKRect(floating.Origin.X + floating.Offset.X, floating.Origin.Y + floating.Offset.Y,
                floating.Origin.X + floating.Offset.X + floating.Pixels.Width,
                floating.Origin.Y + floating.Offset.Y + floating.Pixels.Height);
            if (docPoint.X >= g.Left && docPoint.X <= g.Right && docPoint.Y >= g.Top && docPoint.Y <= g.Bottom)
            {
                floatingLast = e.GetPosition((Visual)s);
                e.Pointer.Capture((IInputElement)s);
                return;
            }
            CommitFloating();
        }

        if (currentTool == Tool.Crop)
        {
            CropPressed(docPoint, mods);
            RenderCanvas();
            RefreshToolHeader();
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Distort)
        {
            DistortPressed(docPoint);
            RenderCanvas();
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Shape)
        {
            shapeAnchor = docPoint;
            shapeSquare = mods.HasFlag(KeyModifiers.Shift);
            shapeFromCenter = mods.HasFlag(KeyModifiers.Alt);
            shapeRect = new SKRect(docPoint.X, docPoint.Y, docPoint.X, docPoint.Y);
            RenderCanvas();
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Gradient)
        {
            BeginGradient(docPoint);
            gradientDragging = true;
            RenderCanvas();
            RefreshToolHeader();
            e.Pointer.Capture((IInputElement)s);
            return;
        }

        if (currentTool == Tool.Marquee)
        {
            marqueeStart = docPoint;
            marqueeSquare = mods.HasFlag(KeyModifiers.Shift);
            marqueeFromCenter = mods.HasFlag(KeyModifiers.Alt);
            draftMode = EffectiveSelMode(mods);
            // Add/Subtract用にdrag前を保存 (live中はReplace表示→releaseで合成)
            preDragSel = doc.Selection; preDragKind = doc.SelKind;
            preDragPolygon = doc.SelectionPolygon != null ? new List<SKPoint>(doc.SelectionPolygon) : null;
            preDragMask = doc.Selection != null ? SelectionTools.CurrentMask(doc) : null;
            marqueeActive = true;
            marqueePreview = new SKRect(docPoint.X, docPoint.Y, docPoint.X, docPoint.Y);
            RenderCanvas();
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Lasso)
        {
            if (e.ClickCount >= 2) { ConfirmDrafts(); return; }
            draftMode = lassoDraft == null ? EffectiveSelMode(mods) : draftMode;
            lassoDraft ??= new List<SKPoint>();
            lassoDraft.Add(docPoint);
            Log($"Lasso draft pts={lassoDraft.Count}");
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Polygon)
        {
            if (e.ClickCount >= 2) { ConfirmDrafts(); return; }
            draftMode = polygonDraft == null ? EffectiveSelMode(mods) : draftMode;
            polygonDraft ??= new List<SKPoint>();
            polygonDraft.Add(docPoint);
            hasPolygonCursor = false;
            Log($"Polygon verts={polygonDraft.Count}");
            RenderCanvas();
            return;
        }
        if (currentTool == Tool.Wand)
        {
            DoWand(docPoint, mods);
            return;
        }
        if (currentTool == Tool.Eyedropper)
        {
            DoEyedropper(docPoint);
            return;
        }
        // Alt-click採取: Brush/Eraser/Clone/Smudge/Heal のいずれからも色を拾う (Photoshop相当)
        if (mods.HasFlag(KeyModifiers.Alt) && currentTool is Tool.Brush or Tool.Eraser or Tool.Smudge or Tool.Heal)
        {
            DoEyedropper(docPoint);
            return;
        }
        if (currentTool == Tool.Clone && mods.HasFlag(KeyModifiers.Alt))
        {
            // CloneはAlt-clickで採取点 (色採取より採取点が優先)
            if (docPoint.X >= 0 && docPoint.Y >= 0 && docPoint.X < doc.Width && docPoint.Y < doc.Height)
            {
                cloneSource = new SKPoint(MathF.Round(docPoint.X), MathF.Round(docPoint.Y));
                cloneOffset = null;
                Log($"Clone source=({cloneSource.Value.X},{cloneSource.Value.Y})");
                RefreshToolHeader();
                RenderCanvas();
            }
            return;
        }
        if (currentTool == Tool.Brush || currentTool == Tool.Eraser)
        {
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (!l.Visible || l.Locked || l.Bitmap == null) continue;
                if (!l.HitTest(docPoint) || !InsideSelection(docPoint)) continue;
                undoStack.Push(doc);
                strokePoints = new List<SKPoint> { ToLayerPixel(l, docPoint) };
                strokeDocPoints = new List<SKPoint> { docPoint };
                drawnDabs = PaintEngine.DabCount(strokePoints, BrushDiameter(), brushSpacing);
                dragLayer = l;
                // 覆面選択中は画素ではなく覆面へ描画 (Mac mask painting相当: B=白/E=黒)
                strokeOnMask = l.MaskSelected && MaskOps.HasMask(l);
                if (strokeOnMask)
                    MaskOps.PaintStroke(l, strokePoints, currentTool == Tool.Brush ? (byte)255 : (byte)0,
                        BrushDiameter(), brushHardness, brushOpacity);
                else
                    PaintEngine.PaintBrushStroke(l, strokePoints, BrushPaintColor(),
                        CurrentBrushSettings(), currentTool == Tool.Eraser, SelectionLayerRect(l));
                RenderCanvas();
                RefreshLayerList();
                break;
            }
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Clone)
        {
            if (cloneSource == null) { Log("Clone: Alt-clickで採取点を先に設定"); return; }
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (!l.Visible || l.Locked || l.Bitmap == null) continue;
                if (!l.HitTest(docPoint) || !InsideSelection(docPoint)) continue;
                var off = CloneTools.StrokeOffset(docPoint, cloneSource, cloneOffset, cloneAligned);
                if (off == null) return;
                undoStack.Push(doc);
                cloneSampleBmp?.Dispose();
                cloneSampleBmp = CloneTools.CloneSample(doc, l, cloneSampleAll);
                if (cloneAligned) cloneOffset = off;
                strokePoints = new List<SKPoint> { ToLayerPixel(l, docPoint) };
                strokeDocPoints = new List<SKPoint> { docPoint };
                dragLayer = l;
                var settings = CurrentBrushSettings();
                var offDoc = new SKPoint(off.Value.Width, off.Value.Height);
                var layer = l;
                CloneTools.PaintCloneStroke(l, cloneSampleBmp, strokeDocPoints, offDoc,
                    settings, p => ToLayerPixel(layer, p), p => InsideSelection(p));
                Log($"Clone stroke offset=({off.Value.Width},{off.Value.Height}) allLayers={cloneSampleAll}");
                RenderCanvas();
                RefreshLayerList();
                break;
            }
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Heal)
        {
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (!l.Visible || l.Locked || l.Bitmap == null) continue;
                if (!l.HitTest(docPoint) || !InsideSelection(docPoint)) continue;
                undoStack.Push(doc);
                strokePoints = new List<SKPoint> { ToLayerPixel(l, docPoint) };
                strokeDocPoints = new List<SKPoint> { docPoint };
                dragLayer = l;
                HealTools.SpotHeal(l, strokePoints[0], BrushDiameter(), brushOpacity, healMode,
                    p => InsideSelection(ToDocPixel(l, p)));
                RenderCanvas();
                RefreshLayerList();
                break;
            }
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        if (currentTool == Tool.Smudge)
        {
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (!l.Visible || l.Locked || l.Bitmap == null) continue;
                if (!l.HitTest(docPoint) || !InsideSelection(docPoint)) continue;
                undoStack.Push(doc);
                Document.EnsureUniqueBitmap(l);   // COW: Push後の直接画素編集がUndo snapshotを汚さない
                strokePoints = new List<SKPoint> { ToLayerPixel(l, docPoint) };
                strokeDocPoints = new List<SKPoint> { docPoint };
                dragLayer = l;
                smudgeLastDoc = docPoint;
                if (smudgeMode == "Smudge")
                {
                    activeSmudge = new SmudgeStroke(BrushDiameter(), brushOpacity, brushHardness);
                    activeSmudge.PickUp(l.Bitmap, strokePoints[0]);
                    activeSmudge.SmudgeAt(l.Bitmap, strokePoints[0]);
                }
                else if (smudgeMode == "Blur")
                {
                    blurSampleBmp?.Dispose();
                    blurSampleBmp = SmudgeStroke.BlurSampleLayer(l, BrushDiameter());
                    if (blurSampleBmp != null)
                        SmudgeStroke.BlurAt(l.Bitmap, blurSampleBmp, strokePoints[0],
                            BrushDiameter(), brushHardness, brushOpacity);
                }
                // Liquifyは移動量が必要なためMoved側で処理 (Pressedでは起点のみ)
                Log($"Smudge start mode={smudgeMode}");
                RenderCanvas();
                RefreshLayerList();
                break;
            }
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        // Move: 選択内掴みは枠移動、Ctrl+掴みは画素切出移動、Alt+掴みは複写移動 (Mac PixelMove相当)
        if (currentTool == Tool.Move && doc.Selection != null && doc.InsideSelection(docPoint))
        {
            if (mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta) || mods.HasFlag(KeyModifiers.Alt))
            {
                bool duplicate = mods.HasFlag(KeyModifiers.Alt);
                for (int i = doc.Layers.Count - 1; i >= 0; i--)
                {
                    var l = doc.Layers[i];
                    if (!l.Visible || l.Locked || l.Bitmap == null || !l.HitTest(docPoint)) continue;
                    undoStack.Push(doc);   // 切出しを1エントリに (EscはUndoで復元)
                    floating = SelectionTools.BeginPixelMove(doc, l, duplicate);
                    floatingLayer = l;
                    if (floating == null) break;
                    floatingLast = e.GetPosition((Visual)s);
                    SafeSelect(doc.Layers.Count - 1 - i);
                    Log($"PixelMove start duplicate={duplicate}");
                    RenderCanvas();
                    break;
                }
            }
            else
            {
                floatingFrameDrag = true;
                frameDragLast = docPoint;
                Log("Selection frame drag start");
            }
            e.Pointer.Capture((IInputElement)s);
            return;
        }
        // Move / Hand: topmost hit layer drag (Handはスクロール任せのためMoveと同等)
        for (int i = doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = doc.Layers[i];
            if (l.Visible && !l.Locked && l.HitTest(docPoint))
            {
                undoStack.Push(doc);   // ドラッグ前の状態を保存（Released時pushではUndoが効かない）
                dragLayer = l;
                dragStart = e.GetPosition((Visual)s);
                dragLayerPos = l.Position;
                SafeSelect(doc.Layers.Count - 1 - i);
                break;
            }
        }
        e.Pointer.Capture((IInputElement)s);
    }

    /// <summary>Paint stroke終了: 状態クリアとstrokeスナップショット破棄 (UndoはPressed時push済み).</summary>
    void EndPaintStroke()
    {
        strokePoints = null;
        strokeDocPoints = null;
        strokeOnMask = false;
        dragLayer = null;
        drawnDabs = 0;
        activeSmudge = null;
        smudgeLastDoc = null;
        cloneSampleBmp?.Dispose(); cloneSampleBmp = null;
        blurSampleBmp?.Dispose(); blurSampleBmp = null;
    }

    SKPoint CanvasPoint(PointerEventArgs e, Visual visual)
    {
        var p = e.GetPosition(visual);
        return new SKPoint((float)(p.X / zoom), (float)(p.Y / zoom));
    }

    void OnCanvasPointerMoved(object s, PointerEventArgs e)
    {
        var docPoint = CanvasPoint(e, (Control)s);
        if (currentTool == Tool.Crop && cropDrag != null)
        {
            CropMoved(docPoint, e.KeyModifiers);
            RefreshToolHeader();
            return;
        }
        if (currentTool == Tool.Distort && distortDragging)
        {
            DistortMoved(docPoint, e.KeyModifiers);
            return;
        }
        if (currentTool == Tool.Shape && shapeRect != null)
        {
            var propsS = e.GetCurrentPoint((Control)s).Properties;
            if (!propsS.IsLeftButtonPressed) { shapeRect = null; RenderCanvas(); return; }
            shapeRect = ShapeOps.DragRect(shapeAnchor, docPoint, shapeSquare, shapeFromCenter);
            RenderCanvas();
            return;
        }
        if (currentTool == Tool.Gradient && gradientDragging)
        {
            var propsG = e.GetCurrentPoint((Control)s).Properties;
            if (!propsG.IsLeftButtonPressed) return;
            MoveGradient(docPoint, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        // ブラシ環ホバー追従 (ボタン押下なし・描画系工具のみ)
        if (strokePoints == null && !marqueeActive && floating == null && !floatingFrameDrag
            && currentTool is Tool.Brush or Tool.Eraser or Tool.Clone or Tool.Heal or Tool.Smudge)
        {
            var props0 = e.GetCurrentPoint((Control)s).Properties;
            if (!props0.IsLeftButtonPressed && !props0.IsRightButtonPressed)
            {
                hoverDoc = docPoint; hasHover = true;
                RenderCanvas();
                return;
            }
        }
        if (marqueeActive)
        {
            // 選択はUndo履歴に載せない（Photoshop同様、選択自体は履歴対象外）。
            // live中はpreview表示のみ。加減算はrelease時にpreDragへ合成する。
            var box = SelectionTools.DragBoxRect(marqueeStart, docPoint, marqueeSquare, marqueeFromCenter);
            box.Intersect(new SKRect(0, 0, doc.Width, doc.Height));
            if (draftMode == SelectionMode.Replace)
                SelectionTools.SetRectSelection(doc, box, SelectionMode.Replace, marqueeShape);
            else
                marqueePreview = box;
            RenderCanvas();
            return;
        }
        if (currentTool == Tool.Lasso && lassoDraft != null)
        {
            var props = e.GetCurrentPoint((Control)s).Properties;
            if (!props.IsLeftButtonPressed) return;
            var last = lassoDraft[^1];
            float dx = docPoint.X - last.X, dy = docPoint.Y - last.Y;
            if (dx * dx + dy * dy >= 4)   // 2px以上で記録
            {
                lassoDraft.Add(docPoint);
                RenderCanvas();
            }
            return;
        }
        if (currentTool == Tool.Polygon && polygonDraft != null)
        {
            polygonCursor = docPoint;
            hasPolygonCursor = true;
            RenderCanvas();
            return;
        }
        if (floating != null && floatingLayer != null)
        {
            var props = e.GetCurrentPoint((Control)s).Properties;
            if (!props.IsLeftButtonPressed) return;
            var fnow = e.GetPosition((Visual)s);
            SelectionTools.MoveFloating(floating,
                (float)((fnow.X - floatingLast.X) / zoom), (float)((fnow.Y - floatingLast.Y) / zoom));
            floatingLast = fnow;
            RenderCanvas();
            return;
        }
        if (floatingFrameDrag && doc.Selection != null)
        {
            var props = e.GetCurrentPoint((Control)s).Properties;
            if (!props.IsLeftButtonPressed) { floatingFrameDrag = false; return; }
            ShiftSelectionFrame(docPoint.X - frameDragLast.X, docPoint.Y - frameDragLast.Y);
            frameDragLast = docPoint;
            RenderCanvas();
            return;
        }
        if (strokePoints != null && dragLayer != null && dragLayer.Bitmap != null)
        {
            var props = e.GetCurrentPoint((Control)s).Properties;
            if (!props.IsLeftButtonPressed) { EndPaintStroke(); return; }
            docPoint = CanvasPoint(e, (Control)s);
            if (!InsideSelection(docPoint))
            {
                // 選択外では描かないが、Liquify再入時の飛び (stale smudgeLastDoc) を防ぐため起点だけ更新する
                if (currentTool == Tool.Smudge) smudgeLastDoc = docPoint;
                return;
            }
            var layer = dragLayer;
            var lp = ToLayerPixel(layer, docPoint);
            strokePoints.Add(lp);
            strokeDocPoints?.Add(docPoint);
            var settings = CurrentBrushSettings();
            if (currentTool == Tool.Brush || currentTool == Tool.Eraser)
            {
                if (strokeOnMask && MaskOps.HasMask(layer) && strokePoints.Count >= 2)
                {
                    // 覆面への増分ストローク (COW済みのため追加push不要)
                    var seg = new List<SKPoint> { strokePoints[^2], strokePoints[^1] };
                    MaskOps.PaintStroke(layer, seg, currentTool == Tool.Brush ? (byte)255 : (byte)0,
                        BrushDiameter(), brushHardness, brushOpacity);
                }
                else if (!strokeOnMask)
                {
                    // 増分ダブのみ描画（COW済みのため追加push不要）。選択枠で切り抜き。
                    using (var canvas = new SKCanvas(layer.Bitmap))
                {
                    var clip = SelectionLayerRect(layer);
                    bool clipped = clip is SKRect c && c.Width > 0 && c.Height > 0;
                    if (clipped) { canvas.Save(); canvas.ClipRect(clip.Value); }
                    try
                    {
                        drawnDabs += PaintEngine.PaintDabs(canvas, strokePoints, BrushPaintColor(),
                            settings, currentTool == Tool.Eraser, drawnDabs);
                    }
                    finally { if (clipped) canvas.Restore(); }
                    }
                }
            }
            else if (currentTool == Tool.Clone && cloneSampleBmp != null && strokeDocPoints != null)
            {
                var off = CloneTools.StrokeOffset(docPoint, cloneSource, cloneAligned ? cloneOffset : null, cloneAligned);
                SKPoint offDoc = off == null ? new SKPoint(0, 0) : new SKPoint(off.Value.Width, off.Value.Height);
                if (cloneAligned && off != null) cloneOffset = off;
                var seg = strokeDocPoints.Count >= 2
                    ? new List<SKPoint> { strokeDocPoints[^2], strokeDocPoints[^1] }
                    : new List<SKPoint> { docPoint };
                CloneTools.PaintCloneStroke(layer, cloneSampleBmp, seg, offDoc,
                    settings, p => ToLayerPixel(layer, p), p => InsideSelection(p));
            }
            else if (currentTool == Tool.Heal)
            {
                HealTools.SpotHeal(layer, lp, BrushDiameter(), brushOpacity, healMode,
                    p => InsideSelection(ToDocPixel(layer, p)));
            }
            else if (currentTool == Tool.Smudge)
            {
                // 前回層画素→今回層画素を間隔配置で補間し、高速ドラッグでも切れ目なく stamp する (Clone/Brush相当)。
                // ダブ中心が選択外のものは飛ばす (Clone accept / Heal と同規則)。
                float dia = BrushDiameter();
                var prevLp = strokePoints.Count >= 2 ? strokePoints[^2] : lp;
                var seg = new List<SKPoint> { prevLp, lp };
                if (smudgeMode == "Smudge" && activeSmudge != null)
                {
                    foreach (var dab in BrushTip.DabPositions(seg, dia, brushSpacing))
                    {
                        if (!InsideSelection(ToDocPixel(layer, dab))) continue;
                        activeSmudge.SmudgeAt(layer.Bitmap, dab);
                    }
                }
                else if (smudgeMode == "Blur" && blurSampleBmp != null)
                {
                    foreach (var dab in BrushTip.DabPositions(seg, dia, brushSpacing))
                    {
                        if (!InsideSelection(ToDocPixel(layer, dab))) continue;
                        SmudgeStroke.BlurAt(layer.Bitmap, blurSampleBmp, dab,
                            dia, brushHardness, brushOpacity);
                    }
                }
                else if (smudgeMode == "Liquify" && smudgeLastDoc != null)
                {
                    var prev = ToLayerPixel(layer, smudgeLastDoc.Value);
                    SmudgeStroke.PushAt(layer.Bitmap, prev, lp, dia, brushHardness, brushOpacity);
                }
                smudgeLastDoc = docPoint;
            }
            RenderCanvas();
            return;
        }
        if (dragLayer == null) return;
        Log("OnCanvasPointerMoved dragging");

        var props2 = e.GetCurrentPoint((Control)s).Properties;
        if (!props2.IsLeftButtonPressed) { dragLayer = null; return; }
        var now = e.GetPosition((Visual)s);
        dragLayer.Position = new SKPoint(
            dragLayerPos.X + (float)((now.X - dragStart.X) / zoom),
            dragLayerPos.Y + (float)((now.Y - dragStart.Y) / zoom));
        RenderCanvas();
        RefreshLayerList();
    }

    void OnCanvasPointerReleased(object s, Avalonia.Input.PointerReleasedEventArgs e)
    {
        // UndoはPressed時にpush済み。ここではドラッグ状態の解除のみ。
        if (currentTool == Tool.Crop && cropDrag != null) { CropReleased(); e.Pointer.Capture(null); return; }
        if (currentTool == Tool.Distort && distortDragging) { DistortReleased(); e.Pointer.Capture(null); return; }
        if (currentTool == Tool.Shape && shapeRect != null)
        {
            var r = shapeRect.Value; shapeRect = null;
            r.Intersect(new SKRect(0, 0, doc.Width, doc.Height));
            if (r.Width >= 1 && r.Height >= 1)
            {
                undoStack.Push(doc);
                var nl = ShapeOps.FinishShape(doc, r, shapeKind, BrushPaintColor(),
                    shapeKind == ShapeKind.RoundedRect ? shapeCornerRadius : 0, Selected);
                Log(nl != null ? $"Shape {shapeKind} {r.Width:F0}x{r.Height:F0} -> '{nl.Name}'" : "Shape failed");
                RefreshAll();
            }
            else RenderCanvas();
            e.Pointer.Capture(null); return;
        }
        if (currentTool == Tool.Gradient && gradientDragging)
        {
            gradientDragging = false;
            // clickのみ(線なし)は破棄、それ以外は保留→Enter確定 (Mac endGradientDrag相当)
            if (gradientDraft != null && !gradientDraft.HasLine) CancelGradient();
            else { RenderCanvas(); RefreshToolHeader(); }
            e.Pointer.Capture(null); return;
        }
        if (marqueeActive)
        {
            marqueeActive = false;
            if (draftMode != SelectionMode.Replace)
            {
                // preDragへ今回boxを合成 (Mac SelectionMode相当)。live中はpreviewのみでdoc不変。
                var box = marqueePreview ?? new SKRect(marqueeStart.X, marqueeStart.Y, marqueeStart.X, marqueeStart.Y);
                // preDrag状態を復元 (Wandマスク・投繩ポリゴンも保持)
                doc.Selection = preDragSel; doc.SelKind = preDragKind; doc.SelectionPolygon = preDragPolygon;
                doc.SelectionMask = null; doc.SelectionMaskW = doc.SelectionMaskH = 0;
                if (preDragMask != null && preDragSel != null && preDragKind == SelectionKind.Wand)
                { doc.SelectionMask = preDragMask; doc.SelectionMaskW = doc.Width; doc.SelectionMaskH = doc.Height; }
                if (box.Width >= 3 && box.Height >= 3)
                {
                    var fresh = marqueeShape == SelectionKind.Ellipse
                        ? SelectionTools.RasterizeEllipse(box, doc.Width, doc.Height)
                        : SelectionTools.RasterizeRect(box, doc.Width, doc.Height);
                    SelectionTools.ApplyMask(doc, fresh, marqueeShape, null,
                        preDragSel == null ? SelectionMode.Replace : draftMode);
                }
                // 極小boxはクリック扱い: preDrag復元のまま (何もしない)
            }
            else
            {
                // 極小選択はクリック扱いで解除
                if (doc.Selection is SKRect r && (r.Width < 3 || r.Height < 3))
                    doc.ClearSelection();
            }
            marqueePreview = null; preDragMask = null; preDragPolygon = null; preDragSel = null;
            Log($"Marquee selection={doc.Selection} kind={doc.SelKind} mode={draftMode}");
            RenderCanvas();
        }
        floatingFrameDrag = false;
        EndPaintStroke();
        e.Pointer.Capture(null);
    }
}
