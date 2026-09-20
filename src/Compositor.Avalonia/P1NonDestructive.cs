// Compositor — P1 世界最良規範の非破壊深化 (docs/WORLD_BEST_PAINT.md §4 P1).
// Adjustment Brush式塗り調整 / filter mask・Live層 (.comp v8) / QuickShape /
// ベクター線＋Correct line / ベクター磁石 / spare channel・Quick mask /
// Time-lapse記録 / Simple preset＋Persona弱移植＋Contextual頭欄。
// いずれも現行 DocumentModel/LayerSystem/Adjustments の延長。推測の外部仕様は
// 持ち込まず、各型の XML doc に典拠相当の説明を記す。
using SkiaSharp;

namespace Compositor;

// ---------- Adjustment Brush式塗り調整 ----------
// Photoshop Adjustment Brush (Helpx [確]) の一段階化「塗る→調整層＋層覆面を
// 自動生成」を自前実装で再現する。調整層に黒覆面を付け、筆 stroke が白で
// 覆面を開ける＝塗った所だけ調整が効く。覆面の合成反映は P1Compose が担う。

/// <summary>Adjustment Brush式の塗り調整層の生成と覆面 stroke。</summary>
public static class AdjustmentBrushOps
{
    /// <summary>黒覆面つき調整層を作り、選択層の直上に挿入する。</summary>
    public static Layer CreateBrushLayer(Document doc, AdjustmentKind kind, string name)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        var layer = LayerFilters.NewAdjustmentLayer(kind, name, Math.Max(1, doc.Width), Math.Max(1, doc.Height));
        MaskOps.AddMask(layer, revealing: false);
        int at = doc.ActiveLayerId is Guid id
            ? doc.Layers.FindIndex(l => l.Id == id) + 1
            : doc.Layers.Count;
        doc.Layers.Insert(Math.Clamp(at, 0, doc.Layers.Count), layer);
        doc.ActiveLayerId = layer.Id;
        return layer;
    }

    /// <summary>調整層の覆面へ白 stroke を描く (塗った所だけ調整が効く)。</summary>
    public static void PaintStroke(Layer layer, IList<SKPoint> points, float diameter)
    {
        if (layer == null || !layer.IsAdjustmentLayer) throw new InvalidOperationException("Not an adjustment layer");
        if (!MaskOps.HasMask(layer)) MaskOps.AddMask(layer, revealing: false);
        MaskOps.PaintStroke(layer, points, 255, diameter);
    }
}

// ---------- Live層 (filter mask・並替・再調整可) ----------
// Affinity Live filter layer [確]・GIMP 3 NDE filter [確] の考えを調整層の
// 延長で再現する。Live層＝覆面つき調整層 (filter mask)。並替は層順の移動、
// 再調整は Adjustment 設定の書換え、on/off は Visible で行う。

/// <summary>Live層 (filter maskつき調整・filter層) の運用操作。</summary>
public static class LiveLayerOps
{
    static readonly AdjustmentKind[] LiveKinds =
    {
        AdjustmentKind.Hsv, AdjustmentKind.Levels, AdjustmentKind.Curves,
        AdjustmentKind.Exposure, AdjustmentKind.GradientMap, AdjustmentKind.Grain,
        AdjustmentKind.Noise, AdjustmentKind.Lens, AdjustmentKind.GaussBlur,
        AdjustmentKind.MotionBlur,
    };

    public static bool IsLiveKind(AdjustmentKind kind) => LiveKinds.Contains(kind);

    /// <summary>filter maskつき Live層を作る。覆面は白 (全面に効く) 開始。</summary>
    public static Layer Create(Document doc, AdjustmentKind kind, string name)
    {
        if (!IsLiveKind(kind)) throw new InvalidOperationException($"Not a live kind: {kind}");
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        var layer = LayerFilters.NewAdjustmentLayer(kind, name, Math.Max(1, doc.Width), Math.Max(1, doc.Height));
        MaskOps.AddMask(layer, revealing: true);
        int at = doc.ActiveLayerId is Guid id
            ? doc.Layers.FindIndex(l => l.Id == id) + 1
            : doc.Layers.Count;
        doc.Layers.Insert(Math.Clamp(at, 0, doc.Layers.Count), layer);
        doc.ActiveLayerId = layer.Id;
        return layer;
    }

