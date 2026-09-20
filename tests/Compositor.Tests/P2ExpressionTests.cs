// Compositor — P2 表現拡張の試験 (docs/WORLD_BEST_PAINT.md §4 P2).
// Mixer・対称・Wrap・透視・mesh/puppet・History・macro・workspace・
// HDR/ICC/Gamut・slice/compound/blendRange・3D・動画層・AI補助・
// .comp v9 往復。いずれも決定性・headless 可。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

static class P2Docs
{
    public static Document New(int w = 32, int h = 32, byte r = 200, byte g = 100, byte b = 50)
    {
        var doc = new Document { Width = w, Height = h, Name = "P2" };
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(r, g, b, 255));
        doc.Layers.Add(new Layer { Name = "Base", Bitmap = bmp });
        doc.ActiveLayerId = doc.Layers[0].Id;
        return doc;
    }
}

public class P2MixerTests
{
    [Fact]
    public void DryBrushEqualsPaint()
    {
        using var bmp = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColor.Empty);
        var st = new MixerSettings { Wet = 0, Load = 1, Mix = 0 };
        var brush = new SKColor(10, 20, 30, 255);
        MixerOps.ApplyStroke(bmp, new List<SKPoint> { new(8, 8) }, brush, 6f, st);
        var c = bmp.GetPixel(8, 8);
        Assert.True(Math.Abs(c.Red - 10) <= 2 && Math.Abs(c.Green - 20) <= 2 && Math.Abs(c.Blue - 30) <= 2);
    }

    [Fact]
    public void WetBrushPicksUpCanvas()
    {
        using var bmp = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 0, 0, 255));
        var st = new MixerSettings { Wet = 1, Load = 1, Mix = 1 };
        var carried = MixerOps.ApplyStroke(bmp, new List<SKPoint> { new(8, 8) }, new SKColor(0, 0, 200, 255), 6f, st);
        Assert.True(carried.Red > 50);   // 赤 canvas を拾った
        Assert.NotEqual(new SKColor(0, 0, 200, 255), bmp.GetPixel(8, 8));
    }

    [Fact]
    public void BadSettingsThrow()
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        Assert.Throws<InvalidOperationException>(() => MixerOps.ApplyStroke(bmp,
            new List<SKPoint> { new(1, 1) }, SKColors.Black, 4f, new MixerSettings { Wet = 2 }));
    }
}

public class P2SymmetryWrapTests
{
    [Fact]
    public void MirrorHReflects()
    {
        var pts = SymmetryOps.TransformPoints(new List<SKPoint> { new(2, 5) }, 10, 10, SymmetryMode.MirrorH);
        Assert.Equal(2, pts.Count);
        Assert.Equal(8, pts[1].X, 0);
    }

    [Fact]
    public void MirrorBitmapFlips()
    {
        using var src = new SKBitmap(4, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(SKColor.Empty);
        src.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        using var dst = SymmetryOps.MirrorBitmap(src, flipH: true, flipV: false);
        Assert.Equal(new SKColor(255, 0, 0, 255), dst.GetPixel(3, 0));
    }

    [Fact]
    public void WrapPointModulo()
    {
        var q = WrapOps.WrapPoint(new SKPoint(34, -2), 32, 32);
        Assert.Equal(2, q.X, 0);
        Assert.Equal(30, q.Y, 0);
    }

    [Fact]
    public void WrapStrokeAddsNeighbors()
    {
        var pts = WrapOps.WrapStroke(new List<SKPoint> { new(1, 1) }, 32, 32);
        Assert.Equal(9, pts.Count);
    }

    [Fact]
    public void TiledIsDoubleSize()
    {
        using var src = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(1, 2, 3, 255));
        using var t = WrapOps.RenderTiled(src);
        Assert.Equal(16, t.Width);
        Assert.Equal(new SKColor(1, 2, 3, 255), t.GetPixel(9, 9));
    }
}

