// Compositor — adjustment + filter engine port (Mac Document/Curves.swift,
// Document/Levels.swift, Document/LevelsAutomatic.swift, Document/Gradient.swift
// (GradientMap part), Document/HueSaturation.swift, Document/ImageAdjustments.swift
// (Exposure/GradientMap/Grain), Document/Filters.swift (PixelFilter subset),
// Document/LayerAdjustment.swift, Document/AdjustmentEditing.swift subset,
// Rendering/AdjustPixels.c, Rendering/LevelsPixels.c, Rendering/NoisePixels.c,
// Rendering/LensPixels.c).
//
// CPU path works in straight (unpremultiplied) RGB, which is exactly what the Mac
// C kernels do for translucent pixels (they unpremultiply before the lookup and
// premultiply after). For opaque pixels the result is bit-identical to the C math.
// Blur uses SkiaSharp image filters; Motion Blur is a directional multi-tap average
// (Core Image CIMotionBlur is unavailable — documented approximation, same reach).
//
// Migration note (task item 6): the preferred path for new adjustments is an
// adjustment layer (Layer.IsAdjustmentLayer + Layer.Adjustment), which applies to
// the composite below at draw/compose time (Mac LayerAdjustment/AdjustmentEditing
// subset). The legacy per-layer Brightness/Contrast/Saturation/Blur/Invert fields
// on Layer remain for compatibility but are deprecated for new adjustments.
using SkiaSharp;

namespace Compositor;

// ---------- shared ----------
public struct AdjustmentColor
{
    public double Red, Green, Blue;   // 0..1 straight sRGB
    public AdjustmentColor(double r, double g, double b) { Red = r; Green = g; Blue = b; }
    public bool IsValid => Finite01(Red) && Finite01(Green) && Finite01(Blue);
    public AdjustmentColor Clamped => new(Clamp01(Red), Clamp01(Green), Clamp01(Blue));
    static bool Finite01(double v) => double.IsFinite(v) && v >= 0 && v <= 1;
    static double Clamp01(double v) => double.IsFinite(v) ? Math.Min(1, Math.Max(0, v)) : 0;
}

static class AdjustMath
{
    public static double Clamp(double v, double lo, double hi, double fallback) =>
        double.IsFinite(v) ? Math.Min(hi, Math.Max(lo, v)) : fallback;
    public static float LerpTable(float[] t, int channel, double v)
    {
        double x = Math.Min(255, Math.Max(0, v * 255));
        int lo = Math.Min(254, (int)x), hi = lo + 1;
        int b = channel * 256;
        return (float)(t[b + lo] + (t[b + hi] - t[b + lo]) * (x - lo));
    }
    public static byte ClampB(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}

// ---------- Curves (Curves.swift) ----------
public struct CurvePoint
{
    public double X, Y;   // 0..255 in curve space
    public CurvePoint(double x, double y) { X = x; Y = y; }
}

public class CurvesSettings
{
    public LevelsChannel Channel = LevelsChannel.Rgb;
    public List<List<CurvePoint>> Channels = new()
    {
        new() { new(0, 0), new(255, 255) },
        new() { new(0, 0), new(255, 255) },
        new() { new(0, 0), new(255, 255) },
        new() { new(0, 0), new(255, 255) },
    };

    public bool IsValid =>
        Channels.Count == 4 && Channels.All(p =>
            p.Count >= 2 && p.Count <= 32 && p[0].X == 0 && p[^1].X == 255 &&
            p.All(q => double.IsFinite(q.X) && double.IsFinite(q.Y) &&
                q.X >= 0 && q.X <= 255 && q.Y >= 0 && q.Y <= 255) &&
            ZipStrictlyIncreasing(p));

    static bool ZipStrictlyIncreasing(List<CurvePoint> p)
    {
        for (int i = 0; i + 1 < p.Count; i++)
            if (!(p[i].X < p[i + 1].X)) return false;
        return true;
    }

    /// <summary>Shape-preserving cubic Hermite interpolation (Mac CurvesSettings.value port).</summary>
    public double Value(double x, int channel)
    {
        var p = Channels[channel];
        int i = Math.Min(p.Count - 2, Math.Max(0, LastIndexLE(p, x)));
        double[] d = new double[p.Count - 1];
        for (int k = 0; k < d.Length; k++) d[k] = (p[k + 1].Y - p[k].Y) / (p[k + 1].X - p[k].X);
        double Slope(int j)
        {
            if (j == 0) return d[0];
            if (j == p.Count - 1) return d[^1];
            if (d[j - 1] * d[j] <= 0) return 0;
            return 2 / (1 / d[j - 1] + 1 / d[j]);
        }
        double h = p[i + 1].X - p[i].X, t = Math.Min(1, Math.Max(0, (x - p[i].X) / h));
        double y = (2 * t * t * t - 3 * t * t + 1) * p[i].Y + (t * t * t - 2 * t * t + t) * h * Slope(i)
            + (-2 * t * t * t + 3 * t * t) * p[i + 1].Y + (t * t * t - t * t) * h * Slope(i + 1);
        return Math.Min(255, Math.Max(0, y));
    }

    static int LastIndexLE(List<CurvePoint> p, double x)
    {
        int r = -1;
        for (int i = 0; i < p.Count; i++) if (p[i].X <= x) r = i;
        return r < 0 ? 0 : r;
    }

    /// <summary>Mac apply(): per-channel curve first, then the master RGB curve.</summary>
    public float[] BuildTables()
    {
        var t = new float[3 * 256];
        for (int ch = 1; ch <= 3; ch++)
            for (int i = 0; i < 256; i++)
                t[(ch - 1) * 256 + i] = (float)(Value(Value(i, ch), 0) / 255);
        return t;
    }