    /// <summary>Live層を1段上/下へ並替える (reorder 可＝Live層の要件)。</summary>
    public static bool Reorder(Document doc, Guid layerId, bool up)
    {
        if (doc == null) return false;
        int i = doc.Layers.FindIndex(l => l.Id == layerId);
        if (i < 0) return false;
        int j = up ? i + 1 : i - 1;
        if (j < 0 || j >= doc.Layers.Count) return false;
        (doc.Layers[i], doc.Layers[j]) = (doc.Layers[j], doc.Layers[i]);
        return true;
    }

    /// <summary>Live層の係数を書換える (後から再調整可)。</summary>
    public static void Retune(Layer layer, Action<LayerAdjustment> tune)
    {
        if (layer?.Adjustment == null || !layer.IsAdjustmentLayer)
            throw new InvalidOperationException("Not an adjustment layer");
        tune(layer.Adjustment);
        if (!layer.Adjustment.IsValid) throw new InvalidOperationException("Invalid adjustment");
    }
}

// ---------- 覆面つき調整合成 (P1Compose) ----------
// 現行 ComposeWithAdjustments は調整層の覆面を無視する (全面適用)。
// P1で Adjustment Brush / filter mask が効くよう、覆面がある調整層は
// 調整前後の線形補間 (k=覆面被覆率) で適用する。

/// <summary>覆面つき調整層の合成 helper。</summary>
public static class P1Compose
{
    /// <summary>調整済み acc と調整前 before を覆面 k で混ぜて acc へ戻す。</summary>
    public static void BlendMasked(SKBitmap acc, SKBitmap before, Layer layer, int docW, int docH)
    {
        if (acc == null || before == null || layer == null) return;
        if (!MaskOps.IsActive(layer)) return;
        int mw = layer.MaskW, mh = layer.MaskH;
        var mask = layer.Mask;
        if (mask == null || mw <= 0 || mh <= 0) return;
        for (int y = 0; y < docH; y++)
        {
            int my = mh == docH ? y : (int)((long)y * mh / Math.Max(1, docH));
            my = Math.Clamp(my, 0, mh - 1);
            for (int x = 0; x < docW; x++)
            {
                int mx = mw == docW ? x : (int)((long)x * mw / Math.Max(1, docW));
                mx = Math.Clamp(mx, 0, mw - 1);
                float k = mask[my * mw + mx] / 255f;
                if (k <= 0f || k >= 1f)
                {
                    if (k <= 0f) acc.SetPixel(x, y, before.GetPixel(x, y));
                    continue;
                }
                var a = acc.GetPixel(x, y);
                var b = before.GetPixel(x, y);
                acc.SetPixel(x, y, new SKColor(
                    (byte)MathF.Round(b.Red + (a.Red - b.Red) * k),
                    (byte)MathF.Round(b.Green + (a.Green - b.Green) * k),
                    (byte)MathF.Round(b.Blue + (a.Blue - b.Blue) * k),
                    (byte)MathF.Round(b.Alpha + (a.Alpha - b.Alpha) * k)));
            }
        }
    }
}

// ---------- QuickShape ----------
// Procreate QuickShape (handbook [確]) の考え: 描いて保持→直線・弧・楕円・
// 三角・四角に snap。第二指で正形、15°刻み磁石回転、保持 drag で拡縮回転。

public enum QuickShapeKind { None, Line, Rectangle, Ellipse, Triangle }

/// <summary>手描き stroke の図形 snap (自前ヒューリスティクス・決定性)。</summary>
public static class QuickShapeOps
{
    /// <summary>角度を15°刻みに磁石吸着する (Procreate 15°磁石の考え)。</summary>
    public static double Snap15(double angleDeg)
    {
        double s = Math.Round(angleDeg / 15.0) * 15.0;
        s %= 360;
        if (s > 180) s -= 360;
        if (s <= -180) s += 360;
        return s;
    }

    public static SKPoint SnapLine45(SKPoint start, SKPoint end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double snapped = Snap15(deg) * Math.PI / 180.0;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return new SKPoint(start.X + (float)(Math.Cos(snapped) * len), start.Y + (float)(Math.Sin(snapped) * len));
    }

    static double DistToLine(SKPoint p, SKPoint a, SKPoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        return Math.Abs(dy * p.X - dx * p.Y + b.X * a.Y - b.Y * a.X) / len;
    }

