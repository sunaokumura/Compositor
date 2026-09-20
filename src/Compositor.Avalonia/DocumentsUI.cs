// Compositor — document/color/JPEG UI wiring
// (Mac ProjectWorkspace/ProjectTabs/NewCanvasSheet/JPEGExportSheet/ColorPickerSheet/ColorPaletteControls subset).
// Partial class: engine logic lives in ProjectFormat.cs / JpegExport.cs / PaletteState.cs / ImageImport.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Compositor;

public partial class MainWindow
{
    PaletteState palette = new();
    JpegOptions jpegOpts = new() { Quality = 0.85 };
    bool updatingPalette;   // SyncPalettePanel rebuild中の SelectionChanged 再入ガード（ListBox.Clear/Add時の発火対策）

    // ---------- tab strip + document title (Mac ProjectTabs subset) ----------

    void RefreshDocTabs()
    {
        if (DocTabs == null) return;
        updatingTabs = true;
        try
        {
            while (DocTabs.Items.Count < openDocs.Count)
                DocTabs.Items.Add(new TabItem { Header = "" });
            while (DocTabs.Items.Count > openDocs.Count)
                DocTabs.Items.RemoveAt(DocTabs.Items.Count - 1);
            for (int i = 0; i < openDocs.Count; i++)
            {
                string mark = openDocs[i].IsModified ? " •" : "";
                ((TabItem)DocTabs.Items[i]).Header = openDocs[i].Name + mark;
            }
            int cur = openDocs.IndexOf(doc);
            if (cur >= 0 && DocTabs.SelectedIndex != cur) DocTabs.SelectedIndex = cur;
            if (DocNameBox != null && DocNameBox.Text != doc.Name) DocNameBox.Text = doc.Name;
            if (StatusDoc != null)
                StatusDoc.Text = $"{doc.Name} · {doc.Width} x {doc.Height} px · {doc.Resolution:F0}dpi";
        }
        finally { updatingTabs = false; }
        SyncPalettePanel();
    }

    void OnRenameDoc(object s, RoutedEventArgs e)
    {
        var name = (DocNameBox?.Text ?? "").Trim();
        if (name.Length == 0 || name == doc.Name) return;
        undoStack.Push(doc);
        doc.Name = name.Length > 128 ? name[..128] : name;
        Log($"RenameDoc '{doc.Name}'");
        RefreshDocTabs();
        RenderCanvas();
    }

    // ---------- save / open (Mac ProjectController save/open subset) ----------

    async System.Threading.Tasks.Task<bool> ConfirmDiscard()
    {
        if (!doc.IsModified || doc.Layers.Count == 0) return true;
        var w = new ConfirmWindow($"Save changes to {doc.Name}?", "Your changes will be lost if you don't save them.");
        var choice = await w.ShowDialog<ConfirmWindow.Choice>(this);
        if (choice == ConfirmWindow.Choice.Cancel) return false;
        if (choice == ConfirmWindow.Choice.Save) return await SaveAsync(asNew: false);
        return true;
    }

    async void OnSave(object s, RoutedEventArgs e) => await SaveAsync(asNew: false);
    async void OnSaveAs(object s, RoutedEventArgs e) => await SaveAsync(asNew: true);

    async System.Threading.Tasks.Task<bool> SaveAsync(bool asNew)
    {
        try
        {
            string dest = (!asNew && !string.IsNullOrEmpty(doc.ProjectPath)) ? doc.ProjectPath : null;
            if (dest == null)
            {
                var dlg = new SaveFileDialog
                {
                    DefaultExtension = "comp",
                    InitialFileName = (string.IsNullOrWhiteSpace(doc.Name) ? "Untitled" : doc.Name) + ".comp",
                };
                dest = await dlg.ShowAsync(this);
                if (string.IsNullOrEmpty(dest)) return false;
                if (!dest.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) dest += ".comp";
            }
            doc.ActiveLayerId = Selected?.Id;
            ProjectFormat.Save(doc, dest);
            doc.Name = Path.GetFileNameWithoutExtension(dest);
            Log($"Saved project '{dest}' layers={doc.Layers.Count}");
            RefreshDocTabs();
            await new MessageWindow("保存しました: " + dest).ShowDialog(this);
            return true;
        }
        catch (Exception ex)
        {
            Log("Save ERROR: " + ex);
            await new MessageWindow("保存に失敗: " + ex.Message).ShowDialog(this);
            return false;
        }
    }

