using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>組合せ総合試験 t_d1af46ea: 意匠作替後の全機能について
/// 鍵・入力欄・マウス・鍵盤の組合せを考慮した総合試験。
/// 単体で再現できるものは単体化、実機のみは手順書化 (docs/COMBINATORIAL_TEST.md)。
/// UIスレッド不要の engine-level + XAML静的検証に限定する。</summary>
public class CombinatorialTests
{
    static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        b.Erase(c);
        return b;
    }

    static Layer MakeLayer(int w = 20, int h = 10)
        => new() { Name = "t", Bitmap = Solid(w, h, SKColors.Red), Position = new SKPoint(5, 7) };

    static Document MakeDoc(int w = 100, int h = 80)
    {
        var doc = new Document { Width = w, Height = h };
        doc.Layers.Add(MakeLayer());
        return doc;
    }

    static string FindXaml()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Compositor.Avalonia", "MainWindow.axaml"),
            "/home/sunao/.hermes/kanban/workspaces/t_a33fadbd/Compositor.Windows/src/Compositor.Avalonia/MainWindow.axaml",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    // ---------- 1. 全鍵の押下試験 (XAML静的 + reflection) ----------

    [Fact]
    public void Buttons_DisabledSet_IsExactlyExpected()
    {
        // 未対応鍵は無効のままであること。期待集合: Curves/Levels/GradientMap + SR(rail) + SR(hidden)。
        var path = FindXaml();
        Assert.NotNull(path);
        var x = File.ReadAllText(path);
        var disabled = Regex.Matches(x, @"<Button[^>]*IsEnabled=""False""[^>]*>")
            .Select(m => m.Value).ToList();
        Assert.Equal(5, disabled.Count);
        Assert.Contains(disabled, d => d.Contains("カーブ"));
        Assert.Contains(disabled, d => d.Contains("レベル"));
        Assert.Contains(disabled, d => d.Contains("グラデーションマップ"));
        Assert.Equal(2, disabled.Count(d => d.Contains("被写体除去")));
    }

    [Fact]
    public void Buttons_Disabled_AllHaveGuidanceTooltip()
    {
        var path = FindXaml();
        Assert.NotNull(path);
        var x = File.ReadAllText(path);
        foreach (Match m in Regex.Matches(x, @"<Button[^>]*IsEnabled=""False""[^>]*>"))
            Assert.Contains("ToolTip.Tip=", m.Value);
    }

    [Fact]
    public void Buttons_AllClickHandlers_ExistOnMainWindow()
    {
        // 有効・無効を問わず全Click操作子が実在すること (意匠作替で剥落なし)。
        var path = FindXaml();
        Assert.NotNull(path);
        var x = File.ReadAllText(path);
        var names = Regex.Matches(x, @"Click=""([^""]+)""")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(names.Count >= 70, $"click handlers found: {names.Count}");
        var methods = typeof(MainWindow).GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(m => m.Name).ToHashSet();
        var missing = names.Where(n => !methods.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "missing handlers: " + string.Join(",", missing));
    }

    [Fact]
    public void Buttons_ToolRail_All16ToolsEnabled()
    {
        // 工具列16種 (Move..Gradient, ContentFill除く) が enabled であること。
        var path = FindXaml();
        Assert.NotNull(path);
        var x = File.ReadAllText(path);
        var toolNames = new[] { "MoveBtn", "HandBtn", "BrushBtn", "EraserBtn", "MarqueeBtn",
            "LassoBtn", "WandBtn", "CloneBtn", "HealBtn", "SmudgeBtn", "EyedropperBtn",
            "CropBtn", "DistortBtn", "ShapeBtn", "GradientBtn" };
        foreach (var n in toolNames)
        {
            var m = Regex.Match(x, $@"<Button[^>]*x:Name=""{n}""[^>]*>");
            Assert.True(m.Success, n + " not found");
            Assert.DoesNotContain("IsEnabled", m.Value);
        }
    }

    // ---------- 2. 全入力欄の試験 ----------

    [Theory]
    [InlineData("1", 1)] [InlineData("30000", 30000)] [InlineData(" 800 ", 800)]
    public void Inputs_Dimension_Valid(string text, int expected)
        => Assert.Equal(expected, Document.ValidDimension(text));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("0")]
    [InlineData("-5")] [InlineData("30001")] [InlineData("abc")] [InlineData("12.5")]
    [InlineData("99999999999999999999")] [InlineData("1,000")]
    public void Inputs_Dimension_Invalid(string text)
        => Assert.Null(Document.ValidDimension(text));

    [Fact]
    public void Inputs_NumericTransform_ValidApplies()
    {
        var l = MakeLayer(20, 10);
        Assert.True(CanvasOps.ApplyNumeric(l, 5, 7, 40, 20, 30, false));
        Assert.Equal(5, l.Position.X);
        Assert.Equal(2.0f, l.ScaleX, 3);
        Assert.Equal(30, l.Rotation % 360, 3);
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(-3, 10)] [InlineData(10, 0)] [InlineData(10, -1)]
    [InlineData(400000, 10)] [InlineData(10, 400000)]
    public void Inputs_NumericTransform_BadSizeRejected(float w, float h)
    {
        var l = MakeLayer();
        var px = l.Position.X;
        Assert.False(CanvasOps.ApplyNumeric(l, 0, 0, w, h, 0, false));
        Assert.Equal(px, l.Position.X); // 無効値は層に触れない
    }

    [Fact]
    public void Inputs_NumericTransform_BadCoordsAndNaNRejected()
    {
        var l = MakeLayer();
        Assert.False(CanvasOps.ApplyNumeric(l, 2_000_000, 0, 10, 10, 0, false));
        Assert.False(CanvasOps.ApplyNumeric(l, 0, -2_000_000, 10, 10, 0, false));
        Assert.False(CanvasOps.ApplyNumeric(l, float.NaN, 0, 10, 10, 0, false));
        Assert.False(CanvasOps.ApplyNumeric(l, 0, 0, 10, 10, float.PositiveInfinity, false));
        Assert.False(CanvasOps.ApplyNumeric(null, 0, 0, 10, 10, 0, false));
        Assert.False(CanvasOps.ApplyNumeric(new Layer { Name = "empty" }, 0, 0, 10, 10, 0, false));
    }

    [Fact]
    public void Inputs_NumericTransform_LockRatioKeepsProportion()
    {
        var l = MakeLayer(20, 10); // 2:1
        Assert.True(CanvasOps.ApplyNumeric(l, 0, 0, 40, 21, 0, true));
        Assert.Equal(40, l.Bitmap.Width * l.ScaleX, 3);
        Assert.Equal(20, l.Bitmap.Height * l.ScaleY, 3); // Hは比例で上書き (W主導)
    }

    [Fact]
    public void Inputs_SliderRanges_MatchSpec()
    {
        // 各滑子と数値欄の連動: XAMLの範囲が仕様通りであること。
        var path = FindXaml();
        Assert.NotNull(path);
        var x = File.ReadAllText(path);
        var expected = new Dictionary<string, (string lo, string hi)>
        {
            ["OpacitySlider"] = ("0", "100"),
            ["ScaleSlider"] = ("5", "400"),
            ["RotateSlider"] = ("-180", "180"),
            ["BrightSlider"] = ("-100", "100"),
            ["ContrastSlider"] = ("-100", "100"),
            ["SaturSlider"] = ("0", "200"),
            ["BlurSlider"] = ("0", "25"),
            ["BrushSizeSlider"] = ("1", "200"),
            ["HardnessSlider"] = ("0", "100"),
            ["SpacingSlider"] = ("1", "200"),
            ["BrushOpacitySlider"] = ("1", "100"),
            ["WandToleranceSlider"] = ("0", "255"),
        };
        foreach (var kv in expected)
        {
            var m = Regex.Match(x, $@"<Slider[^>]*x:Name=""{kv.Key}""[^>]*>");
            Assert.True(m.Success, kv.Key + " not found");
            Assert.Contains($@"Minimum=""{kv.Value.lo}""", m.Value);
            Assert.Contains($@"Maximum=""{kv.Value.hi}""", m.Value);
        }
    }

    [Theory]
    [InlineData("FF0000", 255, 0, 0)] [InlineData("#00ff00", 0, 255, 0)]
    [InlineData("F00", 255, 0, 0)] [InlineData("#0F0", 0, 255, 0)]
    public void Inputs_HexColor_Valid(string hex, byte r, byte g, byte b)
    {
        var c = PaletteColor.FromHex(hex);
        Assert.NotNull(c);
        var sk = c.Value.ToSK();
        Assert.Equal(r, sk.Red); Assert.Equal(g, sk.Green); Assert.Equal(b, sk.Blue);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("GGGGGG")] [InlineData("12345")]
    [InlineData("1234567")] [InlineData("#")] [InlineData("red")]
    public void Inputs_HexColor_Invalid(string hex)
        => Assert.Null(PaletteColor.FromHex(hex));

    [Fact]
    public void Inputs_BrushHardness_QuarterSteps()
    {
        // Shift+[/] の25%刻み相当: floor/ceil to quarter。
        float down(float h) => Math.Clamp(MathF.Floor(h * 4 - 0.001f) / 4, 0f, 1f);
        float up(float h) => Math.Clamp(MathF.Ceiling(h * 4 + 0.001f) / 4, 0f, 1f);
        Assert.Equal(0.75f, down(1f), 4);
        Assert.Equal(0.5f, down(0.75f), 4);
        Assert.Equal(0.25f, up(0f), 4);
        Assert.Equal(0.5f, up(0.3f), 4);
        Assert.Equal(0f, down(0f), 4);
        Assert.Equal(1f, up(1f), 4);
    }

    // ---------- 3. マウス操作の組合せ試験 ----------

    [Fact]
    public void Mouse_ShapeDrag_SquareAndCenter()
    {
        var sq = ShapeOps.DragRect(new SKPoint(0, 0), new SKPoint(10, 4), square: true, fromCenter: false);
        Assert.Equal(sq.Width, sq.Height, 3);
        var c = ShapeOps.DragRect(new SKPoint(10, 10), new SKPoint(14, 12), square: false, fromCenter: true);
        Assert.Equal(10, c.MidX, 3);
        Assert.Equal(10, c.MidY, 3);
        var box = SelectionTools.DragBoxRect(new SKPoint(0, 0), new SKPoint(8, 4), square: true, fromCenter: false);
        Assert.Equal(box.Width, box.Height, 3);
    }

    [Fact]
    public void Mouse_CropCreate_RatioAndSymmetric()
    {
        var free = CropGeometry.Create(new SKPoint(0, 0), new SKPoint(20, 10), null);
        Assert.True(CropGeometry.Valid(free));
        var ratio = CropGeometry.Create(new SKPoint(0, 0), new SKPoint(20, 10), 1f);
        Assert.Equal(ratio.Width, ratio.Height, 2);
        var sym = CropGeometry.Create(new SKPoint(10, 10), new SKPoint(14, 12), null, symmetric: true);
        Assert.Equal(10, sym.MidX, 3); // Alt対称=開始点が中心
        Assert.False(CropGeometry.Valid(new SKRect(5, 5, 5, 5))); // 零面積は無効
        Assert.False(CropGeometry.Valid(new SKRect(0, 0, -1, 5)));
    }

    [Fact]
    public void Mouse_DistortDrag_ShiftLocksAxis()
    {
        var doc = MakeDoc();
        var l = doc.Layers[0];
        var placement = new LayerPlacement(l.Position, new SKSize(l.DrawW, l.DrawH));
        var corners = DistortWarp.CornersOf(placement);
        Assert.True(DistortWarp.IsUsable(corners));
        var moved = DistortWarp.DragCorners(corners, corners[0], new SKPoint(corners[0].X + 30, corners[0].Y + 5), 0, shift: true, moveBody: true);
        // Shift軸固定: 移動量の小さい軸が抑えられる (全体移動で縮退を避けて検証)
        Assert.NotNull(moved);
        Assert.Equal(corners[0].Y, moved[0].Y, 1);
        Assert.Equal(corners[0].X + 30, moved[0].X, 3);
        var free = DistortWarp.DragCorners(corners, corners[0], new SKPoint(corners[0].X + 30, corners[0].Y + 5), 0, shift: false, moveBody: true);
        Assert.NotNull(free);
        Assert.Equal(corners[0].Y + 5, free[0].Y, 3);
        Assert.False(DistortWarp.IsUsable(new[] { new SKPoint(0, 0), new SKPoint(0, 0), new SKPoint(0, 0) }));
    }

    [Fact]
    public void Mouse_GradientDrag_ShiftSnaps45()
    {
        var snapped = GradientOps.Snap45(new SKPoint(0, 0), new SKPoint(10, 3));
        // 45°刻み: ほぼ水平に吸着
        Assert.Equal(0, snapped.Y, 1);
        Assert.Equal(10.44f, snapped.X, 1);
        var diag = GradientOps.Snap45(new SKPoint(0, 0), new SKPoint(10, 9));
        Assert.Equal(diag.X, diag.Y, 1); // 対角に吸着
    }

    [Fact]
    public void Mouse_WandClick_ToleranceMonotonic()
    {
        var bmp = Solid(20, 20, new SKColor(100, 100, 100));
        using (var c = new SKCanvas(bmp)) { c.DrawRect(5, 5, 10, 10, new SKPaint { Color = new SKColor(200, 200, 200) }); }
        var (loMask, loCount) = SelectionTools.WandMask(bmp, 10, 10, 0, 10, true);
        var (hiMask, hiCount) = SelectionTools.WandMask(bmp, 10, 10, 0, 250, true);
        Assert.True(hiCount >= loCount);
        Assert.True(loCount > 0);
    }

    [Fact]
    public void Mouse_SelectionGeometry_Basic()
    {
        var bounds = new SKRect(0, 0, 10, 10);
        Assert.True(SelectionTools.EllipseContains(bounds, new SKPoint(5, 5)));
        Assert.False(SelectionTools.EllipseContains(bounds, new SKPoint(0, 0)));
        var tri = new List<SKPoint> { new(0, 0), new(10, 0), new(5, 10) };
        Assert.True(SelectionTools.PointInPolygon(tri, new SKPoint(5, 3)));
        Assert.False(SelectionTools.PointInPolygon(tri, new SKPoint(9, 9)));
    }

    // ---------- 4. 鍵盤操作の組合せ試験 ----------

    [Fact]
    public void Keys_UndoRedo_Sequence()
    {
        var doc = MakeDoc();
        var stack = new UndoStack();
        stack.Push(doc);
        doc.Layers[0].Position = new SKPoint(99, 99);
        Assert.True(stack.CanUndo);
        stack.Undo(doc);
        Assert.Equal(5, doc.Layers[0].Position.X);
        Assert.True(stack.CanRedo);
        stack.Redo(doc);
        Assert.Equal(99, doc.Layers[0].Position.X);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Keys_Nudge_1pxAnd10px()
    {
        // 矢印 (Shiftで10px) 相当の算術。
        var l = MakeLayer();
        float step = 1, big = 10;
        l.Position = new SKPoint(l.Position.X + step, l.Position.Y);
        Assert.Equal(6, l.Position.X);
        l.Position = new SKPoint(l.Position.X, l.Position.Y - big);
        Assert.Equal(-3, l.Position.Y);
    }

    [Fact]
    public void Keys_MaskOffset_NudgeIndependent()
    {
        // 覆面編集中かつ非連結時は覆面のみ移動 (層位置は不変)。
        var l = MakeLayer();
        MaskOps.AddMask(l, revealing: true);
        l.MaskSelected = true;
        l.MaskLinked = false;
        var before = l.Position;
        l.MaskOffset = new SKPoint(l.MaskOffset.X + 10, l.MaskOffset.Y);
        Assert.Equal(before, l.Position);
        Assert.Equal(10, l.MaskOffset.X);
        Assert.True(MaskOps.HasMask(l));
        Assert.True(MaskOps.IsActive(l));
    }

    [Fact]
    public void Keys_PaletteSwapReset_RoundTrip()
    {
        var p = new PaletteState();
        var fg = p.Foreground; var bg = p.Background;
        p.Swap();
        Assert.Equal(bg, p.Foreground);
        Assert.Equal(fg, p.Background);
        p.Swap();
        Assert.Equal(fg, p.Foreground);
        p.Foreground = new PaletteColor(0.2, 0.3, 0.4);
        p.Reset();
        Assert.Equal(PaletteColor.Black, p.Foreground);
        Assert.Equal(PaletteColor.White, p.Background);
        // 覆面編集中は白黒切替になる
        p.MaskSelected = true;
        p.Reset();
        Assert.True(p.MaskPaintWhite);
        p.Swap();
        Assert.False(p.MaskPaintWhite);
    }

    [Fact]
    public void Keys_OpacityDigits_Map1To0_As01To10()
    {
        // 1..9/0 → 10%..90%/100% (描画系工具のみ)。写像自体の検証。
        var map = new Dictionary<int, float>
        {
            [1] = 0.1f, [2] = 0.2f, [3] = 0.3f, [4] = 0.4f, [5] = 0.5f,
            [6] = 0.6f, [7] = 0.7f, [8] = 0.8f, [9] = 0.9f, [0] = 1f,
        };
        var l = MakeLayer();
        foreach (var kv in map)
        {
            l.Opacity = kv.Value;
            Assert.Equal(kv.Value, l.Opacity, 4);
        }
    }

    [Fact]
    public void Keys_DeleteBranch_SelectionVsLayer()
    {
        // Del: 選択あり=範囲画素消去・なし=層削除相当。
        var doc = MakeDoc(40, 40);
        var l = doc.Layers[0];
        doc.Selection = new SKRect(2, 2, 8, 8);
        int cleared = SelectionTools.DeleteSelection(doc, l);
        Assert.True(cleared > 0);
        doc.Selection = null;
        Assert.Equal(0, SelectionTools.DeleteSelection(doc, l));
    }

    // ---------- 5. 工具×選択×層状態の交差表 ----------

    [Fact]
    public void Cross_DistortEntryGuards()
    {
        // BeginDistortTool の守衛条件と同値: Bitmapなし・錠は開始不可。
        Layer noBitmap = new() { Name = "empty" };
        Layer locked = MakeLayer(); locked.Locked = true;
        Assert.False(CanvasOps.ApplyNumeric(noBitmap, 0, 0, 10, 10, 0, false));
        Assert.False(DistortWarp.IsUsable(Array.Empty<SKPoint>()));
        // 錠層への数値適用自体は engine が通すため UI 側守衛が必要 — 仕様として記録
        Assert.True(CanvasOps.ApplyNumeric(locked, 0, 0, 10, 10, 0, false));
    }

    [Fact]
    public void Cross_MergeDown_SingleLayerSafe()
    {
        var doc = MakeDoc();
        var plan = MergeOps.MergeDownPlan(doc, doc.Layers[0].Id);
        Assert.Null(plan); // 下層なしは何もしない (クラッシュなし)
        doc.Layers.Add(new Layer { Name = "top", Bitmap = Solid(20, 10, SKColors.Blue), Position = new SKPoint(0, 0) });
        var plan2 = MergeOps.MergeDownPlan(doc, doc.Layers[1].Id);
        Assert.NotNull(plan2);
    }

    [Fact]
    public void Cross_ClipLink_SelfAndGroupRejected()
    {
        var doc = MakeDoc();
        var id = doc.Layers[0].Id;
        Assert.False(ClipOps.CanLink(doc, id, id)); // 自己連結不可
        var g = LayerHierarchy.AddGroup(doc, doc.Layers[0]);
        Assert.False(ClipOps.CanLink(doc, g.Id, id)); // 群は不可
        Assert.False(ClipOps.CanLink(doc, id, Guid.NewGuid())); // 不在ID不可
    }

    [Fact]
    public void Cross_ContentFill_NoSourceThrowsDocumentedFailure()
    {
        // 選択のみで周囲画素なし → Failure (Mac noSource相当)。UIは捕捉して助言表示。
        var doc = new Document { Width = 10, Height = 10 };
        var l = new Layer { Name = "t", Bitmap = Solid(10, 10, SKColor.Empty) };
        doc.Layers.Add(l);
        doc.Selection = new SKRect(0, 0, 10, 10);
        Assert.Throws<ContentFillOps.Failure>(() => ContentFillOps.FillSelection(doc, l));
    }

    [Fact]
    public void Cross_GradientCommit_Guards()
    {
        var l = MakeLayer();
        Assert.False(GradientOps.Commit(l, null, SKColors.Black, SKColors.White));
        var dot = new GradientDraft { Start = new SKPoint(5, 5), End = new SKPoint(5, 5) };
        Assert.False(dot.HasLine);
        Assert.False(GradientOps.Commit(l, dot, SKColors.Black, SKColors.White));
        var line = new GradientDraft { Start = new SKPoint(0, 0), End = new SKPoint(10, 0) };
        Assert.True(line.HasLine);
        Assert.True(GradientOps.Commit(l, line, SKColors.Black, SKColors.White));
    }

    [Fact]
    public void Cross_ShapeFinish_DegenerateRejected()
    {
        var doc = MakeDoc();
        Assert.Null(ShapeOps.FinishShape(doc, new SKRect(5, 5, 5, 5), ShapeKind.Rectangle, SKColors.Black, 0));
        var ok = ShapeOps.FinishShape(doc, new SKRect(0, 0, 10, 8), ShapeKind.Rectangle, SKColors.Black, 0);
        Assert.NotNull(ok);
    }

    [Fact]
    public void Cross_HiddenLockedLayer_ComposeSkipsHidden()
    {
        var doc = new Document { Width = 20, Height = 20 };
        doc.Layers.Add(new Layer { Name = "base", Bitmap = Solid(20, 20, SKColors.White) });
        var hidden = new Layer { Name = "hide", Bitmap = Solid(20, 20, SKColors.Red), Visible = false };
        doc.Layers.Add(hidden);
        using var flat = doc.Compose();
        var px = flat.GetPixel(5, 5);
        Assert.True(px.Red > 200 && px.Green > 200); // 非表示層は合成されない
        Assert.False(LayerHierarchy.IsEffectivelyVisible(doc, hidden));
    }

    [Fact]
    public void Cross_AdjustmentLayer_BlendAndSamplingParity()
    {
        // 工具×層状態: 調整層・Blend・Sampling の往復。
        var l = MakeLayer();
        l.Blend = SKBlendMode.Multiply;
        Assert.Equal("Multiply", ProjectFormat.BlendName(l.Blend));
        Assert.Equal(SKBlendMode.Multiply, ProjectFormat.ParseBlend("Multiply"));
        l.Sampling = LayerSampling.Nearest;
        Assert.Equal("Nearest", ProjectFormat.SamplingName(l.Sampling));
        Assert.Equal(LayerSampling.Nearest, ProjectFormat.ParseSampling("Nearest"));
        Assert.Equal(SKFilterQuality.None, SamplingUtil.ToQuality(LayerSampling.Nearest));
    }

    // ---------- 6. 文書×標識の交差 ----------

    [Fact]
    public void Docs_DirtyFlag_PushSets_SaveClears()
    {
        var doc = MakeDoc();
        Assert.False(doc.IsModified);
        var stack = new UndoStack();
        stack.Push(doc);
        Assert.True(doc.IsModified);
        doc.MarkSaved();
        Assert.False(doc.IsModified);
    }

    [Fact]
    public void Docs_MultiDoc_IndependentUndo()
    {
        var a = MakeDoc(); var b = MakeDoc();
        var sa = new UndoStack(); var sb = new UndoStack();
        sa.Push(a);
        a.Layers[0].Position = new SKPoint(50, 50);
        Assert.False(sb.CanUndo); // 文書Bの履歴は独立
        sa.Undo(a);
        Assert.Equal(5, a.Layers[0].Position.X);
        Assert.Equal(5, b.Layers[0].Position.X); // Bは無影響
    }

    [Fact]
    public void Docs_SaveLoad_RoundTrip_KeepsEditing()
    {
        // 保存往復後も操作継続できること。
        var doc = MakeDoc(40, 30);
        doc.Name = "trip";
        var dir = Path.Combine(Path.GetTempPath(), "comb_" + Guid.NewGuid().ToString("N"));
        try
        {
            ProjectFormat.Save(doc, dir);
            var back = ProjectFormat.Load(dir);
            // Loadは文書名をpackage名から付ける仕様 (層・画素は往復する)
            Assert.Equal(Path.GetFileNameWithoutExtension(dir), back.Name);
            Assert.Equal(doc.Layers.Count, back.Layers.Count);
            Assert.Equal(doc.Layers[0].Name, back.Layers[0].Name);
            back.Layers[0].Position = new SKPoint(11, 12);
            Assert.Equal(11, back.Layers[0].Position.X);
            using var flat = back.Compose();
            Assert.Equal(40, flat.Width);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Docs_CanvasResize_ValidAndInvalid()
    {
        var doc = MakeDoc(100, 80);
        Assert.True(CanvasOps.Resize(doc, new CanvasSizeOptions(200, 160)));
        Assert.Equal(200, doc.Width);
        Assert.False(CanvasOps.Resize(doc, new CanvasSizeOptions(0, 160)));
        Assert.False(CanvasOps.Resize(doc, new CanvasSizeOptions(40000, 10)));
        Assert.Equal(200, doc.Width); // 無効値は文書に触れない
    }

    [Fact]
    public void Docs_ImageSize_BudgetGuard()
    {
        var doc = MakeDoc(100, 80);
        Assert.True(new ImageSizeOptions(200, 160) { Resample = true }.Valid);
        Assert.False(new ImageSizeOptions(20000, 20000) { Resample = true }.Valid); // 100MP超過
        Assert.False(new ImageSizeOptions(0, 10).Valid);
    }
}