    public CurvesSettings Clone()
    {
        var c = new CurvesSettings { Channel = Channel };
        c.Channels = Channels.Select(l => l.Select(p => new CurvePoint(p.X, p.Y)).ToList()).ToList();
        return c;
    }
}

// ---------- Levels (Levels.swift / LevelsAutomatic.swift / LevelsPixels.c) ----------
public enum LevelsChannel { Rgb, Red, Green, Blue }

public struct LevelRange
{
    public double Black = 0, Gamma = 1, White = 255, OutputBlack = 0, OutputWhite = 255;
    public LevelRange() { }
    // NOTE: `new LevelRange[n]` zero-fills (field initializers do NOT run), so every
    // constructor below assigns all fields explicitly. Array holders must fill with
    // `new LevelRange()`, never rely on `new LevelRange[n]` defaults.
    public LevelRange(double black, double white) { Black = black; Gamma = 1; White = white; OutputBlack = 0; OutputWhite = 255; }
    public LevelRange(double black, double gamma, double white, double ob, double ow)
    { Black = black; Gamma = gamma; White = white; OutputBlack = ob; OutputWhite = ow; }

    public LevelRange Normalized
    {
        get
        {
            var r = this;
            r.Black = AdjustMath.Clamp(Black, 0, 254, 0);
            r.White = AdjustMath.Clamp(White, r.Black + 1, 255, 255);
            r.Gamma = AdjustMath.Clamp(Gamma, 0.1, 9.99, 1);
            r.OutputBlack = AdjustMath.Clamp(OutputBlack, 0, 255, 0);
            r.OutputWhite = AdjustMath.Clamp(OutputWhite, 0, 255, 255);
            return r;
        }
    }

    public double Apply(double value)
    {
        var s = Normalized;
        double input = Math.Min(1, Math.Max(0, (value * 255 - s.Black) / (s.White - s.Black)));
        return (s.OutputBlack + Math.Pow(input, 1 / s.Gamma) * (s.OutputWhite - s.OutputBlack)) / 255;
    }
}

public class LevelsSettings
{
    public LevelsChannel Channel = LevelsChannel.Rgb;
    // Explicit per-element init: `new LevelRange[4]` would zero-fill (Gamma=0...).
    public LevelRange[] Ranges = new[] { new LevelRange(), new LevelRange(), new LevelRange(), new LevelRange() };
    public LevelRange Current
    {
        get => Ranges[(int)Channel];
        set => Ranges[(int)Channel] = value.Normalized;
    }
    public bool IsIdentity => Ranges.All(r => r.Normalized.Equals(new LevelRange()));
    /// <summary>Individual channel first, then the composite RGB (Mac LevelsSettings.apply port).</summary>
    public double Apply(double value, LevelsChannel channel) =>
        Ranges[0].Apply(Ranges[(int)channel].Apply(value));

    public float[] BuildTables()
    {
        var t = new float[3 * 256];
        var chs = new[] { LevelsChannel.Red, LevelsChannel.Green, LevelsChannel.Blue };
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 256; i++)
                t[c * 256 + i] = (float)Apply(i / 255.0, chs[c]);
        return t;
    }

    public LevelsSettings Clone()
    {
        var c = new LevelsSettings { Channel = Channel };
        Ranges.CopyTo(c.Ranges, 0);
        return c;
    }
}

public enum LevelsAutoMode { Contrast, Color, Neutral }
public enum LevelsSampleMode { Black, Gray, White }

public static class LevelsOps
{
    public static void ApplyToBitmap(SKBitmap bmp, LevelsSettings settings)
    {
        if (bmp == null || settings.IsIdentity) return;
        var tables = settings.BuildTables();
        ApplyTablesToBitmap(bmp, tables);
    }

    /// <summary>levels_apply port (C): LUT with linear interpolation between entries.</summary>
    public static void ApplyTablesToBitmap(SKBitmap bmp, float[] tables)
    {
        if (bmp == null || tables == null || tables.Length < 768) return;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 0) continue;
                bmp.SetPixel(x, y, new SKColor(
                    AdjustMath.ClampB(AdjustMath.LerpTable(tables, 0, c.Red / 255.0) * 255),
                    AdjustMath.ClampB(AdjustMath.LerpTable(tables, 1, c.Green / 255.0) * 255),
                    AdjustMath.ClampB(AdjustMath.LerpTable(tables, 2, c.Blue / 255.0) * 255),
                    c.Alpha));
            }
    }

    /// <summary>levels_histogram port: bins[0..3] = mean,R,G,B (256 each).</summary>
    public static double[] Histogram(SKBitmap bmp, byte[] coverage = null)
    {
        var bins = new double[1024];
        if (bmp == null) return bins;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.Alpha == 0) continue;
                int i = y * bmp.Width + x;
                double w = p.Alpha / 255.0 * (coverage != null ? coverage[i] / 255.0 : 1);
                int[] v = { p.Red, p.Green, p.Blue };
                for (int c = 0; c < 3; c++)
                {
                    bins[(c + 1) * 256 + v[c]] += w;
                    bins[v[c]] += w / 3.0;
                }
            }
        return bins;
    }

    public static double[][] Histogram4(SKBitmap bmp, byte[] coverage = null)
    {
        var flat = Histogram(bmp, coverage);
        return Enumerable.Range(0, 4).Select(k => flat.Skip(k * 256).Take(256).ToArray()).ToArray();
    }

    /// <summary>LevelsHistogramDisplay.scale port: cap isolated spikes at 4x the 95th interior percentile.</summary>
    public static double DisplayScale(double[] bins)
    {
        var pos = bins.Where(v => double.IsFinite(v) && v > 0).ToList();
        if (pos.Count == 0) return 0;
        double peak = pos.Max();
        var interior = bins.Skip(1).Take(Math.Max(0, bins.Length - 2))
            .Where(v => double.IsFinite(v) && v > 0).OrderBy(v => v).ToList();
        if (interior.Count == 0) return peak;
        double typical = interior[(int)((interior.Count - 1) * 0.95)];
        return Math.Min(peak, typical * 4);
    }

    static (double low, double high)? Endpoints(double[] bins)
    {
        double total = bins.Sum();
        if (total <= 0) return null;
        double sum = 0; int low = 0, high = 255;
        for (int i = 0; i < 256; i++) { sum += bins[i]; if (sum > total * 0.001) { low = i; break; } }
        sum = 0;
        for (int i = 255; i >= 0; i--) { sum += bins[i]; if (sum > total * 0.001) { high = i; break; } }
        return low < high ? (low, high) : null;
    }

    /// <summary>LevelsAuto.settings port. histogram = 4x256 (mean,R,G,B).</summary>
    public static LevelsSettings AutoSettings(double[][] histogram, LevelsAutoMode mode)
    {
        var result = new LevelsSettings();
        if (mode == LevelsAutoMode.Contrast)
        {
            var limits = histogram.Skip(1).Select(Endpoints).Where(e => e != null).ToList();
            if (limits.Count > 0)
            {
                double low = limits.Min(e => e.Value.low), high = limits.Max(e => e.Value.high);
                if (low < high) result.Ranges[0] = new LevelRange(low, high);
            }
        }
        else
        {
            for (int c = 1; c <= 3; c++)
            {
                var ep = Endpoints(histogram[c]);
                if (ep == null) continue;
                var range = new LevelRange(ep.Value.low, ep.Value.high);
                if (mode == LevelsAutoMode.Neutral)
                {
                    double total = histogram[c].Sum();
                    double mean = 0;
                    for (int i = 0; i < 256; i++) mean += range.Apply(i / 255.0) * histogram[c][i];
                    mean /= total;
                    if (mean > 0 && mean < 1)
                        range.Gamma = Math.Min(9.99, Math.Max(0.1, Math.Log(mean) / Math.Log(0.5)));
                }
                result.Ranges[c] = range.Normalized;
            }
        }
        return result;
    }

    /// <summary>LevelsSettings.sampling port: rgb = unpremultiplied 0..1 triple.</summary>
    public static LevelsSettings Sample(LevelsSettings settings, double[] rgb, LevelsSampleMode mode)
    {
        var result = settings.Clone();
        result.Ranges[0] = new LevelRange();
        for (int c = 1; c <= 3; c++)
        {
            var range = result.Ranges[c];
            double v = rgb[c - 1] * 255;
            switch (mode)
            {
                case LevelsSampleMode.Black: range.Black = Math.Min(range.White - 1, Math.Max(0, v)); break;
                case LevelsSampleMode.White: range.White = Math.Max(range.Black + 1, Math.Min(255, v)); break;
                case LevelsSampleMode.Gray:
                    double frac = (v - range.Black) / (range.White - range.Black);
                    if (frac <= 0 || frac >= 1) continue;
                    range.Gamma = Math.Log(frac) / Math.Log(0.5);
                    break;
            }
            range.OutputBlack = 0; range.OutputWhite = 255;
            result.Ranges[c] = range.Normalized;
        }
        return result;
    }
}