public class P2PerspectiveMeshTests
{
    [Fact]
    public void SnapNearGuide()
    {
        var vp = new SKPoint(0, 0);
        var p = new SKPoint(10, 0.5f);   // 0°線の近傍
        var q = PerspectiveOps.Snap(p, vp, 8f);
        Assert.Equal(0, q.Y, 1);
    }

    [Fact]
    public void FarPointUnchanged()
    {
        var vp = new SKPoint(0, 0);
        var p = new SKPoint(10, 5);   // 0°線から遠い
        var q = PerspectiveOps.Snap(p, vp, 0.1f);
        Assert.Equal(p.X, q.X, 3);
        Assert.Equal(p.Y, q.Y, 3);
    }

    [Fact]
    public void MeshIdentityIsNoOp()
    {
        using var src = new SKBitmap(12, 12, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(90, 80, 70, 255));
        src.SetPixel(3, 3, new SKColor(1, 2, 3, 255));
        using var dst = MeshOps.Apply(src, MeshWarp.Identity(4, 4));
        Assert.Equal(src.GetPixel(3, 3), dst.GetPixel(3, 3));
        Assert.Equal(src.GetPixel(9, 9), dst.GetPixel(9, 9));
    }

    [Fact]
    public void MeshBulgeMovesPixels()
    {
        using var src = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                src.SetPixel(x, y, x < 8 ? new SKColor(200, 0, 0, 255) : new SKColor(0, 0, 200, 255));
        var warp = MeshWarp.Identity(4, 4);
        warp.Dx[1, 1] = 6; warp.Dx[2, 2] = -6;
        using var dst = MeshOps.Apply(src, warp);
        bool changed = false;
        for (int y = 0; y < 16 && !changed; y++)
            for (int x = 0; x < 16 && !changed; x++)
                if (dst.GetPixel(x, y) != src.GetPixel(x, y)) changed = true;
        Assert.True(changed);
    }

    [Fact]
    public void PuppetZeroDeltaIsNoOp()
    {
        using var src = new SKBitmap(12, 12, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(40, 50, 60, 255));
        using var dst = PuppetOps.Apply(src, new SKPoint(6, 6), new SKPoint(0, 0), 8f);
        Assert.Equal(src.GetPixel(6, 6), dst.GetPixel(6, 6));
    }

    [Fact]
    public void PuppetDeltaMovesPixels()
    {
        using var src = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                src.SetPixel(x, y, x < 8 ? new SKColor(200, 0, 0, 255) : new SKColor(0, 0, 200, 255));
        using var dst = PuppetOps.Apply(src, new SKPoint(8, 8), new SKPoint(6, 0), 6f);
        bool changed = false;
        for (int y = 0; y < 16 && !changed; y++)
            for (int x = 0; x < 16 && !changed; x++)
                if (dst.GetPixel(x, y) != src.GetPixel(x, y)) changed = true;
        Assert.True(changed);
    }
}

public class P2HistoryMacroTests
{
    [Fact]
    public void HistoryCapsAt200()
    {
        var doc = P2Docs.New();
        for (int i = 0; i < 205; i++) HistoryOps.Record(doc, $"op{i}");
        Assert.Equal(200, doc.History.Count);
        Assert.Equal("op5", doc.History[0].Action);
    }

    [Fact]
    public void MacroReplayAdjustsLayer()
    {
        var doc = P2Docs.New();
        MacroOps.Record(doc, MacroOp.Brightness, 10);
        MacroOps.Record(doc, MacroOp.Contrast, 10);
        MacroOps.Replay(doc.Layers[0], doc.Macro);
        Assert.Equal(10, doc.Layers[0].Brightness);
        Assert.Equal(10, doc.Layers[0].Contrast);
    }

    [Fact]
    public void MacroBadValueThrows()
    {
        var doc = P2Docs.New();
        Assert.Throws<InvalidOperationException>(() => MacroOps.Record(doc, MacroOp.Brightness, 500));
    }