    async void OnOpenProject(object s, RoutedEventArgs e)
    {
        try
        {
            if (!await ConfirmDiscard()) return;
            var dlg = new OpenFolderDialog { Title = "Open Project (.comp package)" };
            var dir = await dlg.ShowAsync(this);
            if (string.IsNullOrEmpty(dir)) return;
            var loaded = ProjectFormat.Load(dir);
            ReplaceDoc(loaded, loaded.Name);
            Log($"Opened project '{dir}' layers={loaded.Layers.Count}");
        }
        catch (Exception ex)
        {
            Log("Open ERROR: " + ex);
            await new MessageWindow("開けませんでした: " + ex.Message).ShowDialog(this);
        }
    }

    void ReplaceDoc(Document d, string tabName)
    {
        int idx = openDocs.IndexOf(doc);
        docStacks.Remove(doc);
        d.Name = tabName;
        if (idx >= 0) openDocs[idx] = d; else openDocs.Add(d);
        var st = new UndoStack();
        st.Changed += UpdateUndoButtons;
        docStacks[d] = st;
        doc = d; undoStack = st;
        doc.Changed += RefreshAll;
        RefreshAll();
        RefreshDocTabs();
    }

    // ---------- New Canvas (Mac NewCanvasSheet subset: size + background + open) ----------

    async void OnNewCanvas(object s, RoutedEventArgs e)
    {
        try
        {
            if (!await ConfirmDiscard()) return;
            var w = new NewCanvasWindow();
            var res = await w.ShowDialog<NewCanvasWindow.Result>(this);
            if (res == null) return;
            var d = new Document { Width = res.Width, Height = res.Height, Name = $"Untitled {openDocs.Count + 1}" };
            if (res.Background != SKColors.Transparent)
            {
                var bg = new SKBitmap(res.Width, res.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                bg.Erase(res.Background);
                d.Layers.Add(new Layer { Name = "Background", Bitmap = bg });
            }
            ReplaceDoc(d, d.Name);
            Log($"NewCanvas {res.Width}x{res.Height} bg={res.Background}");
        }
        catch (Exception ex)
        {
            Log("NewCanvas ERROR: " + ex);
            await new MessageWindow("作成に失敗: " + ex.Message).ShowDialog(this);
        }
    }

    // ---------- JPEG export (Mac JPEGExportSheet subset) + Copy Merged ----------

    async void OnExportJpeg(object s, RoutedEventArgs e)
    {
        try
        {
            if (doc.Layers.Count == 0) { await new MessageWindow("レイヤーがありません。").ShowDialog(this); return; }
            var w = new JpegExportWindow(doc, jpegOpts.Clone());
            var res = await w.ShowDialog<JpegExportWindow.Result>(this);
            if (res == null) return;
            jpegOpts = res.Options.Clone();
            var dlg = new SaveFileDialog { DefaultExtension = "jpg", InitialFileName = doc.Name + ".jpg" };
            var path = await dlg.ShowAsync(this);
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllBytes(path, res.Bytes);
            Log($"ExportJPEG q={res.Options.Quality:F2} {res.Bytes.Length}B -> {path}");
            await new MessageWindow("書き出しました: " + path).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log("ExportJPEG ERROR: " + ex);
            await new MessageWindow("JPEG出力に失敗: " + ex.Message).ShowDialog(this);
        }
    }

    async System.Threading.Tasks.Task DoCopyMerged()
    {
        try
        {
            var bytes = JpegExport.CopyMergedPng(doc);
            var cl = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cl == null) return;
            var dObj = new DataObject();
            dObj.Set(ClipboardPngFormat, bytes);
            await cl.SetDataObjectAsync(dObj);
            Log($"CopyMerged {doc.Width}x{doc.Height} ({bytes.Length}B) -> clipboard");
        }
        catch (Exception ex) { Log("CopyMerged ERROR: " + ex); }
    }
    async void OnCopyMerged(object s, RoutedEventArgs e) => await DoCopyMerged();

    // ---------- color: foreground/background + palette + picker (Mac ColorPaletteControls subset) ----------