// ---------- Exposure (ImageAdjustments.swift) ----------
public class ExposureSettings
{
    public double Exposure;   // stops, -20..20
    public double Offset;     // -0.5..0.5 linear
    public double Gamma = 1;  // 0.01..9.99
    public bool IsValid =>
        Exposure is >= -20 and <= 20 && Offset is >= -0.5 and <= 0.5 && Gamma is >= 0.01 and <= 9.99;
    public ExposureSettings Normalized => new()
    {
        Exposure = AdjustMath.Clamp(Exposure, -20, 20, 0),
        Offset = AdjustMath.Clamp(Offset, -0.5, 0.5, 0),
        Gamma = AdjustMath.Clamp(Gamma, 0.01, 9.99, 1),
    };
    public float[] Table
    {
        get
        {
            var n = Normalized;
            double scale = Math.Pow(2, n.Exposure);
            return Enumerable.Range(0, 256).Select(i =>
            {
                double enc = i / 255.0;
                double lin = enc <= 0.04045 ? enc / 12.92 : Math.Pow((enc + 0.055) / 1.055, 2.4);
                lin = Math.Pow(Math.Max(0, lin * scale + n.Offset), 1 / n.Gamma);
                double output = lin <= 0.0031308 ? lin * 12.92 : 1.055 * Math.Pow(lin, 1 / 2.4) - 0.055;
                return (float)Math.Min(1, Math.Max(0, output));
            }).ToArray();
        }
    }
    public ExposureSettings Clone() => new() { Exposure = Exposure, Offset = Offset, Gamma = Gamma };
    public override bool Equals(object o) => o is ExposureSettings e &&
        Exposure == e.Exposure && Offset == e.Offset && Gamma == e.Gamma;
    public override int GetHashCode() => HashCode.Combine(Exposure, Offset, Gamma);
}

public static class ExposureOps
{
    public static void ApplyToBitmap(SKBitmap bmp, ExposureSettings settings)
    {
        if (bmp == null) return;
        var n = settings.Normalized;
        if (n.Equals(new ExposureSettings())) return;
        var one = n.Table;
        var tables = one.Concat(one).Concat(one).ToArray();
        LevelsOps.ApplyTablesToBitmap(bmp, tables);
    }
}

// ---------- Gradient Map (ImageAdjustments.swift + AdjustPixels.c) ----------
public class GradientMapSettings
{
    public AdjustmentColor Shadows = new(0, 0, 0);
    public AdjustmentColor Highlights = new(1, 1, 1);
    public bool Reversed;
    public bool IsValid => Shadows.IsValid && Highlights.IsValid;
    public GradientMapSettings Normalized => new()
    {
        Shadows = Shadows.Clamped, Highlights = Highlights.Clamped, Reversed = Reversed,
    };
    public (AdjustmentColor dark, AdjustmentColor light) Ends =>
        Reversed ? (Highlights, Shadows) : (Shadows, Highlights);
    public byte[] Table
    {
        get
        {
            var (dark, light) = Ends;
            var t = new byte[256 * 3];
            for (int i = 0; i < 256; i++)
            {
                double k = i / 255.0;
                t[i * 3] = AdjustMath.ClampB((dark.Red + (light.Red - dark.Red) * k) * 255);
                t[i * 3 + 1] = AdjustMath.ClampB((dark.Green + (light.Green - dark.Green) * k) * 255);
                t[i * 3 + 2] = AdjustMath.ClampB((dark.Blue + (light.Blue - dark.Blue) * k) * 255);
            }
            return t;
        }
    }
    public GradientMapSettings Clone() => new()
    { Shadows = Shadows, Highlights = Highlights, Reversed = Reversed };
}

public static class GradientMapOps
{
    /// <summary>adjust_gradient_map port (luminance weights 2126/7152/722).</summary>
    public static void ApplyToBitmap(SKBitmap bmp, GradientMapSettings settings)
    {
        if (bmp == null) return;
        var table = settings.Normalized.Table;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 0) continue;
                uint level = (2126u * c.Red + 7152u * c.Green + 722u * c.Blue + 5000u) / 10000u;
                if (level > 255) level = 255;
                bmp.SetPixel(x, y, new SKColor(
                    table[level * 3], table[level * 3 + 1], table[level * 3 + 2], c.Alpha));
            }
    }
}