    static List<SKPoint> Rdp(IList<SKPoint> pts, double eps)
    {
        if (pts.Count <= 2) return pts.ToList();
        var first = pts[0]; var last = pts[^1];
        double max = 0; int idx = 0;
        for (int i = 1; i < pts.Count - 1; i++)
        {
            double d = DistToLine(pts[i], first, last);
            if (d > max) { max = d; idx = i; }
        }
        if (max > eps)
        {
            var left = Rdp(pts.Take(idx + 1).ToList(), eps);
            var right = Rdp(pts.Skip(idx).ToList(), eps);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }
        return new List<SKPoint> { first, last };
    }

    /// <summary>stroke を図形に分類する。閉じていない自由線は None (自由線のまま)。</summary>
    public static QuickShapeKind Fit(IList<SKPoint> points)
    {
        if (points == null || points.Count < 3) return QuickShapeKind.None;
        var a = points[0]; var b = points[^1];
        double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        if (diag < 4) return QuickShapeKind.None;
        double maxErr = points.Max(p => DistToLine(p, a, b));
        if (len > diag * 0.5 && maxErr < len * 0.06) return QuickShapeKind.Line;
        // 閉形判定: 始終点が対角の1/4以内で接している
        if (len > diag * 0.25) return QuickShapeKind.None;
        var corners = Rdp(points.ToList(), diag * 0.05);
        // RDPは始終点を両端に含むため閉形では重複を除く
        int n = corners.Count;
        if (n >= 2)
        {
            var f = corners[0]; var l = corners[^1];
            if (Math.Sqrt((f.X - l.X) * (f.X - l.X) + (f.Y - l.Y) * (f.Y - l.Y)) < diag * 0.1) n--;
        }
        return n switch
        {
            <= 2 => QuickShapeKind.Ellipse,
            3 => QuickShapeKind.Triangle,
            4 => QuickShapeKind.Rectangle,
            _ => QuickShapeKind.Ellipse,
        };
    }

    /// <summary>正形拘束 (第二指相当): bbox を正方形に寄せる。</summary>
    public static SKRect SquareConstrain(SKRect r)
    {
        float s = Math.Max(r.Width, r.Height);
        return SKRect.Create(r.Left, r.Top, s, s);
    }
}

// ---------- ベクター線＋Correct line＋ベクター磁石 ----------
// CLIP STUDIO ベクター層 (manual [確]) の考え: 開始点・終点・曲率を記録し
// 後から筆先・太さ・形状を変えられる。Correct line 系 (制御点移動/線幅
// 調整/単純化/結線) とベクター磁石 (吸着) を束ねる。

/// <summary>ベクター stroke (ラスタ層に焼く前の編集可能データ)。</summary>
public class VectorStroke
{
    public List<SKPoint> Points = new();
    public float Width = 4f;
    public bool Closed;
    public VectorStroke Clone() => new()
    {
        Points = Points.Select(p => new SKPoint(p.X, p.Y)).ToList(),
        Width = Width, Closed = Closed,
    };
}

/// <summary>ベクター線の編集操作 (Correct line系＋磁石)。</summary>
public static class VectorStrokeOps
{
    public const float MinWidth = 0.5f;
    public const float MaxWidth = 200f;
    public const int MaxPoints = 100_000;

    public static void Validate(VectorStroke s)
    {
        if (s == null) throw new InvalidOperationException("No vector stroke");
        if (s.Points.Count < 2 || s.Points.Count > MaxPoints)
            throw new InvalidOperationException($"Bad point count {s.Points.Count}");
        if (!(s.Width >= MinWidth && s.Width <= MaxWidth))
            throw new InvalidOperationException($"Bad width {s.Width}");
    }

    /// <summary>制御点移動 (Correct line: 制御点移動相当)。</summary>
    public static void MovePoint(VectorStroke s, int index, SKPoint to)
    {
        Validate(s);
        if (index < 0 || index >= s.Points.Count) throw new InvalidOperationException("Bad index");
        s.Points[index] = to;
    }

    /// <summary>線幅調整 (Correct line: 線幅調整相当)。</summary>
    public static void SetWidth(VectorStroke s, float width)
    {
        if (s == null) throw new InvalidOperationException("No vector stroke");
        s.Width = Math.Clamp(width, MinWidth, MaxWidth);
    }

