// Compositor — P0 世界最良規範の現行延長 (docs/WORLD_BEST_PAINT.md §4 P0).
// gap-closing fill (Krita 準拠の考え・自前実装) / canvas 回転 view / Solo 表示 /
// 複数選択 UI 下地 / ASE 読込 / Harmony 盤 / profile preview / `/` filter 検索.
// いずれも現行 DocumentModel/LayerSystem/PaletteState の延長。推測の外部仕様は
// 持ち込まず、各型の XML doc に典拠相当の説明を記す。
using SkiaSharp;

namespace Compositor;

// ---------- gap-closing fill (Krita gap-closing fill の考え) ----------
public static class GapCloseOps
{
    /// <summary>線画 barrier: 不透明で暗い画素 (luminance &lt; threshold).</summary>
    public static bool[] BarrierMask(SKBitmap sample, byte lineThreshold = 128)
    {
        int w = sample.Width, h = sample.Height;
        var px = sample.Pixels;
        var barrier = new bool[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            var c = px[i];
            if (c.Alpha == 0) continue;
            double lum = 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;
            barrier[i] = lum < lineThreshold;
        }
        return barrier;
    }

    /// <summary>Chebyshev (正方形) 膨張。gapRadius px 以内の隙間を閉じる。</summary>
    public static bool[] Dilate(bool[] mask, int w, int h, int radius)
    {
        var out_ = new bool[w * h];
        if (radius <= 0) { Array.Copy(mask, out_, mask.Length); return out_; }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x]) continue;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        out_[ny * w + nx] = true;
                    }
            }
        return out_;
    }

    static bool WandMatches(SKColor p, int[] reference, int tolerance) =>
        Math.Abs(p.Red - reference[0]) <= tolerance &&
        Math.Abs(p.Green - reference[1]) <= tolerance &&
        Math.Abs(p.Blue - reference[2]) <= tolerance &&
        Math.Abs(p.Alpha - reference[3]) <= tolerance;

    /// <summary>Wand flood fill の gap-closing 版。線画 barrier を gapRadius
    /// 膨張させ立入禁止にすることで微小な隙からの漏出を防ぐ。gapRadius=0 は
    /// SelectionTools.WandMask と同等。</summary>
    public static (byte[] mask, int count) WandMaskGapClosed(SKBitmap sample, int seedX, int seedY,
        int radius, int tolerance, bool contiguous, int gapRadius)
    {
        int w = sample.Width, h = sample.Height;
        var empty = new byte[w * h];
        if (w <= 0 || h <= 0 || seedX < 0 || seedY < 0 || seedX >= w || seedY >= h) return (empty, 0);
        bool[] blocked = gapRadius > 0 ? Dilate(BarrierMask(sample), w, h, gapRadius) : new bool[w * h];
        if (blocked[seedY * w + seedX]) return (empty, 0);   // seed が線画上
        var pixels = sample.Pixels;
        int x0 = Math.Max(0, seedX - radius), x1 = Math.Min(w - 1, seedX + radius);
        int y0 = Math.Max(0, seedY - radius), y1 = Math.Min(h - 1, seedY + radius);
        long[] sums = new long[4]; long samples = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = pixels[y * w + x];
                sums[0] += c.Red; sums[1] += c.Green; sums[2] += c.Blue; sums[3] += c.Alpha;
                samples++;
            }
        int[] reference = new int[4];
        for (int c = 0; c < 4; c++) reference[c] = (int)((sums[c] + samples / 2) / Math.Max(1, samples));
        var mask = new byte[w * h];
        int count = 0;
        if (!contiguous)
        {
            for (int i = 0; i < pixels.Length; i++)
                if (!blocked[i] && WandMatches(pixels[i], reference, tolerance)) { mask[i] = 1; count++; }
            return (mask, count);
        }
        var stack = new Stack<(int x, int y)>();
        stack.Push((seedX, seedY));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            int row = y * w;
            if (mask[row + x] != 0 || blocked[row + x] || !WandMatches(pixels[row + x], reference, tolerance)) continue;
            int left = x, right = x;
            while (left > 0 && mask[row + left - 1] == 0 && !blocked[row + left - 1] && WandMatches(pixels[row + left - 1], reference, tolerance)) left--;
            while (right + 1 < w && mask[row + right + 1] == 0 && !blocked[row + right + 1] && WandMatches(pixels[row + right + 1], reference, tolerance)) right++;
            for (int i = left; i <= right; i++) mask[row + i] = 1;
            count += right - left + 1;
            for (int side = 0; side < 2; side++)
            {
                if (side == 0 ? y == 0 : y + 1 >= h) continue;
                int ny = side == 0 ? y - 1 : y + 1, nrow = ny * w;
                bool inRun = false;
                for (int nx = left; nx <= right; nx++)
                {
                    bool candidate = mask[nrow + nx] == 0 && !blocked[nrow + nx] && WandMatches(pixels[nrow + nx], reference, tolerance);
                    if (candidate && !inRun) stack.Push((nx, ny));
                    inRun = candidate;
                }
            }
        }
        return (mask, count);
    }

    /// <summary>Document 点での gap-closing wand 選択。現行 WandSelect の延長。</summary>
    public static int WandSelectGapClosed(Document doc, SKBitmap sample, SKPoint docPoint,
        int tolerance, int sampleRadius, bool contiguous, SelectionMode mode, int gapRadius)
    {
        if (sample == null) return 0;
        int sx = (int)MathF.Floor(docPoint.X), sy = (int)MathF.Floor(docPoint.Y);
        if (sample.Width != doc.Width || sample.Height != doc.Height) return 0;
        tolerance = Math.Clamp(tolerance, 0, 255);
        sampleRadius = Math.Clamp(sampleRadius, 0, 2);
        gapRadius = Math.Clamp(gapRadius, 0, 8);
        var (mask, count) = WandMaskGapClosed(sample, sx, sy, sampleRadius, tolerance, contiguous, gapRadius);
        if (count == 0)
        {
            if (mode == SelectionMode.Replace) doc.ClearSelection();
            return 0;
        }
        SelectionTools.ApplyMask(doc, mask, SelectionKind.Wand, null, mode);
        return count;
    }
}

