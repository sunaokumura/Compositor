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

    enum Tool { Move, Hand, Brush, Eraser, Marquee, Lasso, Polygon, Wand }
    Tool currentTool = Tool.Move;
    List<SKPoint> strokePoints;       // active brush stroke (layer-pixel space)
    SKPoint marqueeStart;             // marquee anchor (document px)
    bool marqueeActive;
    bool marqueeSquare, marqueeFromCenter;   // Shift / Option held at drag start
    SelectionKind marqueeShape = SelectionKind.Rectangle;   // Mで Rectangle/Ellipse 切替
    SelectionKind lassoKind = SelectionKind.Freehand;       // Lで Freehand/Polygon 切替
    SelectionMode selModeChoice = SelectionMode.Replace;    // 工具栏Mode (Shift=Add/Option=Subtractで上書き)
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

    static readonly (string Name, SKColor Color)[] BrushColors = new[]
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
                ScaleSlider.Value = sel.Scale * 100;
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
            }
            finally { updatingList = false; }
        }
        RenderCanvas();
        UpdateUndoButtons();
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
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                LayerList.Items.Add($"{(l.Visible ? "" : "[hidden] ")}{l.Name}   {l.Opacity:P0}   pos({l.Position.X:F0},{l.Position.Y:F0})");
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
            DrawFloatingPreview(canvas);
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
        if (t != Tool.Lasso) lassoDraft = null;
        if (t != Tool.Polygon) { polygonDraft = null; hasPolygonCursor = false; }
        currentTool = t;
        marqueeActive = false;
        strokePoints = null;
        var (title, hint) = t switch
        {
            Tool.Move => ("Transform", "選択内Drag=枠移動 · Ctrl+Drag=画素移動 · Alt+Drag=複写"),
            Tool.Hand => ("Pan", "Drag to pan · +/- zoom"),
            Tool.Brush => ("Brush", "Drag to paint · [ ] size"),
            Tool.Eraser => ("Eraser", "Drag to erase · [ ] size"),
            Tool.Marquee => (marqueeShape == SelectionKind.Ellipse ? "Marquee (Ellipse)" : "Marquee",
                "Dragで選択 · Shift=正方形 · Alt=中心 · M=楕円切替 · Ctrl+D解除"),
            Tool.Lasso => ("Lasso (Freehand)", "Dragで投繩 · Enter/ダブルクリック確定 · Esc取消 · L=多角切替"),
            Tool.Polygon => ("Lasso (Polygonal)", "Clickで頂点 · Enter/ダブルクリック確定 · Esc取消 · L=自由切替"),
            Tool.Wand => ("Magic Wand", "Clickで類似色選択 · Tolerance/Contiguous設定 · Shift=加算 Alt=減算"),
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
        var on = (Avalonia.Media.IBrush)Avalonia.Media.Brushes.DimGray;
        var off = (Avalonia.Media.IBrush)Avalonia.Media.Brushes.Transparent;
        if (MoveBtn != null) MoveBtn.Background = currentTool == Tool.Move ? on : off;
        if (HandBtn != null) HandBtn.Background = currentTool == Tool.Hand ? on : off;
        if (BrushBtn != null) BrushBtn.Background = currentTool == Tool.Brush ? on : off;
        if (EraserBtn != null) EraserBtn.Background = currentTool == Tool.Eraser ? on : off;
        if (MarqueeBtn != null) MarqueeBtn.Background = currentTool == Tool.Marquee ? on : off;
        if (LassoBtn != null) LassoBtn.Background = (currentTool == Tool.Lasso || currentTool == Tool.Polygon) ? on : off;
        if (WandBtn != null) WandBtn.Background = currentTool == Tool.Wand ? on : off;
    }

    void OnToolMove(object s, RoutedEventArgs e) => SetTool(Tool.Move);
    void OnToolHand(object s, RoutedEventArgs e) => SetTool(Tool.Hand);
    void OnToolBrush(object s, RoutedEventArgs e) => SetTool(Tool.Brush);
    void OnToolEraser(object s, RoutedEventArgs e) => SetTool(Tool.Eraser);
    void OnToolMarquee(object s, RoutedEventArgs e) => SetTool(Tool.Marquee);
    void OnToolLasso(object s, RoutedEventArgs e) => SetTool(lassoKind == SelectionKind.Polygon ? Tool.Polygon : Tool.Lasso);
    void OnToolWand(object s, RoutedEventArgs e) => SetTool(Tool.Wand);

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
        var dlg = new OpenFileDialog { Filters = { new FileDialogFilter { Name = "Images", Extensions = { "png", "jpg", "jpeg", "bmp" } } } };
        var files = await dlg.ShowAsync(this);
        if (files is not { Length: > 0 }) return;
        SKBitmap src;
        try { src = SKBitmap.Decode(files[0]); }
        catch (Exception ex) { await new MessageWindow("読み込み失敗: " + ex.Message).ShowDialog(this); return; }
        if (src == null) { await new MessageWindow("デコードできませんでした: " + files[0]).ShowDialog(this); return; }
        AddLayerBitmap(src, System.IO.Path.GetFileNameWithoutExtension(files[0]));
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
        doc.Layers.Remove(l);
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
            Position = new SKPoint(l.Position.X + 10, l.Position.Y + 10), Scale = l.Scale,
            Blend = l.Blend, Rotation = l.Rotation, FlipH = l.FlipH, FlipV = l.FlipV,
            Brightness = l.Brightness, Contrast = l.Contrast, Saturation = l.Saturation,
            Blur = l.Blur, Invert = l.Invert,
        });
        Log($"Duplicate '{l.Name}'");
        RefreshAll();
    }

    void OnMergeDown(object s, RoutedEventArgs e)
    {
        if (Selected is not { } top) return;
        int i = doc.Layers.IndexOf(top);
        if (i <= 0) return;   // bottom-up: index 0 is bottom, needs a layer below
        var below = doc.Layers[i - 1];
        if (below.Bitmap == null || top.Bitmap == null) return;
        undoStack.Push(doc);
        Document.EnsureUniqueBitmap(below);
        using (var canvas = new SKCanvas(below.Bitmap))
            Document.DrawLayer(canvas, new Layer
            {
                Bitmap = top.Bitmap, Visible = true, Opacity = top.Opacity,
                Position = new SKPoint(top.Position.X - below.Position.X, top.Position.Y - below.Position.Y),
                Scale = top.Scale, Blend = top.Blend, Rotation = top.Rotation,
                FlipH = top.FlipH, FlipV = top.FlipV, Brightness = top.Brightness,
                Contrast = top.Contrast, Saturation = top.Saturation, Blur = top.Blur, Invert = top.Invert,
            }, 1f);
        doc.Layers.Remove(top);
        Log($"MergeDown '{top.Name}' into '{below.Name}'");
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
            case Key.Enter:
                if (floating != null) CommitFloating();
                else ConfirmDrafts();
                e.Handled = true; break;
            case Key.Escape:
                if (floating != null) CancelFloating();
                else if (lassoDraft != null || polygonDraft != null) CancelDrafts();
                else { doc.ClearSelection(); RenderCanvas(); Log("Deselect"); }
                e.Handled = true; break;
            case Key.OemOpenBrackets when mods == KeyModifiers.None:
                BrushSizeSlider.Value = Math.Max(BrushSizeSlider.Minimum, BrushSizeSlider.Value - 4);
                e.Handled = true; break;
            case Key.OemCloseBrackets when mods == KeyModifiers.None:
                BrushSizeSlider.Value = Math.Min(BrushSizeSlider.Maximum, BrushSizeSlider.Value + 4);
                e.Handled = true; break;
            case Key.D when mods.HasFlag(KeyModifiers.Control):
                lassoDraft = null; polygonDraft = null; hasPolygonCursor = false;
                if (floating != null) CommitFloating();
                doc.ClearSelection(); RenderCanvas(); Log("Deselect"); e.Handled = true; break;
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
                    var p = nudge.Position;
                    if (k == Key.Left) p.X -= d;
                    if (k == Key.Right) p.X += d;
                    if (k == Key.Up) p.Y -= d;
                    if (k == Key.Down) p.Y += d;
                    nudge.Position = p;
                    RenderCanvas();
                    RefreshLayerList();
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
        }
        base.OnKeyDown(e);
    }

    // ---------- canvas interaction (Move / Brush / Eraser / Marquee) ----------

    SKColor BrushPaintColor()
    {
        int i = BrushColorBox?.SelectedIndex ?? 0;
        if (i >= 0 && i < BrushColors.Length) return BrushColors[i].Color;
        return SKColors.Black;
    }

    SKPoint ToLayerPixel(Layer l, SKPoint docPoint) =>
        new((docPoint.X - l.Position.X) / Math.Max(1e-6f, l.Scale),
            (docPoint.Y - l.Position.Y) / Math.Max(1e-6f, l.Scale));

    /// <summary>Marquee selection (document px) converted to layer-pixel space for clip.</summary>
    SKRect? SelectionLayerRect(Layer l)
    {
        if (doc.Selection is not SKRect r) return null;
        float s = Math.Max(1e-6f, l.Scale);
        return new SKRect((r.Left - l.Position.X) / s, (r.Top - l.Position.Y) / s,
                          (r.Right - l.Position.X) / s, (r.Bottom - l.Position.Y) / s);
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
        if (currentTool == Tool.Brush || currentTool == Tool.Eraser)
        {
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var l = doc.Layers[i];
                if (!l.Visible || l.Locked || l.Bitmap == null) continue;
                if (!l.HitTest(docPoint) || !InsideSelection(docPoint)) continue;
                undoStack.Push(doc);   // stroke単位で1エントリ（Moved中は追加pushしない）
                strokePoints = new List<SKPoint> { ToLayerPixel(l, docPoint) };
                dragLayer = l;
                Document.PaintStroke(l, strokePoints, BrushPaintColor(),
                    (float)(BrushSizeSlider?.Value ?? 24), currentTool == Tool.Eraser, 1f, SelectionLayerRect(l));
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

    SKPoint CanvasPoint(PointerEventArgs e, Visual visual)
    {
        var p = e.GetPosition(visual);
        return new SKPoint((float)(p.X / zoom), (float)(p.Y / zoom));
    }

    void OnCanvasPointerMoved(object s, PointerEventArgs e)
    {
        var docPoint = CanvasPoint(e, (Control)s);
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
            if (!props.IsLeftButtonPressed) { strokePoints = null; dragLayer = null; return; }
            docPoint = CanvasPoint(e, (Control)s);
            if (!InsideSelection(docPoint)) return;
            var lp = ToLayerPixel(dragLayer, docPoint);
            strokePoints.Add(lp);
            // 直近セグメントのみ描画（COW済みのため追加push不要）。選択枠で切り抜き。
            using (var canvas = new SKCanvas(dragLayer.Bitmap))
            {
                var clip = SelectionLayerRect(dragLayer);
                bool clipped = clip is SKRect c && c.Width > 0 && c.Height > 0;
                if (clipped) { canvas.Save(); canvas.ClipRect(clip.Value); }
                try
                {
            using (var paint = new SKPaint
            {
                Color = currentTool == Tool.Eraser ? SKColors.Transparent : BrushPaintColor(),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)(BrushSizeSlider?.Value ?? 24),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
                BlendMode = currentTool == Tool.Eraser ? SKBlendMode.Clear : SKBlendMode.SrcOver,
            })
            {
                canvas.DrawLine(strokePoints[^2], strokePoints[^1], paint);
            }
                }
                finally { if (clipped) canvas.Restore(); }
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
        strokePoints = null;
        dragLayer = null;
        e.Pointer.Capture(null);
    }
}