// ---------- Grain (ImageAdjustments.swift + AdjustPixels.c) ----------
public class GrainSettings
{
    public double Amount = 25;    // 0..100
    public double Size = 1.5;     // document px, 0.5..20
    public double Roughness = 50; // 0..100
    public uint Seed;
    public bool IsValid => Amount is >= 0 and <= 100 && Size is >= 0.5 and <= 20 && Roughness is >= 0 and <= 100;
    public GrainSettings Normalized => new()
    {
        Amount = AdjustMath.Clamp(Amount, 0, 100, 25),
        Size = AdjustMath.Clamp(Size, 0.5, 20, 1.5),
        Roughness = AdjustMath.Clamp(Roughness, 0, 100, 50),
        Seed = Seed,
    };
    public GrainSettings Clone() => new() { Amount = Amount, Size = Size, Roughness = Roughness, Seed = Seed };
}

public static class GrainOps
{
    static uint Mix32(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
        return x;
    }
    static float Lattice(long ix, long iy, uint seed)
    {
        uint h = Mix32((uint)ix * 0x9E3779B1U ^ Mix32((uint)iy * 0x85EBCA77U ^ seed));
        return (h & 0xFFFFU) / 65535f + (h >> 16) / 65535f - 1f;
    }

    /// <summary>adjust_grain port. origin/unitsPerPixel fix the pattern in document space.</summary>
    public static void ApplyToBitmap(SKBitmap bmp, GrainSettings settings,
        double originX = 0, double originY = 0, double unitsPerPixel = 1, uint? seedOverride = null)
    {
        if (bmp == null) return;
        var n = settings.Normalized;
        if (!(n.Amount > 0) || !(unitsPerPixel > 0)) return;
        double size = n.Size > 0 ? n.Size : 1;
        float strength = (float)(n.Amount > 100 ? 1.0 : n.Amount / 100.0) * 0.35f * 255f;
        float rough = (float)(n.Roughness < 0 ? 0 : n.Roughness > 100 ? 1 : n.Roughness / 100.0);
        uint seed = seedOverride ?? n.Seed;
        uint fineSeed = Mix32(seed ^ 0xA511E9B3U);
        for (int y = 0; y < bmp.Height; y++)
        {
            double v = originY + (y + 0.5) * unitsPerPixel;
            double cellY = Math.Floor(v / size);
            float ty = (float)(v / size - cellY);
            ty = ty * ty * (3f - 2f * ty);
            long iy = (long)cellY, fineY = (long)Math.Floor(v);
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 0) continue;
                double u = originX + (x + 0.5) * unitsPerPixel;
                double cellX = Math.Floor(u / size);
                float tx = (float)(u / size - cellX);
                tx = tx * tx * (3f - 2f * tx);
                long ix = (long)cellX;
                float n00 = Lattice(ix, iy, seed), n10 = Lattice(ix + 1, iy, seed);
                float n01 = Lattice(ix, iy + 1, seed), n11 = Lattice(ix + 1, iy + 1, seed);
                float top = n00 + (n10 - n00) * tx, bot = n01 + (n11 - n01) * tx;
                float smooth = (top + (bot - top) * ty) * 1.6f;
                float fine = Lattice((long)Math.Floor(u), fineY, fineSeed);
                float noise = smooth + (fine - smooth) * rough;
                float level = Math.Min(1, (0.2126f * c.Red + 0.7152f * c.Green + 0.0722f * c.Blue) / 255f);
                float delta = noise * strength * (0.4f + 2.4f * level * (1f - level));
                bmp.SetPixel(x, y, new SKColor(
                    AdjustMath.ClampB(c.Red + delta), AdjustMath.ClampB(c.Green + delta),
                    AdjustMath.ClampB(c.Blue + delta), c.Alpha));
            }
        }
    }
}

// ---------- Hue/Saturation (HueSaturation.swift) ----------
public enum ColorRange { Master, Reds, Yellows, Greens, Cyans, Blues, Magentas }

public static class ColorRangeExt
{
    public static HueBand DefaultBand(this ColorRange r) => r switch
    {
        ColorRange.Master => new(0, 0, 360, 360),
        ColorRange.Reds => new(315, 345, 15, 45),
        ColorRange.Yellows => new(15, 45, 75, 105),
        ColorRange.Greens => new(75, 105, 135, 165),
        ColorRange.Cyans => new(135, 165, 195, 225),
        ColorRange.Blues => new(195, 225, 255, 285),
        ColorRange.Magentas => new(255, 285, 315, 345),
        _ => new(0, 0, 360, 360),
    };
    public static IEnumerable<ColorRange> Chromatic() =>
        Enum.GetValues<ColorRange>().Where(r => r != ColorRange.Master);
}

public struct HueBand
{
    public double FalloffStart, RangeStart, RangeEnd, FalloffEnd;
    public HueBand(double fs, double rs, double re, double fe)
    { FalloffStart = fs; RangeStart = rs; RangeEnd = re; FalloffEnd = fe; }

    public static double Forward(double from, double to)
    {
        double d = (to - from) % 360;
        return d < 0 ? d + 360 : d;
    }

    public double Weight(double hue)
    {
        double span = Forward(FalloffStart, FalloffEnd);
        if (span <= 0) return 1;
        double pos = Forward(FalloffStart, hue);
        if (pos > span) return 0;
        double rampIn = Forward(FalloffStart, RangeStart);
        double plateauEnd = Forward(FalloffStart, RangeEnd);
        if (pos < rampIn) return rampIn > 0 ? pos / rampIn : 1;
        if (pos <= plateauEnd) return 1;
        double rampOut = span - plateauEnd;
        return rampOut > 0 ? (span - pos) / rampOut : 1;
    }

    public double[] Handles => new[] { FalloffStart, RangeStart, RangeEnd, FalloffEnd };

