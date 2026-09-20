// Compositor — Transform / Crop / Distort / Canvas-size UI wiring
// (Mac TransformInspector / CropControls / CanvasSizeSheet / ImageSizeSheet subset).
// Partial class: model logic lives in TransformTools.cs (unit-tested there);
// this file only wires canvas gestures, numeric fields and dialogs.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkiaSharp;

namespace Compositor;

public partial class MainWindow
{
    // --- Distort session (Mac TransformEdit.corners subset: draft only, applied on Enter) ---
    Layer distortLayer;
    SKPoint[] distortOriginal;    // 4 corners (doc px) when the edit began
    SKPoint[] distortCorners;     // current draft (null = no edit)
    SKPoint distortStart;         // press point (doc px)
    int distortHandle = -1;       // 0..3 corner, -1 = body move
    bool distortDragging;

    // --- Crop session (Mac cropRect + CropDrag subset) ---
    SKRect? cropRect;
    CropDrag cropDrag;
    bool cropSpaceHeld;           // Space = move frame (Mac Space-drag parity)

    // ---------- tool entry ----------

    void BeginDistortTool()
    {
        if (Selected is not { Bitmap: not null } l || l.Locked)
        {
            Log("Distort: 層を選択してください");
            SetTool(Tool.Move);
            return;
        }
        distortLayer = l;
        var placement = new LayerPlacement(l.Position, new SKSize(l.DrawW, l.DrawH),
            l.Rotation, l.FlipH, l.FlipV);
        distortOriginal = DistortWarp.CornersOf(placement);
        distortCorners = (SKPoint[])distortOriginal.Clone();
        distortDragging = false;
        SetTool(Tool.Distort);
        RefreshToolHeader();
        RenderCanvas();
        Log($"Distort start layer='{l.Name}'");
    }

    void CancelDistortSilently()
    {
        distortLayer = null; distortOriginal = null; distortCorners = null;
        distortDragging = false; distortHandle = -1;
    }

    // ---------- numeric transform (Mac TransformInspector subset) ----------