    [Fact]
    public void MacroInvertFlipsPixel()
    {
        var doc = P2Docs.New();
        var before = doc.Layers[0].Bitmap.GetPixel(4, 4);
        MacroOps.Replay(doc.Layers[0], new List<MacroStep> { new() { Op = MacroOp.Invert } });
        using var bmp = doc.Compose();
        var after = bmp.GetPixel(4, 4);
        Assert.NotEqual(before.Red, after.Red);
    }
}

public class P2WorkspaceApiTests
{
    [Fact]
    public void PresetRoundTrip()
    {
        var doc = P2Docs.New();
        doc.Persona = PersonaKind.Export;
        doc.SimpleMode = true;
        var p = WorkspaceOps.Capture(doc, "E2E");
        string json = WorkspaceOps.ToJson(p);
        var back = WorkspaceOps.FromJson(json);
        var doc2 = P2Docs.New();
        WorkspaceOps.Apply(doc2, back);
        Assert.Equal(PersonaKind.Export, doc2.Persona);
        Assert.True(doc2.SimpleMode);
    }

    [Fact]
    public void BadPresetThrows()
    {
        Assert.Throws<InvalidOperationException>(() => WorkspaceOps.FromJson("{\"Name\":\"  \"}"));
        Assert.Throws<InvalidOperationException>(() => WorkspaceOps.Apply(P2Docs.New(), null));
    }

    [Fact]
    public void StubContainsDocName()
    {
        var doc = P2Docs.New();
        doc.Name = "StubDoc";
        string stub = PythonApiStub.GenerateWithSummary(doc);
        Assert.Contains("StubDoc", stub);
        Assert.Contains("compositor", stub);
    }
}

public class P2ColorPipelineTests
{
    [Fact]
    public void ExposureDoubles()
    {
        using var src = new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(60, 60, 60, 255));
        using var dst = HdrOps.ApplyExposure(src, 1.0);
        Assert.Equal(120, dst.GetPixel(1, 1).Red);
    }

    [Fact]
    public void BadEvThrows()
    {
        using var src = new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        Assert.Throws<InvalidOperationException>(() => HdrOps.ApplyExposure(src, 99));
    }

    [Fact]
    public void ToneMapMidGray()
    {
        using var src = new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(128, 128, 128, 255));
        using var dst = HdrOps.ToneMap(src);
        Assert.InRange(dst.GetPixel(1, 1).Red, 168, 172);
    }

    [Fact]
    public void IccRoundTrip()
    {
        var c = new PaletteColor(0.8, 0.2, 0.3);
        var p3 = IccOps.Convert(c, IccProfileKind.SRGB, IccProfileKind.DisplayP3);
        var back = IccOps.Convert(p3, IccProfileKind.DisplayP3, IccProfileKind.SRGB);
        Assert.InRange(back.R, c.R - 0.01, c.R + 0.01);
        Assert.Equal(c, IccOps.Convert(c, IccProfileKind.SRGB, IccProfileKind.SRGB));
    }

    [Fact]
    public void GamutWindows()
    {
        var red = new PaletteColor(1, 0, 0);
        var cyan = new PaletteColor(0, 1, 1);
        Assert.True(GamutMaskOps.IsAllowed(red, red, GamutKind.Full));
        Assert.True(GamutMaskOps.IsAllowed(red, red, GamutKind.Analogous90));
        Assert.False(GamutMaskOps.IsAllowed(cyan, red, GamutKind.Analogous90));
        Assert.True(GamutMaskOps.IsAllowed(cyan, red, GamutKind.Complementary));
    }
}

public class P2SliceCompoundBlendTests
{
    [Fact]
    public void SliceCutSize()
    {
        using var comp = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        comp.Erase(new SKColor(9, 9, 9, 255));
        var s = new Slice { Name = "A", X = 4, Y = 4, W = 8, H = 6 };
        using var cut = SliceOps.Cut(comp, s);
        Assert.Equal(8, cut.Width);
        Assert.Equal(6, cut.Height);
        Assert.Equal(new SKColor(9, 9, 9, 255), cut.GetPixel(0, 0));
    }