    void SyncPalettePanel()
    {
        if (updatingPalette) return;
        updatingPalette = true;
        try
        {
            var sel = Selected;
            palette.MaskSelected = sel?.MaskSelected == true && MaskOps.HasMask(sel);
            if (FgBtn != null)
            {
                var c = palette.PaintColor(false).ToSK();
                FgBtn.Background = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
                FgBtn.Content = palette.MaskSelected ? (palette.MaskPaintWhite ? "W" : "B") : "FG";
            }
            if (BgBtn != null)
            {
                var c = palette.PaintColor(true).ToSK();
                BgBtn.Background = new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
                BgBtn.Content = palette.MaskSelected ? "M" : "BG";
            }
            if (PaletteBox != null)
            {
                int keep = PaletteBox.SelectedIndex;
                PaletteBox.Items.Clear();
                foreach (var sw in palette.Swatches)
                    PaletteBox.Items.Add($"#{ProfilePreviewOps.Preview(sw, previewProfile).Hex}");   // P0 profile preview (表示のみ)
                if (PaletteBox.ItemCount > 0)
                {
                    int want = Math.Clamp(keep, 0, PaletteBox.ItemCount - 1);
                    if (PaletteBox.SelectedIndex != want) PaletteBox.SelectedIndex = want;
                }
            }
        }
        catch (Exception ex) { Log("SyncPalette ERROR: " + ex); }
        finally { updatingPalette = false; }
    }

    void OnFgPick(object s, RoutedEventArgs e) => _ = PickColorAsync(background: false);
    void OnBgPick(object s, RoutedEventArgs e) => _ = PickColorAsync(background: true);
    void OnSwapColors(object s, RoutedEventArgs e) { palette.Swap(); SyncPalettePanel(); RefreshToolHeader(); }
    void OnResetColors(object s, RoutedEventArgs e) { palette.Reset(); SyncPalettePanel(); RefreshToolHeader(); }

    async System.Threading.Tasks.Task PickColorAsync(bool background)
    {
        try
        {
            if (palette.MaskSelected)
            {
                // Mask target: choose reveal-white vs hide-black (Mac mask popover subset).
                var w = new ConfirmWindow(background ? "Mask background" : "Mask foreground",
                    "Black hides, white reveals.", "Black · Hide", "White · Reveal");
                var ch = await w.ShowDialog<ConfirmWindow.Choice>(this);
                palette.Set(ch == ConfirmWindow.Choice.Save ? PaletteColor.White : PaletteColor.Black, background);
                SyncPalettePanel();
                return;
            }
            var win = new ColorPickerWindow(palette.Current(background), doc);
            var chosen = await win.ShowDialog<PaletteColor?>(this);
            if (chosen != null)
            {
                palette.Set(chosen.Value.Quantized, background);
                Log($"Palette {(background ? "BG" : "FG")} #{chosen.Value.Hex}");
                SyncPalettePanel();
                RefreshToolHeader();
            }
        }
        catch (Exception ex) { Log("PickColor ERROR: " + ex); }
    }

    void OnPaletteSelected(object s, SelectionChangedEventArgs e)
    {
        if (updatingPalette) return;
        if (PaletteBox == null || PaletteBox.SelectedIndex < 0 || PaletteBox.SelectedIndex >= palette.Swatches.Count) return;
        palette.Set(palette.Swatches[PaletteBox.SelectedIndex], background: false);
        SyncPalettePanel();
    }

    void OnPaletteAdd(object s, RoutedEventArgs e)
    {
        if (palette.AddSwatch(palette.Foreground)) SyncPalettePanel();
        else Log("Palette full or duplicate");
    }

    void OnPaletteDelete(object s, RoutedEventArgs e)
    {
        if (PaletteBox != null && palette.RemoveSwatchAt(PaletteBox.SelectedIndex)) SyncPalettePanel();
    }

    // ---------- import helper (extended formats incl. HEIC/TIFF probe) ----------

    void ImportDecoded(SKBitmap src, string name)
    {
        long remaining = ImageImport.MaxPixels - ImageImport.DocumentPixels(doc);
        if ((long)src.Width * src.Height > remaining)
        {
            src.Dispose();
            _ = new MessageWindow("100メガピクセルの予算を超えます。").ShowDialog(this);
            return;
        }
        AddLayerBitmap(src, name);
    }
}

