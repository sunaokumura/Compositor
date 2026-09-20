// Compositor — P2 UI 配線 (docs/WORLD_BEST_PAINT.md §4 P2).
// Mixer・対称・Wrap・透視・mesh/puppet・History・macro・workspace・
// HDR/ICC/Gamut・slice/compound/blendRange・動画層・3D・AI補助。
// 既存 x:Name・操作子名は不変 (追加のみ)。破壊適用は Undo push。
// AI系は手動下位・opt-inのみ (自動適用なし)。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor;

public partial class MainWindow
{
    // ---------- Mixer筆 ----------

    async void OnMixerDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Mixer筆", Width = 360, Height = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "Wet=拾い・Load=筆量・Mix=混色率。対角 stroke を選択層へ破壊適用 (Undo可)。", TextWrapping = TextWrapping.Wrap });
        var wet = Row(p, "Wet", 0, 1, 0.5, out _);
        var load = Row(p, "Load", 0, 1, 1, out _);
        var mix = Row(p, "Mix", 0, 1, 0.5, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bApply); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("Mixer: 対象層なし"); return; }
            var st = new MixerSettings { Wet = wet.Value, Load = load.Value, Mix = mix.Value };
            if (!st.IsValid) { Log("Mixer: 無効な係数"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                Document.EnsureUniqueBitmap(l);
                var pts = new List<SKPoint> { new(4, 4), new(l.Bitmap.Width / 2f, l.Bitmap.Height / 2f),
                    new(l.Bitmap.Width - 5, l.Bitmap.Height - 5) };
                MixerOps.ApplyStroke(l.Bitmap, pts, BrushPaintColor(), Math.Max(2, BrushDiameter()), st);
                HistoryOps.Record(doc, "mixer");
                TimelapseOps.Record(doc, "mixer");
                Log("Mixer筆を適用");
            }
            catch (Exception ex) { Log("Mixer failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- 対称・Wrap ----------

    async void OnSymmetryDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "対称描画", Width = 360, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "選択層の鏡像複製を新規層に作る (GIMP Symmetryの考え)。", TextWrapping = TextWrapping.Wrap });
        var modeBox = new ComboBox { Width = 220, SelectedIndex = 0 };
        foreach (var m in new[] { "MirrorH (左右)", "MirrorV (上下)", "MirrorHV (両方)", "Kaleido4 (4回対称)" })
            modeBox.Items.Add(m);
        p.Children.Add(modeBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bMake = new Button { Content = "複製層を作る" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bMake); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bMake.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("対称: 対象層なし"); return; }
            var mode = (SymmetryMode)(modeBox.SelectedIndex + 1);
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                bool fh = mode is SymmetryMode.MirrorH or SymmetryMode.MirrorHV or SymmetryMode.Kaleido4;
                bool fv = mode is SymmetryMode.MirrorV or SymmetryMode.MirrorHV;
                using var mirrored = SymmetryOps.MirrorBitmap(l.Bitmap, fh, fv);
                var nl = new Layer { Name = $"{l.Name} 対称{mode}" };
                nl.Bitmap = new SKBitmap(mirrored.Width, mirrored.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var c = new SKCanvas(nl.Bitmap)) c.DrawBitmap(mirrored, 0, 0);
                doc.Layers.Add(nl);
                doc.ActiveLayerId = nl.Id;
                doc.Symmetry = mode;
                HistoryOps.Record(doc, $"symmetry:{mode}");
                TimelapseOps.Record(doc, $"symmetry:{mode}");
                Log($"対称複製層を作成 ({mode})");
            }
            catch (Exception ex) { Log("対称 failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnWrapDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Wrap-Around", Width = 360, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "tiling継ぎ目の2x2 previewを書出する (画素不変・Krita Wの考え)。", TextWrapping = TextWrapping.Wrap });
        var on = new CheckBox { Content = "Wrap preview 有効 (view状態)", IsChecked = doc.WrapEnabled };
        p.Children.Add(on);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bPrev = new Button { Content = "Preview書出" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bPrev); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        on.Checked += (_, _) => { doc.WrapEnabled = true; Log("Wrap preview ON"); };
        on.Unchecked += (_, _) => { doc.WrapEnabled = false; Log("Wrap preview OFF"); };
        bPrev.Click += (_, _) =>
        {
            try
            {
                using var comp = doc.Compose();
                using var tiled = WrapOps.RenderTiled(comp);
                string path = Path.Combine(Path.GetTempPath(), $"wrap-{doc.DocumentId:N}.png");
                File.WriteAllBytes(path, SliceOps.EncodePng(tiled));
                HistoryOps.Record(doc, "wrap:preview");
                Log($"Wrap previewを書出: {path} ({tiled.Width}x{tiled.Height})");
            }
            catch (Exception ex) { Log("Wrap preview failed: " + ex.Message); }
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- 透視助手 ----------

    async void OnPerspectiveDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "透視助手", Width = 360, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "消失点を設定し、選択ベクター線の制御点を15°放射線へ吸着する。", TextWrapping = TextWrapping.Wrap });
        var vx = Row(p, "消失点 X", 0, Math.Max(1, doc.Width), doc.HasVanishingPoint ? doc.VanishingPoint.X : doc.Width / 2f, out _);
        var vy = Row(p, "消失点 Y", 0, Math.Max(1, doc.Height), doc.HasVanishingPoint ? doc.VanishingPoint.Y : doc.Height / 2f, out _);
        var tol = Row(p, "許容 px", 0, 64, 8, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bSet = new Button { Content = "消失点を設定" };
        var bSnap = new Button { Content = "ベクターへ吸着" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bSet); row.Children.Add(bSnap); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bSet.Click += (_, _) =>
        {
            doc.VanishingPoint = new SKPoint((float)vx.Value, (float)vy.Value);
            doc.HasVanishingPoint = true;
            HistoryOps.Record(doc, "perspective:set-vp");
            Log($"消失点を設定 ({doc.VanishingPoint.X:F0},{doc.VanishingPoint.Y:F0})");
        };
        bSnap.Click += (_, _) =>
        {
            if (!doc.HasVanishingPoint) { Log("透視: 消失点を先に設定"); return; }
            if (Selected is not { IsVectorLayer: true, Vector: not null } l) { Log("透視: ベクター線の層を選択"); return; }
            undoStack.Push(doc);
            try
            {
                var snapped = PerspectiveOps.SnapStroke(l.Vector.Points, doc.VanishingPoint, (float)tol.Value);
                l.Vector.Points = snapped;
                VectorStrokeOps.Rasterize(l, l.Vector, DominantColor(l.Bitmap), doc.Width, doc.Height);
                HistoryOps.Record(doc, "perspective:snap");
                TimelapseOps.Record(doc, "perspective:snap");
                Log("ベクター線を透視放射線へ吸着");
            }
            catch (Exception ex) { Log("透視吸着 failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- mesh・puppet ----------

    async void OnMeshDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Mesh・Puppet", Width = 360, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "粗格子ワープ (mesh) とハンドル1点の減衰移動 (puppet入口)。選択層へ破壊適用。", TextWrapping = TextWrapping.Wrap });
        var grid = Row(p, "格子点数", 2, 8, 4, out _);
        var bulge = Row(p, "膨らみ px", -40, 40, 12, out _);
        var pdx = Row(p, "Puppet dx", -60, 60, 20, out _);
        var pr = Row(p, "Puppet半径", 4, 200, 40, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bMesh = new Button { Content = "Mesh適用" };
        var bPuppet = new Button { Content = "Puppet適用" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bMesh); row.Children.Add(bPuppet); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bMesh.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("Mesh: 対象層なし"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                int g = (int)Math.Round(grid.Value);
                var warp = MeshWarp.Identity(g, g);
                float cx = l.Bitmap.Width / 2f, cy = l.Bitmap.Height / 2f;
                for (int y = 0; y < g; y++)
                    for (int x = 0; x < g; x++)
                    {
                        float px = x / (float)(g - 1) * l.Bitmap.Width, py = y / (float)(g - 1) * l.Bitmap.Height;
                        float d = MathF.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                        float k = d < 1 ? 1 : 1 / (1 + d / 40);
                        warp.Dx[y, x] = (px - cx) * (float)(bulge.Value / 100) * k;
                        warp.Dz[y, x] = (py - cy) * (float)(bulge.Value / 100) * k;
                    }
                Document.EnsureUniqueBitmap(l);
                var dst = MeshOps.Apply(l.Bitmap, warp);
                l.Bitmap.Dispose();
                l.Bitmap = dst;
                HistoryOps.Record(doc, "mesh");
                TimelapseOps.Record(doc, "mesh");
                Log("Mesh変形を適用");
            }
            catch (Exception ex) { Log("Mesh failed: " + ex.Message); }
            RefreshAll();
        };
        bPuppet.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("Puppet: 対象層なし"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                Document.EnsureUniqueBitmap(l);
                var handle = new SKPoint(l.Bitmap.Width / 2f, l.Bitmap.Height / 2f);
                var dst = PuppetOps.Apply(l.Bitmap, handle, new SKPoint((float)pdx.Value, 0), (float)pr.Value);
                l.Bitmap.Dispose();
                l.Bitmap = dst;
                HistoryOps.Record(doc, "puppet");
                TimelapseOps.Record(doc, "puppet");
                Log("Puppet移動を適用");
            }
            catch (Exception ex) { Log("Puppet failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- History・macro ----------

    async void OnHistoryDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "History", Width = 420, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = $"操作名＋時刻＋層数の記録 ({doc.History.Count}件・上限{HistoryOps.MaxEntries}・.comp v9で往復・画素なし)。" });
        var list = new ListBox { Height = 140 };
        foreach (var h in doc.History.TakeLast(50)) list.Items.Add($"{h.Time:HH:mm:ss} [{h.Layers}] {h.Action}");
        p.Children.Add(list);
        var nameBox = new TextBox { Width = 260, Watermark = "操作名を記録" };
        p.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bRec = new Button { Content = "記録" };
        var bExp = new Button { Content = "書出" };
        var bClear = new Button { Content = "消去" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bRec); row.Children.Add(bExp); row.Children.Add(bClear); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bRec.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) { Log("History: 操作名を入力"); return; }
            HistoryOps.Record(doc, nameBox.Text);
            list.Items.Add($"{DateTime.UtcNow:HH:mm:ss} [{doc.Layers.Count}] {nameBox.Text.Trim()}");
            Log("Historyに記録");
        };
        bExp.Click += (_, _) =>
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"history-{doc.DocumentId:N}.txt");
                File.WriteAllText(path, HistoryOps.ExportText(doc));
                Log($"Historyを書出: {path} ({doc.History.Count}件)");
            }
            catch (Exception ex) { Log("History書出 failed: " + ex.Message); }
        };
        bClear.Click += (_, _) => { HistoryOps.Clear(doc); list.Items.Clear(); Log("Historyを消去"); };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnMacroDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Macro", Width = 400, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var state = new TextBlock { Text = $"記録 {doc.Macro.Count}手 (上限{MacroOps.MaxSteps}・.comp v9で往復)。" };
        p.Children.Add(state);
        p.Children.Add(new TextBlock { Text = "単純調整opのみ (Brightness/Contrast/Invert)。再生は選択層へ破壊適用。", TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bB = new Button { Content = "Bright+10記録" };
        var bC = new Button { Content = "Contrast+10記録" };
        var bI = new Button { Content = "Invert記録" };
        row.Children.Add(bB); row.Children.Add(bC); row.Children.Add(bI);
        p.Children.Add(row);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bPlay = new Button { Content = "再生" };
        var bClear = new Button { Content = "消去" };
        var bClose = new Button { Content = "閉じる" };
        row2.Children.Add(bPlay); row2.Children.Add(bClear); row2.Children.Add(bClose);
        p.Children.Add(row2);
        dlg.Content = p;
        void Refresh() => state.Text = $"記録 {doc.Macro.Count}手 (上限{MacroOps.MaxSteps}・.comp v9で往復)。";
        bB.Click += (_, _) => { try { MacroOps.Record(doc, MacroOp.Brightness, 10); Refresh(); } catch (Exception ex) { Log("Macro記録 failed: " + ex.Message); } };
        bC.Click += (_, _) => { try { MacroOps.Record(doc, MacroOp.Contrast, 10); Refresh(); } catch (Exception ex) { Log("Macro記録 failed: " + ex.Message); } };
        bI.Click += (_, _) => { try { MacroOps.Record(doc, MacroOp.Invert); Refresh(); } catch (Exception ex) { Log("Macro記録 failed: " + ex.Message); } };
        bPlay.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("Macro再生: 対象層なし"); return; }
            if (doc.Macro.Count == 0) { Log("Macro再生: 記録なし"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                MacroOps.Replay(l, doc.Macro);
                HistoryOps.Record(doc, "macro:replay");
                TimelapseOps.Record(doc, "macro:replay");
                Log($"Macroを再生 ({doc.Macro.Count}手)");
            }
            catch (Exception ex) { Log("Macro再生 failed: " + ex.Message); }
            RefreshAll();
        };
        bClear.Click += (_, _) => { MacroOps.Clear(doc); Refresh(); Log("Macroを消去"); };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- workspace・HDR ----------

    async void OnWorkspaceDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Workspace・Python API", Width = 420, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "workspace preset (Persona・Simple) の保存・復元と文書要約stubの生成 (interpreterは将来)。", TextWrapping = TextWrapping.Wrap });
        var nameBox = new TextBox { Width = 220, Watermark = "Preset名" };
        p.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bSave = new Button { Content = "Preset保存" };
        var bLoad = new Button { Content = "Preset復元" };
        var bStub = new Button { Content = "Stub書出" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bSave); row.Children.Add(bLoad); row.Children.Add(bStub); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        string lastJson = "";
        bSave.Click += (_, _) =>
        {
            try
            {
                var preset = WorkspaceOps.Capture(doc, nameBox.Text);
                lastJson = WorkspaceOps.ToJson(preset);
                string path = Path.Combine(Path.GetTempPath(), "workspace-preset.json");
                File.WriteAllText(path, lastJson);
                Log($"Workspace presetを保存: {path}");
            }
            catch (Exception ex) { Log("Preset保存 failed: " + ex.Message); }
        };
        bLoad.Click += (_, _) =>
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "workspace-preset.json");
                if (!File.Exists(path)) { Log("Preset復元: 保存なし"); return; }
                var preset = WorkspaceOps.FromJson(File.ReadAllText(path));
                WorkspaceOps.Apply(doc, preset);
                HistoryOps.Record(doc, "workspace:apply");
                Log($"Workspace presetを復元 ({preset.Name})");
                RefreshAll();
            }
            catch (Exception ex) { Log("Preset復元 failed: " + ex.Message); }
        };
        bStub.Click += (_, _) =>
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"compositor-api-{doc.DocumentId:N}.py");
                File.WriteAllText(path, PythonApiStub.GenerateWithSummary(doc));
                Log($"Python API stubを書出: {path}");
            }
            catch (Exception ex) { Log("Stub書出 failed: " + ex.Message); }
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnHdrDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "HDR・ICC・Gamut", Width = 380, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "露出EV・profile近似・和声窓の確認。画素操作は明示適用のみ。", TextWrapping = TextWrapping.Wrap });
        var ev = Row(p, "露出 EV", -4, 4, doc.HdrEv, out _);
        var iccBox = new ComboBox { Width = 220, SelectedIndex = (int)doc.IccProfile };
        foreach (var k in new[] { "sRGB", "Display-P3 近似", "AdobeRGB 近似" }) iccBox.Items.Add(k);
        p.Children.Add(new TextBlock { Text = "ICC profile (文書に保存・.comp v9)" });
        p.Children.Add(iccBox);
        var gamutBox = new ComboBox { Width = 220, SelectedIndex = (int)doc.Gamut };
        foreach (var k in new[] { "Full (制限なし)", "Analogous90", "Complementary" }) gamutBox.Items.Add(k);
        p.Children.Add(new TextBlock { Text = "Gamut窓 (session-only・前景色の判定)" });
        p.Children.Add(gamutBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bEv = new Button { Content = "露出を適用" };
        var bTone = new Button { Content = "ToneMap適用" };
        var bGamut = new Button { Content = "前景色を判定" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bEv); row.Children.Add(bTone); row.Children.Add(bGamut); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        iccBox.SelectionChanged += (_, _) =>
        {
            doc.IccProfile = (IccProfileKind)Math.Clamp(iccBox.SelectedIndex, 0, 2);
            Log($"ICC: {IccOps.Label(doc.IccProfile)}");
        };
        gamutBox.SelectionChanged += (_, _) =>
        {
            doc.Gamut = (GamutKind)Math.Clamp(gamutBox.SelectedIndex, 0, 2);
            Log($"Gamut窓: {doc.Gamut}");
        };
        bEv.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("HDR露出: 対象層なし"); return; }
            if (ev.Value is < -8 or > 8) { Log("HDR露出: 範囲外"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                Document.EnsureUniqueBitmap(l);
                using var adj = HdrOps.ApplyExposure(l.Bitmap, ev.Value);
                l.Bitmap.Dispose();
                l.Bitmap = adj;
                doc.HdrEv = ev.Value;
                HistoryOps.Record(doc, $"hdr:{ev.Value:F1}ev");
                Log($"露出 {ev.Value:F1}EV を適用");
            }
            catch (Exception ex) { Log("HDR露出 failed: " + ex.Message); }
            RefreshAll();
        };
        bTone.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("ToneMap: 対象層なし"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                Document.EnsureUniqueBitmap(l);
                using var adj = HdrOps.ToneMap(l.Bitmap);
                l.Bitmap.Dispose();
                l.Bitmap = adj;
                HistoryOps.Record(doc, "hdr:tonemap");
                Log("ToneMapを適用");
            }
            catch (Exception ex) { Log("ToneMap failed: " + ex.Message); }
            RefreshAll();
        };
        bGamut.Click += (_, _) =>
        {
            var fg = BrushPaintColor();
            var basis = new PaletteColor(fg.Red / 255.0, fg.Green / 255.0, fg.Blue / 255.0);
            bool ok = GamutMaskOps.IsAllowed(basis, basis, doc.Gamut);
            Log($"Gamut判定: 前景色は窓{doc.Gamut}に{(ok ? "収まる" : "収まらない")} (基色起点のため常に収まる・他色は palette 目視)");
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- slice・compound・blend範囲 ----------

    async void OnSliceDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Export slice", Width = 400, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = $"現選択をslice登録しPNGで書出する ({doc.Slices.Count}件・.comp v9で往復)。" });
        var list = new ListBox { Height = 120 };
        foreach (var sl in doc.Slices) list.Items.Add($"{sl.Name} ({sl.X},{sl.Y} {sl.W}x{sl.H})");
        p.Children.Add(list);
        var nameBox = new TextBox { Width = 220, Watermark = "Slice名" };
        p.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bAdd = new Button { Content = "選択を登録" };
        var bExp = new Button { Content = "書出" };
        var bDel = new Button { Content = "削除" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bAdd); row.Children.Add(bExp); row.Children.Add(bDel); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bAdd.Click += (_, _) =>
        {
            if (doc.Selection is not SKRect r) { Log("Slice: 選択なし"); return; }
            if (doc.Slices.Count >= SliceOps.MaxSlices) { Log("Slice: 上限"); return; }
            var sl = new Slice
            {
                Name = string.IsNullOrWhiteSpace(nameBox.Text) ? $"Slice {doc.Slices.Count + 1}" : nameBox.Text.Trim(),
                X = Math.Clamp((int)MathF.Floor(r.Left), 0, doc.Width - 1),
                Y = Math.Clamp((int)MathF.Floor(r.Top), 0, doc.Height - 1),
                W = Math.Clamp((int)MathF.Ceiling(r.Width), 1, doc.Width),
                H = Math.Clamp((int)MathF.Ceiling(r.Height), 1, doc.Height),
            };
            sl.W = Math.Min(sl.W, doc.Width - sl.X); sl.H = Math.Min(sl.H, doc.Height - sl.Y);
            try
            {
                SliceOps.Validate(sl, doc.Width, doc.Height);
                doc.Slices.Add(sl);
                list.Items.Add($"{sl.Name} ({sl.X},{sl.Y} {sl.W}x{sl.H})");
                HistoryOps.Record(doc, $"slice:{sl.Name}");
                Log($"Slice「{sl.Name}」を登録");
            }
            catch (Exception ex) { Log("Slice登録 failed: " + ex.Message); }
        };
        bExp.Click += (_, _) =>
        {
            if (doc.Slices.Count == 0) { Log("Slice: 登録なし"); return; }
            try
            {
                using var comp = doc.Compose();
                foreach (var sl in doc.Slices)
                {
                    using var cut = SliceOps.Cut(comp, sl);
                    string safe = string.Concat(sl.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                    if (string.IsNullOrWhiteSpace(safe)) safe = "slice";
                    string path = Path.Combine(Path.GetTempPath(), $"{safe}-{doc.DocumentId:N}.png");
                    File.WriteAllBytes(path, SliceOps.EncodePng(cut));
                    Log($"Slice「{sl.Name}」を書出: {path}");
                }
                HistoryOps.Record(doc, "slice:export");
            }
            catch (Exception ex) { Log("Slice書出 failed: " + ex.Message); }
        };
        bDel.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= doc.Slices.Count) return;
            list.Items.RemoveAt(i);
            doc.Slices.RemoveAt(i);
            Log("Sliceを削除");
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnCompoundDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "Compound mask", Width = 360, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "現選択と spare channel 先頭の集合演算を選択へ戻す (選択は履歴対象外)。", TextWrapping = TextWrapping.Wrap });
        if (doc.SpareChannels.Count == 0)
            p.Children.Add(new TextBlock { Text = "spare channel なし (Channel保存で作る)。" });
        var modeBox = new ComboBox { Width = 200, SelectedIndex = 0 };
        foreach (var m in new[] { "Union (和)", "Intersect (積)", "Subtract (差)", "Xor (排他)" }) modeBox.Items.Add(m);
        p.Children.Add(modeBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "選択へ" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bApply); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            if (doc.SpareChannels.Count == 0) { Log("Compound: channelなし"); return; }
            var ch = doc.SpareChannels[0];
            if (ch.W != doc.Width || ch.H != doc.Height) { Log("Compound: channel寸法が文書と不一致"); return; }
            var mode = (CompoundMode)Math.Clamp(modeBox.SelectedIndex, 0, 3);
            dlg.Close();
            try
            {
                var cur = SelectionTools.CurrentMask(doc);
                var combined = CompoundMaskOps.Combine(cur, ch.Mask, doc.Width, doc.Height, mode);
                var sel = new byte[combined.Length];
                for (int i = 0; i < sel.Length; i++) sel[i] = combined[i] != 0 ? (byte)1 : (byte)0;
                SelectionTools.ApplyMask(doc, sel, SelectionKind.Wand, null, SelectionMode.Replace);
                HistoryOps.Record(doc, $"compound:{mode}");
                Log($"Compound {mode} を選択へ");
            }
            catch (Exception ex) { Log("Compound failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnBlendRangeDialog(object s, RoutedEventArgs e)
    {
        if (Selected is not { } l0) { Log("Blend範囲: 層を選択"); return; }
        var dlg = new Window { Title = "Blend範囲", Width = 360, Height = 340, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = $"「{l0.Name}」の下地輝度での効き具合 (.comp v9で往復)。" });
        var lo = Row(p, "Lo", 0, 1, l0.UseBlendRange ? l0.BlendLo : 0, out _);
        var hi = Row(p, "Hi", 0, 1, l0.UseBlendRange ? l0.BlendHi : 1, out _);
        var fe = Row(p, "Feather", 0, 0.5, l0.UseBlendRange ? l0.BlendFeather : 0, out _);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bApply = new Button { Content = "適用" };
        var bClear = new Button { Content = "解除" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bApply); row.Children.Add(bClear); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bApply.Click += (_, _) =>
        {
            var br = new BlendRange { Lo = lo.Value, Hi = hi.Value, Feather = fe.Value };
            if (!br.IsValid) { Log("Blend範囲: Lo<=Hi・0..1・Feather<=0.5"); return; }
            dlg.Close();
            undoStack.Push(doc);
            l0.UseBlendRange = true;
            l0.BlendLo = br.Lo; l0.BlendHi = br.Hi; l0.BlendFeather = br.Feather;
            HistoryOps.Record(doc, "blendrange:set");
            Log("Blend範囲を適用");
            RefreshAll();
        };
        bClear.Click += (_, _) =>
        {
            dlg.Close();
            undoStack.Push(doc);
            l0.UseBlendRange = false;
            HistoryOps.Record(doc, "blendrange:clear");
            Log("Blend範囲を解除");
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    // ---------- 動画層・3D・AI補助 ----------

    async void OnVideoDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "動画層", Width = 400, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        var state = new TextBlock { Text = $"{doc.VideoFrames.Count} frames (上限{VideoLayerOps.MaxFrames}・session-only・timeline本格は対象外)。" };
        p.Children.Add(state);
        p.Children.Add(new TextBlock { Text = "現合成を frame に積み、onion preview と連番 manifest を書出す。", TextWrapping = TextWrapping.Wrap });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bAdd = new Button { Content = "Frame追加" };
        var bOnion = new Button { Content = "Onion書出" };
        var bExp = new Button { Content = "Manifest書出" };
        var bClear = new Button { Content = "消去" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bAdd); row.Children.Add(bOnion); row.Children.Add(bExp); row.Children.Add(bClear); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        void Refresh() => state.Text = $"{doc.VideoFrames.Count} frames (上限{VideoLayerOps.MaxFrames}・session-only・timeline本格は対象外)。";
        bAdd.Click += (_, _) =>
        {
            try
            {
                using var comp = doc.Compose();
                VideoLayerOps.AddFrame(doc, comp);
                HistoryOps.Record(doc, "video:add-frame");
                TimelapseOps.Record(doc, "video:add-frame");
                Refresh();
                Log("Frameを追加");
            }
            catch (Exception ex) { Log("Frame追加 failed: " + ex.Message); }
        };
        bOnion.Click += (_, _) =>
        {
            if (doc.VideoFrames.Count < 1) { Log("Onion: frameなし"); return; }
            try
            {
                var cur = doc.VideoFrames[^1];
                var prev = doc.VideoFrames.Count >= 2 ? doc.VideoFrames[^2] : null;
                using var onion = VideoLayerOps.Onion(cur, prev);
                string path = Path.Combine(Path.GetTempPath(), $"onion-{doc.DocumentId:N}.png");
                File.WriteAllBytes(path, SliceOps.EncodePng(onion));
                Log($"Onion previewを書出: {path}");
            }
            catch (Exception ex) { Log("Onion書出 failed: " + ex.Message); }
        };
        bExp.Click += (_, _) =>
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"video-{doc.DocumentId:N}.txt");
                File.WriteAllText(path, VideoLayerOps.ExportManifest(doc));
                Log($"動画 manifestを書出: {path} ({doc.VideoFrames.Count} frames)");
            }
            catch (Exception ex) { Log("Manifest書出 failed: " + ex.Message); }
        };
        bClear.Click += (_, _) => { VideoLayerOps.Clear(doc); Refresh(); Log("動画層を消去"); };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnProxy3DDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "3D素材", Width = 340, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "手続き primitive を新規層へ描く (3D素材の置き場・本格3Dは対象外)。", TextWrapping = TextWrapping.Wrap });
        var kindBox = new ComboBox { Width = 200, SelectedIndex = 0 };
        foreach (var k in new[] { "Box", "Sphere", "Cylinder" }) kindBox.Items.Add(k);
        p.Children.Add(kindBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bMake = new Button { Content = "層を作る" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bMake); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bMake.Click += (_, _) =>
        {
            var kind = (Proxy3DKind)Math.Clamp(kindBox.SelectedIndex, 0, 2);
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                using var bmp = Proxy3DOps.Render(kind, Math.Min(512, Math.Max(64, doc.Width / 2)),
                    Math.Min(512, Math.Max(64, doc.Height / 2)), BrushPaintColor());
                var nl = new Layer { Name = $"3D {kind}", Position = new SKPoint(doc.Width / 4f, doc.Height / 4f) };
                nl.Bitmap = new SKBitmap(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var c = new SKCanvas(nl.Bitmap)) c.DrawBitmap(bmp, 0, 0);
                doc.Layers.Add(nl);
                doc.ActiveLayerId = nl.Id;
                HistoryOps.Record(doc, $"proxy3d:{kind}");
                TimelapseOps.Record(doc, $"proxy3d:{kind}");
                Log($"3D素材層を作成 ({kind})");
            }
            catch (Exception ex) { Log("3D作成 failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    async void OnAiAssistDialog(object s, RoutedEventArgs e)
    {
        var dlg = new Window { Title = "AI補助 (手動下位・opt-in)", Width = 400, Height = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        p.Children.Add(new TextBlock { Text = "自動切抜・生成Fillは対象外。羽化refine・背景hint提案・保護noiseのみ (適用は明示操作)。", TextWrapping = TextWrapping.Wrap });
        var feather = Row(p, "羽化半径", 0, 16, 2, out _);
        var tol = Row(p, "背景許容", 0, 128, 32, out _);
        var disturb = Row(p, "保護noise", 0, 100, 20, out _);
        var confirmBg = new CheckBox { Content = "背景hintを選択へ適用する (確認の上でopt-in)" };
        p.Children.Add(confirmBg);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var bFeather = new Button { Content = "羽化を適用" };
        var bBg = new Button { Content = "背景hint" };
        var bNoise = new Button { Content = "保護noise" };
        var bClose = new Button { Content = "閉じる" };
        row.Children.Add(bFeather); row.Children.Add(bBg); row.Children.Add(bNoise); row.Children.Add(bClose);
        p.Children.Add(row);
        dlg.Content = p;
        bFeather.Click += (_, _) =>
        {
            try
            {
                var cur = SelectionTools.CurrentMask(doc);
                var f = AiAssistOps.Feather(cur, doc.Width, doc.Height, (int)Math.Round(feather.Value));
                var sel = new byte[f.Length];
                for (int i = 0; i < sel.Length; i++) sel[i] = f[i] != 0 ? (byte)1 : (byte)0;
                SelectionTools.ApplyMask(doc, sel, SelectionKind.Wand, null, SelectionMode.Replace);
                HistoryOps.Record(doc, "ai:feather");
                Log("選択の羽化refineを適用");
            }
            catch (Exception ex) { Log("羽化 failed: " + ex.Message); }
            RefreshAll();
        };
        bBg.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("背景hint: 対象層なし"); return; }
            try
            {
                var proposal = AiAssistOps.ProposeBackgroundMask(l.Bitmap, (int)Math.Round(tol.Value));
                int fg = proposal.Count(b => b != 0);
                Log($"背景hint提案: 前景 {fg}px ({proposal.Length}px中)。適用は確認チェックつきでのみ。");
                if (confirmBg.IsChecked == true)
                {
                    var sel = new byte[proposal.Length];
                    for (int i = 0; i < sel.Length; i++) sel[i] = proposal[i] != 0 ? (byte)1 : (byte)0;
                    SelectionTools.ApplyMask(doc, sel, SelectionKind.Wand, null, SelectionMode.Replace);
                    HistoryOps.Record(doc, "ai:bg-hint-apply");
                    Log("背景hintを選択へ適用 (opt-in確認済み)");
                    RefreshAll();
                }
            }
            catch (Exception ex) { Log("背景hint failed: " + ex.Message); }
        };
        bNoise.Click += (_, _) =>
        {
            if (Selected is not { Bitmap: not null } l) { Log("保護noise: 対象層なし"); return; }
            dlg.Close();
            undoStack.Push(doc);
            try
            {
                Document.EnsureUniqueBitmap(l);
                AiAssistOps.Disturbance(l.Bitmap, disturb.Value);
                HistoryOps.Record(doc, "ai:disturbance");
                Log("保護noiseを適用 (Undoで可逆)");
            }
            catch (Exception ex) { Log("保護noise failed: " + ex.Message); }
            RefreshAll();
        };
        bClose.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }
}