// ---------- canvas 回転 view (GIMP 作画検証回転の考え・表示のみ) ----------
public static class CanvasViewOps
{
    /// <summary>角度を (-180, 180] に正規化。画素は触らない (view のみ)。</summary>
    public static float NormalizeAngle(float deg)
    {
        deg %= 360f;
        if (deg <= -180f) deg += 360f;
        else if (deg > 180f) deg -= 360f;
        return deg;
    }

    /// <summary>中心 c の周りの時計回り回転 (canvas 回転 preview の座標対応用)。</summary>
    public static SKPoint RotatePoint(SKPoint p, SKPoint c, float degClockwise)
    {
        double rad = degClockwise * Math.PI / 180.0;
        double dx = p.X - c.X, dy = p.Y - c.Y;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        return new SKPoint((float)(c.X + dx * cos - dy * sin), (float)(c.Y + dx * sin + dy * cos));
    }
}

// ---------- Solo 表示 (ibisPaint Solo の考え) ----------
public static class SoloOps
{
    public static void SetSolo(Document doc, Guid layerId) => doc.SoloLayerId = layerId;
    public static void Clear(Document doc) => doc.SoloLayerId = null;
    public static bool IsSoloActive(Document doc) => doc != null && doc.SoloLayerId.HasValue;
    /// <summary>Solo 有効時は対象層のみ表示 (view 状態・Undo 対象外)。</summary>
    public static bool IsShown(Document doc, Layer layer)
    {
        if (doc?.SoloLayerId is not Guid solo) return true;
        return layer != null && layer.Id == solo;
    }
}

