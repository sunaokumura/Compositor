// Compositor — P2 世界最良規範の表現拡張 (docs/WORLD_BEST_PAINT.md §4 P2).
// 9 engine分化の入口 (Smudge・Mixer先行) / Wrap-Around・対称・透視助手 /
// mesh→puppet / History保存・macro / workspace preset・Python API下地 /
// HDR・ICC・Gamut / Export slice・compound mask・blend range /
// 3D素材・動画層 / AI系 (手動下位・opt-inのみ)。
// いずれも現行 DocumentModel/LayerSystem/Adjustments の延長。推測の外部仕様は
// 持ち込まず、各型の XML doc に典拠相当の説明を記す。不採用項目
// (生成AI Fill・ML自動切抜・Vision系・MSIX署名) は対象外維持。
using SkiaSharp;

namespace Compositor;

// ---------- Mixer筆 (Photoshop Mixer Brushの考え・自前実装) ----------
// 「濡れ絵具の混ざり」を Wet (canvasからの拾い)・Load (筆の絵具量)・Mix
// (混色率) の三係数で再現する。SmudgeStroke のにじみとは別物で、筆色と
// 運搬色 carried の線形混合を dab 毎に行う。決定性あり・Undoは呼出側。

/// <summary>Mixer筆の三係数 (0..1)。Wet=拾い・Load=筆量・Mix=混色率。</summary>
public class MixerSettings
{
    public double Wet = 0.5;
    public double Load = 1.0;
    public double Mix = 0.5;
    public bool IsValid => Wet is >= 0 and <= 1 && Load is >= 0 and <= 1 && Mix is >= 0 and <= 1;
    public MixerSettings Clone() => new() { Wet = Wet, Load = Load, Mix = Mix };
}

/// <summary>Mixer筆の絵具運搬 stroke (Photoshop Mixer Brushの考え)。</summary>
public static class MixerOps
{
    static SKColor AvgUnder(SKBitmap bmp, SKPoint c, float radius)
    {
        long r = 0, g = 0, b = 0, a = 0, n = 0;
        int x0 = Math.Max(0, (int)MathF.Floor(c.X - radius)), x1 = Math.Min(bmp.Width - 1, (int)MathF.Ceiling(c.X + radius));
        int y0 = Math.Max(0, (int)MathF.Floor(c.Y - radius)), y1 = Math.Min(bmp.Height - 1, (int)MathF.Ceiling(c.Y + radius));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var px = bmp.GetPixel(x, y);
                if (px.Alpha == 0) continue;
                r += px.Red; g += px.Green; b += px.Blue; a += px.Alpha; n++;
            }
        if (n == 0) return SKColor.Empty;
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
    }

    static SKColor Lerp(SKColor a, SKColor b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return new SKColor(
            (byte)Math.Round(a.Red + (b.Red - a.Red) * k),
            (byte)Math.Round(a.Green + (b.Green - a.Green) * k),
            (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * k),
            (byte)Math.Round(a.Alpha + (b.Alpha - a.Alpha) * k));
    }

    /// <summary>stroke を適用する。戻り値は最終運搬色 carried。</summary>
    public static SKColor ApplyStroke(SKBitmap bmp, IList<SKPoint> points, SKColor brushColor,
        float diameter, MixerSettings settings, float opacity = 1f)
    {
        if (bmp == null) throw new InvalidOperationException("No bitmap");
        if (settings == null || !settings.IsValid) throw new InvalidOperationException("Bad mixer settings");
        if (points == null || points.Count == 0 || diameter <= 0) return brushColor;
        var carried = Lerp(SKColor.Empty, brushColor, settings.Load);
        if (carried.Alpha == 0) carried = brushColor;
        float radius = Math.Max(0.5f, diameter / 2);
        using var canvas = new SKCanvas(bmp);
        foreach (var p in points)
        {
            var canvasAvg = AvgUnder(bmp, p, radius);
            if (canvasAvg.Alpha != 0)
                carried = Lerp(carried, canvasAvg, settings.Wet);
            var deposit = Lerp(brushColor, carried, settings.Mix);
            using var paint = new SKPaint
            {
                Color = deposit.WithAlpha((byte)Math.Round(opacity * 255)),
                Style = SKPaintStyle.Fill, IsAntialias = true, BlendMode = SKBlendMode.SrcOver,
            };
            canvas.DrawCircle(p, radius, paint);
        }
        return carried;
    }
}

// ---------- 対称・Wrap-Around (GIMP Symmetry・Krita Wrapの考え) ----------