    public HueBand Centered(double hue)
    {
        double core = Forward(RangeStart, RangeEnd);
        double leading = Forward(FalloffStart, RangeStart);
        double trailing = Forward(RangeEnd, FalloffEnd);
        static double Wrap(double v) { double r = v % 360; return r < 0 ? r + 360 : r; }
        double start = Wrap(hue - core / 2);
        return new HueBand(Wrap(start - leading), start, Wrap(start + core), Wrap(start + core + trailing));
    }

    public HueBand Including(double hue)
    {
        var b = this;
        if (b.Weight(hue) >= 1) return b;
        double shoulderIn = Forward(b.FalloffStart, b.RangeStart);
        double shoulderOut = Forward(b.RangeEnd, b.FalloffEnd);
        if (Forward(hue, b.RangeStart) <= Forward(b.RangeEnd, hue))
        { b.RangeStart = hue; b.FalloffStart = hue - shoulderIn; }
        else
        { b.RangeEnd = hue; b.FalloffEnd = hue + shoulderOut; }
        return b.Normalized();
    }

    public HueBand Excluding(double hue)
    {
        var b = this;
        if (b.Weight(hue) <= 0) return b;
        double shoulderIn = Forward(b.FalloffStart, b.RangeStart);
        double shoulderOut = Forward(b.RangeEnd, b.FalloffEnd);
        if (Forward(b.FalloffStart, hue) <= Forward(hue, b.FalloffEnd))
        { b.FalloffStart = hue + 1; b.RangeStart = hue + 1 + shoulderIn; }
        else
        { b.FalloffEnd = hue - 1; b.RangeEnd = hue - 1 - shoulderOut; }
        return b.Normalized();
    }

    public HueBand Normalized()
    {
        var b = this;
        static double Wrap(double v) { double r = v % 360; return r < 0 ? r + 360 : r; }
        b.FalloffStart = Wrap(b.FalloffStart); b.RangeStart = Wrap(b.RangeStart);
        b.RangeEnd = Wrap(b.RangeEnd); b.FalloffEnd = Wrap(b.FalloffEnd);
        if (Forward(b.FalloffStart, b.FalloffEnd) > 350)
            b.FalloffEnd = Wrap(b.FalloffStart + 350);
        return b;
    }

    public HueBand WithHandle(int index, double degrees)
    {
        var u = this;
        double v = ((degrees % 360) + 360) % 360;
        if (index == 0) u.FalloffStart = v;
        else if (index == 1) u.RangeStart = v;
        else if (index == 2) u.RangeEnd = v;
        else u.FalloffEnd = v;
        double span = Forward(u.FalloffStart, u.FalloffEnd);
        double toStart = Forward(u.FalloffStart, u.RangeStart);
        double toEnd = Forward(u.FalloffStart, u.RangeEnd);
        if (!(span > 1 && span <= 350 && toStart <= toEnd && toEnd <= span)) return this;
        return u;
    }
}

public struct RangeAdjustment
{
    public double Hue, Saturation, Lightness;
    public RangeAdjustment(double h = 0, double s = 0, double l = 0) { Hue = h; Saturation = s; Lightness = l; }
    public static bool operator ==(RangeAdjustment a, RangeAdjustment b) =>
        a.Hue == b.Hue && a.Saturation == b.Saturation && a.Lightness == b.Lightness;
    public static bool operator !=(RangeAdjustment a, RangeAdjustment b) => !(a == b);
    public override bool Equals(object o) => o is RangeAdjustment r && this == r;
    public override int GetHashCode() => HashCode.Combine(Hue, Saturation, Lightness);
}

public class HueSaturationSettings
{
    public ColorRange Range = ColorRange.Master;
    public bool Colorize;
    public bool InvertRange;
    public Dictionary<ColorRange, RangeAdjustment> Adjustments = new();
    public Dictionary<ColorRange, HueBand> Bands = new();

    public HueSaturationSettings() : this(0, 0, 0) { }
    public HueSaturationSettings(double hue, double saturation, double lightness,
        bool colorize = false, ColorRange range = ColorRange.Master)
    {
        Range = range; Colorize = colorize;
        foreach (ColorRange r in Enum.GetValues<ColorRange>()) Bands[r] = r.DefaultBand();
        Adjustments[range] = new RangeAdjustment(hue, saturation, lightness);
    }

    public double Hue
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Hue : 0;
        set { var a = Adjustments.TryGetValue(Range, out var e) ? e : new(); a.Hue = value; Adjustments[Range] = a; }
    }
    public double Saturation
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Saturation : 0;
        set { var a = Adjustments.TryGetValue(Range, out var e) ? e : new(); a.Saturation = value; Adjustments[Range] = a; }
    }
    public double Lightness
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Lightness : 0;
        set { var a = Adjustments.TryGetValue(Range, out var e) ? e : new(); a.Lightness = value; Adjustments[Range] = a; }
    }
    public HueBand Band
    {
        get => Bands.TryGetValue(Range, out var b) ? b : Range.DefaultBand();
        set => Bands[Range] = value;
    }

    public static HueSaturationSettings ColorizeStart(double hue = 0) =>
        new(hue, 25, 0, colorize: true);

    public bool IsIdentity => !Colorize && Adjustments.Values.All(a => a == new RangeAdjustment());

    public double Weight(ColorRange range, double hue)
    {
        if (range == ColorRange.Master) return 1;
        double w = (Bands.TryGetValue(range, out var b) ? b : range.DefaultBand()).Weight(hue);
        return InvertRange && range == Range ? 1 - w : w;
    }

    public HueSaturationSettings Clone()
    {
        var c = new HueSaturationSettings
        { Range = Range, Colorize = Colorize, InvertRange = InvertRange };
        c.Adjustments = new(Adjustments);
        c.Bands = new(Bands);
        return c;
    }
}

public static class HueOps
{
    public record HueResponse(double Shift, double Saturation, double Lightness);

    public static HueResponse[] ResponseTable(HueSaturationSettings s)
    {
        var table = new HueResponse[361];
        for (int deg = 0; deg <= 360; deg++)
        {
            double sh = 0, sa = 0, li = 0;
            foreach (var (range, adj) in s.Adjustments)
            {
                if (adj == new RangeAdjustment()) continue;
                double w = s.Weight(range, deg);
                if (w <= 0) continue;
                sh += adj.Hue * w; sa += adj.Saturation * w; li += adj.Lightness * w;
            }
            table[deg] = new HueResponse(sh, sa, li);
        }
        return table;
    }

