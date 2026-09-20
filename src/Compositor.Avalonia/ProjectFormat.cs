// Compositor — project save/open (.comp package, Mac ProjectStore.swift subset).
// Package layout: <name>.comp/ manifest.json + images/<layer UUID>.png + images/<layer UUID>.mask.png
// Manifest versions 1-7 readable, new saves always version 7 (adjustment layers).
// PNG assets only (Mac parity); atomic replace via sibling temp dir + move.
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Compositor;

public class ProjectFormatException : Exception
{
    public ProjectFormatException(string message) : base(message) { }
}

#region Manifest DTOs (JSON, camelCase via property names matching Mac keys)

public class ManifestTransform
{
    public ManifestPoint origin { get; set; } = new();
    public ManifestSize size { get; set; } = new();
    public double rotation { get; set; }
    public bool flipX { get; set; }
    public bool flipY { get; set; }
    public string sampling { get; set; } = "High quality";
}
public class ManifestPoint { public double x { get; set; } public double y { get; set; } }
public class ManifestSize { public double width { get; set; } public double height { get; set; } }

public class ManifestShape
{
    public string kind { get; set; } = "Rectangle";
    public double r { get; set; } public double g { get; set; } public double b { get; set; }
    public double cornerRadius { get; set; }
}

public class ManifestLayer
{
    public Guid id { get; set; }
    public string name { get; set; } = "";
    public bool isVisible { get; set; } = true;
    public ManifestTransform transform { get; set; } = new();
    public string imageFile { get; set; }
    public Guid? parentID { get; set; }
    public bool? isGroup { get; set; }
    public double? opacity { get; set; }
    public string blendMode { get; set; }
    public string maskFile { get; set; }
    public bool? maskEnabled { get; set; }
    public Guid? maskSourceID { get; set; }
    public JsonElement? adjustment { get; set; }
    public string adjustmentKind { get; set; }
    public ManifestTransform maskPlacement { get; set; }
    public bool? maskLinked { get; set; }
    public ManifestShape shape { get; set; }
}

public class ProjectManifest
{
    public string format { get; set; } = "com.compositor.project";
    public int version { get; set; } = 7;
    public string colorSpace { get; set; } = "sRGB";
    public double? resolution { get; set; }
    public Guid documentID { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public Guid? activeLayerID { get; set; }
    public List<ManifestLayer> layers { get; set; } = new();
}
#endregion

/// <summary>Project save/open engine (Mac ProjectStore.swift + EditorSession+Projects.swift subset).
/// Layers/groups/masks/clips/adjustments/shapes round-trip; undo history and viewport stay session-only.</summary>
public static class ProjectFormat
{
    public const string FormatId = "com.compositor.project";
    public const int CurrentVersion = 7;
    public const long MaxManifestBytes = 4L * 1024 * 1024;
    public const long MaxAssetBytes = 512L * 1024 * 1024;
    public const int MaxSide = 30_000;
    public const int MaxLayers = 10_000;
    public const long MaxImagePixels = 100_000_000;
    public const int MaxDepth = 64;
    public const int MaxClipChain = 256;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    static readonly JsonSerializerOptions AdjOpts = new()
    {
        IncludeFields = true,
        // Computed read-only properties (LevelRange.Normalized, IsIdentity, ...) recurse or
        // duplicate state: persist fields only. Round-trip stays exact for real settings.
        IgnoreReadOnlyProperties = true,
    };

    // ---------- blend / sampling names (Mac LayerBlendMode subset + Windows extras) ----------