    /// <summary>単純化 (Correct line: 単純化相当・RDP間引き)。</summary>
    public static int Simplify(VectorStroke s, double tolerancePx = 1.5)
    {
        Validate(s);
        var pts = s.Points;
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double max = 0; int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                double dx = pts[b].X - pts[a].X, dy = pts[b].Y - pts[a].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                double d = len < 1e-6
                    ? Math.Sqrt((pts[i].X - pts[a].X) * (pts[i].X - pts[a].X) + (pts[i].Y - pts[a].Y) * (pts[i].Y - pts[a].Y))
                    : Math.Abs(dy * pts[i].X - dx * pts[i].Y + pts[b].X * pts[a].Y - pts[b].Y * pts[a].X) / len;
                if (d > max) { max = d; idx = i; }
            }
            if (idx >= 0 && max > tolerancePx)
            {
                keep[idx] = true;
                stack.Push((a, idx)); stack.Push((idx, b));
            }
        }
        var next = pts.Where((_, i) => keep[i]).ToList();
        int removed = pts.Count - next.Count;
        s.Points = next;
        return removed;
    }

    /// <summary>結線 (Correct line: 結線相当・2 stroke の端点結合)。</summary>
    public static VectorStroke Join(VectorStroke a, VectorStroke b)
    {
        Validate(a); Validate(b);
        var pts = a.Points.Concat(b.Points).ToList();
        if (pts.Count > MaxPoints) throw new InvalidOperationException("Too many points");
        return new VectorStroke { Points = pts, Width = (a.Width + b.Width) / 2 };
    }

    /// <summary>ベクター磁石: 端点を近傍の既存端点・頂点へ吸着する。</summary>
    public static int MagnetSnap(VectorStroke target, IEnumerable<VectorStroke> others, float radiusPx = 8f)
    {
        Validate(target);
        if (target.Points.Count == 0) return 0;
        var pool = (others ?? Enumerable.Empty<VectorStroke>())
            .Where(o => o != null && !ReferenceEquals(o, target))
            .SelectMany(o => o.Points).ToList();
        if (pool.Count == 0) return 0;
        int snapped = 0;
        for (int i = 0; i < target.Points.Count; i++)
        {
            // 磁石は端点と既存頂点の対応に限定せず全点を候補にするが、
            // 中央部の誤吸着を避けるため端点2点のみ吸着する (ClipStudio ベクター磁石の考え)。
            if (i != 0 && i != target.Points.Count - 1) continue;
            var p = target.Points[i];
            SKPoint best = p; double bestD = radiusPx;
            foreach (var q in pool)
            {
                double d = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
                if (d < bestD) { bestD = d; best = q; }
            }
            if (bestD < radiusPx) { target.Points[i] = best; snapped++; }
        }
        return snapped;
    }

    /// <summary>ベクターを層ビットマップへ焼く (ラスタ化・再編集用に Vector を保持)。</summary>
    public static void Rasterize(Layer layer, VectorStroke stroke, SKColor color, int w, int h)
    {
        Validate(stroke);
        if (layer == null) throw new InvalidOperationException("No layer");
        var bmp = new SKBitmap(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = color, StrokeWidth = stroke.Width, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var path = new SKPath();
        path.MoveTo(stroke.Points[0]);
        for (int i = 1; i < stroke.Points.Count; i++) path.LineTo(stroke.Points[i]);
        if (stroke.Closed) path.Close();
        canvas.DrawPath(path, paint);
        layer.Bitmap?.Dispose();
        layer.Bitmap = bmp;
        layer.Vector = stroke.Clone();
        layer.IsVectorLayer = true;
    }
}

// ---------- spare channel・Quick mask ----------
// Affinity spare channel [確]・GIMP Quick mask [確] の考え: 選択を channel
// として保存・層化編集し、Quick mask で赤 overlay として一時編集する。

/// <summary>保存選択 (spare channel)。</summary>
public class SpareChannel
{
    public string Name = "Channel";
    public byte[] Mask;
    public int W, H;
    public SpareChannel Clone() => new()
    {
        Name = Name, W = W, H = H,
        Mask = Mask != null ? (byte[])Mask.Clone() : null,
    };
}

public static class SpareChannelOps
{
    public const int MaxChannels = 64;