public enum SymmetryMode { Off, MirrorH, MirrorV, MirrorHV, Kaleido4 }

/// <summary>対称描画 (GIMP Symmetry Paintingの考え): stroke点列の鏡像複製。</summary>
public static class SymmetryOps
{
    public static List<SKPoint> TransformPoints(IList<SKPoint> points, int w, int h, SymmetryMode mode)
    {
        var src = (points ?? Enumerable.Empty<SKPoint>()).ToList();
        var out_ = new List<SKPoint>(src);
        if (mode == SymmetryMode.Off || w <= 0 || h <= 0) return out_;
        float cx = w / 2f, cy = h / 2f;
        void Add(float x, float y) => out_.Add(new SKPoint(x, y));
        foreach (var p in src)
        {
            switch (mode)
            {
                case SymmetryMode.MirrorH: Add(2 * cx - p.X, p.Y); break;
                case SymmetryMode.MirrorV: Add(p.X, 2 * cy - p.Y); break;
                case SymmetryMode.MirrorHV:
                    Add(2 * cx - p.X, p.Y); Add(p.X, 2 * cy - p.Y); Add(2 * cx - p.X, 2 * cy - p.Y);
                    break;
                case SymmetryMode.Kaleido4:
                    // 4回対称: 鏡像＋90°回転複製 (中心周り)
                    Add(2 * cx - p.X, p.Y);
                    float dx = p.X - cx, dy = p.Y - cy;
                    Add(cx - dy, cy + dx); Add(cx + dy, cy - dx);
                    break;
            }
        }
        return out_;
    }

    /// <summary>層ビットマップの鏡像反転複製 (新規層の画素として使う)。</summary>
    public static SKBitmap MirrorBitmap(SKBitmap src, bool flipH, bool flipV)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(dst);
        float cx = src.Width / 2f, cy = src.Height / 2f;
        canvas.Translate(cx, cy);
        canvas.Scale(flipH ? -1 : 1, flipV ? -1 : 1);
        canvas.Translate(-cx, -cy);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }
}

/// <summary>Wrap-Around (Krita Wキーの考え): tiling継ぎ目なし作画の下地。</summary>
public static class WrapOps
{
    /// <summary>点を文書内に折り返す (tiling live表示の下地)。</summary>
    public static SKPoint WrapPoint(SKPoint p, int w, int h)
    {
        if (w <= 0 || h <= 0) return p;
        float x = ((p.X % w) + w) % w, y = ((p.Y % h) + h) % h;
        return new SKPoint(x, y);
    }

    /// <summary>stroke点列に9近傍の折返し複製を加える (継ぎ目またぎ描画)。</summary>
    public static List<SKPoint> WrapStroke(IList<SKPoint> points, int w, int h)
    {
        var src = (points ?? Enumerable.Empty<SKPoint>()).ToList();
        var out_ = new List<SKPoint>(src);
        if (w <= 0 || h <= 0) return out_;
        foreach (var p in src)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    out_.Add(new SKPoint(p.X + dx * w, p.Y + dy * h));
                }
        return out_;
    }

    /// <summary>tiling preview: 文書合成の2x2並置 (継ぎ目確認用)。</summary>
    public static SKBitmap RenderTiled(SKBitmap src)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        var dst = new SKBitmap(src.Width * 2, src.Height * 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(dst);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                canvas.DrawBitmap(src, x * src.Width, y * src.Height);
        return dst;
    }
}

// ---------- 透視助手 (Krita drawing assistants・SAI perspective rulerの考え) ----------

/// <summary>透視消失点への吸着 (消失点drag・軸吸着の考え)。</summary>
public static class PerspectiveOps
{
    /// <summary>点pを消失点vpからの放射線のうち最近傍へ吸着する。15°刻み guides。</summary>
    public static SKPoint Snap(SKPoint p, SKPoint vp, float tolerancePx = 8f)
    {
        float dx = p.X - vp.X, dy = p.Y - vp.Y;
        if (MathF.Sqrt(dx * dx + dy * dy) < 1e-3f) return p;
        double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double snapped = Math.Round(ang / 15.0) * 15.0 * Math.PI / 180.0;
        double len = Math.Sqrt(dx * dx + dy * dy);
        var q = new SKPoint(vp.X + (float)(Math.Cos(snapped) * len), vp.Y + (float)(Math.Sin(snapped) * len));
        double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
        return d <= tolerancePx ? q : p;
    }

    public static List<SKPoint> SnapStroke(IList<SKPoint> points, SKPoint vp, float tolerancePx = 8f) =>
        (points ?? Enumerable.Empty<SKPoint>()).Select(p => Snap(p, vp, tolerancePx)).ToList();
}

