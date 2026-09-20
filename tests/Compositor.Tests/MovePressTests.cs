using Compositor;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

/// <summary>t_04a6dbab: 選択中の層ごと移動の不具合の回帰試験。
/// 押下振分け (MovePress.Classify) の単体試験で再現・保証する:
/// 枠内押→FrameDrag (dragLayer未設定・層位置不変に対応)、Hand押→Pan (層不変)、
/// 枠外押→LayerDrag (仕様として文書化)。枠境界の包含 (inclusive) も保証する。</summary>
public class MovePressTests
{
    // --- routing: Hand は常に Pan (層・選択・Undoに不干渉。MacのHandは移動しない) ---

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void HandPress_AlwaysPans(bool hasSelection, bool insideSelection)
    {
        Assert.Equal(MovePressKind.Pan,
            MovePress.Classify(isMoveTool: false, isHandTool: true,
                hasSelection, insideSelection,
                cutOrCopyKeys: false, duplicateKey: false));
    }

    [Fact]
    public void HandPress_WithModifiers_StillPans()
    {
        Assert.Equal(MovePressKind.Pan,
            MovePress.Classify(false, true, true, true, true, true));
    }

    // --- routing: Move ---

    [Fact]
    public void MovePress_InsideWithoutModifiers_FrameDrag()
    {
        Assert.Equal(MovePressKind.FrameDrag,
            MovePress.Classify(true, false, true, true, false, false));
    }

    [Fact]
    public void MovePress_InsideWithCtrl_PixelMove()
    {
        Assert.Equal(MovePressKind.PixelMove,
            MovePress.Classify(true, false, true, true, true, false));
    }

    [Theory]
    [InlineData(true, true)]    // Alt単独
    [InlineData(true, false)]   // duplicateKey のみでも複写 (handlerはAltをcutKeysに含める)
    public void MovePress_InsideWithAlt_PixelDuplicate(bool cutKeys, bool _)
    {
        Assert.Equal(MovePressKind.PixelDuplicate,
            MovePress.Classify(true, false, true, true, cutKeys, true));
    }

    [Fact]
    public void MovePress_OutsideWithSelection_LayerDrag_SelectionKept()
    {
        // 枠外押の仕様: 選択は保持したまま最前面層を掴む (Mac/Photoshop準拠・SHORTCUTS.mdに文書化)
        Assert.Equal(MovePressKind.LayerDrag,
            MovePress.Classify(true, false, true, false, false, false));
    }

    [Fact]
    public void MovePress_NoSelection_LayerDrag()
    {
        Assert.Equal(MovePressKind.LayerDrag,
            MovePress.Classify(true, false, false, false, false, false));
    }

    [Fact]
    public void OtherTools_NotRouted()
    {
        Assert.Equal(MovePressKind.None,
            MovePress.Classify(false, false, true, true, false, false));
    }

    // --- 枠境界: 矩形選択の右端・下端は枠内 (SKRect.Contains は右/下端を除外するため inclusive 判定) ---

    static Document RectDoc()
    {
        var doc = new Document { Width = 100, Height = 100 };
        SelectionTools.SetRectSelection(doc, new SKRect(10, 10, 50, 50),
            SelectionMode.Replace, SelectionKind.Rectangle);
        return doc;
    }

    [Theory]
    [InlineData(30, 30)]   // 中央
    [InlineData(10, 30)]   // 左端
    [InlineData(30, 10)]   // 上端
    [InlineData(50, 30)]   // 右端 (SKRect.Contains では false になる)
    [InlineData(30, 50)]   // 下端 (SKRect.Contains では false になる)
    [InlineData(50, 50)]   // 右下角
    [InlineData(10, 10)]   // 左上角
    public void RectSelection_EdgeIsInside(float x, float y)
    {
        Assert.True(RectDoc().InsideSelection(new SKPoint(x, y)));
    }

    [Theory]
    [InlineData(9.9f, 30)]
    [InlineData(50.1f, 30)]
    [InlineData(30, 9.9f)]
    [InlineData(30, 50.1f)]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    public void RectSelection_OutsideIsOutside(float x, float y)
    {
        Assert.False(RectDoc().InsideSelection(new SKPoint(x, y)));
    }
}