    static readonly Dictionary<SKBlendMode, string> BlendToName = new()
    {
        [SKBlendMode.SrcOver] = "Normal", [SKBlendMode.Multiply] = "Multiply",
        [SKBlendMode.Screen] = "Screen", [SKBlendMode.Overlay] = "Overlay",
        [SKBlendMode.Darken] = "Darken", [SKBlendMode.Lighten] = "Lighten",
        [SKBlendMode.Difference] = "Difference", [SKBlendMode.ColorDodge] = "Color Dodge",
        [SKBlendMode.ColorBurn] = "Color Burn", [SKBlendMode.HardLight] = "Hard Light",
        [SKBlendMode.SoftLight] = "Soft Light", [SKBlendMode.Exclusion] = "Exclusion",
        [SKBlendMode.Hue] = "Hue", [SKBlendMode.Saturation] = "Saturation",
        [SKBlendMode.Color] = "Color", [SKBlendMode.Luminosity] = "Luminosity",
    };
    static readonly Dictionary<string, SKBlendMode> NameToBlend =
        BlendToName.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static string BlendName(SKBlendMode m) => BlendToName.TryGetValue(m, out var n) ? n : "Normal";
    public static SKBlendMode ParseBlend(string n) =>
        n != null && NameToBlend.TryGetValue(n, out var m) ? m : throw new ProjectFormatException($"Unknown blend mode '{n}'.");

    public static string SamplingName(LayerSampling s) => s switch
    {
        LayerSampling.Nearest => "Nearest", LayerSampling.Smooth => "Smooth", _ => "High quality",
    };
    public static LayerSampling ParseSampling(string n) => n switch
    {
        "Nearest" => LayerSampling.Nearest, "Smooth" => LayerSampling.Smooth,
        "High quality" => LayerSampling.High, null => LayerSampling.High,
        _ => throw new ProjectFormatException($"Unknown sampling '{n}'."),
    };

    // ---------- build manifest from a live document ----------

    public static ProjectManifest BuildManifest(Document doc)
    {
        if (doc == null) throw new ProjectFormatException("No document.");
        if (doc.Width < 1 || doc.Width > MaxSide || doc.Height < 1 || doc.Height > MaxSide)
            throw new ProjectFormatException($"Canvas {doc.Width}x{doc.Height} exceeds 1..30000.");
        if (doc.Layers.Count > MaxLayers) throw new ProjectFormatException("Too many layers (max 10000).");
        if (doc.Resolution is < 1 or > 9600 || !double.IsFinite(doc.Resolution))
            throw new ProjectFormatException("Resolution must be 1..9600.");

        var m = new ProjectManifest
        {
            version = CurrentVersion,
            resolution = Math.Abs(doc.Resolution - 72) < 1e-9 ? null : doc.Resolution,
            documentID = doc.DocumentId == Guid.Empty ? Guid.NewGuid() : doc.DocumentId,
            width = doc.Width, height = doc.Height,
            activeLayerID = doc.ActiveLayerId,
        };
        foreach (var l in doc.Layers)
        {
            if (string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 16384)
                throw new ProjectFormatException($"Bad layer name '{l.Name}'.");
            double w, h;
            if (l.IsGroup) { w = l.FolderW > 0 ? l.FolderW : doc.Width; h = l.FolderH > 0 ? l.FolderH : doc.Height; }
            else if (l.IsAdjustmentLayer) { w = doc.Width; h = doc.Height; }
            else if (l.Bitmap != null) { w = l.Bitmap.Width * l.ScaleX; h = l.Bitmap.Height * l.ScaleY; }
            else { w = doc.Width; h = doc.Height; }   // blank layer: full-canvas box (Mac blankSize)
            var rec = new ManifestLayer
            {
                id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id,
                name = l.Name, isVisible = l.Visible,
                transform = new ManifestTransform
                {
                    origin = new ManifestPoint { x = l.Position.X, y = l.Position.Y },
                    size = new ManifestSize { width = w, height = h },
                    rotation = l.Rotation, flipX = l.FlipH, flipY = l.FlipV,
                    sampling = SamplingName(l.Sampling),
                },
                imageFile = (!l.IsGroup && !l.IsAdjustmentLayer && l.Bitmap != null) ? $"{l.Id}.png" : null,
                parentID = l.ParentId,
                isGroup = l.IsGroup ? true : null,
                opacity = Math.Abs(l.Opacity - 1) < 1e-6 ? null : (double?)Math.Round(l.Opacity, 6),
                blendMode = l.Blend == SKBlendMode.SrcOver ? null : BlendName(l.Blend),
                maskFile = MaskOps.HasMask(l) ? $"{l.Id}.mask.png" : null,
                maskEnabled = MaskOps.HasMask(l) ? (l.MaskEnabled ? null : (bool?)false) : null,
                maskSourceID = l.ClipSourceId,
            };
            if (MaskOps.HasMask(l))
            {
                var px = l.Position.X + (l.MaskLinked ? 0 : l.MaskOffset.X);
                var py = l.Position.Y + (l.MaskLinked ? 0 : l.MaskOffset.Y);
                rec.maskPlacement = new ManifestTransform
                {
                    origin = new ManifestPoint { x = px, y = py },
                    size = new ManifestSize { width = l.MaskW, height = l.MaskH },
                    sampling = SamplingName(LayerSampling.Nearest),
                };
                if (!l.MaskLinked) rec.maskLinked = false;
            }
            if (l.IsAdjustmentLayer)
            {
                if (l.Adjustment == null || !l.Adjustment.IsValid)
                    throw new ProjectFormatException($"Invalid adjustment on '{l.Name}'.");
                rec.adjustmentKind = l.Adjustment.Kind.ToString();
                rec.adjustment = JsonSerializer.SerializeToElement(l.Adjustment, AdjOpts);
            }
            if (l.Shape != null)
                rec.shape = new ManifestShape
                {
                    kind = l.Shape.Kind.ToString(), r = l.Shape.R, g = l.Shape.G, b = l.Shape.B,
                    cornerRadius = l.Shape.CornerRadius,
                };
            m.layers.Add(rec);
        }
        Validate(m);
        return m;
    }