#region Dialog windows (code-built, no XAML)

/// <summary>Save / Don't Save / Cancel (Mac confirmReplacement subset).</summary>
public class ConfirmWindow : Window
{
    public enum Choice { Cancel, Save, DontSave }
    public ConfirmWindow(string title, string body, string saveLabel = "Save", string dontLabel = "Don't Save")
    {
        Title = title; Width = 420; Height = 180; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var bSave = new Button { Content = saveLabel }; bSave.Click += (_, _) => Close(Choice.Save);
        var bDont = new Button { Content = dontLabel }; bDont.Click += (_, _) => Close(Choice.DontSave);
        var bCancel = new Button { Content = "Cancel" }; bCancel.Click += (_, _) => Close(Choice.Cancel);
        row.Children.Add(bSave); row.Children.Add(bDont); row.Children.Add(bCancel);
        panel.Children.Add(row);
        Content = panel;
    }
}

/// <summary>New canvas: dimensions 1..30000 + background (Mac NewCanvasSheet subset).</summary>
public class NewCanvasWindow : Window
{
    public class Result { public int Width, Height; public SKColor Background; }
    TextBox wBox, hBox; ComboBox bgBox; TextBlock hint;
    public NewCanvasWindow()
    {
        Title = "New canvas"; Width = 380; Height = 280; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "New canvas", FontWeight = FontWeight.SemiBold, FontSize = 16 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        wBox = new TextBox { Text = "1920", Width = 100 }; hBox = new TextBox { Text = "1080", Width = 100 };
        row.Children.Add(new TextBlock { Text = "W", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(wBox);
        row.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(hBox);
        row.Children.Add(new TextBlock { Text = "px", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(row);
        var bgRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bgRow.Children.Add(new TextBlock { Text = "Background", VerticalAlignment = VerticalAlignment.Center });
        bgBox = new ComboBox { Width = 150, SelectedIndex = 0 };
        bgBox.Items.Add("Transparent"); bgBox.Items.Add("White"); bgBox.Items.Add("Black");
        bgRow.Children.Add(bgBox);
        panel.Children.Add(bgRow);
        hint = new TextBlock { Text = "Transparent canvas · sRGB", Foreground = Brushes.Gray };
        panel.Children.Add(hint);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Create canvas" }; ok.Click += (_, _) => TryCreate();
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(null);
        btns.Children.Add(cancel); btns.Children.Add(ok);
        panel.Children.Add(btns);
        Content = panel;
    }
    void TryCreate()
    {
        var w = Document.ValidDimension(wBox.Text); var h = Document.ValidDimension(hBox.Text);
        if (w == null || h == null) { hint.Text = "Enter whole numbers from 1 to 30,000 pixels."; return; }
        var bg = bgBox.SelectedIndex == 1 ? SKColors.White : bgBox.SelectedIndex == 2 ? SKColors.Black : SKColors.Transparent;
        Close(new Result { Width = w.Value, Height = h.Value, Background = bg });
    }
}

/// <summary>JPEG export: quality slider + matte + live encoded size + fitted preview (Mac JPEGExportSheet subset).</summary>
public class JpegExportWindow : Window
{
    public class Result { public byte[] Bytes; public JpegOptions Options; }
    readonly Document doc; readonly JpegOptions opts;
    Slider qSlider; ComboBox matteBox; TextBlock info, sizeLabel; Image preview; ProgressBar busy;
    System.Threading.CancellationTokenSource cts;
    public JpegExportWindow(Document doc, JpegOptions opts)
    {
        this.doc = doc; this.opts = opts;
        Title = "Export JPEG"; Width = 620; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Export JPEG", FontWeight = FontWeight.SemiBold, FontSize = 16 });
        preview = new Image { Width = 560, Height = 300, Stretch = Stretch.Uniform };
        panel.Children.Add(preview);
        var qRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        qRow.Children.Add(new TextBlock { Text = "Quality", VerticalAlignment = VerticalAlignment.Center });
        qSlider = new Slider { Minimum = 1, Maximum = 100, Value = opts.Quality * 100, Width = 380 };
        qSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) OnOptsChanged(); };
        sizeLabel = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        qRow.Children.Add(qSlider); qRow.Children.Add(sizeLabel);
        panel.Children.Add(qRow);
        var mRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        mRow.Children.Add(new TextBlock { Text = "Background for transparency", VerticalAlignment = VerticalAlignment.Center });
        matteBox = new ComboBox { Width = 130, SelectedIndex = 0 };
        matteBox.Items.Add("White"); matteBox.Items.Add("Black"); matteBox.Items.Add("Gray");
        matteBox.SelectionChanged += (_, _) => OnOptsChanged();
        mRow.Children.Add(matteBox);
        panel.Children.Add(mRow);
        info = new TextBlock { Text = $"{doc.Width} × {doc.Height} px · sRGB", Foreground = Brushes.Gray };
        panel.Children.Add(info);
        busy = new ProgressBar { IsIndeterminate = true, IsVisible = false, Height = 4 };
        panel.Children.Add(busy);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => { cts?.Cancel(); Close(null); };
        var ok = new Button { Content = "Export…" }; ok.Click += (_, _) => DoExport();
        btns.Children.Add(cancel); btns.Children.Add(ok);
        panel.Children.Add(btns);
        Content = panel;
        OnOptsChanged();
    }
    void ReadOpts()
    {
        opts.Quality = Math.Clamp(qSlider.Value / 100, 0.01, 1);
        (opts.MatteR, opts.MatteG, opts.MatteB) = matteBox.SelectedIndex == 1 ? ((byte)0, (byte)0, (byte)0)
            : matteBox.SelectedIndex == 2 ? ((byte)128, (byte)128, (byte)128) : ((byte)255, (byte)255, (byte)255);
        sizeLabel.Text = $"{(int)Math.Round(opts.Quality * 100)}%";
    }
    async void OnOptsChanged()
    {
        ReadOpts();
        cts?.Cancel(); cts = new System.Threading.CancellationTokenSource();
        var token = cts.Token; var snapshot = opts.Clone();
        busy.IsVisible = true; info.Text = "Updating preview…";
        try
        {
            await System.Threading.Tasks.Task.Delay(200, token);
            var bytes = await System.Threading.Tasks.Task.Run(() => JpegExport.Export(doc, snapshot), token);
            using var pv = await System.Threading.Tasks.Task.Run(() => JpegExport.DecodePreview(bytes), token);
            token.ThrowIfCancellationRequested();
            var wb = new WriteableBitmap(new PixelSize(pv.Width, pv.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = wb.Lock())
            using (var surf = SKSurface.Create(new SKImageInfo(pv.Width, pv.Height, SKColorType.Bgra8888, SKAlphaType.Premul), fb.Address, fb.RowBytes))
            {
                var canvas = surf.Canvas; canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(pv, 0, 0);
            }
            preview.Source = wb;
            info.Text = $"{doc.Width} × {doc.Height} px · sRGB · {bytes.Length / 1024} KB (encoded preview, fitted)";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { info.Text = "Preview failed: " + ex.Message; }
        finally { if (!token.IsCancellationRequested) busy.IsVisible = false; }
    }
    async void DoExport()
    {
        ReadOpts();
        busy.IsVisible = true;
        try
        {
            var snapshot = opts.Clone();
            var bytes = await System.Threading.Tasks.Task.Run(() => JpegExport.Export(doc, snapshot));
            Close(new Result { Bytes = bytes, Options = snapshot });
        }
        catch (Exception ex) { info.Text = "Export failed: " + ex.Message; busy.IsVisible = false; }
    }
}

/// <summary>HSB picker: hue/sat/bright sliders + RGB + hex + preview + canvas sample (Mac ColorPickerSheet subset).</summary>
public class ColorPickerWindow : Window
{
    PickerHSB hsb; TextBlock hexPreview; TextBox rBox, gBox, bBox, hexBox; Border swatch; bool syncing;
    public ColorPickerWindow(PaletteColor initial, Document doc)
    {
        hsb = new PickerHSB(initial.Quantized);
        Title = "Color Picker"; Width = 400; Height = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Color Picker", FontWeight = FontWeight.SemiBold, FontSize = 16 });
        swatch = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(5) };
        panel.Children.Add(swatch);
        hsbRow(panel, "H", 0, 360, () => hsb.Hue, v => hsb.Hue = v);
        hsbRow(panel, "S", 0, 100, () => hsb.Saturation * 100, v => hsb.Saturation = v / 100);
        hsbRow(panel, "B", 0, 100, () => hsb.Brightness * 100, v => hsb.Brightness = v / 100);
        var rgb = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rBox = numBox(rgb, "R"); gBox = numBox(rgb, "G"); bBox = numBox(rgb, "B");
        panel.Children.Add(rgb);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hexRow.Children.Add(new TextBlock { Text = "#", VerticalAlignment = VerticalAlignment.Center });
        hexBox = new TextBox { Width = 100 }; hexBox.Text = hsb.RGB.Hex;
        hexBox.LostFocus += (_, _) => ApplyHex();
        hexRow.Children.Add(hexBox);
        hexPreview = new TextBlock { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        hexRow.Children.Add(hexPreview);
        panel.Children.Add(hexRow);
        var hint = new TextBlock { Text = "RGB 0-255 · hex RRGGBB / RGB / #RRGGBB", Foreground = Brushes.Gray };
        panel.Children.Add(hint);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK" }; ok.Click += (_, _) => Close(hsb.RGB.Quantized);
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(null);
        btns.Children.Add(cancel); btns.Children.Add(ok);
        panel.Children.Add(btns);
        Content = panel;
        SyncFields();
    }
    TextBox numBox(StackPanel parent, string label)
    {
        parent.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var t = new TextBox { Width = 56 };
        t.LostFocus += (_, _) => ApplyRgb();
        parent.Children.Add(t);
        return t;
    }
    void hsbRow(StackPanel panel, string label, double min, double max, Func<double> get, Action<double> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 14, VerticalAlignment = VerticalAlignment.Center });
        var s = new Slider { Minimum = min, Maximum = max, Value = get(), Width = 260 };
        var v = new TextBlock { Width = 56, VerticalAlignment = VerticalAlignment.Center };
        s.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || syncing) return;
            set(s.Value); v.Text = $"{s.Value:F0}"; SyncFields(except: s);
        };
        v.Text = $"{s.Value:F0}";
        row.Children.Add(s); row.Children.Add(v);
        panel.Children.Add(row);
    }
    void SyncFields(Slider except = null)
    {
        syncing = true;
        try
        {
            var c = hsb.RGB.Quantized;
            rBox.Text = $"{(int)Math.Round(c.R * 255)}";
            gBox.Text = $"{(int)Math.Round(c.G * 255)}";
            bBox.Text = $"{(int)Math.Round(c.B * 255)}";
            hexBox.Text = c.Hex;
            hexPreview.Text = $"#{c.Hex}";
            var sk = c.ToSK();
            swatch.Background = new SolidColorBrush(Color.FromRgb(sk.Red, sk.Green, sk.Blue));
            // Refresh sliders not being dragged.
            if (Content is StackPanel panel)
                foreach (var row in panel.Children.OfType<StackPanel>())
                    foreach (var s in row.Children.OfType<Slider>())
                        if (s != except)
                        {
                            var lbl = (row.Children[0] as TextBlock)?.Text;
                            s.Value = lbl == "H" ? hsb.Hue : lbl == "S" ? hsb.Saturation * 100 : hsb.Brightness * 100;
                        }
        }
        finally { syncing = false; }
    }
    void ApplyRgb()
    {
        if (syncing) return;
        if (int.TryParse(rBox.Text, out int r) && int.TryParse(gBox.Text, out int g) && int.TryParse(bBox.Text, out int b))
        {
            r = Math.Clamp(r, 0, 255); g = Math.Clamp(g, 0, 255); b = Math.Clamp(b, 0, 255);
            var keep = hsb;
            hsb.SetRGB(new PaletteColor(r / 255.0, g / 255.0, b / 255.0));
            // Keep hue/sat survival semantics of SetRGB (grays keep hue, black keeps sat).
            SyncFields();
        }
        else SyncFields();
    }
    void ApplyHex()
    {
        var parsed = PaletteColor.FromHex(hexBox.Text);
        if (parsed != null) { hsb.SetRGB(parsed.Value); }
        SyncFields();
    }
}
#endregion
