using System.IO;
using System.Linq;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_fec99135: 専門製品意匠への作替え回帰 (XAML静的検証).
/// 機能変更なしを担保: x:Name・操作子名の不変、未対応鍵の無効+助言維持、画像化の維持。</summary>
public class ThemeRegressionTests
{
    static string XamlPath()
    {
        // tests/Compositor.Tests -> src/Compositor.Avalonia/MainWindow.axaml
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TOOLS_PARITY.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var p = Path.Combine(dir.FullName, "src", "Compositor.Avalonia", "MainWindow.axaml");
        Assert.True(File.Exists(p), $"MainWindow.axaml not found: {p}");
        return p;
    }

    static string Xaml() => File.ReadAllText(XamlPath());

    // 工具列の文字鍵 (1文字 Content) が残っていないこと。状態表示鍵 (ShapeKind/MarqueeShape/LassoKind) は除外。
    [Fact]
    public void ToolRail_UsesPathIcon_NotSingleLetterContent()
    {
        var x = Xaml();
        foreach (var n in new[] { "MoveBtn", "HandBtn", "BrushBtn", "EraserBtn", "MarqueeBtn",
                 "LassoBtn", "WandBtn", "CloneBtn", "HealBtn", "SmudgeBtn",
                 "EyedropperBtn", "CropBtn", "DistortBtn", "ShapeBtn", "GradientBtn" })
        {
            int i = x.IndexOf($"x:Name=\"{n}\"");
            Assert.True(i >= 0, $"missing {n}");
            // 同一 Button 要素内 (~1500字以内) に PathIcon があること
            int end = x.IndexOf("</Button>", i);
            Assert.True(end > i, $"unclosed {n}");
            Assert.Contains("<PathIcon", x.Substring(i, end - i));
        }
        // 1文字 Content の文字鍵が残存しないこと
        Assert.DoesNotContain("Content=\"M\"", x);
        Assert.DoesNotContain("Content=\"B\"", x);
        Assert.DoesNotContain("Content=\"F\"", x);
    }

    // 未対応鍵は画像+無効+助言を維持し、削除されていないこと
    [Fact]
    public void UnimplementedKeys_StayDisabledWithTooltip()
    {
        var x = Xaml();
        Assert.Contains("IsEnabled=\"False\"", x);
        Assert.Contains("Mac版の被写体除去(Vision)。Windows版では未対応です。", x);
        Assert.Contains("Mac版のカーブ調整。Windows版では未対応です。", x);
        Assert.Contains("Mac版のレベル調整。Windows版では未対応です。", x);
        Assert.Contains("Mac版のグラデーションマップ。Windows版では未対応です。", x);
        Assert.Contains("x:Name=\"SubjectRemovalBtn\"", x); // 不可視の予備も削除禁止
    }

    // 暗色題材の統一仕様が存在すること
    [Fact]
    public void DarkTheme_SpecUnified()
    {
        var x = Xaml();
        foreach (var c in new[] { "#1E1E1E", "#252526", "#2D2D30", "#3E3E42",
                 "#E8E8E8", "#9D9D9D", "#094771", "#007ACC" })
            Assert.Contains(c, x);
        Assert.DoesNotContain("Foreground=\"Gray\"", x); // 未統一の Gray 残存なし
    }

    // x:Name 87件が全て健在であること (後続試験工程のため)
    [Fact]
    public void AllXNames_Preserved()
    {
        var x = Xaml();
        foreach (var n in new[] { "Root", "DocTabs", "DocNameBox", "ZoomInBtn", "ZoomOutBtn",
                 "ZoomLabel", "UndoBtn", "RedoBtn", "CopyBtn", "PasteBtn",
                 "ToolHeaderTitle", "ToolHeaderHint", "CanvasScroll", "CanvasImage",
                 "MoveBtn", "HandBtn", "BrushBtn", "EraserBtn", "MarqueeBtn", "LassoBtn",
                 "WandBtn", "CloneBtn", "HealBtn", "SmudgeBtn", "EyedropperBtn", "CropBtn",
                 "DistortBtn", "ShapeBtn", "GradientBtn", "LayerList", "LayerCountLabel",
                 "VisibleCheck", "LockCheck", "RenameBox", "BlendBox", "OpacitySlider",
                 "OpacityLabel", "ScaleSlider", "RotateSlider", "RotateLabel", "LockRatioCheck",
                 "TxX", "TxY", "TxW", "TxH", "TxAngle", "SamplingBox", "DistortApplyBtn",
                 "DistortCancelBtn", "CropRatioBox", "CropApplyBtn", "CropCancelBtn",
                 "BrightSlider", "BrightLabel", "ContrastSlider", "ContrastLabel",
                 "SaturSlider", "SaturLabel", "BlurSlider", "BlurLabel", "BrushSizeSlider",
                 "BrushColorBox", "FgBtn", "BgBtn", "PaletteBox", "HardnessSlider",
                 "HardnessLabel", "SpacingSlider", "SpacingLabel", "BrushOpacitySlider",
                 "BrushOpacityLabel", "CloneAlignedCheck", "CloneSampleBox", "SmudgeModeBox",
                 "HealModeBox", "EyedropAllCheck", "MarqueeShapeBtn", "LassoKindBtn",
                 "SelModeBox", "WandToleranceSlider", "WandContiguousCheck",
                 "WandSampleAllCheck", "ShapeKindBtn", "StatusZoom", "StatusDoc",
                 "StatusHint", "SubjectRemovalBtn" })
            Assert.Contains($"x:Name=\"{n}\"", x);
    }

    // 主要操作子が全て健在であること
    [Fact]
    public void AllHandlers_Preserved()
    {
        var x = Xaml();
        foreach (var h in new[] { "OnToolMove", "OnToolHand", "OnToolBrush", "OnToolEraser",
                 "OnToolMarquee", "OnToolLasso", "OnToolWand", "OnToolClone", "OnToolHeal",
                 "OnToolSmudge", "OnToolEyedropper", "OnToolCrop", "OnToolDistort",
                 "OnToolShape", "OnToolGradient", "OnContentFill", "OnLayerSelected",
                 "OnAddLayer", "OnDeleteLayer", "OnUp", "OnDown", "OnDuplicateLayer",
                 "OnMergeDown", "OnMergeGroup", "OnUndo", "OnRedo", "OnCopy", "OnPaste",
                 "OnSave", "OnSaveAs", "OnOpenProject", "OnNewCanvas", "OnImport",
                 "OnExport", "OnExportJpeg", "OnCopyMerged", "OnDeselect",
                 "OnDeleteSelection", "OnCanvasPointerPressed", "OnCanvasPointerMoved",
                 "OnCanvasPointerReleased" })
            Assert.Contains(h, x);
    }

    // 絵文字・代替文字化けの混入がないこと (サロゲートペア・私用領域の検出)
    [Fact]
    public void NoEmojiOrMojibake()
    {
        var x = Xaml();
        var bad = x.Where(c => (c >= 0xD800 && c <= 0xDFFF) || (c >= 0xE000 && c <= 0xF8FF)
                            || c == 0xFFFD).Take(5).ToList();
        Assert.True(bad.Count == 0, $"emoji/mojibake chars: {string.Join(",", bad.Select(c => ((int)c).ToString("X4")))}");
    }
}