    // ---------- validation (Mac ProjectStore.validate + LayerHierarchy/LiveMaskGraph subset) ----------

    public static void Validate(ProjectManifest m)
    {
        if (m.format != FormatId) throw new ProjectFormatException("Not a Compositor project.");
        if (m.version is < 1 or > 7) throw new ProjectFormatException($"Unsupported version {m.version} (supports 1-7).");
        if (m.colorSpace != "sRGB") throw new ProjectFormatException("colorSpace must be sRGB.");
        if (m.resolution is double r && (!double.IsFinite(r) || r < 1 || r > 9600))
            throw new ProjectFormatException("Bad resolution.");
        if (m.width is < 1 or > MaxSide || m.height is < 1 or > MaxSide)
            throw new ProjectFormatException("Bad canvas size.");
        if (m.layers.Count > MaxLayers) throw new ProjectFormatException("Too many layers.");

        var ids = new HashSet<Guid>();
        foreach (var l in m.layers)
        {
            if (!ids.Add(l.id)) throw new ProjectFormatException("Duplicate layer id.");
            if (string.IsNullOrWhiteSpace(l.name) || l.name.Length > 16384)
                throw new ProjectFormatException("Bad layer name.");
            if (!IsValidTransform(l.transform)) throw new ProjectFormatException($"Bad transform on '{l.name}'.");
            bool isGroup = l.isGroup == true;
            if (l.adjustment != null || l.adjustmentKind != null)
            {
                if (m.version < 7 || isGroup || l.imageFile != null)
                    throw new ProjectFormatException($"Adjustment layer '{l.name}' needs version 7.");
            }
            if (l.maskFile != null)
            {
                int need = isGroup ? 6 : 4;
                if (m.version < need || l.maskFile != $"{l.id}.mask.png")
                    throw new ProjectFormatException($"Bad mask file on '{l.name}'.");
                if (l.maskPlacement == null || !IsValidTransform(l.maskPlacement))
                    throw new ProjectFormatException($"Bad mask placement on '{l.name}'.");
            }
            if (l.maskEnabled != null && l.maskFile == null)
                throw new ProjectFormatException($"maskEnabled without mask on '{l.name}'.");
            double op = l.opacity ?? 1;
            string blend = l.blendMode ?? "Normal";
            if (!double.IsFinite(op) || op < 0 || op > 1) throw new ProjectFormatException($"Bad opacity on '{l.name}'.");
            if (!NameToBlend.ContainsKey(blend)) throw new ProjectFormatException($"Bad blend on '{l.name}'.");
            if (m.version < 3 && (op != 1 || blend != "Normal"))
                throw new ProjectFormatException($"Version {m.version} cannot carry appearance on '{l.name}'.");
            if (isGroup && (op != 1 || blend != "Normal"))
                throw new ProjectFormatException($"Group '{l.name}' must use default appearance.");
            if (l.imageFile != null && l.imageFile != $"{l.id}.png")
                throw new ProjectFormatException($"Bad image file on '{l.name}'.");
            if (isGroup && l.imageFile != null)
                throw new ProjectFormatException($"Group '{l.name}' cannot carry pixels.");
        }
        ValidateHierarchy(m);
        ValidateClips(m);
        if (m.version < 5 && m.layers.Any(l => l.maskSourceID != null))
            throw new ProjectFormatException("maskSourceID needs version 5+.");
        if (m.version == 1 && m.layers.Any(l => l.parentID != null || l.isGroup == true))
            throw new ProjectFormatException("Version 1 cannot carry groups.");
        if (m.activeLayerID is Guid a && !ids.Contains(a))
            throw new ProjectFormatException("Bad activeLayerID.");
    }