    [Fact]
    public void BadSliceThrows()
    {
        using var comp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        Assert.Throws<InvalidOperationException>(() => SliceOps.Cut(comp,
            new Slice { Name = "X", X = 6, Y = 6, W = 8, H = 8 }));
    }

    [Fact]
    public void CompoundTruthTable()
    {
        var a = new byte[] { 255, 255, 0, 0 };
        var b = new byte[] { 255, 0, 255, 0 };
        Assert.Equal(new byte[] { 255, 255, 255, 0 }, CompoundMaskOps.Combine(a, b, 2, 2, CompoundMode.Union));
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, CompoundMaskOps.Combine(a, b, 2, 2, CompoundMode.Intersect));
        Assert.Equal(new byte[] { 0, 255, 0, 0 }, CompoundMaskOps.Combine(a, b, 2, 2, CompoundMode.Subtract));
        Assert.Equal(new byte[] { 0, 255, 255, 0 }, CompoundMaskOps.Combine(a, b, 2, 2, CompoundMode.Xor));
    }

    [Fact]
    public void BlendRangeFactors()
    {
        var r = new BlendRange { Lo = 0.3, Hi = 0.7, Feather = 0.1 };
        Assert.Equal(0, BlendRangeOps.Factor(0.0, r));
        Assert.Equal(1, BlendRangeOps.Factor(0.5, r));
        Assert.Equal(0, BlendRangeOps.Factor(1.0, r));
        double mid = BlendRangeOps.Factor(0.25, r);
        Assert.InRange(mid, 0.01, 0.99);
        Assert.Throws<InvalidOperationException>(() => BlendRangeOps.Factor(0.5,
            new BlendRange { Lo = 0.8, Hi = 0.2, Feather = 0 }));
    }

    [Fact]
    public void BlendRangeModulatesCompose()
    {
        // 下地=暗灰の上に白層。範囲が下地輝度を外すと抑制され、含むと適用される。
        var doc = new Document { Width = 8, Height = 8, Name = "BR" };
        var dark = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        dark.Erase(new SKColor(40, 40, 40, 255));
        doc.Layers.Add(new Layer { Name = "dark", Bitmap = dark });
        var white = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        white.Erase(new SKColor(255, 255, 255, 255));
        var top = new Layer { Name = "top", Bitmap = white, UseBlendRange = true,
            BlendLo = 0.9, BlendHi = 1.0, BlendFeather = 0 };
        doc.Layers.Add(top);
        using (var bmp = doc.Compose())
            Assert.Equal(new SKColor(40, 40, 40, 255), bmp.GetPixel(3, 3));   // 抑制
        top.BlendLo = 0; top.BlendHi = 1;
        using (var bmp = doc.Compose())
            Assert.Equal(new SKColor(255, 255, 255, 255), bmp.GetPixel(3, 3));   // 適用
    }
}

public class P2MediaAiTests
{
    [Fact]
    public void Proxy3DNonEmpty()
    {
        foreach (var k in new[] { Proxy3DKind.Box, Proxy3DKind.Sphere, Proxy3DKind.Cylinder })
        {
            using var bmp = Proxy3DOps.Render(k, 32, 32, new SKColor(100, 150, 200, 255));
            bool any = false;
            for (int y = 0; y < 32 && !any; y++)
                for (int x = 0; x < 32 && !any; x++)
                    if (bmp.GetPixel(x, y).Alpha != 0) any = true;
            Assert.True(any, k.ToString());
        }
    }

    [Fact]
    public void VideoFramesRoundTrip()
    {
        var doc = P2Docs.New();
        using var f = doc.Compose();
        VideoLayerOps.AddFrame(doc, f);
        Assert.Single(doc.VideoFrames);
        using var onion = VideoLayerOps.Onion(doc.VideoFrames[0], null);
        Assert.Equal(32, onion.Width);
        string manifest = VideoLayerOps.ExportManifest(doc);
        Assert.Contains("frame-0000", manifest);
        Assert.Throws<InvalidOperationException>(() => VideoLayerOps.AddFrame(doc,
            new SKBitmap(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul)));
        VideoLayerOps.Clear(doc);
        Assert.Empty(doc.VideoFrames);
    }