    public static (double h, double s, double l) ToHSL(double r, double g, double b)
    {
        double hi = Math.Max(r, Math.Max(g, b)), lo = Math.Min(r, Math.Min(g, b));
        double l = (hi + lo) / 2, d = hi - lo;
        if (d <= 0) return (0, 0, l);
        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = hi == r ? (g - b) / d : hi == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, Math.Min(1, s), l);
    }

    public static (double r, double g, double b) ToRGB(double h, double s, double l)
    {
        if (s <= 0) return (l, l, l);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double sector = h / 60;
        double x = c * (1 - Math.Abs(sector % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = (int)sector switch
        {
            0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x),
            3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x),
        };
        return (Cl01(r + m), Cl01(g + m), Cl01(b + m));
        static double Cl01(double v) => Math.Min(1, Math.Max(0, v));
    }

    public static (double r, double g, double b) AdjustPixel(double r, double g, double b,
        HueSaturationSettings s, HueResponse[] response = null)
    {
        var (h, sat, li) = ToHSL(r, g, b);
        double amount;
        if (s.Colorize)
        {
            h = s.Hue % 360;
            sat = Math.Min(1, Math.Max(0, s.Saturation / 100));
            amount = s.Lightness / 100;
        }
        else
        {
            var table = response ?? ResponseTable(s);
            var smp = table[Math.Min(table.Length - 1, Math.Max(0, (int)Math.Round(h)))];
            amount = smp.Lightness / 100;
            h = (h + smp.Shift) % 360;
            if (h < 0) h += 360;
            sat = Math.Min(1, Math.Max(0, sat * (1 + smp.Saturation / 100)));
        }
        amount = Math.Min(1, Math.Max(-1, amount));
        li = amount >= 0 ? li + (1 - li) * amount : li * (1 + amount);
        return ToRGB(h, sat, Math.Min(1, Math.Max(0, li)));
    }

    public static void ApplyToBitmap(SKBitmap bmp, HueSaturationSettings settings)
    {
        if (bmp == null || settings.IsIdentity) return;
        var table = ResponseTable(settings);
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha == 0) continue;
                var (r, g, b) = AdjustPixel(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0, settings, table);
                bmp.SetPixel(x, y, new SKColor(
                    AdjustMath.ClampB(r * 255), AdjustMath.ClampB(g * 255), AdjustMath.ClampB(b * 255), c.Alpha));
            }
    }

    public static double ShiftedHue(double hue, HueSaturationSettings s)
    {
        double shift = 0;
        foreach (var (range, adj) in s.Adjustments)
            if (adj.Hue != 0) shift += adj.Hue * s.Weight(range, hue);
        double r = (hue + shift) % 360;
        return r < 0 ? r + 360 : r;
    }

    /// <summary>Mac sampledHue port: HSB hue of a pixel, null when near-neutral.</summary>
    public static double? SampledHue(SKBitmap bmp, int x, int y)
    {
        if (bmp == null || x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return null;
        var c = bmp.GetPixel(x, y);
        if (c.Alpha == 0) return null;
        var (h, s, _) = RgbToHsb(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0);
        return s > 0.02 ? h : null;
    }

    public static (double h, double s, double b) RgbToHsb(double r, double g, double bl)
    {
        double hi = Math.Max(r, Math.Max(g, bl)), lo = Math.Min(r, Math.Min(g, bl));
        double d = hi - lo;
        double h = 0;
        if (d > 0)
        {
            h = hi == r ? (g - bl) / d : hi == g ? (bl - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
            if (h < 0) h += 360;
        }
        return (h, hi <= 0 ? 0 : d / hi, hi);
    }
}

// ---------- Filters: noise / lens / blur-expand (Filters.swift + NoisePixels.c + LensPixels.c) ----------
public class NoiseSettings
{
    public double Amount = 10;   // 0.1..400 Photoshop %
    public bool Gaussian;
    public bool Monochromatic;
    public uint Seed;
    public NoiseSettings Normalized => new()
    {
        Amount = AdjustMath.Clamp(Amount, 0.1, 400, 10),
        Gaussian = Gaussian, Monochromatic = Monochromatic, Seed = Seed,
    };
    public NoiseSettings Clone() => new()
    { Amount = Amount, Gaussian = Gaussian, Monochromatic = Monochromatic, Seed = Seed };
}

public static class NoiseOps
{
    static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
        return x;
    }
    static float Unit(uint key) => (Hash(key) >> 8) * (1f / 16777216f);

    /// <summary>noise_add port (straight-space equivalent; opaque pixels bit-identical).</summary>
    public static void ApplyToBitmap(SKBitmap bmp, NoiseSettings settings)
    {
        if (bmp == null) return;
        var n = settings.Normalized;
        float spread = (float)(n.Amount / 100.0) * 127.5f;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.Alpha == 0) continue;
                uint bas = Hash(n.Seed ^ Hash((uint)(y * bmp.Width + x)));
                int[] v = { p.Red, p.Green, p.Blue };
                for (int c = 0; c < 3; c++)
                {
                    uint key = n.Monochromatic ? bas : bas + (uint)c * 0x9e3779b9U;
                    float add;
                    if (n.Gaussian)
                    {
                        float u1 = Unit(key), u2 = Unit(key ^ 0x68e31da4U);
                        add = MathF.Sqrt(-2f * MathF.Log(1f - u1)) * MathF.Cos(6.2831853f * u2) * spread * (2f / 3f);
                    }
                    else add = (Unit(key) * 2f - 1f) * spread;
                    v[c] = AdjustMath.ClampB(v[c] + add);
                }
                bmp.SetPixel(x, y, new SKColor((byte)v[0], (byte)v[1], (byte)v[2], p.Alpha));
            }
    }
}

