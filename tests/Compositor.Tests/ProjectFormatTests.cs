using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public class ProjectFormatTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    static Document TwoLayerDoc()
    {
        var doc = new Document { Width = 32, Height = 24, Name = "T" };
        doc.Layers.Add(new Layer { Name = "base", Bitmap = Solid(32, 24, SKColors.Red) });
        doc.Layers.Add(new Layer
        {
            Name = "top", Bitmap = Solid(8, 6, SKColors.Blue),
            Position = new SKPoint(4, 5), Opacity = 0.5f, Blend = SKBlendMode.Multiply,
            Rotation = 90, FlipH = true, Sampling = LayerSampling.Nearest,
        });
        return doc;
    }

    static string TempPkg(string name) =>
        Path.Combine(Path.GetTempPath(), $"comp-test-{name}-{Guid.NewGuid():N}.comp");

    [Fact]
    public void ValidDimension_Accepts1To30000()
    {
        Assert.Equal(1920, Document.ValidDimension("1920"));
        Assert.Equal(1920, Document.ValidDimension(" 1920 "));
        Assert.Null(Document.ValidDimension("0"));
        Assert.Null(Document.ValidDimension("30001"));
        Assert.Null(Document.ValidDimension("abc"));
        Assert.Null(Document.ValidDimension(""));
        Assert.Null(Document.ValidDimension(null));
    }

    [Fact]
    public void Roundtrip_BasicLayers()
    {
        var doc = TwoLayerDoc();
        string pkg = TempPkg("basic");
        try
        {
            ProjectFormat.Save(doc, pkg);
            Assert.False(doc.IsModified);
            Assert.Equal(pkg, doc.ProjectPath);
            Assert.True(Directory.Exists(pkg));
            Assert.True(File.Exists(Path.Combine(pkg, "manifest.json")));
            var back = ProjectFormat.Load(pkg);
            Assert.Equal(32, back.Width);
            Assert.Equal(24, back.Height);
            Assert.Equal(2, back.Layers.Count);
            var top = back.Layers[1];
            Assert.Equal("top", top.Name);
            Assert.Equal(0.5f, top.Opacity, precision: 5);
            Assert.Equal(SKBlendMode.Multiply, top.Blend);
            Assert.Equal(90, top.Rotation);
            Assert.True(top.FlipH);
            Assert.Equal(LayerSampling.Nearest, top.Sampling);
            Assert.Equal(4, top.Position.X);
            Assert.Equal(5, top.Position.Y);
            // Pixels survive: base red, top blue box scaled 1:1.
            using var flat = back.Compose();
            Assert.Equal(SKColors.Red, flat.GetPixel(0, 0));
            Assert.False(back.IsModified);
        }
        finally { if (Directory.Exists(pkg)) Directory.Delete(pkg, true); }
    }

    [Fact]
    public void Roundtrip_GroupMaskClipFolderMask()
    {
        var doc = new Document { Width = 20, Height = 20, Name = "G" };
        var g = new Layer { Name = "Folder", IsGroup = true };
        doc.Layers.Add(g);
        var child = new Layer { Name = "kid", Bitmap = Solid(10, 10, SKColors.Green), ParentId = g.Id };
        MaskOps.AddMask(child, revealing: true);
        child.MaskLinked = false;
        child.MaskOffset = new SKPoint(2, 3);
        doc.Layers.Add(child);
        var gm = new byte[4 * 4];
        Array.Fill(gm, (byte)128);
        g.Mask = gm; g.MaskW = 4; g.MaskH = 4; g.MaskEnabled = true;
        var clip = new Layer { Name = "clip", Bitmap = Solid(10, 10, SKColors.Blue), ClipSourceId = child.Id };
        doc.Layers.Add(clip);

        string pkg = TempPkg("group");
        try
        {
            ProjectFormat.Save(doc, pkg);
            var back = ProjectFormat.Load(pkg);
            Assert.Equal(3, back.Layers.Count);
            var bg = back.Layers.First(l => l.IsGroup);
            Assert.True(MaskOps.HasMask(bg));   // v6 folder mask round-trips
            var bk = back.Layers.First(l => l.Name == "kid");
            Assert.Equal(bg.Id, bk.ParentId);
            Assert.True(MaskOps.HasMask(bk));
            Assert.False(bk.MaskLinked);
            Assert.Equal(2, bk.MaskOffset.X);
            Assert.Equal(3, bk.MaskOffset.Y);
            var bc = back.Layers.First(l => l.Name == "clip");
            Assert.Equal(bk.Id, bc.ClipSourceId);
        }
        finally { if (Directory.Exists(pkg)) Directory.Delete(pkg, true); }
    }

    [Fact]
    public void Roundtrip_AdjustmentShapeResolutionActive()
    {
        var doc = new Document { Width = 16, Height = 16, Name = "A", Resolution = 144 };
        doc.Layers.Add(new Layer { Name = "base", Bitmap = Solid(16, 16, SKColors.White) });
        var adj = new Layer { Name = "Hue/Saturation", IsAdjustmentLayer = true, Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Hsv } };
        doc.Layers.Add(adj);
        var sh = new Layer
        {
            Name = "shape", Bitmap = Solid(6, 6, SKColors.Red),
            Shape = new ShapeInfo { Kind = ShapeKind.Ellipse, R = 1, G = 0, B = 0, CornerRadius = 3 },
        };
        doc.Layers.Add(sh);
        doc.ActiveLayerId = adj.Id;

        string pkg = TempPkg("adj");
        try
        {
            ProjectFormat.Save(doc, pkg);
            var back = ProjectFormat.Load(pkg);
            Assert.Equal(144, back.Resolution);
            Assert.Equal(adj.Id, back.ActiveLayerId);
            var ba = back.Layers.First(l => l.IsAdjustmentLayer);
            Assert.NotNull(ba.Adjustment);
            Assert.Equal(AdjustmentKind.Hsv, ba.Adjustment.Kind);
            var bs = back.Layers.First(l => l.Name == "shape");
            Assert.NotNull(bs.Shape);
            Assert.Equal(ShapeKind.Ellipse, bs.Shape.Kind);
        }
        finally { if (Directory.Exists(pkg)) Directory.Delete(pkg, true); }
    }

    [Fact]
    public void Save_TwiceToSamePath_Replaces()
    {
        var doc = TwoLayerDoc();
        string pkg = TempPkg("twice");
        try
        {
            ProjectFormat.Save(doc, pkg);
            doc.Layers.Add(new Layer { Name = "third", Bitmap = Solid(4, 4, SKColors.Black) });
            ProjectFormat.Save(doc, pkg);
            Assert.Equal(3, ProjectFormat.Load(pkg).Layers.Count);
        }
        finally { if (Directory.Exists(pkg)) Directory.Delete(pkg, true); }
    }

    [Fact]
    public void Validate_RejectsBadManifests()
    {
        ProjectManifest Base(int version = 7)
        {
            var id = Guid.NewGuid();
            return new ProjectManifest
            {
                version = version, documentID = Guid.NewGuid(), width = 10, height = 10,
                layers = new List<ManifestLayer>
                {
                    new() { id = id, name = "a", transform = new ManifestTransform
                    {
                        origin = new ManifestPoint(), size = new ManifestSize { width = 10, height = 10 },
                    } },
                },
            };
        }
        // Bad version.
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(Base(99)));
        // v1 with group.
        var v1 = Base(1);
        v1.layers[0].isGroup = true;
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(v1));
        // v3 with non-default appearance.
        var v2 = Base(2);
        v2.layers[0].opacity = 0.5;
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(v2));
        // Duplicate ids.
        var dup = Base();
        dup.layers.Add(new ManifestLayer { id = dup.layers[0].id, name = "b", transform = dup.layers[0].transform });
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(dup));
        // Clip self-link.
        var self = Base(7);
        self.layers[0].maskSourceID = self.layers[0].id;
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(self));
        // Unsafe image file.
        var evil = Base();
        evil.layers[0].imageFile = "../evil.png";
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(evil));
        // Unknown blend.
        var blend = Base();
        blend.layers[0].blendMode = "Nope";
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(blend));
        // Group with default appearance passes; with opacity fails.
        var grp = Base();
        grp.layers[0].isGroup = true;
        grp.layers[0].opacity = 0.3;
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(grp));
    }

    [Fact]
    public void Load_MissingPackage_Throws()
    {
        Assert.Throws<ProjectFormatException>(() =>
            ProjectFormat.Load(Path.Combine(Path.GetTempPath(), "comp-nope-" + Guid.NewGuid().ToString("N"))));
    }
}
