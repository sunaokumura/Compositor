// Compositor — color selection (Mac ColorPalette.swift + ColorPickerSheet subset).
// Foreground/background + >6 editable palette + HSB picker with RGB/hex + mask paint mode.
using SkiaSharp;

namespace Compositor;

/// <summary>sRGB color 0..1 (Mac PaletteColor subset).</summary>
public struct PaletteColor : IEquatable<PaletteColor>
{
    public double R, G, B;
    public PaletteColor(double r, double g, double b) { R = r; G = g; B = b; }
    public static readonly PaletteColor Black = new(0, 0, 0);
    public static readonly PaletteColor White = new(1, 1, 1);
    public bool Equals(PaletteColor o) => R == o.R && G == o.G && B == o.B;
    public override bool Equals(object o) => o is PaletteColor p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public static bool operator ==(PaletteColor a, PaletteColor b) => a.Equals(b);
    public static bool operator !=(PaletteColor a, PaletteColor b) => !a.Equals(b);
    public SKColor ToSK() => new((byte)Math.Round(R * 255), (byte)Math.Round(G * 255), (byte)Math.Round(B * 255));
    public static PaletteColor FromSK(SKColor c) => new(c.Red / 255.0, c.Green / 255.0, c.Blue / 255.0);
    /// <summary>Snap to the 8-bit values painting and export actually store.</summary>
    public PaletteColor Quantized => new(Math.Round(R * 255) / 255, Math.Round(G * 255) / 255, Math.Round(B * 255) / 255);
    public string Hex => $"{(int)Math.Round(R * 255):X2}{(int)Math.Round(G * 255):X2}{(int)Math.Round(B * 255):X2}";
    /// <summary>Accepts RRGGBB or shorthand RGB, with or without '#'.</summary>
    public static PaletteColor? FromHex(string hex)
    {
        if (hex == null) return null;
        var text = hex.Trim();
        if (text.StartsWith("#")) text = text[1..];
        if (text.Length == 3) text = string.Concat(text.SelectMany(c => new[] { c, c }));
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v))
            return null;
        return new PaletteColor(((v >> 16) & 0xFF) / 255.0, ((v >> 8) & 0xFF) / 255.0, (v & 0xFF) / 255.0);
    }
}

/// <summary>Hue degrees + saturation/brightness 0..1. Hue survives grays, saturation survives
/// black — matching Photoshop's field (Mac PickerHSB subset).</summary>
public struct PickerHSB
{
    public double Hue, Saturation, Brightness;
    public PickerHSB(double h, double s, double b) { Hue = h; Saturation = s; Brightness = b; }
    public PickerHSB(PaletteColor c) : this(0, 0, 0) => SetRGB(c);
    public PaletteColor RGB
    {
        get
        {
            double h = ((Hue % 360 + 360) % 360) / 60;
            double c = Brightness * Saturation;
            double x = c * (1 - Math.Abs(h % 2 - 1));
            double m = Brightness - c;
            (double r, double g, double b) = (int)h switch
            {
                0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x),
                3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x),
            };
            return new PaletteColor(r + m, g + m, b + m);
        }
    }
    public void SetRGB(PaletteColor color)
    {
        double high = Math.Max(color.R, Math.Max(color.G, color.B));
        double low = Math.Min(color.R, Math.Min(color.G, color.B));
        double delta = high - low;
        Brightness = high;
        if (high > 0) Saturation = delta / high;
        if (delta <= 0) return;
        double h = high == color.R ? (color.G - color.B) / delta
            : high == color.G ? (color.B - color.R) / delta + 2 : (color.R - color.G) / delta + 4;
        h *= 60;
        Hue = h < 0 ? h + 360 : h;
    }
}

/// <summary>Foreground/background + editable palette + mask-paint mode (Mac EditorSession palette subset).</summary>
public class PaletteState
{
    public PaletteColor Foreground = PaletteColor.Black;
    public PaletteColor Background = PaletteColor.White;
    /// <summary>True while a mask is the paint target: palette edits flip paint polarity instead.</summary>
    public bool MaskSelected;
    public bool MaskPaintWhite = true;   // false = paint black (hide), true = paint white (reveal)
    public List<PaletteColor> Swatches = new()
    {
        PaletteColor.Black, PaletteColor.White,
        new PaletteColor(1, 0, 0), new PaletteColor(0, 1, 0), new PaletteColor(0, 0, 1),
        new PaletteColor(1, 1, 0), new PaletteColor(1, 0.5, 0), new PaletteColor(0.5, 0, 0.5),
        new PaletteColor(0, 0.5, 0.5), new PaletteColor(0.5, 0.5, 0.5), new PaletteColor(1, 0.75, 0.8),
        new PaletteColor(0.4, 0.25, 0.15),
    };
    public const int MaxSwatches = 48;

    public PaletteColor Current(bool background) => background ? Background : Foreground;

    /// <summary>Brush color honoring mask mode (Mac paletteColor subset).</summary>
    public PaletteColor PaintColor(bool background)
    {
        if (MaskSelected) return (background ? !MaskPaintWhite : MaskPaintWhite) ? PaletteColor.White : PaletteColor.Black;
        return background ? Background : Foreground;
    }

    public void Set(PaletteColor c, bool background)
    {
        if (MaskSelected)
        {
            bool white = c == PaletteColor.White;
            MaskPaintWhite = background ? !white : white;
        }
        else if (background) Background = c;
        else Foreground = c;
    }

    public void Swap()
    {
        if (MaskSelected) MaskPaintWhite = !MaskPaintWhite;
        else (Foreground, Background) = (Background, Foreground);
    }

    public void Reset()
    {
        if (MaskSelected) MaskPaintWhite = true;
        else { Foreground = PaletteColor.Black; Background = PaletteColor.White; }
    }

    public bool AddSwatch(PaletteColor c)
    {
        c = c.Quantized;
        if (Swatches.Contains(c) || Swatches.Count >= MaxSwatches) return false;
        Swatches.Add(c);
        return true;
    }

    public bool RemoveSwatchAt(int i)
    {
        if (i < 0 || i >= Swatches.Count) return false;
        Swatches.RemoveAt(i);
        return true;
    }

    /// <summary>Composited sRGB color of the visible layers at one document pixel. Null outside
    /// the canvas or over fully transparent pixels (Mac sampleCompositeColor subset).</summary>
    public static PaletteColor? SampleComposite(Document doc, float x, float y)
    {
        if (doc == null || x < 0 || y < 0 || x >= doc.Width || y >= doc.Height) return null;
        using var flat = doc.Compose();
        int ix = Math.Clamp((int)MathF.Floor(x), 0, flat.Width - 1);
        int iy = Math.Clamp((int)MathF.Floor(y), 0, flat.Height - 1);
        var px = flat.GetPixel(ix, iy);
        if (px.Alpha == 0) return null;
        double a = px.Alpha;
        double ch(byte v) => Math.Round(Math.Min(a, (double)v) / a * 255) / 255;
        return new PaletteColor(ch(px.Red), ch(px.Green), ch(px.Blue));
    }
}