    static bool Finite(double v) => double.IsFinite(v);
    static bool IsValidTransform(ManifestTransform t)
    {
        if (t == null) return false;
        double[] vs = { t.origin.x, t.origin.y, t.size.width, t.size.height, t.rotation };
        if (!vs.All(Finite)) return false;
        if (t.size.width is < 1 or > 300_000 || t.size.height is < 1 or > 300_000) return false;
        if (Math.Abs(t.origin.x) > 1_000_000 || Math.Abs(t.origin.y) > 1_000_000) return false;
        return t.sampling is "Nearest" or "Smooth" or "High quality";
    }

    public static void ValidateHierarchy(ProjectManifest m)
    {
        var byId = m.layers.ToDictionary(l => l.id);
        foreach (var l in m.layers)
        {
            if (l.parentID is Guid p)
            {
                if (!byId.TryGetValue(p, out var parent) || parent.isGroup != true)
                    throw new ProjectFormatException($"Bad parent on '{l.name}'.");
            }
        }
        foreach (var l in m.layers)
        {
            var seen = new HashSet<Guid> { l.id };
            var cur = l.parentID;
            int depth = 0;
            while (cur is Guid c)
            {
                if (!seen.Add(c)) throw new ProjectFormatException("Group cycle.");
                if (!byId.TryGetValue(c, out var n)) break;
                cur = n.parentID;
                if (++depth > MaxDepth) throw new ProjectFormatException("Group nesting exceeds 64.");
            }
        }
    }

    public static void ValidateClips(ProjectManifest m)
    {
        var byId = m.layers.ToDictionary(l => l.id);
        foreach (var l in m.layers)
        {
            if (l.maskSourceID is not Guid s) continue;
            if (s == l.id) throw new ProjectFormatException("Clip self-link.");
            if (!byId.TryGetValue(s, out var src))
                throw new ProjectFormatException($"Missing clip source for '{l.name}'.");
            if (l.isGroup == true || src.isGroup == true)
                throw new ProjectFormatException("Groups cannot clip.");
            if (l.adjustment != null || l.adjustmentKind != null)
                throw new ProjectFormatException("Adjustment layers cannot clip.");
        }
        foreach (var l in m.layers)
        {
            var seen = new HashSet<Guid> { l.id };
            var cur = l.maskSourceID;
            int n = 0;
            while (cur is Guid c)
            {
                if (!seen.Add(c)) throw new ProjectFormatException("Clip cycle.");
                if (!byId.TryGetValue(c, out var node)) break;
                cur = node.maskSourceID;
                if (++n > MaxClipChain) throw new ProjectFormatException("Clip chain exceeds 256.");
            }
        }
    }

    // ---------- save / load ----------

