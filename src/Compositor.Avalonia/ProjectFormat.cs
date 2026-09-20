// Compositor — project save/open (.comp package, Mac ProjectStore.swift subset).
// Package layout: <name>.comp/ manifest.json + images/<layer UUID>.png + images/<layer UUID>.mask.png
// Manifest versions 1-9 readable, new saves always version 9 (P2: history + slices +
// macro + blendRange + icc/hdrEv。video frames・3D・mesh係数・対称・VPは session-only)。
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

public class ManifestVectorPoint { public double x { get; set; } public double y { get; set; } }

public class ManifestVector
{
    public List<ManifestVectorPoint> points { get; set; } = new();
    public double width { get; set; }
    public bool closed { get; set; }
}

public class ManifestChannel
{
    public Guid id { get; set; }
    public string name { get; set; } = "";
    public string channelFile { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class ManifestHistory
{
    public string time { get; set; } = "";
    public string action { get; set; } = "";
    public int layers { get; set; }
}

public class ManifestSlice
{
    public string name { get; set; } = "";
    public int x { get; set; }
    public int y { get; set; }
    public int w { get; set; }
    public int h { get; set; }
}

public class ManifestMacro
{
    public string op { get; set; } = "";
    public double value { get; set; }
}

public class ManifestBlendRange
{
    public double lo { get; set; }
    public double hi { get; set; } = 1;
    public double feather { get; set; }
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
    public ManifestVector vector { get; set; }   // P1 v8: ベクター線の編集データ
    public bool? isVector { get; set; }
    public ManifestBlendRange blendRange { get; set; }   // P2 v9: blend range
}

public class ProjectManifest
{
    public string format { get; set; } = "com.compositor.project";
    public int version { get; set; } = 9;
    public string colorSpace { get; set; } = "sRGB";
    public double? resolution { get; set; }
    public Guid documentID { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public Guid? activeLayerID { get; set; }
    public List<ManifestLayer> layers { get; set; } = new();
    public List<ManifestChannel> channels { get; set; }   // P1 v8: spare channel
    public List<ManifestHistory> history { get; set; }    // P2 v9: History保存
    public List<ManifestSlice> slices { get; set; }       // P2 v9: Export slice
    public List<ManifestMacro> macro { get; set; }        // P2 v9: macro
    public string icc { get; set; }                       // P2 v9: ICC profile名
    public double? hdrEv { get; set; }                    // P2 v9: HDR露出
}
#endregion

/// <summary>Project save/open engine (Mac ProjectStore.swift + EditorSession+Projects.swift subset).
/// Layers/groups/masks/clips/adjustments/shapes/vectors/channels/history/slices/macros round-trip; undo history and viewport stay session-only.
/// Manifest versions 1-9 readable, new saves always version 9 (P2: history + slices + macro + blendRange + icc/hdrEv).</summary>
public static class ProjectFormat
{
    public const string FormatId = "com.compositor.project";
    public const int CurrentVersion = 9;
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
            if (l.IsVectorLayer && l.Vector != null)   // P1 v8 ベクター線
            {
                VectorStrokeOps.Validate(l.Vector);
                rec.isVector = true;
                rec.vector = new ManifestVector
                {
                    width = l.Vector.Width, closed = l.Vector.Closed,
                    points = l.Vector.Points.Select(p => new ManifestVectorPoint { x = p.X, y = p.Y }).ToList(),
                };
            }
            if (l.UseBlendRange)   // P2 v9 blend range
            {
                var br = new BlendRange { Lo = l.BlendLo, Hi = l.BlendHi, Feather = l.BlendFeather };
                if (!br.IsValid) throw new ProjectFormatException($"Bad blend range on '{l.Name}'.");
                rec.blendRange = new ManifestBlendRange { lo = l.BlendLo, hi = l.BlendHi, feather = l.BlendFeather };
            }
            m.layers.Add(rec);
        }
        if (doc.SpareChannels.Count > 0)   // P1 v8 spare channel
        {
            if (doc.SpareChannels.Count > SpareChannelOps.MaxChannels)
                throw new ProjectFormatException("Too many channels.");
            m.channels = new List<ManifestChannel>();
            foreach (var ch in doc.SpareChannels)
            {
                if (ch.Mask == null || ch.Mask.Length != ch.W * ch.H || ch.W < 1 || ch.H < 1)
                    throw new ProjectFormatException($"Bad channel '{ch.Name}'.");
                var id = Guid.NewGuid();
                m.channels.Add(new ManifestChannel
                {
                    id = id, name = ch.Name ?? "Channel",
                    channelFile = $"{id}.channel.png", width = ch.W, height = ch.H,
                });
            }
        }
        if (doc.History.Count > 0)   // P2 v9 History保存 (上限200・画素なし)
        {
            if (doc.History.Count > HistoryOps.MaxEntries) throw new ProjectFormatException("Too many history entries.");
            m.history = new List<ManifestHistory>();
            foreach (var e in doc.History)
            {
                if (string.IsNullOrWhiteSpace(e.Action) || e.Action.Length > 1024)
                    throw new ProjectFormatException("Bad history entry.");
                m.history.Add(new ManifestHistory { time = e.Time.ToString("O"), action = e.Action, layers = e.Layers });
            }
        }
        if (doc.Slices.Count > 0)   // P2 v9 Export slice
        {
            if (doc.Slices.Count > SliceOps.MaxSlices) throw new ProjectFormatException("Too many slices.");
            m.slices = new List<ManifestSlice>();
            foreach (var s in doc.Slices)
            {
                SliceOps.Validate(s, doc.Width, doc.Height);
                m.slices.Add(new ManifestSlice { name = s.Name, x = s.X, y = s.Y, w = s.W, h = s.H });
            }
        }
        if (doc.Macro.Count > 0)   // P2 v9 macro
        {
            if (doc.Macro.Count > MacroOps.MaxSteps) throw new ProjectFormatException("Too many macro steps.");
            m.macro = new List<ManifestMacro>();
            foreach (var s in doc.Macro)
            {
                if (!Enum.IsDefined(s.Op)) throw new ProjectFormatException("Bad macro op.");
                m.macro.Add(new ManifestMacro { op = s.Op.ToString(), value = s.Value });
            }
        }
        if (doc.IccProfile != IccProfileKind.SRGB) m.icc = doc.IccProfile.ToString();   // P2 v9
        if (Math.Abs(doc.HdrEv) > 1e-9)
        {
            if (!double.IsFinite(doc.HdrEv) || doc.HdrEv is < -8 or > 8)
                throw new ProjectFormatException("Bad HDR EV.");
            m.hdrEv = doc.HdrEv;
        }
        Validate(m);
        return m;
    }

    // ---------- validation (Mac ProjectStore.validate + LayerHierarchy/LiveMaskGraph subset) ----------

    public static void Validate(ProjectManifest m)
    {
        if (m.format != FormatId) throw new ProjectFormatException("Not a Compositor project.");
        if (m.version is < 1 or > 9) throw new ProjectFormatException($"Unsupported version {m.version} (supports 1-9).");
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
            if (l.isVector == true || l.vector != null)   // P1 v8 ベクター線
            {
                if (m.version < 8)
                    throw new ProjectFormatException($"Vector layer '{l.name}' needs version 8.");
                if (l.vector == null || l.vector.points == null || l.vector.points.Count < 2
                    || l.vector.points.Count > VectorStrokeOps.MaxPoints)
                    throw new ProjectFormatException($"Bad vector on '{l.name}'.");
                if (l.vector.width is < VectorStrokeOps.MinWidth or > VectorStrokeOps.MaxWidth)
                    throw new ProjectFormatException($"Bad vector width on '{l.name}'.");
            }
            if (l.adjustmentKind != null &&   // P1 v8 Live filter kinds
                (l.adjustmentKind == nameof(AdjustmentKind.Noise) || l.adjustmentKind == nameof(AdjustmentKind.Lens)
                || l.adjustmentKind == nameof(AdjustmentKind.GaussBlur) || l.adjustmentKind == nameof(AdjustmentKind.MotionBlur))
                && m.version < 8)
                throw new ProjectFormatException($"Live filter '{l.name}' needs version 8.");
            if (l.blendRange != null)   // P2 v9 blend range
            {
                if (m.version < 9) throw new ProjectFormatException($"Blend range on '{l.name}' needs version 9.");
                var br = new BlendRange { Lo = l.blendRange.lo, Hi = l.blendRange.hi, Feather = l.blendRange.feather };
                if (!br.IsValid) throw new ProjectFormatException($"Bad blend range on '{l.name}'.");
            }
        }
        if (m.channels != null && m.channels.Count > 0)   // P1 v8 spare channel
        {
            if (m.version < 8) throw new ProjectFormatException("Channels need version 8+.");
            if (m.channels.Count > SpareChannelOps.MaxChannels) throw new ProjectFormatException("Too many channels.");
            var cids = new HashSet<Guid>();
            foreach (var c in m.channels)
            {
                if (!cids.Add(c.id)) throw new ProjectFormatException("Duplicate channel id.");
                if (string.IsNullOrWhiteSpace(c.name) || c.name.Length > 1024)
                    throw new ProjectFormatException("Bad channel name.");
                if (c.channelFile != $"{c.id}.channel.png") throw new ProjectFormatException($"Bad channel file on '{c.name}'.");
                if (c.width is < 1 or > MaxSide || c.height is < 1 or > MaxSide
                    || (long)c.width * c.height > MaxImagePixels)
                    throw new ProjectFormatException($"Bad channel size on '{c.name}'.");
            }
        }
        ValidateHierarchy(m);
        ValidateClips(m);
        if (m.version < 5 && m.layers.Any(l => l.maskSourceID != null))
            throw new ProjectFormatException("maskSourceID needs version 5+.");
        if ((m.history != null && m.history.Count > 0) || (m.slices != null && m.slices.Count > 0)
            || (m.macro != null && m.macro.Count > 0) || m.icc != null || m.hdrEv != null)
        {
            // P2 v9: history・slice・macro・icc・hdrEv
            if (m.version < 9) throw new ProjectFormatException("History/slices/macro/icc/hdr needs version 9+.");
            if (m.history != null)
            {
                if (m.history.Count > HistoryOps.MaxEntries) throw new ProjectFormatException("Too many history entries.");
                foreach (var e in m.history)
                    if (string.IsNullOrWhiteSpace(e.action) || e.action.Length > 1024)
                        throw new ProjectFormatException("Bad history entry.");
            }
            if (m.slices != null)
            {
                if (m.slices.Count > SliceOps.MaxSlices) throw new ProjectFormatException("Too many slices.");
                foreach (var s in m.slices)
                {
                    if (string.IsNullOrWhiteSpace(s.name) || s.name.Length > 256)
                        throw new ProjectFormatException("Bad slice name.");
                    if (s.w < 1 || s.h < 1 || s.x < 0 || s.y < 0 || s.x + s.w > m.width || s.y + s.h > m.height)
                        throw new ProjectFormatException($"Bad slice rect '{s.name}'.");
                }
            }
            if (m.macro != null)
            {
                if (m.macro.Count > MacroOps.MaxSteps) throw new ProjectFormatException("Too many macro steps.");
                foreach (var s in m.macro)
                    if (!Enum.TryParse<MacroOp>(s.op, out _) || (s.op != nameof(MacroOp.Invert) && (s.value is < -100 or > 100 || !double.IsFinite(s.value))))
                        throw new ProjectFormatException("Bad macro step.");
            }
            if (m.icc != null && !Enum.TryParse<IccProfileKind>(m.icc, out _))
                throw new ProjectFormatException("Bad ICC profile.");
            if (m.hdrEv is double ev && (!double.IsFinite(ev) || ev is < -8 or > 8))
                throw new ProjectFormatException("Bad HDR EV.");
        }
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
        if (manifest.channels != null && manifest.channels.Count > 0)   // P1 v8 spare channel
        {
            if (manifest.channels.Count != doc.SpareChannels.Count)
                throw new ProjectFormatException("Channel manifest mismatch.");
            for (int i = 0; i < doc.SpareChannels.Count; i++)
            {
                var ch = doc.SpareChannels[i];
                long n = (long)ch.W * ch.H;
                if (n > MaxImagePixels - maskPixels)
                    throw new ProjectFormatException($"Channel too large on '{ch.Name}'.");
                maskPixels += n;
                assets[manifest.channels[i].channelFile] = EncodeMaskPng(ch.Mask, ch.W, ch.H);
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
            if (rec.vector != null || rec.isVector == true)   // P1 v8 ベクター線
            {
                if (rec.vector == null) throw new ProjectFormatException($"Bad vector on '{rec.name}'.");
                var pts = rec.vector.points.Select(p => new SKPoint((float)p.x, (float)p.y)).ToList();
                var vs = new VectorStroke { Points = pts, Width = (float)rec.vector.width, Closed = rec.vector.closed };
                VectorStrokeOps.Validate(vs);
                l.Vector = vs;
                l.IsVectorLayer = true;
            }
            if (rec.blendRange != null)   // P2 v9 blend range
            {
                var br = new BlendRange { Lo = rec.blendRange.lo, Hi = rec.blendRange.hi, Feather = rec.blendRange.feather };
                if (!br.IsValid) throw new ProjectFormatException($"Bad blend range on '{rec.name}'.");
                l.UseBlendRange = true;
                l.BlendLo = br.Lo; l.BlendHi = br.Hi; l.BlendFeather = br.Feather;
            }
            doc.Layers.Add(l);
            byId[l.Id] = l;
        }
        if (m.channels != null && m.channels.Count > 0)   // P1 v8 spare channel
        {
            foreach (var c in m.channels)
            {
                string p = SafeName(imgDir, c.channelFile);
                var fi = new FileInfo(p);
                if (!fi.Exists || fi.Length > MaxAssetBytes) throw new ProjectFormatException($"Missing channel for '{c.name}'.");
                byte[] bytes = File.ReadAllBytes(p);
                if (!IsPng(bytes)) throw new ProjectFormatException($"Missing channel for '{c.name}'.");
                using var bmp = SKBitmap.Decode(bytes) ?? throw new ProjectFormatException($"Missing channel for '{c.name}'.");
                if (bmp.Width != c.width || bmp.Height != c.height)
                    throw new ProjectFormatException($"Bad channel size on '{c.name}'.");
                if ((long)bmp.Width * bmp.Height > MaxImagePixels - maskPixels)
                    throw new ProjectFormatException("Channel too large.");
                maskPixels += (long)bmp.Width * bmp.Height;
                var gray = new byte[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        gray[y * bmp.Width + x] = bmp.GetPixel(x, y).Red;
                doc.SpareChannels.Add(new SpareChannel { Name = c.name, Mask = gray, W = c.width, H = c.height });
            }
        }
        if (m.history != null)   // P2 v9 History保存
            foreach (var e in m.history)
            {
                if (!DateTime.TryParse(e.time, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                    t = DateTime.UtcNow;
                doc.History.Add(new HistoryEntry { Time = t, Action = e.action ?? "", Layers = e.layers });
            }
        if (m.slices != null)   // P2 v9 Export slice
            foreach (var s in m.slices)
                doc.Slices.Add(new Slice { Name = s.name, X = s.x, Y = s.y, W = s.w, H = s.h });
        if (m.macro != null)   // P2 v9 macro
            foreach (var s in m.macro)
            {
                if (!Enum.TryParse<MacroOp>(s.op, out var op)) throw new ProjectFormatException("Bad macro step.");
                doc.Macro.Add(new MacroStep { Op = op, Value = s.value });
            }
        if (m.icc != null)   // P2 v9 ICC
        {
            if (!Enum.TryParse<IccProfileKind>(m.icc, out var icc)) throw new ProjectFormatException("Bad ICC profile.");
            doc.IccProfile = icc;
        }
        if (m.hdrEv is double hev) doc.HdrEv = hev;   // P2 v9 HDR
        doc.MarkSaved();
        return doc;
    }
}