// ---------- ASE 読込 (Adobe Swatch Exchange・RGB/CMYK/Gray) ----------
public static class AseReader
{
    /// <summary>ASE バイト列を読んで swatch 列にする。未知 block・LAB は飛ばす。
    /// 形式: "ASEF" + BE16 ver(1.0) + BE32 blocks; color block(0x0001):
    /// BE16 nameLen + UTF16BE名 + 4Byte model + BE float値 + BE16 colorType。</summary>
    public static List<PaletteColor> Read(byte[] data)
    {
        var result = new List<PaletteColor>();
        if (data == null || data.Length < 12) throw new InvalidDataException("Too short for ASEF");
        if (data[0] != (byte)'A' || data[1] != (byte)'S' || data[2] != (byte)'E' || data[3] != (byte)'F')
            throw new InvalidDataException("Missing ASEF signature");
        int pos = 4;
        ushort verMajor = U16(data, ref pos), verMinor = U16(data, ref pos);
        if (verMajor != 1 || verMinor != 0) throw new InvalidDataException($"Unsupported ASE version {verMajor}.{verMinor}");
        uint blocks = U32(data, ref pos);
        for (uint b = 0; b < blocks; b++)
        {
            if (pos + 6 > data.Length) throw new InvalidDataException("Truncated block header");
            ushort type = U16(data, ref pos);
            uint len = U32(data, ref pos);
            if (pos + len > (uint)data.Length) throw new InvalidDataException("Truncated block");
            int end = pos + (int)len;
            if (type == 0x0001)
            {
                var c = ReadColor(data, ref pos, end);
                if (c.HasValue) result.Add(c.Value);
                pos = end;
            }
            else pos = end;   // group start/end 等は読み飛ばし
        }
        return result;
    }

    static PaletteColor? ReadColor(byte[] d, ref int pos, int end)
    {
        if (pos + 2 > end) return null;
        ushort nameLen = U16(d, ref pos);
        pos += nameLen * 2;   // UTF-16BE 名 (null 終端込) を飛ばす
        if (pos + 4 > end) return null;
        string model = new(new[] { (char)d[pos], (char)d[pos + 1], (char)d[pos + 2], (char)d[pos + 3] });
        pos += 4;
        try
        {
            switch (model)
            {
                case "RGB ":
                    if (pos + 12 > end) return null;
                    return new PaletteColor(Clamp01(F32(d, ref pos)), Clamp01(F32(d, ref pos)), Clamp01(F32(d, ref pos)));
                case "CMYK":
                    if (pos + 16 > end) return null;
                    float c = F32(d, ref pos), m = F32(d, ref pos), y = F32(d, ref pos), k = F32(d, ref pos);
                    return new PaletteColor(Clamp01(1 - Math.Min(1, c * (1 - k) + k)),
                        Clamp01(1 - Math.Min(1, m * (1 - k) + k)), Clamp01(1 - Math.Min(1, y * (1 - k) + k)));
                case "Gray":
                    if (pos + 4 > end) return null;
                    double g = Clamp01(F32(d, ref pos));
                    return new PaletteColor(g, g, g);
                default: return null;   // LAB 等は対象外として飛ばす
            }
        }
        catch { return null; }
    }

    static double Clamp01(float v) => double.IsFinite(v) ? Math.Min(1, Math.Max(0, v)) : 0;

    static float F32(byte[] d, ref int pos) { uint u = U32(d, ref pos); return BitConverter.ToSingle(BitConverter.GetBytes(u), 0); }

    static ushort U16(byte[] d, ref int pos)
    {
        if (pos + 2 > d.Length) throw new InvalidDataException("Truncated u16");
        ushort v = (ushort)((d[pos] << 8) | d[pos + 1]);
        pos += 2;
        return v;
    }

    static uint U32(byte[] d, ref int pos)
    {
        if (pos + 4 > d.Length) throw new InvalidDataException("Truncated u32");
        uint v = ((uint)d[pos] << 24) | ((uint)d[pos + 1] << 16) | ((uint)d[pos + 2] << 8) | d[pos + 3];
        pos += 4;
        return v;
    }
}

// ---------- Harmony 盤 (Procreate Harmony 式・5色盤の考え) ----------
public enum HarmonyKind { Complementary, Analogous, Triadic, SplitComplementary, Square }

