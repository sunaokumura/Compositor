// Compositor — P1 UI 配線 (docs/WORLD_BEST_PAINT.md §4 P1).
// Adjustment Brush / Live層 / QuickShape / ベクター線＋Correct line /
// spare channel・Quick mask / Time-lapse / Simple preset＋Persona＋
// Contextual頭欄。既存 x:Name・操作子名は不変 (追加のみ)。
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
    List<SKPoint> quickShapePoints;   // QuickShape/ベクター線の記録中 stroke (doc px)
    bool strokeOnQuickMask;           // Quick mask への描画中
    SKPoint quickMaskLast;
    bool vectorMagnet = true;         // ベクター磁石の既定 ON
    float vectorMagnetRadius = 8f;

    // ---------- Adjustment Brush式塗り調整 ----------

    static readonly (string label, AdjustmentKind kind)[] AdjBrushKinds =
    {
        ("Hue-Saturation", AdjustmentKind.Hsv), ("Levels", AdjustmentKind.Levels),
        ("Curves", AdjustmentKind.Curves), ("Exposure", AdjustmentKind.Exposure),
        ("GradientMap", AdjustmentKind.GradientMap), ("Grain", AdjustmentKind.Grain),
    };

    async void OnAdjBrushDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Adjustment Brush", Width = 360, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "塗った所だけ調整が効く層を作る。作成後は Brush で白く塗る (覆面へ描画)。", TextWrapping = TextWrapping.Wrap });
        var kindBox = new ComboBox { Width = 220, SelectedIndex = 0 };
        foreach (var (label, _) in AdjBrushKinds) kindBox.Items.Add(label);
        p.Children.Add(kindBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bCreate = new Button { Content = "作成して塗る" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bCreate); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        bCreate.Click += (_, _) =>
        {
            var kind = AdjBrushKinds[Math.Clamp(kindBox.SelectedIndex, 0, AdjBrushKinds.Length - 1)].kind;
            dlg.Close();
            undoStack.Push(doc);
            var layer = AdjustmentBrushOps.CreateBrushLayer(doc, kind, $"Brush {kind}");
            layer.MaskSelected = true;
            SafeSelect(doc.Layers.Count - 1 - doc.Layers.IndexOf(layer));
            SetTool(Tool.Brush);
            TimelapseOps.Record(doc, $"adjbrush:{kind}");
            Log($"Adjustment Brush '{layer.Name}' を作成 (覆面へBrushで塗る)");
            RefreshAll();
        };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- Live層 (filter mask・並替・再調整) ----------

    static readonly (string label, AdjustmentKind kind)[] LiveKinds =
    {
        ("Hue-Saturation", AdjustmentKind.Hsv), ("Levels", AdjustmentKind.Levels),
        ("Curves", AdjustmentKind.Curves), ("Exposure", AdjustmentKind.Exposure),
        ("GradientMap", AdjustmentKind.GradientMap), ("Grain", AdjustmentKind.Grain),
        ("Noise", AdjustmentKind.Noise), ("Lens補正", AdjustmentKind.Lens),
        ("Gaussian Blur", AdjustmentKind.GaussBlur), ("Motion Blur", AdjustmentKind.MotionBlur),
    };

    /// <summary>量 slider 値 (0..100) を Live層の係数へ写像する。</summary>
    static void ApplyLiveAmount(LayerAdjustment adj, double v)
    {
        switch (adj.Kind)
        {
            case AdjustmentKind.Hsv: adj.HueSat = new HueSaturationSettings(0, 0, v - 50, false); break;
            case AdjustmentKind.Levels:
                adj.Levels = new LevelsSettings();
                adj.Levels.Ranges[0] = new LevelRange(0, Math.Clamp(0.1 + v / 100 * 2.9, 0.1, 9.99), 255, 0, 255).Normalized;
                break;
            case AdjustmentKind.Curves:
                var cs = new CurvesSettings();
                cs.Channels[0] = new List<CurvePoint> { new(0, 0), new(128, Math.Clamp(v / 100 * 255, 0, 255)), new(255, 255) };
                adj.Curves = cs;
                break;
            case AdjustmentKind.Exposure: adj.Exposure = new ExposureSettings { Exposure = v / 100 * 6 - 3 }; break;
            case AdjustmentKind.GradientMap: adj.GradientMap = new GradientMapSettings { Reversed = v >= 50 }; break;
            case AdjustmentKind.Grain: adj.Grain = new GrainSettings { Amount = v }; break;
            case AdjustmentKind.Noise: adj.Noise = new NoiseSettings { Amount = v * 4, Seed = 11 }; break;
            case AdjustmentKind.Lens: adj.LensDistortion = v - 50; break;
            case AdjustmentKind.GaussBlur: adj.GaussRadius = Math.Max(0.5, v / 10); break;
            case AdjustmentKind.MotionBlur: adj.MotionAngle = 0; adj.MotionDistance = Math.Max(1, v / 2); break;
        }
    }

    async void OnLiveFilterAdd(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Live層を追加", Width = 360, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "係数は後から再調整・並替・覆面編集可 (filter mask は白開始)。", TextWrapping = TextWrapping.Wrap });
        var kindBox = new ComboBox { Width = 220, SelectedIndex = 0 };
        foreach (var (label, _) in LiveKinds) kindBox.Items.Add(label);
        p.Children.Add(kindBox);
        var amount = Row(p, "Amount", 0, 100, 50, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bAdd = new Button { Content = "追加" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bAdd); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        bAdd.Click += (_, _) =>
        {
            var kind = LiveKinds[Math.Clamp(kindBox.SelectedIndex, 0, LiveKinds.Length - 1)].kind;
            double v = amount.Value;
            dlg.Close();
            undoStack.Push(doc);
            var layer = LiveLayerOps.Create(doc, kind, $"Live {kind}");
            LiveLayerOps.Retune(layer, a => ApplyLiveAmount(a, v));
            TimelapseOps.Record(doc, $"live:{kind}");
            Log($"Live層 '{layer.Name}' を追加");
            RefreshAll();
        };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnLiveRetune(object s, RoutedEventArgs e)
    {
        if (Selected is not { IsAdjustmentLayer: true, Adjustment: not null } l)
        {
            Log("再調整: 調整層・Live層を選択");
            return;
        }
        var dlg = new Window { Title = $"再調整 ({l.Adjustment.Kind})", Width = 340, Height = 240, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Live層の係数を後から変える (非破壊・Undo可)。", TextWrapping = TextWrapping.Wrap });
        var amount = Row(p, "Amount", 0, 100, 50, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bApply); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            double v = amount.Value;
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                LiveLayerOps.Retune(l, a => ApplyLiveAmount(a, v));
                TimelapseOps.Record(doc, $"retune:{l.Adjustment.Kind}");
                Log($"Live層 '{l.Name}' を再調整");
            }
            catch (Exception ex) { Log("再調整 failed: " + ex.Message); }
            RefreshAll();
        };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    void OnLiveMoveUp(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) { Log("並替: 層を選択"); return; }
        undoStack.Push(doc);
        if (!LiveLayerOps.Reorder(doc, l.Id, up: true)) { undoStack.Undo(doc); Log("並替: これ以上上へ行けない"); return; }
        TimelapseOps.Record(doc, "live:reorder");
        Log($"'{l.Name}' を上へ");
        RefreshAll();
    }

    void OnLiveMoveDown(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l) { Log("並替: 層を選択"); return; }
        undoStack.Push(doc);
        if (!LiveLayerOps.Reorder(doc, l.Id, up: false)) { undoStack.Undo(doc); Log("並替: これ以上下へ行けない"); return; }
        TimelapseOps.Record(doc, "live:reorder");
        Log($"'{l.Name}' を下へ");
        RefreshAll();
    }

    // ---------- QuickShape / ベクター線 (canvas stroke 系) ----------

    void OnQuickShapeTool(object s, RoutedEventArgs e) => SetTool(Tool.QuickShape);
    void OnVectorTool(object s, RoutedEventArgs e) => SetTool(Tool.VectorLine);

    void BeginQuickShape(SKPoint docPoint)
    {
        quickShapePoints = new List<SKPoint> { docPoint };
    }

    void FinishQuickShape()
    {
        var pts = quickShapePoints;
        quickShapePoints = null;
        if (pts == null || pts.Count < 3 || doc == null) return;
        if (currentTool == Tool.VectorLine)
        {
            var vs = new VectorStroke
            {
                Points = pts.Select(p => new SKPoint(p.X, p.Y)).ToList(),
                Width = Math.Clamp(BrushDiameter(), VectorStrokeOps.MinWidth, VectorStrokeOps.MaxWidth),
            };
            VectorStrokeOps.Simplify(vs, 1.0);
            if (vectorMagnet)
                VectorStrokeOps.MagnetSnap(vs,
                    doc.Layers.Where(l => l.IsVectorLayer && l.Vector != null).Select(l => l.Vector),
                    vectorMagnetRadius);
            undoStack.Push(doc);
            var layer = new Layer { Name = "Vector" };
            doc.Layers.Add(layer);
            doc.ActiveLayerId = layer.Id;
            VectorStrokeOps.Rasterize(layer, vs, BrushPaintColor(), doc.Width, doc.Height);
            TimelapseOps.Record(doc, "vector");
            Log($"ベクター線 ({vs.Points.Count}点・幅{vs.Width:F0}) を作成・後から Correct line で編集可");
            RefreshAll();
            return;
        }
        var kind = QuickShapeOps.Fit(pts);
        if (kind == QuickShapeKind.None) { Log("QuickShape: 図形にできませんでした (自由線のまま)"); return; }
        undoStack.Push(doc);
        var layer2 = new Layer { Name = $"QuickShape {kind}" };
        layer2.Bitmap = new SKBitmap(Math.Max(1, doc.Width), Math.Max(1, doc.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        layer2.Bitmap.Erase(SKColor.Empty);
        using (var canvas = new SKCanvas(layer2.Bitmap))
        using (var paint = new SKPaint
        {
            Color = BrushPaintColor(), StrokeWidth = Math.Max(1, BrushDiameter()),
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        })
        {
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            var box = SKRect.Create(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
            switch (kind)
            {
                case QuickShapeKind.Line:
                    var snapped = QuickShapeOps.SnapLine45(pts[0], pts[^1]);
                    canvas.DrawLine(pts[0], snapped, paint);
                    break;
                case QuickShapeKind.Rectangle:
                    canvas.DrawRect(box, paint);
                    break;
                case QuickShapeKind.Ellipse:
                    canvas.DrawOval(box, paint);
                    break;
                case QuickShapeKind.Triangle:
                    using (var path = new SKPath())
                    {
                        // 閉形の3頂点を RDP で求める (QuickShapeOps.Fit と同判定)
                        var cx = pts.Select(p => new SKPoint(p.X, p.Y)).ToList();
                        var corners = new List<SKPoint> { cx[0] };
                        foreach (var q in cx)
                            if ((q - corners[^1]).Length > 2) corners.Add(q);
                        path.MoveTo(corners[0]);
                        // 重心から最も遠い3点を頂点にする
                        var g = new SKPoint(corners.Average(c => c.X), corners.Average(c => c.Y));
                        var top = corners.OrderByDescending(c => (c - g).Length).Take(3).ToList();
                        path.MoveTo(top[0]); path.LineTo(top[1]); path.LineTo(top[2]); path.Close();
                        canvas.DrawPath(path, paint);
                    }
                    break;
            }
        }
        doc.Layers.Add(layer2);
        doc.ActiveLayerId = layer2.Id;
        TimelapseOps.Record(doc, $"quickshape:{kind}");
        Log($"QuickShape {kind} を作成");
        RefreshAll();
    }

    // ---------- Correct line (ベクター線の後編集) ----------

    static SKColor DominantColor(SKBitmap bmp)
    {
        if (bmp == null) return SKColors.Black;
        foreach (var c in bmp.Pixels)
            if (c.Alpha > 16) return c;
        return SKColors.Black;
    }

    async void OnCorrectLine(object s, RoutedEventArgs e)
    {
        if (Selected is not { IsVectorLayer: true, Vector: not null, Bitmap: not null } l)
        {
            Log("Correct line: ベクター線の層を選択");
            return;
        }
        var dlg = new Window { Title = "Correct line", Width = 360, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = $"「{l.Name}」({l.Vector.Points.Count}点・幅{l.Vector.Width:F1})。編集後は同色で焼き直す。", TextWrapping = TextWrapping.Wrap });
        var width = Row(p, "Width", VectorStrokeOps.MinWidth, VectorStrokeOps.MaxWidth, l.Vector.Width, out _);
        var idxBox = new TextBox { Width = 60, Text = "0", Watermark = "点#" };
        var xBox = new TextBox { Width = 70, Text = "0", Watermark = "X" };
        var yBox = new TextBox { Width = 70, Text = "0", Watermark = "Y" };
        var ptRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        ptRow.Children.Add(new TextBlock { Text = "制御点", VerticalAlignment = VerticalAlignment.Center });
        ptRow.Children.Add(idxBox); ptRow.Children.Add(xBox); ptRow.Children.Add(yBox);
        p.Children.Add(ptRow);
        var magnet = new CheckBox { Content = "ベクター磁石", IsChecked = vectorMagnet };
        p.Children.Add(magnet);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bWidth = new Button { Content = "線幅" };
        var bMove = new Button { Content = "制御点移動" };
        var bSimple = new Button { Content = "単純化" };
        var bMagnet = new Button { Content = "磁石吸着" };
        row.Children.Add(bWidth); row.Children.Add(bMove); row.Children.Add(bSimple); row.Children.Add(bMagnet);
        p.Children.Add(row);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bJoin = new Button { Content = "結線" };
        var joinBox = new ComboBox { Width = 150, SelectedIndex = -1 };
        var others = doc.Layers.Where(x => x.IsVectorLayer && x.Vector != null && x.Id != l.Id).ToList();
        foreach (var o in others) joinBox.Items.Add(o.Name);
        if (joinBox.Items.Count > 0) joinBox.SelectedIndex = 0;
        var bClose = new Button { Content = "閉じる" };
        row2.Children.Add(bJoin); row2.Children.Add(joinBox); row2.Children.Add(bClose);
        p.Children.Add(row2);
        dlg.Content = p;
        vectorMagnet = magnet.IsChecked == true;
        magnet.Checked += (_, _) => vectorMagnet = true;
        magnet.Unchecked += (_, _) => vectorMagnet = false;
        void Rebake()
        {
            var keep = l.Vector;
            var bmp = new SKBitmap(Math.Max(1, doc.Width), Math.Max(1, doc.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.Erase(SKColor.Empty);
            using var canvas = new SKCanvas(bmp);
            using var paint = new SKPaint
            {
                Color = DominantColor(l.Bitmap), StrokeWidth = keep.Width, IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            };
            using var path = new SKPath();
            path.MoveTo(keep.Points[0]);
            for (int i = 1; i < keep.Points.Count; i++) path.LineTo(keep.Points[i]);
            if (keep.Closed) path.Close();
            canvas.DrawPath(path, paint);
            l.Bitmap.Dispose();
            l.Bitmap = bmp;
            TimelapseOps.Record(doc, "correctline");
            RefreshAll();
        }
        bWidth.Click += (_, _) =>
        {
            undoStack.Push(doc);
            VectorStrokeOps.SetWidth(l.Vector, (float)width.Value);
            Rebake();
            Log($"Correct line: 線幅 {l.Vector.Width:F1}");
        };
        bMove.Click += (_, _) =>
        {
            if (!int.TryParse(idxBox.Text, out int idx)
                || !float.TryParse(xBox.Text, out float x)
                || !float.TryParse(yBox.Text, out float y)) { Log("Correct line: 数値が不正"); return; }
            undoStack.Push(doc);
            try { VectorStrokeOps.MovePoint(l.Vector, idx, new SKPoint(x, y)); Rebake(); Log($"Correct line: 点{idx}を移動"); }
            catch (Exception ex) { Log("Correct line failed: " + ex.Message); }
        };
        bSimple.Click += (_, _) =>
        {
            undoStack.Push(doc);
            int removed = VectorStrokeOps.Simplify(l.Vector, 1.5);
            Rebake();
            Log($"Correct line: 単純化 {removed}点間引き");
        };
        bMagnet.Click += (_, _) =>
        {
            undoStack.Push(doc);
            int n = VectorStrokeOps.MagnetSnap(l.Vector,
                doc.Layers.Where(x => x.IsVectorLayer && x.Vector != null && x.Id != l.Id).Select(x => x.Vector),
                vectorMagnetRadius);
            Rebake();
            Log($"Correct line: 磁石 {n}点吸着");
        };
        bJoin.Click += (_, _) =>
        {
            int ji = joinBox.SelectedIndex;
            if (ji < 0 || ji >= others.Count) { Log("Correct line: 結線相手なし"); return; }
            undoStack.Push(doc);
            try
            {
                var joined = VectorStrokeOps.Join(l.Vector, others[ji].Vector);
                l.Vector = joined;
                Rebake();
                Log($"Correct line: 「{others[ji].Name}」と結線");
            }
            catch (Exception ex) { Log("Correct line failed: " + ex.Message); }
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- spare channel・Quick mask ----------

    async void OnChannelSave(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "選択を channel に保存", Width = 340, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "現選択を spare channel として保存する (.comp v8 で往復)。", TextWrapping = TextWrapping.Wrap });
        var nameBox = new TextBox { Width = 220, Watermark = "Channel 名" };
        p.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bSave = new Button { Content = "保存" };
        var bCancel = new Button { Content = "取消" };
        row.Children.Add(bSave); row.Children.Add(bCancel);
        p.Children.Add(row);
        dlg.Content = p;
        bSave.Click += (_, _) =>
        {
            dlg.Close();
            try
            {
                var mask = SelectionTools.CurrentMask(doc);
                SpareChannelOps.Save(doc, nameBox.Text, mask, doc.Width, doc.Height);
                TimelapseOps.Record(doc, "channel:save");
                Log($"spare channel を保存 ({doc.SpareChannels.Count}件)");
                RefreshAll();
            }
            catch (Exception ex) { Log("channel 保存 failed: " + ex.Message); }
        };
        bCancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnChannelLoad(object s, RoutedEventArgs e)
    {
        if (doc.SpareChannels.Count == 0) { Log("channel なし"); return; }
        var dlg = new Window { Title = "spare channel", Width = 340, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var list = new ListBox { Height = 140 };
        foreach (var c in doc.SpareChannels) list.Items.Add($"{c.Name} ({c.W}x{c.H})");
        list.SelectedIndex = 0;
        p.Children.Add(list);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "選択へ" };
        var bDel = new Button { Content = "削除" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bApply); row.Children.Add(bDel); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            dlg.Close();
            if (i < 0 || i >= doc.SpareChannels.Count) return;
            undoStack.Push(doc);
            var mask = SpareChannelOps.Load(doc, i);
            SelectionTools.ApplyMask(doc, mask, SelectionKind.Wand, null, SelectionMode.Replace);
            TimelapseOps.Record(doc, "channel:load");
            Log($"channel「{doc.SpareChannels[i].Name}」を選択へ");
            RefreshAll();
        };
        bDel.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= doc.SpareChannels.Count) return;
            list.Items.RemoveAt(i);
            SpareChannelOps.Delete(doc, i);
            Log("channel を削除");
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    void OnQuickMaskToggled(object s, RoutedEventArgs e)
    {
        if (QuickMaskCheck == null) return;
        try
        {
            if (QuickMaskCheck.IsChecked == true)
            {
                QuickMaskOps.Enable(doc);
                Log("Quick mask ON (赤域が非選択・Brush白で開ける/黒で閉じる)");
            }
            else
            {
                QuickMaskOps.Disable(doc);
                Log("Quick mask OFF");
            }
        }
        catch (Exception ex) { Log("Quick mask failed: " + ex.Message); QuickMaskCheck.IsChecked = false; }
        RenderCanvas();
    }

    // ---------- Time-lapse記録 ----------

    async void OnTimelapse(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Time-lapse", Width = 380, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var state = new TextBlock { Text = $"状態: {(doc.TimelapseRecording ? "記録中" : "停止中")} ({doc.Timelapse.Count}件)" };
        p.Children.Add(state);
        p.Children.Add(new TextBlock { Text = "操作名＋時刻の過程 log。連番画像の書出は将来 (P2動画層)。", TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bStart = new Button { Content = "Start" };
        var bStop = new Button { Content = "Stop" };
        var bExport = new Button { Content = "Export" };
        var bClear = new Button { Content = "Clear" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bStart); row.Children.Add(bStop); row.Children.Add(bExport); row.Children.Add(bClear); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bStart.Click += (_, _) => { TimelapseOps.Start(doc); state.Text = $"状態: 記録中 ({doc.Timelapse.Count}件)"; Log("Time-lapse 記録開始"); };
        bStop.Click += (_, _) => { TimelapseOps.Stop(doc); state.Text = $"状態: 停止中 ({doc.Timelapse.Count}件)"; Log("Time-lapse 記録停止"); };
        bExport.Click += (_, _) =>
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"timelapse-{doc.DocumentId:N}.txt");
                File.WriteAllText(path, TimelapseOps.ExportManifest(doc));
                Log($"Time-lapse を書出: {path} ({doc.Timelapse.Count}件)");
            }
            catch (Exception ex) { Log("Time-lapse 書出 failed: " + ex.Message); }
        };
        bClear.Click += (_, _) => { TimelapseOps.Clear(doc); state.Text = $"状態: {(doc.TimelapseRecording ? "記録中" : "停止中")} (0件)"; Log("Time-lapse を消去"); };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- Simple preset＋Persona弱移植＋Contextual頭欄 ----------

    void OnPersonaChanged(object s, SelectionChangedEventArgs e)
    {
        if (!uiReady || PersonaBox == null) return;
        var persona = (PersonaKind)Math.Clamp(PersonaBox.SelectedIndex, 0, 2);
        doc.Persona = persona;
        SetTool(persona switch
        {
            PersonaKind.Paint => Tool.Brush,
            PersonaKind.Retouch => Tool.Clone,
            _ => Tool.Crop,
        });
        if (ToolHeaderTitle != null) ToolHeaderTitle.Text = $"[{PersonaOps.Label(persona)}] {ToolHeaderTitle.Text}";
        Log($"Persona: {PersonaOps.Label(persona)} ({string.Join("/", PersonaOps.ToolsFor(persona))})");
    }

    void OnSimpleChanged(object s, RoutedEventArgs e)
    {
        if (!uiReady || SimpleCheck == null) return;
        doc.SimpleMode = SimpleCheck.IsChecked == true;
        if (!doc.SimpleMode) { Log("Simple preset OFF"); return; }
        if (BrushSizeSlider != null) BrushSizeSlider.Value = 24;
        if (HardnessSlider != null) HardnessSlider.Value = 100;
        if (PersonaBox != null) PersonaBox.SelectedIndex = 0;
        doc.Persona = PersonaKind.Paint;
        palette.Reset();
        SyncPalettePanel();
        SetTool(Tool.Brush);
        if (ToolHeaderTitle != null) ToolHeaderTitle.Text = $"[描く] {ToolHeaderTitle.Text}";
        Log("Simple preset: 初心者既定 (Brush・白黒・描く) を適用");
        RefreshAll();
    }

    internal PersonaKind CurrentPersona => doc?.Persona ?? PersonaKind.Paint;
}