public static class LensOps
{
    public const double Strength = 0.35;   // Mac PixelFilter.lensStrength
    /// <summary>lens_distort port (bilinear, relative to image size). k=0 is a no-op.</summary>
    public static void ApplyToBitmap(SKBitmap bmp, double distortion)
    {
        if (bmp == null) return;
        double k = AdjustMath.Clamp(distortion, -100, 100, 0) / 100 * Strength;
        if (k == 0) return;
        int w = bmp.Width, h = bmp.Height;
        var src = new SKColor[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) src[x, y] = bmp.GetPixel(x, y);
        double cx = w * 0.5, cy = h * 0.5;
        double halfDiag2 = cx * cx + cy * cy;
        for (int y = 0; y < h; y++)
        {
            double dy = y + 0.5 - cy;
            for (int x = 0; x < w; x++)
            {
                double dx = x + 0.5 - cx;
                double scale = 1.0 - k * (dx * dx + dy * dy) / halfDiag2;
                double sx = cx + dx * scale - 0.5, sy = cy + dy * scale - 0.5;
                bmp.SetPixel(x, y, Bilinear(src, w, h, sx, sy));
            }
        }
    }

    static SKColor Bilinear(SKColor[,] s, int w, int h, double sx, double sy)
    {
        double fx0 = Math.Floor(sx), fy0 = Math.Floor(sy);
        double fx = sx - fx0, fy = sy - fy0;
        long x0 = (long)fx0, y0 = (long)fy0;
        double[] acc = new double[4];
        for (int j = 0; j < 2; j++)
        {
            long row = y0 + j;
            if (row < 0 || row >= h) continue;
            double wy = j != 0 ? fy : 1 - fy;
            if (wy == 0) continue;
            for (int i = 0; i < 2; i++)
            {
                long col = x0 + i;
                if (col < 0 || col >= w) continue;
                double wgt = wy * (i != 0 ? fx : 1 - fx);
                if (wgt == 0) continue;
                var p = s[col, row];
                acc[0] += wgt * p.Red; acc[1] += wgt * p.Green;
                acc[2] += wgt * p.Blue; acc[3] += wgt * p.Alpha;
            }
        }
        return new SKColor(AdjustMath.ClampB(acc[0]), AdjustMath.ClampB(acc[1]),
            AdjustMath.ClampB(acc[2]), AdjustMath.ClampB(acc[3]));
    }
}

public static class BlurOps
{
    /// <summary>Mac FilterEdit.blurMargin port: room a blur needs around the layer.</summary>
    public static int GaussianMargin(double radius) => (int)Math.Ceiling(radius * 3 + 2);
    public static int MotionMargin(double distance) => (int)Math.Ceiling(distance / 2 + 2);

    /// <summary>Gaussian blur on a transparent-padded grid (Mac growForBlur subset).
    /// Returns the blurred bitmap; margin = padding per side in layer px.</summary>
    public static (SKBitmap bmp, int margin) GaussianExpand(SKBitmap src, double radius)
    {
        if (src == null) return (null, 0);
        if (radius <= 0) return (src.Copy(), 0);
        int m = GaussianMargin(radius);
        var pad = new SKBitmap(src.Width + 2 * m, src.Height + 2 * m, SKColorType.Bgra8888, SKAlphaType.Premul);
        pad.Erase(SKColor.Empty);
        using (var c = new SKCanvas(pad))
            c.DrawBitmap(src, m, m);
        var soft = new SKBitmap(pad.Width, pad.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        soft.Erase(SKColor.Empty);
        using (var c = new SKCanvas(soft))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur((float)radius, (float)radius) })
            c.DrawBitmap(pad, 0, 0, paint);
        pad.Dispose();
        return (soft, m);
    }

    /// <summary>Motion blur: directional multi-tap average along angle (CCW from
    /// horizontal, Mac Photoshop convention; y-down bitmap flips the sign —
    /// documented). Returns padded bitmap + margin per side.</summary>
    public static (SKBitmap bmp, int margin) MotionExpand(SKBitmap src, double angleDeg, double distance)
    {
        if (src == null) return (null, 0);
        if (distance < 1) return (src.Copy(), 0);
        int m = MotionMargin(distance);
        int w = src.Width + 2 * m, h = src.Height + 2 * m;
        var pad = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        pad.Erase(SKColor.Empty);
        using (var c = new SKCanvas(pad))
            c.DrawBitmap(src, m, m);
        int taps = Math.Clamp((int)Math.Ceiling(distance), 1, 64);
        double a = angleDeg * Math.PI / 180;
        double dx = Math.Cos(a), dy = -Math.Sin(a);   // y-down flip
        var dst = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        dst.Erase(SKColor.Empty);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double[] acc = new double[4];
                for (int k = 0; k < taps; k++)
                {
                    double t = taps == 1 ? 0 : (k / (double)(taps - 1) - 0.5) * distance;
                    acc = Add(acc, SampleBilinear(pad, x + dx * t, y + dy * t));
                }
                dst.SetPixel(x, y, new SKColor(
                    AdjustMath.ClampB(acc[0] / taps), AdjustMath.ClampB(acc[1] / taps),
                    AdjustMath.ClampB(acc[2] / taps), AdjustMath.ClampB(acc[3] / taps)));
            }
        pad.Dispose();
        return (dst, m);
    }

    static double[] Add(double[] a, double[] b)
    { a[0] += b[0]; a[1] += b[1]; a[2] += b[2]; a[3] += b[3]; return a; }

    static double[] SampleBilinear(SKBitmap bmp, double sx, double sy)
    {
        int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
        double fx = sx - x0, fy = sy - y0;
        double[] acc = new double[4];
        for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                int x = x0 + i, y = y0 + j;
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                var p = bmp.GetPixel(x, y);
                double w = (i != 0 ? fx : 1 - fx) * (j != 0 ? fy : 1 - fy);
                acc[0] += w * p.Red; acc[1] += w * p.Green;
                acc[2] += w * p.Blue; acc[3] += w * p.Alpha;
            }
        return acc;
    }

    /// <summary>Mac PixelFilter.trimmed subset: crop fully-transparent borders.</summary>
    public static SKBitmap TrimTransparent(SKBitmap src)
    {
        if (src == null) return null;
        int x0 = src.Width, y0 = src.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                if (src.GetPixel(x, y).Alpha != 0)
                { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < x0) return src.Copy();
        var dst = new SKBitmap(x1 - x0 + 1, y1 - y0 + 1, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
                dst.SetPixel(x, y, src.GetPixel(x + x0, y + y0));
        return dst;
    }
}

