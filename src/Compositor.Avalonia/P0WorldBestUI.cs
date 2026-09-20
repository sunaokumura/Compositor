// Compositor — P0 UI 配線 (docs/WORLD_BEST_PAINT.md §4 P0).
// 調整 sheet 対話 / Solo / 複数選択 UI / canvas 回転 view / ASE・Harmony・
// profile preview / `/` filter 検索。既存 x:Name・操作子名は不変 (追加のみ)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor;

public partial class MainWindow
{
    PreviewProfile previewProfile = PreviewProfile.SRGB;   // P0 profile preview (表示のみ)

    // ---------- 複数選択 UI (Procreate 複数同時の考え・engine は MergeSelectedPlan 済み) ----------

    /// <summary>ListBox の複数選択を Document 順 (bottom-up) の層列にする。</summary>
    List<Layer> SelectedLayers()
    {
        var list = new List<Layer>();
        if (LayerList == null) return list;
        foreach (var item in LayerList.SelectedItems)
        {
            int li = LayerList.Items.IndexOf(item);
            int di = doc.Layers.Count - 1 - li;
            if (di >= 0 && di < doc.Layers.Count) list.Add(doc.Layers[di]);
        }
        list.Sort((a, b) => doc.Layers.IndexOf(a).CompareTo(doc.Layers.IndexOf(b)));
        return list;
    }

    void OnMergeSelected(object s, RoutedEventArgs e)
    {
        var picked = SelectedLayers().Where(l => !l.IsGroup).ToList();
        if (picked.Count < 2) { Log("MergeSelected: 2層以上を選択"); return; }
        var plan = MergeOps.MergeSelectedPlan(doc, picked.Select(l => l.Id).ToList());
        if (plan == null) { Log("MergeSelected: plan なし"); return; }
        undoStack.Push(doc);
        var merged = MergeOps.Execute(doc, plan);
        Log(merged != null ? $"MergeSelected -> '{merged.Name}' ({plan.Ids.Count}層)" : "MergeSelected failed");
        doc.SoloLayerId = null;
        RefreshAll();
    }

    // ---------- Solo 表示 (ibisPaint Solo の考え・表示のみ・Undo対象外) ----------

    void OnSoloChanged(object s, RoutedEventArgs e)
    {
        if (updatingList || SoloCheck == null) return;
        if (SoloCheck.IsChecked == true)
        {
            if (Selected is not { } l) { SoloCheck.IsChecked = false; return; }
            doc.SoloLayerId = l.Id;
            Log($"Solo '{l.Name}'");
        }
        else
        {
            doc.SoloLayerId = null;
            Log("Solo off");
        }
        RefreshLayerList();
        RenderCanvas();
    }

    // ---------- canvas 回転 view (GIMP 作画検証回転の考え・画素不変) ----------

    void OnCanvasAngleChanged(object s, RoutedEventArgs e)
    {
        if (!uiReady || updatingList || CanvasAngleSlider == null) return;
        doc.CanvasAngle = CanvasViewOps.NormalizeAngle((float)CanvasAngleSlider.Value);
        if (CanvasAngleLabel != null) CanvasAngleLabel.Text = $"{doc.CanvasAngle:F0}°";
        ApplyCanvasView();
    }

    void OnCanvasAngleReset(object s, RoutedEventArgs e)
    {
        doc.CanvasAngle = 0;
        if (CanvasAngleSlider != null) CanvasAngleSlider.Value = 0;
        if (CanvasAngleLabel != null) CanvasAngleLabel.Text = "0°";
        ApplyCanvasView();
        Log("CanvasAngle reset");
    }

    void ApplyCanvasView()
    {
        if (CanvasImage == null) return;
        CanvasImage.RenderTransform = doc.CanvasAngle == 0 ? null : new RotateTransform(doc.CanvasAngle);
    }

    // ---------- 調整 sheet 対話 (Procreate 筆塗り調整の UI 側・engine 済み) ----------

    void AddAdjustmentLayer(string name, LayerAdjustment adj)
    {
        if (!adj.IsValid) { Log($"{name}: 無効な設定"); return; }
        undoStack.Push(doc);
        int at = Selected != null ? doc.Layers.IndexOf(Selected) + 1 : doc.Layers.Count;
        doc.Layers.Insert(Math.Clamp(at, 0, doc.Layers.Count),
            new Layer { Name = name, IsAdjustmentLayer = true, Adjustment = adj });
        Log($"{name} 調整層を追加");
        RefreshAll();
    }