    [Fact]
    public void FeatherSmooths()
    {
        int w = 8, h = 8;
        var mask = new byte[w * h];
        mask[4 * w + 4] = 255;
        var f = AiAssistOps.Feather(mask, w, h, 2);
        Assert.True(f[4 * w + 4] < 255 && f[4 * w + 4] > 0);
        var copy = AiAssistOps.Feather(mask, w, h, 0);
        Assert.Equal(mask, copy);
    }

    [Fact]
    public void BackgroundProposalOnUniform()
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 200, 200, 255));
        var m = AiAssistOps.ProposeBackgroundMask(bmp, 32);
        Assert.All(m, b => Assert.Equal(0, b));
    }

    [Fact]
    public void DisturbanceDeterministic()
    {
        using var a = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var b = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        a.Erase(new SKColor(100, 100, 100, 255));
        b.Erase(new SKColor(100, 100, 100, 255));
        AiAssistOps.Disturbance(a, 100, seed: 7);
        AiAssistOps.Disturbance(b, 100, seed: 7);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
        bool changed = false;
        for (int y = 0; y < 8 && !changed; y++)
            for (int x = 0; x < 8 && !changed; x++)
                if (a.GetPixel(x, y).Red != 100) changed = true;
        Assert.True(changed);
        using var c = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
        c.Erase(new SKColor(100, 100, 100, 255));
        AiAssistOps.Disturbance(c, 0);
        Assert.Equal(new SKColor(100, 100, 100, 255), c.GetPixel(3, 3));
    }
}