    public static SpareChannel Save(Document doc, string name, byte[] mask, int w, int h)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (mask == null || mask.Length != w * h || w <= 0 || h <= 0)
            throw new InvalidOperationException("Bad mask");
        if (doc.SpareChannels.Count >= MaxChannels) throw new InvalidOperationException("Too many channels");
        var ch = new SpareChannel { Name = string.IsNullOrWhiteSpace(name) ? $"Channel {doc.SpareChannels.Count + 1}" : name.Trim(), Mask = (byte[])mask.Clone(), W = w, H = h };
        doc.SpareChannels.Add(ch);
        return ch;
    }

    public static byte[] Load(Document doc, int index)
    {
        if (doc == null || index < 0 || index >= doc.SpareChannels.Count)
            throw new InvalidOperationException("Bad channel");
        return (byte[])doc.SpareChannels[index].Mask.Clone();
    }

    public static bool Delete(Document doc, int index)
    {
        if (doc == null || index < 0 || index >= doc.SpareChannels.Count) return false;
        doc.SpareChannels.RemoveAt(index);
        return true;
    }
}

/// <summary>Quick mask: 選択の一時赤 overlay 編集。</summary>
public static class QuickMaskOps
{
    /// <summary>現選択から Quick mask を開始する。</summary>
    public static void Enable(Document doc)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        var mask = SelectionTools.CurrentMask(doc);
        doc.QuickMask = mask;
        doc.QuickMaskW = doc.Width; doc.QuickMaskH = doc.Height;
        doc.QuickMaskEnabled = true;
    }

    public static void Disable(Document doc)
    {
        if (doc == null) return;
        doc.QuickMaskEnabled = false;
    }

    /// <summary>Quick mask へ描く (value 0/255・直径 px)。</summary>
    public static void Paint(Document doc, IList<SKPoint> points, float diameter, byte value)
    {
        if (doc == null || !doc.QuickMaskEnabled || doc.QuickMask == null)
            throw new InvalidOperationException("Quick mask is off");
        if (points == null || points.Count == 0) return;
        float r = Math.Max(0.5f, diameter / 2);
        foreach (var p in points)
        {
            int x0 = Math.Max(0, (int)MathF.Floor(p.X - r)), x1 = Math.Min(doc.QuickMaskW - 1, (int)MathF.Ceiling(p.X + r));
            int y0 = Math.Max(0, (int)MathF.Floor(p.Y - r)), y1 = Math.Min(doc.QuickMaskH - 1, (int)MathF.Ceiling(p.Y + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double d = Math.Sqrt((x + 0.5 - p.X) * (x + 0.5 - p.X) + (y + 0.5 - p.Y) * (y + 0.5 - p.Y));
                    if (d <= r) doc.QuickMask[y * doc.QuickMaskW + x] = value;
                }
        }
    }

    /// <summary>Quick mask を選択へ確定する。</summary>
    public static void Commit(Document doc)
    {
        if (doc == null || !doc.QuickMaskEnabled || doc.QuickMask == null)
            throw new InvalidOperationException("Quick mask is off");
        SelectionTools.ApplyMask(doc, (byte[])doc.QuickMask.Clone(), SelectionKind.Wand, null, SelectionMode.Replace);
        doc.QuickMaskEnabled = false;
    }

    /// <summary>赤 overlay 合成 (非選択域を半透明赤で覆う preview)。</summary>
    public static SKBitmap RenderOverlay(Document doc)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        var bmp = new SKBitmap(Math.Max(1, doc.Width), Math.Max(1, doc.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        if (!doc.QuickMaskEnabled || doc.QuickMask == null) return bmp;
        for (int y = 0; y < doc.Height; y++)
            for (int x = 0; x < doc.Width; x++)
                if (doc.QuickMask[y * doc.Width + x] == 0)
                    bmp.SetPixel(x, y, new SKColor(255, 0, 0, 90));
        return bmp;
    }
}

// ---------- Time-lapse記録 ----------
// Procreate Time-lapse [確]・ibisPaint 過程 video [確] の考えの記録側:
// 操作名＋時刻の軽量 log を文書に残し、過程 video 化の下地にする。
// 画素連番の書出は将来 (P2動画層)。記録は session-only で .comp には
// 含めない (肥大防止・Affinity history 保存の教訓)。

public class TimelapseEntry
{
    public DateTime Time;
    public string Action = "";
}

public static class TimelapseOps
{
    public const int MaxEntries = 10_000;

    public static void Start(Document doc)
    {
        if (doc == null) return;
        doc.TimelapseRecording = true;
    }

    public static void Stop(Document doc)
    {
        if (doc == null) return;
        doc.TimelapseRecording = false;
    }

    public static void Record(Document doc, string action)
    {
        if (doc == null || !doc.TimelapseRecording) return;
        if (string.IsNullOrWhiteSpace(action)) return;
        if (doc.Timelapse.Count >= MaxEntries) doc.Timelapse.RemoveAt(0);
        doc.Timelapse.Add(new TimelapseEntry { Time = DateTime.UtcNow, Action = action.Trim() });
    }

    public static void Clear(Document doc) => doc?.Timelapse.Clear();

    /// <summary>過程 manifest (時刻＋操作列) を text 化する。</summary>
    public static string ExportManifest(Document doc)
    {
        if (doc == null) return "";
        var lines = new List<string> { $"# timelapse {doc.Name} ({doc.Timelapse.Count} events)" };
        foreach (var e in doc.Timelapse)
            lines.Add($"{e.Time:O} {e.Action}");
        return string.Join("\n", lines) + "\n";
    }
}

// ---------- Simple preset＋Persona弱移植＋Contextual頭欄 ----------
// Affinity Persona [確] の弱移植: 「描く／整える／出す」の3頭欄 preset で
// toolset を切替える。ClipStudio Simple Mode [確] の考え: 初心者向け縮小
// preset。Photoshop Contextual Task Bar [確] の考え: 工具毎に頭欄が切替わる。

public enum PersonaKind { Paint, Retouch, Export }

public static class PersonaOps
{
    public static string[] ToolsFor(PersonaKind persona) => persona switch
    {
        PersonaKind.Paint => new[] { "Brush", "Eraser", "Shape", "Gradient", "Vector", "QuickShape", "Smudge", "Eyedropper" },
        PersonaKind.Retouch => new[] { "Clone", "Heal", "AdjustmentBrush", "LiveFilter", "Curves", "Levels", "Hue", "Filter", "Marquee", "Lasso", "Wand", "QuickMask" },
        PersonaKind.Export => new[] { "Crop", "CanvasSize", "ImageSize", "JpegExport", "PngExport", "Timelapse" },
        _ => Array.Empty<string>(),
    };

    public static string Label(PersonaKind persona) => persona switch
    {
        PersonaKind.Paint => "描く",
        PersonaKind.Retouch => "整える",
        PersonaKind.Export => "出す",
        _ => "?",
    };

    /// <summary>Simple preset の必須工具 (初心者入口・5分で一枚の延長)。</summary>
    public static string[] SimpleTools() => new[] { "Brush", "Eraser", "Move", "Import", "Export", "Undo" };

    public static bool IsSimpleVisible(string tool) =>
        SimpleTools().Contains(tool, StringComparer.OrdinalIgnoreCase);
}

public static class ContextualHint
{
    static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Move"] = "Drag to move",
        ["Brush"] = "Drag to paint",
        ["Eraser"] = "Drag to erase",
        ["AdjustmentBrush"] = "塗って調整: 塗った所だけ効く (覆面つき調整層)",
        ["LiveFilter"] = "Live層: 係数は後から再調整・並替可",
        ["QuickShape"] = "描いて保持→図形に snap・15°磁石",
        ["Vector"] = "ベクター線: 後から制御点・線幅を編集可",
        ["QuickMask"] = "赤域が非選択: 白で開ける・黒で閉じる",
        ["SpareChannel"] = "選択を channel に保存・再利用",
        ["Timelapse"] = "過程を記録: Start→操作→Export",
        ["Marquee"] = "Drag to select",
        ["Lasso"] = "囲んで選択・Enter確定",
        ["Wand"] = "Click to select similar",
        ["Clone"] = "Alt-click採取・塗って複写",
        ["Heal"] = "Click to heal",
        ["Smudge"] = "Drag to smudge",
        ["Crop"] = "枠を決めて Enter確定",
        ["Distort"] = "角を掴んで歪み・Enter確定",
        ["Shape"] = "Drag for shape (U)",
        ["Gradient"] = "引いて Gradation・Enter確定",
    };

    public static string For(string tool) =>
        tool != null && Hints.TryGetValue(tool, out var h) ? h : "";
}
