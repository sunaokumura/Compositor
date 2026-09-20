using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_7d185144 P0 世界最良規範の現行延長 (docs/WORLD_BEST_PAINT.md §4 P0).
/// gap-closing fill / canvas 回転 view / Solo / ASE・Harmony・profile preview / `/` 検索。</summary>
public class P0WorldBestTests
{
    static SKBitmap LineArtWithGap()
    {
        // 20x10 白地・x=10 に黒縦線・y=5 の1px隙間あり
        var b = new SKBitmap(20, 10, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(SKColors.White);
        for (int y = 0; y < 10; y++)
        {
            if (y == 5) continue;
            b.SetPixel(10, y, SKColors.Black);
        }
        return b;
    }

    [Fact]
    public void GapClosed_BlocksLeakThroughSmallGap()
    {
        using var bmp = LineArtWithGap();
        var (_, leak) = GapCloseOps.WandMaskGapClosed(bmp, 2, 5, 0, 10, true, gapRadius: 0);
        var (_, closed) = GapCloseOps.WandMaskGapClosed(bmp, 2, 5, 0, 10, true, gapRadius: 2);
        // 隙間あり: gap=0 は右側へ漏出する (左10列=100px より多い)
        Assert.True(leak > 100, $"leak={leak}");
        // gap=2 は右側へ漏れない (左側のみ・隙間1px分を除き100未満)
        Assert.True(closed <= 100, $"closed={closed}");
        Assert.True(closed > 50, $"closed={closed}");
    }

    [Fact]
    public void GapClosed_ZeroEqualsWandMask()
    {
        using var bmp = LineArtWithGap();
        var (a, na) = GapCloseOps.WandMaskGapClosed(bmp, 2, 5, 0, 10, true, 0);
        var (b, nb) = SelectionTools.WandMask(bmp, 2, 5, 0, 10, true);
        Assert.Equal(nb, na);
        Assert.Equal(b, a);
    }

    [Fact]
    public void GapClosed_SeedOnLine_ReturnsZero()
    {
        using var bmp = LineArtWithGap();
        var (_, n) = GapCloseOps.WandMaskGapClosed(bmp, 10, 2, 0, 10, true, 2);
        Assert.Equal(0, n);
    }

    [Fact]
    public void GapClose_Barrier_And_Dilate()
    {
        using var bmp = LineArtWithGap();
        var barrier = GapCloseOps.BarrierMask(bmp);
        Assert.True(barrier[2 * 20 + 10]);    // 線上
        Assert.False(barrier[5 * 20 + 10]);   // 隙間
        Assert.False(barrier[5 * 20 + 2]);    // 白地
        var same = GapCloseOps.Dilate(barrier, 20, 10, 0);
        Assert.Equal(barrier, same);
        var grown = GapCloseOps.Dilate(barrier, 20, 10, 2);
        Assert.True(grown[5 * 20 + 10]);      // 隙間が閉じる
        Assert.False(grown[5 * 20 + 2]);      // 2px離れた白地は開いたまま
    }

    [Fact]
    public void GapClosed_DocSelect_SetsWandSelection()
    {
        var doc = new Document { Width = 20, Height = 10 };
        using var bmp = LineArtWithGap();
        int n = GapCloseOps.WandSelectGapClosed(doc, bmp, new SKPoint(2, 5), 10, 0, true, SelectionMode.Replace, 2);
        Assert.True(n > 0);
        Assert.Equal(SelectionKind.Wand, doc.SelKind);
        Assert.True(doc.InsideSelection(new SKPoint(2, 5)));
        Assert.False(doc.InsideSelection(new SKPoint(15, 5)));
    }

    [Fact]
    public void CanvasView_NormalizeAngle()
    {
        Assert.Equal(-170f, CanvasViewOps.NormalizeAngle(190f));
        Assert.Equal(170f, CanvasViewOps.NormalizeAngle(-190f));
        Assert.Equal(0f, CanvasViewOps.NormalizeAngle(360f));
        Assert.Equal(0f, CanvasViewOps.NormalizeAngle(0f));
        Assert.Equal(180f, CanvasViewOps.NormalizeAngle(180f));
    }

    [Fact]
    public void CanvasView_RotatePoint_Clockwise()
    {
        var r = CanvasViewOps.RotatePoint(new SKPoint(1, 0), new SKPoint(0, 0), 90f);
        Assert.True(Math.Abs(r.X) < 1e-4 && Math.Abs(r.Y - 1) < 1e-4, $"({r.X},{r.Y})");
        var back = CanvasViewOps.RotatePoint(r, new SKPoint(0, 0), -90f);
        Assert.True(Math.Abs(back.X - 1) < 1e-4 && Math.Abs(back.Y) < 1e-4, $"({back.X},{back.Y})");
    }

    [Fact]
    public void Solo_SetClear_IsShown()
    {
        var doc = new Document { Width = 8, Height = 8 };
        var a = new Layer { Name = "A" };
        var b = new Layer { Name = "B" };
        doc.Layers.Add(a); doc.Layers.Add(b);
        Assert.False(SoloOps.IsSoloActive(doc));
        Assert.True(SoloOps.IsShown(doc, a));
        SoloOps.SetSolo(doc, a.Id);
        Assert.True(SoloOps.IsSoloActive(doc));
        Assert.True(SoloOps.IsShown(doc, a));
        Assert.False(SoloOps.IsShown(doc, b));
        SoloOps.Clear(doc);
        Assert.False(SoloOps.IsSoloActive(doc));
        Assert.True(SoloOps.IsShown(doc, b));
    }

    [Fact]
    public void Solo_FiltersEffectiveVisibility()
    {
        var doc = new Document { Width = 8, Height = 8 };
        var a = new Layer { Name = "A" };
        var b = new Layer { Name = "B" };
        doc.Layers.Add(a); doc.Layers.Add(b);
        doc.SoloLayerId = b.Id;
        Assert.True(LayerHierarchy.IsEffectivelyVisible(doc, b));
        Assert.False(LayerHierarchy.IsEffectivelyVisible(doc, a));
        doc.SoloLayerId = null;
        Assert.True(LayerHierarchy.IsEffectivelyVisible(doc, a));
    }

    // ---------- ASE ----------

    static void U16(List<byte> d, ushort v) { d.Add((byte)(v >> 8)); d.Add((byte)(v & 0xFF)); }
    static void U32(List<byte> d, uint v) { d.Add((byte)(v >> 24)); d.Add((byte)((v >> 16) & 0xFF)); d.Add((byte)((v >> 8) & 0xFF)); d.Add((byte)(v & 0xFF)); }
    static void F32(List<byte> d, float v) { U32(d, BitConverter.ToUInt32(BitConverter.GetBytes(v), 0)); }
    static void ColorBlock(List<byte> d, string name, string model, float[] values)
    {
        var body = new List<byte>();
        U16(body, (ushort)(name.Length + 1));
        foreach (char c in name) U16(body, c);
        U16(body, 0);
        foreach (char c in model) body.Add((byte)c);
        foreach (float v in values) F32(body, v);
        U16(body, 2);   // normal
        U16(d, 0x0001);
        U32(d, (uint)body.Count);
        d.AddRange(body);
    }

    static byte[] SampleAse()
    {
        var d = new List<byte> { (byte)'A', (byte)'S', (byte)'E', (byte)'F' };
        U16(d, 1); U16(d, 0);
        U32(d, 3);
        ColorBlock(d, "Red", "RGB ", new[] { 1f, 0f, 0f });
        ColorBlock(d, "Paper", "CMYK", new[] { 0f, 0f, 0f, 0f });
        ColorBlock(d, "Mid", "Gray", new[] { 0.5f });
        return d.ToArray();
    }

    [Fact]
    public void Ase_Read_RGB_CMYK_Gray()
    {
        var list = AseReader.Read(SampleAse());
        Assert.Equal(3, list.Count);
        Assert.Equal(new PaletteColor(1, 0, 0), list[0]);
        Assert.Equal(new PaletteColor(1, 1, 1), list[1]);   // CMYK(0,0,0,0)=白
        Assert.Equal(0.5, list[2].R, 3);
        Assert.Equal(0.5, list[2].G, 3);
    }

    [Fact]
    public void Ase_BadSignature_Throws()
    {
        Assert.Throws<InvalidDataException>(() => AseReader.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
    }

    [Fact]
    public void Ase_UnknownModel_Skipped()
    {
        var d = new List<byte> { (byte)'A', (byte)'S', (byte)'E', (byte)'F' };
        U16(d, 1); U16(d, 0);
        U32(d, 2);
        ColorBlock(d, "Spot", "LAB ", new[] { 50f, 0f, 0f });
        ColorBlock(d, "Blue", "RGB ", new[] { 0f, 0f, 1f });
        var list = AseReader.Read(d.ToArray());
        Assert.Single(list);
        Assert.Equal(new PaletteColor(0, 0, 1), list[0]);
    }

    // ---------- Harmony / profile / catalog ----------

    [Fact]
    public void Harmony_Complementary_HuePlus180()
    {
        var basis = new PaletteColor(1, 0, 0);
        var list = HarmonyOps.Build(basis, HarmonyKind.Complementary);
        Assert.Equal(2, list.Count);
        Assert.Equal(basis, list[0]);
        double h = new PickerHSB(list[1]).Hue;
        Assert.True(Math.Abs(h - 180) < 1.0, $"hue={h}");
    }

    [Fact]
    public void Harmony_Counts()
    {
        var basis = new PaletteColor(0, 0, 1);
        Assert.Equal(3, HarmonyOps.Build(basis, HarmonyKind.Analogous).Count);
        Assert.Equal(3, HarmonyOps.Build(basis, HarmonyKind.Triadic).Count);
        Assert.Equal(3, HarmonyOps.Build(basis, HarmonyKind.SplitComplementary).Count);
        Assert.Equal(4, HarmonyOps.Build(basis, HarmonyKind.Square).Count);
    }

    [Fact]
    public void ProfilePreview_SRGB_Identity_Gray_Luma()
    {
        var red = new PaletteColor(1, 0, 0);
        Assert.Equal(red, ProfilePreviewOps.Preview(red, PreviewProfile.SRGB));
        Assert.Equal(new PaletteColor(1, 1, 1), ProfilePreviewOps.Preview(new PaletteColor(1, 1, 1), PreviewProfile.Grayscale));
        Assert.Equal(new PaletteColor(0, 0, 0), ProfilePreviewOps.Preview(new PaletteColor(0, 0, 0), PreviewProfile.Grayscale));
        var g = ProfilePreviewOps.Preview(red, PreviewProfile.Grayscale);
        Assert.Equal(0.2126, g.R, 4);
        var gray = new PaletteColor(0.4, 0.4, 0.4);
        Assert.Equal(gray, ProfilePreviewOps.Preview(gray, PreviewProfile.DisplayP3));   // 無彩色は不変
        var p3 = ProfilePreviewOps.Preview(red, PreviewProfile.DisplayP3);
        Assert.True(double.IsFinite(p3.R) && p3.R >= 0 && p3.R <= 1);
    }

    [Fact]
    public void FilterCatalog_Search()
    {
        Assert.Equal(FilterCatalog.All.Length, FilterCatalog.Search("").Length);
        Assert.Equal(FilterCatalog.All.Length, FilterCatalog.Search("   ").Length);
        var curv = FilterCatalog.Search("curv");
        Assert.Single(curv);
        Assert.Equal("Curves", curv[0].Name);
        Assert.NotEmpty(FilterCatalog.Search("GRAIN"));   // 大小無視
        Assert.NotEmpty(FilterCatalog.Search("調整"));    // 日本語 category
        Assert.Empty(FilterCatalog.Search("zzz-no-such-filter"));
        Assert.NotEmpty(FilterCatalog.Search("blur"));
    }

    // ---------- sheet 対話の engine 組立 (dialog が作る設定の妥当性) ----------

    [Fact]
    public void Curves_MidBuild_Valid_And_PassesThroughNode()
    {
        var cs = new CurvesSettings();
        cs.Channels[0] = new List<CurvePoint> { new(0, 0), new(128, 200), new(255, 255) };
        Assert.True(cs.IsValid);
        Assert.Equal(200, cs.Value(128, 0), 3);
    }

    [Fact]
    public void Levels_DialogBuild_Valid()
    {
        var ls = new LevelsSettings();
        ls.Ranges[0] = new LevelRange(10, 1.5, 240, 0, 255).Normalized;
        Assert.Equal(4, ls.Ranges.Length);
        double v = ls.Apply(1.0, LevelsChannel.Red);
        Assert.True(v >= 0 && v <= 1, $"v={v}");
    }

    [Fact]
    public void AdjustmentLayer_Curves_Levels_Hue_AreValid()
    {
        var cs = new CurvesSettings();
        Assert.True(new LayerAdjustment { Kind = AdjustmentKind.Curves, Curves = cs }.IsValid);
        var ls = new LevelsSettings();
        Assert.True(new LayerAdjustment { Kind = AdjustmentKind.Levels, Levels = ls }.IsValid);
        var hs = new HueSaturationSettings(30, 10, -5);
        Assert.True(new LayerAdjustment { Kind = AdjustmentKind.Hsv, HueSat = hs }.IsValid);
    }

    [Fact]
    public void MultiSelect_MergePlan_UsesPickedIds()
    {
        var doc = new Document { Width = 8, Height = 8 };
        var a = new Layer { Name = "A", Bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul) };
        var b = new Layer { Name = "B", Bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul) };
        doc.Layers.Add(a); doc.Layers.Add(b);
        var plan = MergeOps.MergeSelectedPlan(doc, new[] { a.Id, b.Id });
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Ids.Count);
    }

    [Fact]
    public void E2E_Import_Edit_Export_Headless()
    {
        // Import: PNG bytes -> DecodeFile (実機 Import の engine 路)
        using var src = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        src.Erase(SKColors.White);
        using (var c = new SKCanvas(src))
        using (var paint = new SKPaint { Color = SKColors.Black })
            c.DrawRect(15, 0, 1, 32, paint);   // 中央縦線 (gap-closing 対象)
        string tmp = Path.Combine(Path.GetTempPath(), $"p0e2e_{Guid.NewGuid():N}.png");
        try
        {
            using (var img = SKImage.FromBitmap(src))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(tmp, data.ToArray());
            var dec = ImageImport.DecodeFile(tmp);
            Assert.False(dec.Unsupported, dec.Error);
            Assert.NotNull(dec.Bitmap);
            // 編集: 文書化 -> Curves 破壊 -> Hue 調整層 -> gap wand -> Solo
            var doc = new Document { Width = 32, Height = 32 };
            var layer = new Layer { Name = " imported", Bitmap = dec.Bitmap };
            doc.Layers.Add(layer);
            var cs = new CurvesSettings();
            cs.Channels[0] = new List<CurvePoint> { new(0, 0), new(128, 140), new(255, 255) };
            LayerFilters.ApplyCurves(layer, cs);
            doc.Layers.Add(new Layer { Name = "Hue 調整", IsAdjustmentLayer = true,
                Adjustment = new LayerAdjustment { Kind = AdjustmentKind.Hsv, HueSat = new HueSaturationSettings(10, 5, 0) } });
            int n = GapCloseOps.WandSelectGapClosed(doc, dec.Bitmap, new SKPoint(2, 16), 10, 0, true, SelectionMode.Replace, 2);
            Assert.True(n > 0);
            SoloOps.SetSolo(doc, layer.Id);
            Assert.True(LayerHierarchy.IsEffectivelyVisible(doc, layer));
            // Export: JPEG + 結合 PNG が復号できること
            byte[] jpeg = JpegExport.Export(doc, new JpegOptions { Quality = 0.85 });
            Assert.True(jpeg.Length > 100);
            using var back = SKBitmap.Decode(jpeg);
            Assert.NotNull(back);
            Assert.Equal(32, back.Width);
            byte[] merged = JpegExport.CopyMergedPng(doc);
            Assert.True(merged.Length > 100);
            SoloOps.Clear(doc);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