// ---------- mesh→puppet (CLIP STUDIO mesh変形・puppet warpの考え・入口) ----------

/// <summary>粗格子ワープ (mesh先行・puppetは上位)。格子点offsetの双線形写像。</summary>
public class MeshWarp
{
    public int GridW = 4;   // 格子点数 (2..16)
    public int GridH = 4;
    public float[,] Dx;     // [GridH, GridW] x offset (px)
    public float[,] Dz;     // [GridH, GridW] y offset (px)
    public bool IsValid => GridW is >= 2 and <= 16 && GridH is >= 2 and <= 16
        && Dx != null && Dz != null && Dx.GetLength(0) == GridH && Dx.GetLength(1) == GridW
        && Dz.GetLength(0) == GridH && Dz.GetLength(1) == GridW;
    public static MeshWarp Identity(int gw = 4, int gh = 4) => new()
    {
        GridW = gw, GridH = gh,
        Dx = new float[gh, gw], Dz = new float[gh, gw],
    };
    public MeshWarp Clone()
    {
        var c = new MeshWarp { GridW = GridW, GridH = GridH,
            Dx = (float[,])Dx.Clone(), Dz = (float[,])Dz.Clone() };
        return c;
    }
}

public static class MeshOps
{
    static SKColor SampleBilinear(SKBitmap bmp, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        SKColor C(int xx, int yy)
        {
            xx = Math.Clamp(xx, 0, bmp.Width - 1); yy = Math.Clamp(yy, 0, bmp.Height - 1);
            return bmp.GetPixel(xx, yy);
        }
        var c00 = C(x0, y0); var c10 = C(x0 + 1, y0); var c01 = C(x0, y0 + 1); var c11 = C(x0 + 1, y0 + 1);
        byte Ch(Func<SKColor, byte> f) => (byte)Math.Round(
            f(c00) * (1 - fx) * (1 - fy) + f(c10) * fx * (1 - fy) + f(c01) * (1 - fx) * fy + f(c11) * fx * fy);
        return new SKColor(Ch(c => c.Red), Ch(c => c.Green), Ch(c => c.Blue), Ch(c => c.Alpha));
    }

    /// <summary>mesh変形を適用する (恒等格子は無操作)。CPU逆写像。</summary>
    public static SKBitmap Apply(SKBitmap src, MeshWarp warp)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        if (warp == null || !warp.IsValid) throw new InvalidOperationException("Bad mesh");
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        int gw = warp.GridW, gh = warp.GridH;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                double gx = (double)x / Math.Max(1, src.Width - 1) * (gw - 1);
                double gy = (double)y / Math.Max(1, src.Height - 1) * (gh - 1);
                int ix = Math.Min(gw - 2, (int)Math.Floor(gx)), iy = Math.Min(gh - 2, (int)Math.Floor(gy));
                double fx = gx - ix, fy = gy - iy;
                double dx = warp.Dx[iy, ix] * (1 - fx) * (1 - fy) + warp.Dx[iy, ix + 1] * fx * (1 - fy)
                    + warp.Dx[iy + 1, ix] * (1 - fx) * fy + warp.Dx[iy + 1, ix + 1] * fx * fy;
                double dy = warp.Dz[iy, ix] * (1 - fx) * (1 - fy) + warp.Dz[iy, ix + 1] * fx * (1 - fy)
                    + warp.Dz[iy + 1, ix] * (1 - fx) * fy + warp.Dz[iy + 1, ix + 1] * fx * fy;
                dst.SetPixel(x, y, SampleBilinear(src, (float)(x - dx), (float)(y - dy)));
            }
        return dst;
    }
}

/// <summary>puppet warpの入口: ハンドル1点のガウス減衰移動 (mesh上位)。</summary>
public static class PuppetOps
{
    public static SKBitmap Apply(SKBitmap src, SKPoint handle, SKPoint delta, float radiusPx)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        if (radiusPx <= 0) throw new InvalidOperationException("Bad radius");
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        float r2 = radiusPx * radiusPx;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                double d2 = (x - handle.X) * (x - handle.X) + (y - handle.Y) * (y - handle.Y);
                double k = Math.Exp(-d2 / (2 * r2 * 0.25));
                float sx = (float)(x - delta.X * k), sy = (float)(y - delta.Y * k);
                sx = Math.Clamp(sx, 0, src.Width - 1); sy = Math.Clamp(sy, 0, src.Height - 1);
                int x0 = Math.Min(src.Width - 2, (int)sx), y0 = Math.Min(src.Height - 2, (int)sy);
                float fx = sx - x0, fy = sy - y0;
                var c00 = src.GetPixel(x0, y0); var c10 = src.GetPixel(x0 + 1, y0);
                var c01 = src.GetPixel(x0, y0 + 1); var c11 = src.GetPixel(x0 + 1, y0 + 1);
                byte Ch(Func<SKColor, byte> f) => (byte)Math.Round(
                    f(c00) * (1 - fx) * (1 - fy) + f(c10) * fx * (1 - fy) + f(c01) * (1 - fx) * fy + f(c11) * fx * fy);
                dst.SetPixel(x, y, new SKColor(Ch(c => c.Red), Ch(c => c.Green), Ch(c => c.Blue), Ch(c => c.Alpha)));
            }
        return dst;
    }
}