public static class HarmonyOps
{
    public static readonly HarmonyKind[] AllKinds =
        { HarmonyKind.Complementary, HarmonyKind.Analogous, HarmonyKind.Triadic,
          HarmonyKind.SplitComplementary, HarmonyKind.Square };

    /// <summary>基色の HSB (S/B 維持) から和声色列を作る。先頭は基色そのもの。</summary>
    public static List<PaletteColor> Build(PaletteColor basis, HarmonyKind kind)
    {
        var hsb = new PickerHSB(basis);
        double[] offsets = kind switch
        {
            HarmonyKind.Complementary => new[] { 0.0, 180.0 },
            HarmonyKind.Analogous => new[] { 0.0, -30.0, 30.0 },
            HarmonyKind.Triadic => new[] { 0.0, 120.0, 240.0 },
            HarmonyKind.SplitComplementary => new[] { 0.0, 150.0, 210.0 },
            HarmonyKind.Square => new[] { 0.0, 90.0, 180.0, 270.0 },
            _ => new[] { 0.0 },
        };
        var list = new List<PaletteColor>();
        foreach (var off in offsets)
        {
            double hue = ((hsb.Hue + off) % 360 + 360) % 360;
            list.Add(new PickerHSB(hue, hsb.Saturation, hsb.Brightness).RGB);
        }
        return list;
    }
}

// ---------- profile preview (ClipStudio profile preview の考え・近似) ----------
public enum PreviewProfile { SRGB, DisplayP3, Grayscale }

public static class ProfilePreviewOps
{
    public static readonly PreviewProfile[] AllProfiles =
        { PreviewProfile.SRGB, PreviewProfile.DisplayP3, PreviewProfile.Grayscale };

    public static string Label(PreviewProfile p) => p switch
    {
        PreviewProfile.DisplayP3 => "Display-P3 模擬",
        PreviewProfile.Grayscale => "Grayscale 模擬",
        _ => "sRGB (素通し)",
    };

    /// <summary>出力 profile 模擬の近似表示。sRGB は恒等。P3 は広色域の
    /// 彩度持ち上がり近似 (luma 中心に chroma を 6% 拡大)。Gray は Rec.709 luma。
    /// いずれも画素は触らず palette 表示専用。</summary>
    public static PaletteColor Preview(PaletteColor c, PreviewProfile p) => p switch
    {
        PreviewProfile.Grayscale => Gray(c),
        PreviewProfile.DisplayP3 => P3Approx(c),
        _ => c,
    };

    static PaletteColor Gray(PaletteColor c)
    {
        double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        return new PaletteColor(l, l, l);
    }

    static PaletteColor P3Approx(PaletteColor c)
    {
        double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        double f(double v) => Math.Min(1, Math.Max(0, l + (v - l) * 1.06));
        return new PaletteColor(f(c.R), f(c.G), f(c.B));
    }
}

// ---------- `/` filter 検索 (GIMP `/` の考え) ----------
public static class FilterCatalog
{
    public record Entry(string Name, string Category, string Tag);
    public static readonly Entry[] All =
    {
        new("Curves", "調整", "curves"), new("Levels", "調整", "levels"),
        new("Hue-Saturation", "調整", "hue"), new("Exposure", "調整", "exposure"),
        new("GradientMap", "調整", "gradientmap"), new("Grain", "フィルタ", "grain"),
        new("Noise", "フィルタ", "noise"), new("Lens補正", "フィルタ", "lens"),
        new("Gaussian Blur", "フィルタ", "blur"), new("Motion Blur", "フィルタ", "motionblur"),
        new("Content Fill", "選択", "contentfill"), new("Invert", "調整", "invert"),
        new("Brightness", "調整", "brightness"), new("Contrast", "調整", "contrast"),
        new("Saturation", "調整", "saturation"),
    };

    /// <summary>部分一致 (大小無視・Name/Category/Tag)。空は全件。</summary>
    public static Entry[] Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;
        string q = query.Trim().ToLowerInvariant();
        return All.Where(e => e.Name.ToLowerInvariant().Contains(q) ||
            e.Category.Contains(query.Trim()) || e.Tag.Contains(q)).ToArray();
    }
}