// ---------- Adjustment layers (LayerAdjustment.swift / AdjustmentEditing.swift subset) ----------
public enum AdjustmentKind { Hsv, Levels, Curves, Exposure, GradientMap, Grain }

public class LayerAdjustment
{
    public AdjustmentKind Kind;
    public HueSaturationSettings HueSat = new();
    public LevelsSettings Levels = new();
    public CurvesSettings Curves = new();
    public ExposureSettings Exposure = new();
    public GradientMapSettings GradientMap = new();
    public GrainSettings Grain = new();

    public bool IsValid => Kind switch
    {
        AdjustmentKind.Hsv => true,
        AdjustmentKind.Levels => Levels.Ranges.Length == 4,
        AdjustmentKind.Curves => Curves.IsValid,
        AdjustmentKind.Exposure => Exposure.IsValid,
        AdjustmentKind.GradientMap => GradientMap.IsValid,
        AdjustmentKind.Grain => Grain.IsValid,
        _ => false,
    };

    /// <summary>Apply to a doc-size composite in place (Mac LayerAdjustment.apply subset;
    /// region/origin place Grain in document space).</summary>
    public void ApplyToComposite(SKBitmap bmp, double originX = 0, double originY = 0, double unitsPerPixel = 1)
    {
        switch (Kind)
        {
            case AdjustmentKind.Hsv: HueOps.ApplyToBitmap(bmp, HueSat); break;
            case AdjustmentKind.Levels: LevelsOps.ApplyToBitmap(bmp, Levels); break;
            case AdjustmentKind.Curves:
                if (!Curves.IsValid) throw new InvalidOperationException("Invalid curves");
                LevelsOps.ApplyTablesToBitmap(bmp, Curves.BuildTables());
                break;
            case AdjustmentKind.Exposure: ExposureOps.ApplyToBitmap(bmp, Exposure); break;
            case AdjustmentKind.GradientMap: GradientMapOps.ApplyToBitmap(bmp, GradientMap); break;
            case AdjustmentKind.Grain:
                GrainOps.ApplyToBitmap(bmp, Grain, originX, originY, unitsPerPixel);
                break;
        }
    }

    public LayerAdjustment Clone() => new()
    {
        Kind = Kind,
        HueSat = HueSat.Clone(),
        Levels = Levels.Clone(),
        Curves = Curves.Clone(),
        Exposure = Exposure.Clone(),
        GradientMap = GradientMap.Clone(),
        Grain = Grain.Clone(),
    };
}

/// <summary>Destructive single-layer applications (Mac commit paths: LevelsFilter.run,
/// CurvesSettings.apply, Exposure/GradientMap/Grain apply, PixelFilter noise/lens/blur).
/// Caller pushes Undo first (stroke/slider-gesture unit, like the brush path).</summary>
public static class LayerFilters
{
    static void Ensure(Layer layer)
    {
        if (layer?.Bitmap == null) throw new InvalidOperationException("No pixels");
        Document.EnsureUniqueBitmap(layer);
    }

    public static void ApplyCurves(Layer layer, CurvesSettings s)
    {
        if (!s.IsValid) throw new InvalidOperationException("Invalid curves");
        Ensure(layer);
        LevelsOps.ApplyTablesToBitmap(layer.Bitmap, s.BuildTables());
    }

    public static void ApplyLevels(Layer layer, LevelsSettings s) { Ensure(layer); LevelsOps.ApplyToBitmap(layer.Bitmap, s); }
    public static void ApplyExposure(Layer layer, ExposureSettings s) { Ensure(layer); ExposureOps.ApplyToBitmap(layer.Bitmap, s); }
    public static void ApplyGradientMap(Layer layer, GradientMapSettings s) { Ensure(layer); GradientMapOps.ApplyToBitmap(layer.Bitmap, s); }
    public static void ApplyGrain(Layer layer, GrainSettings s, double ox = 0, double oy = 0, double upp = 1, uint? seed = null)
    { Ensure(layer); GrainOps.ApplyToBitmap(layer.Bitmap, s, ox, oy, upp, seed); }
    public static void ApplyHueSaturation(Layer layer, HueSaturationSettings s) { Ensure(layer); HueOps.ApplyToBitmap(layer.Bitmap, s); }
    public static void ApplyNoise(Layer layer, NoiseSettings s) { Ensure(layer); NoiseOps.ApplyToBitmap(layer.Bitmap, s); }
    public static void ApplyLens(Layer layer, double distortion) { Ensure(layer); LensOps.ApplyToBitmap(layer.Bitmap, distortion); }

    /// <summary>Blur with edge expansion (Mac growForBlur + trimmed commit subset):
    /// the bitmap grows by the margin and Position shifts so content stays in place.</summary>
    public static int ApplyGaussianExpand(Layer layer, double radius)
    {
        Ensure(layer);
        var (bmp, m) = BlurOps.GaussianExpand(layer.Bitmap, radius);
        layer.Bitmap.Dispose();
        layer.Bitmap = bmp;
        layer.Position = new SKPoint(layer.Position.X - m * layer.ScaleX, layer.Position.Y - m * layer.ScaleY);
        return m;
    }

    public static int ApplyMotionExpand(Layer layer, double angleDeg, double distance)
    {
        Ensure(layer);
        var (bmp, m) = BlurOps.MotionExpand(layer.Bitmap, angleDeg, distance);
        layer.Bitmap.Dispose();
        layer.Bitmap = bmp;
        layer.Position = new SKPoint(layer.Position.X - m * layer.ScaleX, layer.Position.Y - m * layer.ScaleY);
        return m;
    }

    /// <summary>Factory for adjustment layers (Mac addAdjustment subset: blank pixels,
    /// metadata only; rendering happens in ComposeWithAdjustments).</summary>
    public static Layer NewAdjustmentLayer(AdjustmentKind kind, string name, int w, int h)
    {
        var bmp = new SKBitmap(Math.Max(1, w), Math.Max(1, h), SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        var adj = new LayerAdjustment { Kind = kind };
        if (kind == AdjustmentKind.Grain)
            adj.Grain.Seed = (uint)Random.Shared.Next(int.MinValue, int.MaxValue);
        return new Layer { Name = name, Bitmap = bmp, IsAdjustmentLayer = true, Adjustment = adj };
    }
}