// ---------- History保存・macro (Affinity history保存の考え) ----------
// Affinityの教訓 (.comp肥大) のため上限200件・画素なし・操作名＋時刻＋層数のみ。

public class HistoryEntry
{
    public DateTime Time;
    public string Action = "";
    public int Layers;
}

public static class HistoryOps
{
    public const int MaxEntries = 200;
    public static void Record(Document doc, string action)
    {
        if (doc == null || string.IsNullOrWhiteSpace(action)) return;
        if (doc.History.Count >= MaxEntries) doc.History.RemoveAt(0);
        doc.History.Add(new HistoryEntry { Time = DateTime.UtcNow, Action = action.Trim(), Layers = doc.Layers.Count });
    }
    public static void Clear(Document doc) => doc?.History.Clear();
    public static string ExportText(Document doc)
    {
        if (doc == null) return "";
        var lines = new List<string> { $"# history {doc.Name} ({doc.History.Count} events)" };
        foreach (var e in doc.History) lines.Add($"{e.Time:O} [{e.Layers} layers] {e.Action}");
        return string.Join("\n", lines) + "\n";
    }
}

public enum MacroOp { Brightness, Contrast, Invert }

public class MacroStep
{
    public MacroOp Op;
    public double Value;   // Brightness/Contrast用 (-100..100)。Invertは無視。
}

public static class MacroOps
{
    public const int MaxSteps = 64;
    /// <summary>macro記録 (単純調整opのみ・決定性)。</summary>
    public static void Record(Document doc, MacroOp op, double value = 0)
    {
        if (doc == null) return;
        if (doc.Macro.Count >= MaxSteps) throw new InvalidOperationException("Too many macro steps");
        if (op != MacroOp.Invert && (value is < -100 or > 100 || !double.IsFinite(value)))
            throw new InvalidOperationException("Bad macro value");
        doc.Macro.Add(new MacroStep { Op = op, Value = value });
    }
    /// <summary>macro再生: 選択層へ破壊適用 (呼出側でUndo push)。</summary>
    public static void Replay(Layer layer, IList<MacroStep> steps)
    {
        if (layer?.Bitmap == null) throw new InvalidOperationException("No layer");
        if (steps == null || steps.Count == 0) return;
        Document.EnsureUniqueBitmap(layer);
        foreach (var s in steps)
        {
            switch (s.Op)
            {
                case MacroOp.Brightness: layer.Brightness = (float)Math.Clamp(layer.Brightness + s.Value, -100, 100); break;
                case MacroOp.Contrast: layer.Contrast = (float)Math.Clamp(layer.Contrast + s.Value, -100, 100); break;
                case MacroOp.Invert: Document.ApplyInvert(layer); break;
            }
        }
    }
    public static void Clear(Document doc) => doc?.Macro.Clear();
}

// ---------- workspace preset・Python API下地 (Krita workspace・PyKritaの考え) ----------

public class WorkspacePreset
{
    public string Name = "Preset";
    public int Persona;      // PersonaKind int
    public bool Simple;
    public float BrushSize = 24;
    public string Blend = "Normal";
}

public static class WorkspaceOps
{
    public static WorkspacePreset Capture(Document doc, string name) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim(),
        Persona = (int)(doc?.Persona ?? PersonaKind.Paint),
        Simple = doc?.SimpleMode ?? false,
        BrushSize = 24,
    };
    public static void Apply(Document doc, WorkspacePreset preset)
    {
        if (doc == null || preset == null) throw new InvalidOperationException("Bad preset");
        if (preset.Persona is < 0 or > 2) throw new InvalidOperationException("Bad persona");
        doc.Persona = (PersonaKind)preset.Persona;
        doc.SimpleMode = preset.Simple;
    }
    public static string ToJson(WorkspacePreset p) =>
        System.Text.Json.JsonSerializer.Serialize(p, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
    public static WorkspacePreset FromJson(string json)
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<WorkspacePreset>(json,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        if (p == null || string.IsNullOrWhiteSpace(p.Name)) throw new InvalidOperationException("Bad preset json");
        return p;
    }
}

