using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
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
    Bitmap renderedBitmap;   // Avalonia bitmap currently shown (disposed on replace)

    bool uiReady;
    bool updatingList;
    int refreshDepth;   // RefreshAll再入検出用

    public MainWindow()
    {
        InitializeComponent();
        uiReady = true;
        doc.Changed += RefreshAll;
        RefreshAll();
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
        }
        RenderCanvas();
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
        int w = (int)Math.Ceiling(doc.Width * zoom);
        int h = (int)Math.Ceiling(doc.Height * zoom);
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            // checkerboard backdrop
            const int c = 16;
            for (int y = 0; y < h; y += c)
                for (int x = 0; x < w; x += c)
                    using (var p = new SKPaint { Color = ((x / c + y / c) % 2 == 0) ? new SKColor(230, 230, 230) : new SKColor(200, 200, 204) })
                        canvas.DrawRect(x, y, Math.Min(c, w - x), Math.Min(c, h - y), p);
            doc.Draw(canvas, zoom);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var newBmp = new Bitmap(new MemoryStream(data.ToArray()));
        renderedBitmap?.Dispose();
        renderedBitmap = newBmp;
        CanvasImage.Source = newBmp;
        bmp.Dispose();
        ZoomLabel.Text = $"{zoom:P0}";
    }

    // ---------- tools / zoom ----------

    void OnToolMove(object s, RoutedEventArgs e) { }
    void OnToolHand(object s, RoutedEventArgs e) { }
    void OnZoomIn(object s, RoutedEventArgs e) { zoom = Math.Min(4f, zoom * 1.5f); RenderCanvas(); }
    void OnZoomOut(object s, RoutedEventArgs e) { zoom = Math.Max(0.1f, zoom / 1.5f); RenderCanvas(); }

    // ---------- layer ops ----------

    void OnNew(object s, RoutedEventArgs e)
    {
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
        doc.Layers.Add(new Layer { Name = System.IO.Path.GetFileNameWithoutExtension(files[0]), Bitmap = src });
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
        doc.Layers.Add(new Layer { Name = $"Layer {doc.Layers.Count + 1}", Bitmap = EmptyLayer() });
        RefreshAll();
        SafeSelect(LayerList.ItemCount - 1);
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
        doc.Layers.Remove(l);
        RefreshAll();
    }

    void OnUp(object s, RoutedEventArgs e)   // move toward top of stack
    {
        int li = LayerList.SelectedIndex;
        int i = doc.Layers.Count - 1 - li;
        int k = i + 1;                          // higher = closer to top
        if (li < 0 || k >= doc.Layers.Count) return;
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
        if (Selected is not { } l) return;
        l.Opacity = (float)(OpacitySlider.Value / 100);
        OpacityLabel.Text = $"{OpacitySlider.Value:F0}%";
        RefreshLayerList();
        RenderCanvas();
    }

    void OnScaleChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        l.Scale = (float)(ScaleSlider.Value / 100);
        RefreshLayerList();
        RenderCanvas();
    }

    void OnVisibleChanged(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) return;
        l.Visible = VisibleCheck.IsChecked == true;
        RefreshLayerList();
        RenderCanvas();
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
        dragLayer = null;
        e.Pointer.Capture(null);
    }
}