    static byte[] EncodePng(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static byte[] EncodeMaskPng(byte[] gray, int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        var ptr = bmp.GetPixels();
        System.Runtime.InteropServices.Marshal.Copy(gray, 0, ptr, gray.Length);
        bmp.NotifyPixelsChanged();
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        bmp.Dispose();
        return bytes;
    }

    static bool IsPng(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    static string SafeName(string dir, string file)
    {
        var full = Path.GetFullPath(Path.Combine(dir, file));
        var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ProjectFormatException("Unsafe path.");
        return full;
    }

    /// <summary>Atomic save: stage to a sibling temp dir, then replace the destination.</summary>
    public static void Save(Document doc, string packagePath)
    {
        var manifest = BuildManifest(doc);
        string json = JsonSerializer.Serialize(manifest, JsonOpts);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
            throw new ProjectFormatException("Manifest exceeds 4 MiB.");

        long pixels = 0, maskPixels = 0;
        var assets = new Dictionary<string, byte[]>();
        foreach (var l in doc.Layers)
        {
            if (!l.IsGroup && !l.IsAdjustmentLayer && l.Bitmap != null)
            {
                long n = (long)l.Bitmap.Width * l.Bitmap.Height;
                if (l.Bitmap.Width is < 1 or > MaxSide || l.Bitmap.Height is < 1 or > MaxSide || n > MaxImagePixels - pixels)
                    throw new ProjectFormatException($"Image too large on '{l.Name}'.");
                pixels += n;
                assets[$"{l.Id}.png"] = EncodePng(l.Bitmap);
            }
            if (MaskOps.HasMask(l))
            {
                long n = (long)l.MaskW * l.MaskH;
                if (l.MaskW is < 1 or > MaxSide || l.MaskH is < 1 or > MaxSide || n > MaxImagePixels - maskPixels)
                    throw new ProjectFormatException($"Mask too large on '{l.Name}'.");
                maskPixels += n;
                assets[$"{l.Id}.mask.png"] = EncodeMaskPng(l.Mask, l.MaskW, l.MaskH);
            }
        }

        string parent = Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? ".";
        string stage = Path.Combine(parent, ".comp-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, "images"));
        try
        {
            File.WriteAllText(Path.Combine(stage, "manifest.json"), json, System.Text.Encoding.UTF8);
            foreach (var kv in assets)
                File.WriteAllBytes(Path.Combine(stage, "images", kv.Key), kv.Value);
            if (Directory.Exists(packagePath))
            {
                string backup = packagePath + ".bak-" + Guid.NewGuid().ToString("N");
                Directory.Move(packagePath, backup);
                try { Directory.Move(stage, packagePath); Directory.Delete(backup, true); }
                catch { Directory.Move(backup, packagePath); throw; }
            }
            else
            {
                if (File.Exists(packagePath)) throw new ProjectFormatException("Destination is a file.");
                Directory.Move(stage, packagePath);
            }
        }
        finally { if (Directory.Exists(stage)) try { Directory.Delete(stage, true); } catch { } }

        doc.ProjectPath = packagePath;
        doc.MarkSaved();
    }

    /// <summary>Load and validate first: a corrupt project never touches the live document.</summary>
    public static Document Load(string packagePath)
    {
        if (!Directory.Exists(packagePath))
            throw new ProjectFormatException("Project package not found.");
        string manifestPath = SafeName(packagePath, "manifest.json");
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length > MaxManifestBytes)
            throw new ProjectFormatException("Bad manifest.");
        string json = File.ReadAllText(manifestPath, System.Text.Encoding.UTF8);
        ProjectManifest m;
        try { m = JsonSerializer.Deserialize<ProjectManifest>(json) ?? throw new Exception(); }
        catch { throw new ProjectFormatException("Damaged metadata."); }
        Validate(m);

        string imgDir = Path.Combine(packagePath, "images");
        long pixels = 0, maskPixels = 0;
        var doc = new Document
        {
            Width = m.width, Height = m.height, Name = Path.GetFileNameWithoutExtension(packagePath),
            DocumentId = m.documentID, Resolution = m.resolution ?? 72,
            ActiveLayerId = m.activeLayerID, ProjectPath = packagePath,
        };
        var byId = new Dictionary<Guid, Layer>();
        foreach (var rec in m.layers)
        {
            var l = new Layer
            {
                Id = rec.id, Name = rec.name, Visible = rec.isVisible,
                ParentId = rec.parentID, IsGroup = rec.isGroup == true,
                Opacity = (float)(rec.opacity ?? 1), Blend = ParseBlend(rec.blendMode ?? "Normal"),
                Position = new SKPoint((float)rec.transform.origin.x, (float)rec.transform.origin.y),
                Rotation = (float)rec.transform.rotation, FlipH = rec.transform.flipX, FlipV = rec.transform.flipY,
                Sampling = ParseSampling(rec.transform.sampling),
                MaskEnabled = rec.maskEnabled ?? true,
                ClipSourceId = rec.maskSourceID,
            };
            bool isGroup = l.IsGroup;
            if (isGroup) { l.FolderW = (float)rec.transform.size.width; l.FolderH = (float)rec.transform.size.height; }
            if (rec.adjustment != null || rec.adjustmentKind != null)
            {
                try
                {
                    l.IsAdjustmentLayer = true;
                    l.Adjustment = JsonSerializer.Deserialize<LayerAdjustment>(rec.adjustment.Value.GetRawText(), AdjOpts);
                    if (l.Adjustment == null || !l.Adjustment.IsValid) throw new Exception();
                }
                catch { throw new ProjectFormatException($"Bad adjustment on '{rec.name}'."); }
            }
            if (rec.imageFile != null)
            {
                if (rec.imageFile != $"{rec.id}.png") throw new ProjectFormatException("Bad image file.");
                string p = SafeName(imgDir, rec.imageFile);
                var fi = new FileInfo(p);
                if (!fi.Exists || fi.Length > MaxAssetBytes) throw new ProjectFormatException($"Missing image for '{rec.name}'.");
                byte[] bytes = File.ReadAllBytes(p);
                if (!IsPng(bytes)) throw new ProjectFormatException($"Missing image for '{rec.name}'.");
                var bmp = SKBitmap.Decode(bytes) ?? throw new ProjectFormatException($"Missing image for '{rec.name}'.");
                if (bmp.Width is < 1 or > MaxSide || bmp.Height is < 1 or > MaxSide
                    || (long)bmp.Width * bmp.Height > MaxImagePixels - pixels)
                { bmp.Dispose(); throw new ProjectFormatException("Image too large."); }
                pixels += (long)bmp.Width * bmp.Height;
                l.Bitmap = bmp;
                double sx = rec.transform.size.width / Math.Max(1, bmp.Width);
                double sy = rec.transform.size.height / Math.Max(1, bmp.Height);
                l.ScaleX = (float)sx; l.ScaleY = (float)sy;
            }
            if (rec.maskFile != null)
            {
                if (rec.maskFile != $"{rec.id}.mask.png") throw new ProjectFormatException("Bad mask file.");
                string p = SafeName(imgDir, rec.maskFile);
                var fi = new FileInfo(p);
                if (!fi.Exists || fi.Length > MaxAssetBytes) throw new ProjectFormatException($"Missing mask for '{rec.name}'.");
                byte[] bytes = File.ReadAllBytes(p);
                if (!IsPng(bytes)) throw new ProjectFormatException($"Missing mask for '{rec.name}'.");
                using var bmp = SKBitmap.Decode(bytes) ?? throw new ProjectFormatException($"Missing mask for '{rec.name}'.");
                if (bmp.Width is < 1 or > MaxSide || bmp.Height is < 1 or > MaxSide
                    || (long)bmp.Width * bmp.Height > MaxImagePixels - maskPixels)
                    throw new ProjectFormatException("Mask too large.");
                maskPixels += (long)bmp.Width * bmp.Height;
                var gray = new byte[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        gray[y * bmp.Width + x] = bmp.GetPixel(x, y).Red;
                l.Mask = gray; l.MaskW = bmp.Width; l.MaskH = bmp.Height;
                bool linked = rec.maskLinked ?? true;
                l.MaskLinked = linked;
                l.MaskOffset = linked ? new SKPoint(0, 0) : new SKPoint(
                    (float)(rec.maskPlacement.origin.x - rec.transform.origin.x),
                    (float)(rec.maskPlacement.origin.y - rec.transform.origin.y));
            }
            if (rec.shape != null)
            {
                if (!Enum.TryParse<ShapeKind>(rec.shape.kind, out var k))
                    throw new ProjectFormatException($"Bad shape on '{rec.name}'.");
                l.Shape = new ShapeInfo { Kind = k, R = (float)rec.shape.r, G = (float)rec.shape.g, B = (float)rec.shape.b, CornerRadius = (float)rec.shape.cornerRadius };
            }
            doc.Layers.Add(l);
            byId[l.Id] = l;
        }
        doc.MarkSaved();
        return doc;
    }
}