/// <summary>Python API下地: 文書要約JSON＋操作stubの生成 (interpreter組込は将来)。</summary>
public static class PythonApiStub
{
    public static string DocumentSummary(Document doc)
    {
        if (doc == null) return "{}";
        var o = new { name = doc.Name, width = doc.Width, height = doc.Height,
            layers = doc.Layers.Select(l => new { name = l.Name, visible = l.Visible,
                adjustment = l.IsAdjustmentLayer ? l.Adjustment?.Kind.ToString() : null }).ToArray() };
        return System.Text.Json.JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    public static string GenerateStub(Document doc) =>
        "# Compositor Python API stub (P2下地・interpreterは将来)\n" +
        "#   import compositor\n" +
        $"#   doc = compositor.open({System.Text.Json.JsonSerializer.Serialize(doc?.Name ?? "")})\n" +
        "#   doc.layers[i].opacity = 0.5  # setter下地\n" +
        "#   doc.export_png(path)         # 書出下地\n" +
        "\"\"\"summary\"\"\"\n" +
        (_summaryJson ?? "") + "\n";
    static string _summaryJson = "";
    public static string GenerateWithSummary(Document doc)
    {
        _summaryJson = DocumentSummary(doc);
        return GenerateStub(doc);
    }
}

// ---------- HDR・ICC・Gamut (Krita HDR・ICC・Gamut Maskの考え) ----------

public enum IccProfileKind { SRGB, DisplayP3, AdobeRGB }

public static class HdrOps
{
    /// <summary>露出EVの適用 (2^ev倍・preview/破壊両用)。</summary>
    public static SKBitmap ApplyExposure(SKBitmap src, double ev)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        if (!double.IsFinite(ev) || ev is < -8 or > 8) throw new InvalidOperationException("Bad EV");
        double gain = Math.Pow(2, ev);
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                byte Ch(byte v) => (byte)Math.Clamp(Math.Round(v * gain), 0, 255);
                dst.SetPixel(x, y, new SKColor(Ch(c.Red), Ch(c.Green), Ch(c.Blue), c.Alpha));
            }
        return dst;
    }
    /// <summary>Reinhard tone map (HDR→SDR近似)。</summary>
    public static SKBitmap ToneMap(SKBitmap src)
    {
        if (src == null) throw new InvalidOperationException("No bitmap");
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                byte Ch(byte v) { double n = v / 255.0; return (byte)Math.Round(n / (1 + n) * 2 * 255 > 255 ? 255 : n / (1 + n) * 2 * 255); }
                dst.SetPixel(x, y, new SKColor(Ch(c.Red), Ch(c.Green), Ch(c.Blue), c.Alpha));
            }
        return dst;
    }
}

public static class IccOps
{
    public static string Label(IccProfileKind p) => p switch
    {
        IccProfileKind.DisplayP3 => "Display-P3 近似",
        IccProfileKind.AdobeRGB => "AdobeRGB 近似",
        _ => "sRGB (素通し)",
    };
    /// <summary>profile間の近似変換 (chromaの持ち上げ/沈み・画素処理用)。</summary>
    public static PaletteColor Convert(PaletteColor c, IccProfileKind src, IccProfileKind dst)
    {
        if (src == dst) return c;
        double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        double gain = (src, dst) switch
        {
            (IccProfileKind.SRGB, IccProfileKind.DisplayP3) => 1.06,
            (IccProfileKind.DisplayP3, IccProfileKind.SRGB) => 1 / 1.06,
            (IccProfileKind.SRGB, IccProfileKind.AdobeRGB) => 1.03,
            (IccProfileKind.AdobeRGB, IccProfileKind.SRGB) => 1 / 1.03,
            _ => 1.0,
        };
        double F(double v) => Math.Min(1, Math.Max(0, l + (v - l) * gain));
        return new PaletteColor(F(c.R), F(c.G), F(c.B));
    }
}

public enum GamutKind { Full, Analogous90, Complementary }