    static bool TryNum(string s, out float v)
        => float.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v)
           && float.IsFinite(v);

    void SyncTransformPanel(Layer sel)
    {
        if (TxX != null) TxX.Text = $"{sel.Position.X:F0}";
        if (TxY != null) TxY.Text = $"{sel.Position.Y:F0}";
        if (TxW != null) TxW.Text = $"{sel.DrawW:F0}";
        if (TxH != null) TxH.Text = $"{sel.DrawH:F0}";
        if (TxAngle != null) TxAngle.Text = $"{sel.Rotation:F0}";
        if (SamplingBox != null) SamplingBox.SelectedIndex = sel.Sampling switch
        {
            LayerSampling.Nearest => 0, LayerSampling.Smooth => 1, _ => 2,
        };
    }

    void OnTransformApply(object s, RoutedEventArgs e)
    {
        if (Selected is not { Bitmap: not null } l || l.Locked)
        {
            Log("Transform数値: 層を選択してください");
            return;
        }
        if (!TryNum(TxX?.Text, out float x) || !TryNum(TxY?.Text, out float y) ||
            !TryNum(TxW?.Text, out float w) || !TryNum(TxH?.Text, out float h) ||
            !TryNum(TxAngle?.Text, out float angle))
        {
            Log("Transform数値: 数値を入力してください (X Y W H °)");
            return;
        }
        bool locked = LockRatioCheck?.IsChecked == true;
        undoStack.Push(doc);
        if (!CanvasOps.ApplyNumeric(l, x, y, w, h, angle, locked))
        {
            DoUndo();   // 無効値は履歴に残さない
            Log("Transform数値: 範囲外 (W/Hは1以上・座標は±100万以内)");
            return;
        }
        Log($"Transform数値 pos=({x:F0},{y:F0}) size={w:F0}x{h:F0} rot={angle:F0} lock={locked}");
        RefreshAll();
    }

    void OnSamplingChanged(object s, SelectionChangedEventArgs e)
    {
        if (Selected is not { } l || !uiReady || updatingList) return;
        var sampling = (SamplingBox?.SelectedIndex ?? 2) switch
        {
            0 => LayerSampling.Nearest, 1 => LayerSampling.Smooth, _ => LayerSampling.High,
        };
        if (l.Sampling == sampling) return;
        undoStack.Push(doc);
        l.Sampling = sampling;
        Log($"Sampling={sampling}");
        RenderCanvas();
    }

    // ---------- Distort canvas gestures ----------

    static int NearestDistortCorner(SKPoint[] corners, SKPoint p, float tolerance)
    {
        int best = -1; float bestD = tolerance;
        for (int i = 0; i < corners.Length; i++)
        {
            float dx = corners[i].X - p.X, dy = corners[i].Y - p.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    static bool PointInQuad(SKPoint[] q, SKPoint p)
    {
        bool inside = false;
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            var a = q[i]; var b = q[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Returns true when the press was consumed by the Distort tool.</summary>
    bool DistortPressed(SKPoint docPoint)
    {
        if (currentTool != Tool.Distort || distortCorners == null) return false;
        distortStart = docPoint;
        distortHandle = NearestDistortCorner(distortCorners, docPoint, 12 / Math.Max(0.1f, zoom));
        if (distortHandle < 0 && !PointInQuad(distortCorners, docPoint))
        {
            Log("Distort: 角の近くを掴んでください (Shift=軸固定)");
            return true;
        }
        distortDragging = true;
        return true;
    }

    /// <summary>Returns true when the move was consumed by the Distort tool.</summary>
    bool DistortMoved(SKPoint docPoint, KeyModifiers mods)
    {
        if (currentTool != Tool.Distort || !distortDragging || distortCorners == null) return false;
        bool shift = mods.HasFlag(KeyModifiers.Shift);
        var next = distortHandle < 0
            ? DistortWarp.DragCorners(distortOriginal, distortStart, docPoint, 0, shift, moveBody: true)
            : DistortWarp.DragCorners(distortOriginal, distortStart, docPoint,
                distortHandle * 2, shift);
        // DragCorners refuses twisted shapes (returns null): keep the last usable draft.
        if (next == null)
        {
            // Retry from the current draft so an already-distorted shape keeps editing.
            next = distortHandle < 0
                ? DistortWarp.DragCorners(distortCorners, distortStart, docPoint, 0, shift, moveBody: true)
                : DistortWarp.DragCorners(distortCorners, distortStart, docPoint,
                    distortHandle * 2, shift);
        }
        if (next != null)
        {
            distortCorners = next;
            distortStart = docPoint;
            distortOriginal = (SKPoint[])distortCorners.Clone();   // incremental drags
            RenderCanvas();
        }
        return true;
    }

    void DistortReleased()
    {
        distortDragging = false;
        if (currentTool == Tool.Distort) RefreshToolHeader();
    }

    void OnDistortApply(object s, RoutedEventArgs e) => ApplyDistort();

    void ApplyDistort()
    {
        if (distortLayer == null || distortCorners == null)
        {
            Log("Distort: 適用する歪みがありません");
            return;
        }
        var l = distortLayer;
        if (l.Bitmap == null || !doc.Layers.Contains(l))
        {
            Log("Distort: 層が無効です");
            CancelDistortSilently();
            return;
        }
        undoStack.Push(doc);
        var warped = DistortWarp.Warp(l.Bitmap, distortCorners, l.FlipH, l.FlipV, l.Sampling);
        if (warped == null)
        {
            DoUndo();
            Log("Distort: 適用できません (形状が潰れているか寸法超過)");
            return;
        }
        l.Bitmap = warped.Value.bmp;
        l.Position = warped.Value.origin;
        l.ScaleX = 1; l.ScaleY = 1;   // pixels resampled into the shape (Mac warpTrimmed parity)
        l.Rotation = 0; l.FlipH = false; l.FlipV = false;
        Log($"Distort apply {warped.Value.bmp.Width}x{warped.Value.bmp.Height} @({warped.Value.origin.X:F0},{warped.Value.origin.Y:F0})");
        CancelDistortSilently();
        SetTool(Tool.Move);
        RefreshAll();
    }

    void OnDistortCancel(object s, RoutedEventArgs e) => CancelDistort();

    void CancelDistort()
    {
        if (distortCorners == null) return;
        CancelDistortSilently();
        Log("Distort cancelled");
        SetTool(Tool.Move);
        RenderCanvas();
    }

    // ---------- Crop canvas gestures (Mac CropControls/Crop.swift subset) ----------

    float? CropRatio()
    {
        return (CropRatioBox?.SelectedIndex ?? 0) switch
        {
            1 => doc.Width > 0 && doc.Height > 0 ? (float)doc.Width / doc.Height : null,
            2 => 1f,
            3 => 4f / 3,
            4 => 16f / 9,
            _ => null,
        };
    }

    void OnCropRatioChanged(object s, SelectionChangedEventArgs e)
    {
        if (cropRect is not SKRect r) return;
        var ratio = CropRatio();
        if (ratio == null) return;
        float h = r.Width / ratio.Value;
        var next = CropGeometry.Snapped(new SKRect(r.Left, (r.Top + r.Bottom) / 2 - h / 2, r.Right, (r.Top + r.Bottom) / 2 + h / 2));
        if (CropGeometry.Valid(next))
        {
            cropRect = next;
            Log($"Crop ratio -> {next.Width:F0}x{next.Height:F0}");
            RenderCanvas();
        }
    }

    static int NearestCropHandle(SKRect r, SKPoint p, float tolerance)
    {
        var pts = new[]
        {
            new SKPoint(r.Left, r.Top), new SKPoint((r.Left + r.Right) / 2, r.Top),
            new SKPoint(r.Right, r.Top), new SKPoint(r.Right, (r.Top + r.Bottom) / 2),
            new SKPoint(r.Right, r.Bottom), new SKPoint((r.Left + r.Right) / 2, r.Bottom),
            new SKPoint(r.Left, r.Bottom), new SKPoint(r.Left, (r.Top + r.Bottom) / 2),
        };
        int best = -1; float bestD = tolerance;
        for (int i = 0; i < pts.Length; i++)
        {
            float dx = pts[i].X - p.X, dy = pts[i].Y - p.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Returns true when the press was consumed by the Crop tool.</summary>
    bool CropPressed(SKPoint docPoint, KeyModifiers mods)
    {
        if (currentTool != Tool.Crop) return false;
        bool symmetric = mods.HasFlag(KeyModifiers.Alt);
        if (cropSpaceHeld && cropRect is SKRect move)
        {
            cropDrag = new CropDrag(docPoint, move, CropDrag.Mode.Move);
            return true;
        }
        if (cropRect is SKRect r)
        {
            int handle = NearestCropHandle(r, docPoint, 10 / Math.Max(0.1f, zoom));
            if (handle >= 0)
            {
                cropDrag = new CropDrag(docPoint, r, CropDrag.Mode.Resize, handle);
                return true;
            }
            if (docPoint.X >= r.Left && docPoint.X <= r.Right && docPoint.Y >= r.Top && docPoint.Y <= r.Bottom)
            {
                cropDrag = new CropDrag(docPoint, r, CropDrag.Mode.Move);
                return true;
            }
        }
        cropDrag = new CropDrag(docPoint, new SKRect(docPoint.X, docPoint.Y, docPoint.X, docPoint.Y),
            CropDrag.Mode.Create);
        cropDragSymmetric = symmetric;
        return true;
    }

    bool cropDragSymmetric;

    /// <summary>Returns true when the move was consumed by the Crop tool.</summary>
    bool CropMoved(SKPoint docPoint, KeyModifiers mods)
    {
        if (currentTool != Tool.Crop || cropDrag == null) return false;
        bool symmetric = mods.HasFlag(KeyModifiers.Alt) || cropDragSymmetric;
        cropRect = cropDrag.Updated(docPoint, CropRatio(), symmetric);
        RenderCanvas();
        return true;
    }

    void CropReleased()
    {
        if (currentTool != Tool.Crop || cropDrag == null) return;
        cropDrag = null;
        cropDragSymmetric = false;
        RefreshToolHeader();
        RenderCanvas();
    }

    void OnCropApply(object s, RoutedEventArgs e) => ApplyCrop();

    void ApplyCrop()
    {
        if (cropRect is not SKRect r)
        {
            Log("Crop: 枠がありません (Dragで枠作成)");
            return;
        }
        undoStack.Push(doc);
        if (!CanvasOps.Crop(doc, r))
        {
            DoUndo();
            Log("Crop: 適用できません (寸法範囲外)");
            return;
        }
        Log($"Crop apply {r.Width:F0}x{r.Height:F0} @({r.Left:F0},{r.Top:F0})");
        cropRect = null;
        RefreshAll();
    }

    void OnCropCancel(object s, RoutedEventArgs e) => CancelCrop();

    void CancelCrop()
    {
        if (cropRect == null && cropDrag == null) return;
        cropRect = null; cropDrag = null;
        Log("Crop cancelled");
        RenderCanvas();
        RefreshToolHeader();
    }

    // ---------- Canvas size / Image size / Flip canvas ----------

    async void OnCanvasSize(object s, RoutedEventArgs e)
    {
        var options = await TransformDialogs.AskCanvasSize(this, doc.Width, doc.Height);
        if (options == null) return;
        undoStack.Push(doc);
        if (!CanvasOps.Resize(doc, options))
        {
            DoUndo();
            Log("Canvas Size: 適用できません (1-30000px以内)");
            return;
        }
        Log($"Canvas Size -> {options.Width}x{options.Height} anchor={options.Anchor}");
        RefreshAll();
    }

    async void OnImageSize(object s, RoutedEventArgs e)
    {
        var options = await TransformDialogs.AskImageSize(this, doc.Width, doc.Height);
        if (options == null) return;
        undoStack.Push(doc);
        if (!CanvasOps.ImageSize(doc, options))
        {
            DoUndo();
            Log("Image Size: 適用できません (1-30000px・100MP以内)");
            return;
        }
        Log($"Image Size -> {options.Width}x{options.Height} resample={options.Resample} sampling={options.Sampling}");
        RefreshAll();
    }

    void OnFlipCanvasH(object s, RoutedEventArgs e) => DoFlipCanvas(true);
    void OnFlipCanvasV(object s, RoutedEventArgs e) => DoFlipCanvas(false);

    void DoFlipCanvas(bool horizontally)
    {
        undoStack.Push(doc);
        if (!CanvasOps.FlipCanvas(doc, horizontally))
        {
            DoUndo();
            Log("Flip Canvas: 適用できません");
            return;
        }
        Log(horizontally ? "Flip Canvas Horizontal" : "Flip Canvas Vertical");
        RefreshAll();
    }

    // ---------- overlays ----------

    void DrawTransformOverlays(SKCanvas canvas)
    {
        if (currentTool == Tool.Crop && cropRect is SKRect r)
        {
            var rr = new SKRect(r.Left * zoom, r.Top * zoom, r.Right * zoom, r.Bottom * zoom);
            DimOutside(canvas, rr);
            MarchingAnts(canvas, p => canvas.DrawRect(rr, p));
            using var dot = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var dotB = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
            var pts = new[]
            {
                new SKPoint(rr.Left, rr.Top), new SKPoint((rr.Left + rr.Right) / 2, rr.Top),
                new SKPoint(rr.Right, rr.Top), new SKPoint(rr.Right, (rr.Top + rr.Bottom) / 2),
                new SKPoint(rr.Right, rr.Bottom), new SKPoint((rr.Left + rr.Right) / 2, rr.Bottom),
                new SKPoint(rr.Left, rr.Bottom), new SKPoint(rr.Left, (rr.Top + rr.Bottom) / 2),
            };
            foreach (var p in pts)
            {
                canvas.DrawCircle(p.X, p.Y, 5, dotB);
                canvas.DrawCircle(p.X, p.Y, 4, dot);
            }
        }
        if (currentTool == Tool.Distort && distortCorners != null)
        {
            var q = distortCorners.Select(p => new SKPoint(p.X * zoom, p.Y * zoom)).ToArray();
            using var line = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            using var dot = new SKPaint { Color = SKColors.Yellow, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var dotB = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var path = new SKPath();
            path.MoveTo(q[0]);
            path.LineTo(q[1]); path.LineTo(q[2]); path.LineTo(q[3]); path.Close();
            canvas.DrawPath(path, line);
            foreach (var p in q)
            {
                canvas.DrawCircle(p.X, p.Y, 6, dotB);
                canvas.DrawCircle(p.X, p.Y, 5, dot);
            }
        }
    }

    void DimOutside(SKCanvas canvas, SKRect inside)
    {
        float w = doc.Width * zoom, h = doc.Height * zoom;
        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 120) };
        canvas.DrawRect(new SKRect(0, 0, w, inside.Top), dim);
        canvas.DrawRect(new SKRect(0, inside.Bottom, w, h), dim);
        canvas.DrawRect(new SKRect(0, inside.Top, inside.Left, inside.Bottom), dim);
        canvas.DrawRect(new SKRect(inside.Right, inside.Top, w, inside.Bottom), dim);
    }
}

/// <summary>Canvas Size / Image Size dialogs (Mac CanvasSizeSheet / ImageSizeSheet subset),
/// built in code so no extra XAML is needed.</summary>
public static class TransformDialogs
{
    public static async System.Threading.Tasks.Task<CanvasSizeOptions> AskCanvasSize(
        Window parent, int curW, int curH)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<CanvasSizeOptions>();
        var win = new Window { Title = "Canvas Size", Width = 360, Height = 420 };
        var wBox = new TextBox { Text = curW.ToString(), Width = 100 };
        var hBox = new TextBox { Text = curH.ToString(), Width = 100 };
        var anchorBox = new ComboBox { Width = 220, SelectedIndex = 4 };
        foreach (var n in new[] { "Top left", "Top center", "Top right", "Middle left", "Center",
            "Middle right", "Bottom left", "Bottom center", "Bottom right" })
            anchorBox.Items.Add(n);
        var fillBox = new ComboBox { Width = 160, SelectedIndex = 0 };
        foreach (var n in new[] { "Transparent", "White", "Black" }) fillBox.Items.Add(n);
        var msg = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed };
        var ok = new Button { Content = "OK" };
        var cancel = new Button { Content = "Cancel" };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(wBox.Text, out int w) || !int.TryParse(hBox.Text, out int h))
            {
                msg.Text = "整数を入力してください";
                return;
            }
            var opt = new CanvasSizeOptions(w, h, anchorBox.SelectedIndex,
                (fillBox.SelectedIndex switch { 1 => (SKColor?)SKColors.White, 2 => (SKColor?)SKColors.Black, _ => null }));
            if (!opt.Valid)
            {
                msg.Text = "1–30,000 px の範囲で入力してください";
                return;
            }
            tcs.TrySetResult(opt);
            win.Close();
        };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(null);
        win.Content = new StackPanel
        {
            Spacing = 8,
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = $"Current: {curW} × {curH} px" },
                Row("Width", wBox), Row("Height", hBox),
                Row("Anchor", anchorBox), Row("Extension", fillBox), msg,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        await win.ShowDialog(parent);
        return await tcs.Task;
    }

    public static async System.Threading.Tasks.Task<ImageSizeOptions> AskImageSize(
        Window parent, int curW, int curH)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<ImageSizeOptions>();
        var win = new Window { Title = "Image Size", Width = 360, Height = 400 };
        var wBox = new TextBox { Text = curW.ToString(), Width = 100 };
        var hBox = new TextBox { Text = curH.ToString(), Width = 100 };
        var lockCheck = new CheckBox { Content = "Lock aspect ratio", IsChecked = true };
        var resampleCheck = new CheckBox { Content = "Resample", IsChecked = true };
        var samplingBox = new ComboBox { Width = 160, SelectedIndex = 2 };
        foreach (var n in new[] { "Nearest", "Smooth", "High quality" }) samplingBox.Items.Add(n);
        var msg = new TextBlock { Foreground = Avalonia.Media.Brushes.OrangeRed };
        // Aspect-lock: editing one side recomputes the other (Mac locked behavior).
        bool syncing = false;
        void SyncFrom(TextBox from, TextBox to, bool widthAxis)
        {
            if (syncing || lockCheck.IsChecked != true) return;
            if (!double.TryParse(from.Text, out double v) || v <= 0) return;
            syncing = true;
            try { to.Text = ((int)Math.Round((widthAxis ? curH : curW) * v / (widthAxis ? curW : curH))).ToString(); }
            finally { syncing = false; }
        }
        wBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text)) SyncFrom(wBox, hBox, true);
        };
        hBox.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text)) SyncFrom(hBox, wBox, false);
        };
        var ok = new Button { Content = "Resize" };
        var cancel = new Button { Content = "Cancel" };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(wBox.Text, out int w) || !int.TryParse(hBox.Text, out int h))
            {
                msg.Text = "整数を入力してください";
                return;
            }
            var opt = new ImageSizeOptions(w, h)
            {
                Resample = resampleCheck.IsChecked == true,
                Sampling = (samplingBox.SelectedIndex switch { 0 => LayerSampling.Nearest, 1 => LayerSampling.Smooth, _ => LayerSampling.High }),
            };
            if (!opt.Valid)
            {
                msg.Text = "1–30,000 px・100MP以内で入力してください";
                return;
            }
            tcs.TrySetResult(opt);
            win.Close();
        };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(null);
        win.Content = new StackPanel
        {
            Spacing = 8,
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = $"Current: {curW} × {curH} px" },
                Row("Width", wBox), Row("Height", hBox),
                lockCheck, resampleCheck, Row("Sampling", samplingBox), msg,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        await win.ShowDialog(parent);
        return await tcs.Task;
    }

    static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        Spacing = 8,
        Children = { new TextBlock { Text = label, Width = 80, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, control },
    };
}