public class P2FormatV9Tests
{
    [Fact]
    public void V9RoundTrip()
    {
        var doc = P2Docs.New(24, 24);
        HistoryOps.Record(doc, "e2e-history");
        doc.Slices.Add(new Slice { Name = "S1", X = 2, Y = 2, W = 8, H = 8 });
        MacroOps.Record(doc, MacroOp.Brightness, 5);
        doc.Layers[0].UseBlendRange = true;
        doc.Layers[0].BlendLo = 0.2; doc.Layers[0].BlendHi = 0.8; doc.Layers[0].BlendFeather = 0.05;
        doc.IccProfile = IccProfileKind.DisplayP3;
        doc.HdrEv = 1.0;
        string dir = Path.Combine(Path.GetTempPath(), "p2v9-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            ProjectFormat.Save(doc, dir);
            var loaded = ProjectFormat.Load(dir);
            Assert.Single(loaded.History);
            Assert.Equal("e2e-history", loaded.History[0].Action);
            Assert.Single(loaded.Slices);
            Assert.Equal("S1", loaded.Slices[0].Name);
            Assert.Single(loaded.Macro);
            Assert.Equal(MacroOp.Brightness, loaded.Macro[0].Op);
            Assert.True(loaded.Layers[0].UseBlendRange);
            Assert.Equal(0.2, loaded.Layers[0].BlendLo, 6);
            Assert.Equal(IccProfileKind.DisplayP3, loaded.IccProfile);
            Assert.Equal(1.0, loaded.HdrEv, 6);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void V8WithoutNewFieldsStillValid()
    {
        var doc = P2Docs.New(16, 16);
        var m = ProjectFormat.BuildManifest(doc);
        m.version = 8;   // P2 field なし → v8 として valid
        ProjectFormat.Validate(m);
        m.history = new List<ManifestHistory> { new() { time = DateTime.UtcNow.ToString("O"), action = "x", layers = 1 } };
        Assert.Throws<ProjectFormatException>(() => ProjectFormat.Validate(m));
    }
}

public class P2E2ETests
{
    [Fact]
    public void E2E_Import_P2Edit_Export_Headless()
    {
        // Import: PNG bytes -> DecodeFile (実機 Import の engine 路)
        using var src = new SKBitmap(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(new SKColor(120, 140, 160, 255));
        string tmp = Path.Combine(Path.GetTempPath(), $"p2e2e_{Guid.NewGuid():N}.png");
        string dir = Path.Combine(Path.GetTempPath(), "p2e2e-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            using (var img = SKImage.FromBitmap(src))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(tmp, data.ToArray());
            var dec = ImageImport.DecodeFile(tmp);
            Assert.False(dec.Unsupported, dec.Error);
            Assert.NotNull(dec.Bitmap);
            // 編集 (P2一巡り): 取込層 -> Mixer -> 対称複製 -> Mesh -> Slice -> HDR -> macro
            var doc = new Document { Width = 48, Height = 48, Name = "P2E2E" };
            doc.Layers.Add(new Layer { Name = "imported", Bitmap = dec.Bitmap });
            doc.ActiveLayerId = doc.Layers[0].Id;
            Document.EnsureUniqueBitmap(doc.Layers[0]);
            MixerOps.ApplyStroke(doc.Layers[0].Bitmap,
                new List<SKPoint> { new(4, 4), new(24, 24), new(44, 44) },
                new SKColor(200, 50, 50, 255), 8f, new MixerSettings { Wet = 0.5, Load = 1, Mix = 0.5 });
            HistoryOps.Record(doc, "mixer");
            using (var mirrored = SymmetryOps.MirrorBitmap(doc.Layers[0].Bitmap, true, false))
            {
                var nl = new Layer { Name = "sym" };
                nl.Bitmap = new SKBitmap(mirrored.Width, mirrored.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var c = new SKCanvas(nl.Bitmap)) c.DrawBitmap(mirrored, 0, 0);
                doc.Layers.Add(nl);
            }
            var warp = MeshWarp.Identity(4, 4);
            warp.Dx[1, 1] = 3;
            Document.EnsureUniqueBitmap(doc.Layers[0]);
            var warped = MeshOps.Apply(doc.Layers[0].Bitmap, warp);
            doc.Layers[0].Bitmap.Dispose();
            doc.Layers[0].Bitmap = warped;
            doc.Selection = new SKRect(4, 4, 20, 20);
            doc.Slices.Add(new Slice { Name = "E2E", X = 4, Y = 4, W = 16, H = 16 });
            using (var comp0 = doc.Compose())
            using (var cut = SliceOps.Cut(comp0, doc.Slices[0]))
                Assert.Equal(16, cut.Width);
            MacroOps.Record(doc, MacroOp.Brightness, 5);
            MacroOps.Replay(doc.Layers[0], doc.Macro);
            doc.Layers[0].UseBlendRange = true;
            doc.Layers[0].BlendLo = 0.1; doc.Layers[0].BlendHi = 0.9; doc.Layers[0].BlendFeather = 0.05;
            doc.IccProfile = IccProfileKind.DisplayP3;
            doc.HdrEv = 0.5;
            using (var frame = doc.Compose())
                VideoLayerOps.AddFrame(doc, frame);
            // Export: 合成 PNG が復号でき、slice PNG が出ること
            using var composed = doc.Compose();
            Assert.Equal(48, composed.Width);
            using (var img = SKImage.FromBitmap(composed))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                Assert.True(data.ToArray().Length > 100);
            byte[] jpeg = JpegExport.Export(doc, new JpegOptions { Quality = 0.85 });
            Assert.True(jpeg.Length > 100);
            // .comp v9 往復: history・slice・macro・blendRange・icc・hdr が残ること
            ProjectFormat.Save(doc, dir);
            var loaded = ProjectFormat.Load(dir);
            Assert.Single(loaded.History);
            Assert.Single(loaded.Slices);
            Assert.Single(loaded.Macro);
            Assert.True(loaded.Layers[0].UseBlendRange);
            Assert.Equal(IccProfileKind.DisplayP3, loaded.IccProfile);
            Assert.Equal(0.5, loaded.HdrEv, 6);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