/// <summary>Gamut Maskの考え: 和声関係の色相窓に配色を保つ (palette拡張)。</summary>
public static class GamutMaskOps
{
    static double HueOf(PaletteColor c)
    {
        var hsb = new PickerHSB(c);
        return hsb.Hue;
    }
    static double Dist(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }
    /// <summary>基色hue中心の窓に収まるか (Fullは常に真)。</summary>
    public static bool IsAllowed(PaletteColor c, PaletteColor basis, GamutKind kind)
    {
        if (kind == GamutKind.Full) return true;
        double h = HueOf(c), hb = HueOf(basis);
        return kind switch
        {
            GamutKind.Analogous90 => Dist(h, hb) <= 45,
            GamutKind.Complementary => Dist(h, hb) <= 20 || Dist(h, (hb + 180) % 360) <= 20,
            _ => true,
        };
    }
}

// ---------- Export slice・compound mask・blend range (Affinityの考え) ----------

public class Slice
{
    public string Name = "Slice";
    public int X, Y, W, H;
}

public static class SliceOps
{
    public const int MaxSlices = 64;
    public static void Validate(Slice s, int docW, int docH)
    {
        if (s == null) throw new InvalidOperationException("No slice");
        if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 256) throw new InvalidOperationException("Bad slice name");
        if (s.W < 1 || s.H < 1 || s.X < 0 || s.Y < 0 || s.X + s.W > docW || s.Y + s.H > docH)
            throw new InvalidOperationException("Bad slice rect");
    }
    /// <summary>slice矩形の切出し (Export Persona sliceの考え)。</summary>
    public static SKBitmap Cut(SKBitmap composite, Slice s)
    {
        if (composite == null) throw new InvalidOperationException("No composite");
        Validate(s, composite.Width, composite.Height);
        var dst = new SKBitmap(s.W, s.H, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(dst);
        canvas.DrawBitmap(composite, new SKRect(s.X, s.Y, s.X + s.W, s.Y + s.H), new SKRect(0, 0, s.W, s.H));
        return dst;
    }
    public static byte[] EncodePng(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

public enum CompoundMode { Union, Intersect, Subtract, Xor }

/// <summary>compound mask (Affinityの考え): 2覆面の集合演算。</summary>
public static class CompoundMaskOps
{
    public static byte[] Combine(byte[] a, byte[] b, int w, int h, CompoundMode mode)
    {
        if (a == null || b == null || a.Length != w * h || b.Length != w * h || w <= 0 || h <= 0)
            throw new InvalidOperationException("Bad masks");
        var out_ = new byte[w * h];
        for (int i = 0; i < out_.Length; i++)
        {
            bool x = a[i] != 0, y = b[i] != 0;
            bool r = mode switch
            {
                CompoundMode.Union => x || y,
                CompoundMode.Intersect => x && y,
                CompoundMode.Subtract => x && !y,
                CompoundMode.Xor => x ^ y,
                _ => false,
            };
            out_[i] = r ? (byte)255 : (byte)0;
        }
        return out_;
    }
}

/// <summary>blend range (Affinityの考え): 下地輝度での層の効き具合。</summary>
public class BlendRange
{
    public double Lo;       // 0..1
    public double Hi = 1;   // 0..1 (Lo<=Hi)
    public double Feather;  // 0..0.5
    public bool IsValid => Lo is >= 0 and <= 1 && Hi is >= 0 and <= 1 && Lo <= Hi && Feather is >= 0 and <= 0.5;
}

/// <summary>blend rangeの合成適用 (ComposeWithAdjustments側から呼ぶ)。</summary>
public static class P2Blend
{
    /// <summary>適用後 acc と適用前 before を下地輝度 k で混ぜて acc へ戻す。</summary>
    public static void BlendRangeModulate(SKBitmap acc, SKBitmap before, BlendRange range, int docW, int docH)
    {
        if (acc == null || before == null || range == null || !range.IsValid) return;
        for (int y = 0; y < docH; y++)
            for (int x = 0; x < docW; x++)
            {
                var b = before.GetPixel(x, y);
                double luma = (0.2126 * b.Red + 0.7152 * b.Green + 0.0722 * b.Blue) / 255.0;
                double k = BlendRangeOps.Factor(luma, range);
                if (k >= 1) continue;
                if (k <= 0) { acc.SetPixel(x, y, b); continue; }
                var a = acc.GetPixel(x, y);
                acc.SetPixel(x, y, new SKColor(
                    (byte)MathF.Round(b.Red + (a.Red - b.Red) * (float)k),
                    (byte)MathF.Round(b.Green + (a.Green - b.Green) * (float)k),
                    (byte)MathF.Round(b.Blue + (a.Blue - b.Blue) * (float)k),
                    (byte)MathF.Round(b.Alpha + (a.Alpha - b.Alpha) * (float)k)));
            }
    }
}

public static class BlendRangeOps
{
    /// <summary>下地輝度luma01での適用率 (featherで滑らか化)。</summary>
    public static double Factor(double luma01, BlendRange r)
    {
        if (r == null || !r.IsValid) throw new InvalidOperationException("Bad blend range");
        luma01 = Math.Clamp(luma01, 0, 1);
        double lo = Math.Max(0, r.Lo - r.Feather), hi = Math.Min(1, r.Hi + r.Feather);
        if (luma01 < lo || luma01 > hi) return 0;
        double loF = Math.Min(r.Lo + r.Feather, r.Hi), hiF = Math.Max(r.Hi - r.Feather, r.Lo);
        if (luma01 < loF && loF > lo)
        {
            double k = (luma01 - lo) / Math.Max(1e-9, loF - lo);
            return k * k * (3 - 2 * k);
        }
        if (luma01 > hiF && hi < hiF + 1e-9 + (hi - hiF))
        {
            double k = (hi - luma01) / Math.Max(1e-9, hi - hiF);
            return k * k * (3 - 2 * k);
        }
        return 1;
    }
}

// ---------- 3D素材・動画層 (CLIP STUDIO 3D・Dreams Flipbookの考え・入口) ----------
// 本計画は静止画が核のため、3Dは手続き primitive の置き場、動画層は
// frame列＋onion preview＋連番書出の下地に留める (timeline本格は対象外)。

public enum Proxy3DKind { Box, Sphere, Cylinder }

public static class Proxy3DOps
{
    /// <summary>手続き primitive の陰影描画 (3D素材の置き場・ラスタ層化)。</summary>
    public static SKBitmap Render(Proxy3DKind kind, int w, int h, SKColor color)
    {
        if (w < 8 || h < 8 || w > 4096 || h > 4096) throw new InvalidOperationException("Bad size");
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(bmp);
        float cx = w / 2f, cy = h / 2f, r = Math.Min(w, h) * 0.38f;
        switch (kind)
        {
            case Proxy3DKind.Sphere:
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                        if (d > 1) continue;
                        double shade = 0.4 + 0.6 * Math.Sqrt(1 - d * d);
                        bmp.SetPixel(x, y, new SKColor(
                            (byte)Math.Min(255, color.Red * shade), (byte)Math.Min(255, color.Green * shade),
                            (byte)Math.Min(255, color.Blue * shade), 255));
                    }
                break;
            case Proxy3DKind.Cylinder:
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double d = Math.Abs(x - cx) / r;
                        if (d > 1) continue;
                        if (y < cy - r || y > cy + r) continue;
                        double shade = 0.45 + 0.55 * Math.Sqrt(1 - d * d);
                        bmp.SetPixel(x, y, new SKColor(
                            (byte)Math.Min(255, color.Red * shade), (byte)Math.Min(255, color.Green * shade),
                            (byte)Math.Min(255, color.Blue * shade), 255));
                    }
                break;
            default: // Box: 上面 свет＋側面影の2面
                using (var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawRect(SKRect.Create(cx - r, cy - r * 0.7f, r * 2, r * 1.4f), paint);
                using (var paint = new SKPaint { Color = new SKColor(
                    (byte)(color.Red * 0.6), (byte)(color.Green * 0.6), (byte)(color.Blue * 0.6), 255),
                    Style = SKPaintStyle.Fill, IsAntialias = true })
                    canvas.DrawRect(SKRect.Create(cx - r, cy, r * 2, r * 0.7f), paint);
                break;
        }
        return bmp;
    }
}

/// <summary>動画層の下地: frame列 (session-only)＋onion＋連番 manifest。</summary>
public static class VideoLayerOps
{
    public const int MaxFrames = 256;
    public static void AddFrame(Document doc, SKBitmap frame)
    {
        if (doc == null || frame == null) throw new InvalidOperationException("Bad frame");
        if (frame.Width != doc.Width || frame.Height != doc.Height) throw new InvalidOperationException("Bad frame size");
        if (doc.VideoFrames.Count >= MaxFrames) throw new InvalidOperationException("Too many frames");
        var copy = new SKBitmap(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(copy)) c.DrawBitmap(frame, 0, 0);
        doc.VideoFrames.Add(copy);
    }
    /// <summary>onion skin式 preview (前後frameの重ね・Dreams onionの考え)。</summary>
    public static SKBitmap Onion(SKBitmap current, SKBitmap neighbor, byte alpha = 90)
    {
        if (current == null) throw new InvalidOperationException("No frame");
        var dst = new SKBitmap(current.Width, current.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(dst)) c.DrawBitmap(current, 0, 0);
        if (neighbor == null || neighbor.Width != dst.Width || neighbor.Height != dst.Height) return dst;
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
            {
                var n = neighbor.GetPixel(x, y);
                if (n.Alpha == 0) continue;
                var c0 = dst.GetPixel(x, y);
                double k = alpha / 255.0;
                dst.SetPixel(x, y, new SKColor(
                    (byte)Math.Round(c0.Red * (1 - k) + n.Red * k),
                    (byte)Math.Round(c0.Green * (1 - k) + n.Green * k),
                    (byte)Math.Round(c0.Blue * (1 - k) + n.Blue * k), 255));
            }
        return dst;
    }
    public static string ExportManifest(Document doc)
    {
        if (doc == null) return "";
        var lines = new List<string> { $"# video {doc.Name} ({doc.VideoFrames.Count} frames)" };
        for (int i = 0; i < doc.VideoFrames.Count; i++)
            lines.Add($"frame-{i:D4}.png {doc.VideoFrames[i].Width}x{doc.VideoFrames[i].Height}");
        return string.Join("\n", lines) + "\n";
    }
    public static void Clear(Document doc)
    {
        if (doc == null) return;
        foreach (var f in doc.VideoFrames) f.Dispose();
        doc.VideoFrames.Clear();
    }
}

// ---------- AI系 (手動下位・opt-inのみ。自動切抜・生成Fillは対象外) ----------
// ibisPaint AI Disturbance・Background Removalは ethics・model由来のため
// 将来層。本P2では手動 refine の下位として (1) 羽化 refine (2) 背景 hint の
// 提案のみ (適用は利用者の明示確認つき) (3) 保護 noise (可逆・Undo可) のみ。

public static class AiAssistOps
{
    /// <summary>選択覆面の手動 refine: 箱形ぼかし羽化 (髪束級の自動 refine ではない)。</summary>
    public static byte[] Feather(byte[] mask, int w, int h, int radius)
    {
        if (mask == null || mask.Length != w * h || w <= 0 || h <= 0) throw new InvalidOperationException("Bad mask");
        radius = Math.Clamp(radius, 0, 16);
        if (radius == 0) return (byte[])mask.Clone();
        var out_ = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sum = 0, n = 0;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        sum += mask[ny * w + nx]; n++;
                    }
                out_[y * w + x] = (byte)(sum / Math.Max(1, n));
            }
        return out_;
    }

    /// <summary>背景 hint の提案 (自動適用しない・利用者の明示確認が必須)。</summary>
    public static byte[] ProposeBackgroundMask(SKBitmap bmp, int tolerance = 32)
    {
        if (bmp == null) throw new InvalidOperationException("No bitmap");
        tolerance = Math.Clamp(tolerance, 0, 255);
        var corners = new[] { bmp.GetPixel(0, 0), bmp.GetPixel(bmp.Width - 1, 0),
            bmp.GetPixel(0, bmp.Height - 1), bmp.GetPixel(bmp.Width - 1, bmp.Height - 1) };
        byte Ref(Func<SKColor, byte> f) => (byte)Math.Round(corners.Average(c => f(c)));
        int rr = Ref(c => c.Red), gg = Ref(c => c.Green), bb = Ref(c => c.Blue);
        var mask = new byte[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                bool bg = Math.Abs(c.Red - rr) <= tolerance && Math.Abs(c.Green - gg) <= tolerance
                    && Math.Abs(c.Blue - bb) <= tolerance;
                mask[y * bmp.Width + x] = bg ? (byte)0 : (byte)255;   // 前景=255の提案
            }
        return mask;
    }

    /// <summary>保護 noise (ibisPaint AI Disturbanceの考え・強度 slider・Undoで可逆)。</summary>
    public static void Disturbance(SKBitmap bmp, double strength, uint seed = 7)
    {
        if (bmp == null) throw new InvalidOperationException("No bitmap");
        if (strength is < 0 or > 100 || !double.IsFinite(strength)) throw new InvalidOperationException("Bad strength");
        if (strength == 0) return;
        uint s = seed;
        uint Next() { s = s * 1664525u + 1013904223u; return s >> 8; }
        double amp = strength / 100.0 * 24.0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 0) continue;
                double n = (Next() % 1000) / 1000.0 - 0.5;
                int d = (int)Math.Round(n * 2 * amp);
                byte Ch(byte v) => (byte)Math.Clamp(v + d, 0, 255);
                bmp.SetPixel(x, y, new SKColor(Ch(c.Red), Ch(c.Green), Ch(c.Blue), c.Alpha));
            }
    }
}