    void ApplyDestructive(string name, Action<Layer> apply)
    {
        if (Selected is not { Bitmap: not null } l) { Log($"{name}: 対象層なし"); return; }
        undoStack.Push(doc);
        try { apply(l); Log($"{name} を '{l.Name}' に適用 (破壊)"); }
        catch (Exception ex) { Log($"{name} failed: " + ex.Message); }
        RefreshAll();
    }

    static Slider Row(StackPanel p, string label, double min, double max, double value, out TextBlock val)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        var s = new Slider { Minimum = min, Maximum = max, Width = 170, Value = value };
        val = new TextBlock { Text = $"{value:F0}", VerticalAlignment = VerticalAlignment.Center };
        var mine = val;
        s.ValueChanged += (_, _) => mine.Text = $"{s.Value:F1}";
        row.Children.Add(s); row.Children.Add(mine);
        p.Children.Add(row);
        return s;
    }

    async void OnCurvesDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Curves", Width = 340, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "中点 (入力128の出力) で3点カーブを作る。engine: CurvesSettings (Hermite)。", TextWrapping = TextWrapping.Wrap });
        var chBox = new ComboBox { Width = 200, SelectedIndex = 0 };
        chBox.Items.Add("RGB (master)"); chBox.Items.Add("R"); chBox.Items.Add("G"); chBox.Items.Add("B");
        p.Children.Add(chBox);
        var mid = Row(p, "Mid 出力", 0, 255, 128, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用(破壊)" };
        var bLayer = new Button { Content = "調整層として追加" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bApply); row.Children.Add(bLayer); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        CurvesSettings Build()
        {
            var cs = new CurvesSettings();
            int ch = chBox.SelectedIndex;   // 0=master(RGB) 1..3=R/G/B
            var pts = new List<CurvePoint> { new(0, 0), new(128, Math.Clamp(mid.Value, 0, 255)), new(255, 255) };
            cs.Channels[ch == 0 ? 0 : ch] = pts;
            return cs;
        }
        bApply.Click += (_, _) => { var cs = Build(); dlg.Close(); ApplyDestructive("Curves", l => LayerFilters.ApplyCurves(l, cs)); };
        bLayer.Click += (_, _) => { var cs = Build(); dlg.Close(); AddAdjustmentLayer("Curves 調整", new LayerAdjustment { Kind = AdjustmentKind.Curves, Curves = cs }); };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnLevelsDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Levels", Width = 340, Height = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "RGB master の Black/Gamma/White。engine: LevelsOps。", TextWrapping = TextWrapping.Wrap });
        var black = Row(p, "Black", 0, 254, 0, out _);
        var gamma = Row(p, "Gamma", 0.1, 9.99, 1, out _);
        var white = Row(p, "White", 1, 255, 255, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用(破壊)" };
        var bLayer = new Button { Content = "調整層として追加" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bApply); row.Children.Add(bLayer); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        LevelsSettings Build()
        {
            var ls = new LevelsSettings();
            ls.Ranges[0] = new LevelRange(black.Value, gamma.Value, white.Value, 0, 255).Normalized;
            return ls;
        }
        bApply.Click += (_, _) => { var ls = Build(); dlg.Close(); ApplyDestructive("Levels", l => LayerFilters.ApplyLevels(l, ls)); };
        bLayer.Click += (_, _) => { var ls = Build(); dlg.Close(); AddAdjustmentLayer("Levels 調整", new LayerAdjustment { Kind = AdjustmentKind.Levels, Levels = ls }); };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnHueDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Hue-Saturation", Width = 340, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Master 全域の Hue/Sat/Light。engine: HueOps。", TextWrapping = TextWrapping.Wrap });
        var hue = Row(p, "Hue", -180, 180, 0, out _);
        var sat = Row(p, "Saturation", -100, 100, 0, out _);
        var light = Row(p, "Lightness", -100, 100, 0, out _);
        var colorize = new CheckBox { Content = "Colorize" };
        p.Children.Add(colorize);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用(破壊)" };
        var bLayer = new Button { Content = "調整層として追加" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bApply); row.Children.Add(bLayer); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        HueSaturationSettings Build() => new(hue.Value, sat.Value, light.Value, colorize.IsChecked == true);
        bApply.Click += (_, _) => { var hs = Build(); dlg.Close(); ApplyDestructive("Hue", l => LayerFilters.ApplyHueSaturation(l, hs)); };
        bLayer.Click += (_, _) => { var hs = Build(); dlg.Close(); AddAdjustmentLayer("Hue 調整", new LayerAdjustment { Kind = AdjustmentKind.Hsv, HueSat = hs }); };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnFilterDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Filter", Width = 340, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Grain/Noise/Lens/Blur (破壊適用)。engine: Adjustments.cs。", TextWrapping = TextWrapping.Wrap });
        var kindBox = new ComboBox { Width = 200, SelectedIndex = 0 };
        kindBox.Items.Add("Grain"); kindBox.Items.Add("Noise"); kindBox.Items.Add("Lens補正");
        kindBox.Items.Add("Gaussian Blur"); kindBox.Items.Add("Motion Blur");
        p.Children.Add(kindBox);
        var amount = Row(p, "Amount", 0, 100, 25, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用(破壊)" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bApply); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            int kind = kindBox.SelectedIndex;
            double v = amount.Value;
            dlg.Close();
            ApplyDestructive("Filter", l =>
            {
                switch (kind)
                {
                    case 0: LayerFilters.ApplyGrain(l, new GrainSettings { Amount = v }); break;
                    case 1: LayerFilters.ApplyNoise(l, new NoiseSettings { Amount = v * 4 }); break;
                    case 2: LayerFilters.ApplyLens(l, v - 50); break;
                    case 3: LayerFilters.ApplyGaussianExpand(l, Math.Max(0.5, v / 10)); break;
                    default: LayerFilters.ApplyMotionExpand(l, 0, Math.Max(1, v / 5)); break;
                }
            });
        };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- `/` filter 検索 (GIMP `/` の考え) ----------

    void OnFilterSearch(object s, TextChangedEventArgs e)
    {
        if (AdjustButtonsPanel == null) return;
        var hits = new HashSet<string>(FilterCatalog.Search(FilterSearchBox?.Text ?? "").Select(x => x.Tag));
        var map = new Dictionary<string, string>
        {
            ["CurvesDlgBtn"] = "curves", ["LevelsDlgBtn"] = "levels",
            ["HueDlgBtn"] = "hue", ["FilterDlgBtn"] = "grain",
        };
        // Filter 系の語 (grain/noise/lens/blur) では Filter... を残す
        var filterTags = new HashSet<string> { "grain", "noise", "lens", "blur", "motionblur" };
        foreach (var child in AdjustButtonsPanel.Children)
        {
            if (child is not Button b || b.Name == null || !map.TryGetValue(b.Name, out string tag)) continue;
            bool show = hits.Any(h => tag.Contains(h) || h.Contains(tag));
            if (tag == "grain" && !show) show = hits.Any(filterTags.Contains);
            b.IsVisible = show;
        }
    }

    // ---------- ASE 読込・Harmony 盤・profile preview (色の P0) ----------

    async void OnAseLoad(object s, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog { Filters = { new FileDialogFilter { Name = "Adobe Swatch", Extensions = { "ase" } } } };
            var files = await dlg.ShowAsync(this);
            if (files == null || files.Length == 0) return;
            var colors = AseReader.Read(await File.ReadAllBytesAsync(files[0]));
            int added = 0;
            foreach (var c in colors) if (palette.AddSwatch(c.Quantized)) added++;
            SyncPalettePanel();
            Log($"ASE '{Path.GetFileName(files[0])}' {colors.Count}色中 {added}色を追加");
            if (added == 0) await new MessageWindow("ASE の読込: 追加できる新しい色がありませんでした。").ShowDialog(this);
        }
        catch (Exception ex) { Log("ASE load ERROR: " + ex.Message); await new MessageWindow("ASE の読込に失敗: " + ex.Message).ShowDialog(this); }
    }

    void OnHarmonyApply(object s, RoutedEventArgs e)
    {
        var kind = (HarmonyBox?.SelectedIndex ?? 0) switch
        {
            1 => HarmonyKind.Analogous, 2 => HarmonyKind.Triadic,
            3 => HarmonyKind.SplitComplementary, 4 => HarmonyKind.Square,
            _ => HarmonyKind.Complementary,
        };
        var list = HarmonyOps.Build(palette.Foreground, kind);
        int added = 0;
        foreach (var c in list) if (palette.AddSwatch(c.Quantized)) added++;
        SyncPalettePanel();
        Log($"Harmony {kind}: {list.Count}色中 {added}色を追加");
    }

    void OnProfileChanged(object s, SelectionChangedEventArgs e)
    {
        if (ProfileBox == null) return;
        previewProfile = (PreviewProfile)Math.Clamp(ProfileBox.SelectedIndex, 0, 2);
        SyncPalettePanel();
        Log($"Profile preview: {ProfilePreviewOps.Label(previewProfile)}");
    }

    internal PreviewProfile CurrentPreview => previewProfile;
}
