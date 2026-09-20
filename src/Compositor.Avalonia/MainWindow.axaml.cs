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

    readonly UndoStack undoStack = new();

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

        OpacitySlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        ScaleSlider.AddHandler(PointerPressedEvent, (s, e) => { sliderArmed = false; }, RoutingStrategies.Tunnel);
        OpacitySlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        ScaleSlider.AddHandler(PointerReleasedEvent, (s, e) => sliderArmed = false, RoutingStrategies.Direct);
        undoStack.Changed += UpdateUndoButtons;

        doc.Changed += RefreshAll;
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
            OpacitySlider.Value = sel.Opacity * 100;
            ScaleSlider.Value = sel.Scale * 100;
            VisibleCheck.IsChecked = sel.Visible;
            int idx = Array.FindIndex(BlendModes, b => b.Mode == sel.Blend);
            updatingList = true;
            try { BlendBox.SelectedIndex = idx < 0 ? 0 : idx; } finally { updatingList = false; }
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
            doc.Draw(canvas, zoom);
            surface.Flush();
        }
        renderedBitmap?.Dispose();
        renderedBitmap = wb;
        CanvasImage.Source = wb;
        ZoomLabel.Text = $"{zoom:P0}";
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100) Log($"RenderCanvas {w}x{h} took {sw.ElapsedMilliseconds}ms");
    }

    static void DrawCheckerboard(SKCanvas canvas, int w, int h)
    {
        const int c = 16;
        using var light = new SKPaint { Color = new SKColor(230, 230, 230) };
        using var dark = new SKPaint { Color = new SKColor(200, 200, 204) };
        for (int y = 0; y < h; y += c)
            for (int x = 0; x < w; x += c)
                canvas.DrawRect(x, y, Math.Min(c, w - x), Math.Min(c, h - y),
                    ((x / c + y / c) % 2 == 0) ? light : dark);
    }

    // ---------- tools / zoom ----------

    void OnToolMove(object s, RoutedEventArgs e) { }
    void OnToolHand(object s, RoutedEventArgs e) { }
    void OnZoomIn(object s, RoutedEventArgs e) { zoom = Math.Min(4f, zoom * 1.5f); RenderCanvas(); }
    void OnZoomOut(object s, RoutedEventArgs e) { zoom = Math.Max(0.1f, zoom / 1.5f); RenderCanvas(); }

    // ---------- layer ops ----------

    void OnNew(object s, RoutedEventArgs e)
    {
        undoStack.Push(doc);
        doc = new Document { Width = 800, Height = 600 };
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

    void AddLayerBitmap(SKBitmap src, string name)
    {
        undoStack.Push(doc);
        doc.Layers.Add(new Layer { Name = name, Bitmap = src });
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
        if (Selected is not { } l || !uiReady) return;
        if (!sliderArmed) { undoStack.Push(doc); sliderArmed = true; }
        l.Opacity = (float)(OpacitySlider.Value / 100);
        OpacityLabel.Text = $"{OpacitySlider.Value:F0}%";
        RefreshLayerList();
        RenderCanvas();
    }

    void OnScaleChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l || !uiReady) return;
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

    // ---------- clipboard ----------

    const string ClipboardPngFormat = "image/png";

    async void OnCopy(object s, RoutedEventArgs e) => await DoCopy();
    async void OnPaste(object s, RoutedEventArgs e) => await DoPaste();

    async System.Threading.Tasks.Task DoCopy()
    {
        if (Selected is not { Bitmap: not null } l) { Log("Copy: no selected layer"); return; }
        try
        {
            using var img = SKImage.FromBitmap(l.Bitmap);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = data.ToArray();
            var cl = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cl == null) return;
            var dObj = new DataObject();
            dObj.Set(ClipboardPngFormat, bytes);
            await cl.SetDataObjectAsync(dObj);
            Log($"Copy: layer '{l.Name}' {l.Bitmap.Width}x{l.Bitmap.Height} -> clipboard PNG ({bytes.Length}B)");
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
            AddLayerBitmap(src, $"Pasted {DateTime.Now:HHmmss}");
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
            case Key.V when mods == KeyModifiers.None: OnToolMove(null, null); e.Handled = true; break;
            case Key.H when mods == KeyModifiers.None: OnToolHand(null, null); e.Handled = true; break;
            case Key.Delete: OnDeleteLayer(null, null); e.Handled = true; break;
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

    // ---------- canvas interaction (Move tool) ----------

    void OnCanvasPointerPressed(object s, PointerPressedEventArgs e)
    {
        Log("OnCanvasPointerPressed");

        var props = e.GetCurrentPoint((Control)s).Properties;
        if (!props.IsLeftButtonPressed) return;
        var p = CanvasPoint(e, (Control)s);
        for (int i = doc.Layers.Count - 1; i >= 0; i--)
        {
            var l = doc.Layers[i];
            if (l.Visible && !l.Locked && l.HitTest(p))
            {
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
        if (dragLayer == null) return;
        Log("OnCanvasPointerMoved dragging");

        var props = e.GetCurrentPoint((Control)s).Properties;
        if (!props.IsLeftButtonPressed) { dragLayer = null; return; }
        var now = e.GetPosition((Visual)s);
        dragLayer.Position = new SKPoint(
            dragLayerPos.X + (float)((now.X - dragStart.X) / zoom),
            dragLayerPos.Y + (float)((now.Y - dragStart.Y) / zoom));
        RenderCanvas();
        RefreshLayerList();
    }

    void OnCanvasPointerReleased(object s, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (dragLayer != null) { undoStack.Push(doc); sliderArmed = false; }
        dragLayer = null;
        e.Pointer.Capture(null);
    }
}
