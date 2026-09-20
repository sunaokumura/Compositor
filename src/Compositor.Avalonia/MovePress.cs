// Compositor — Move/Hand press routing (pure logic, unit-tested in MovePressTests).
// Single source of truth for OnCanvasPointerPressed's Move/Hand branch:
// which drag session a press starts. The UI handler only executes the decision
// (frame drag / pixel move / layer drag / pan) so the routing is simulatable
// without Avalonia controls.
namespace Compositor;

/// <summary>Press outcome for the Move/Hand tools (Mac parity: Hand pans only,
// Move drags the selection frame inside, cuts/copies pixels with Ctrl/Alt,
// grabs the topmost layer outside while keeping the selection).</summary>
public enum MovePressKind
{
    None,           // Move/Hand以外の工具: 本分岐の対象外
    Pan,            // Hand: 画面移動のみ (層・選択・Undoに不干渉)
    FrameDrag,      // Move+枠内(修飾なし): 選択枠のみ移動
    PixelMove,      // Move+枠内+Ctrl: 画素切出移動
    PixelDuplicate, // Move+枠内+Alt: 画素複写移動
    LayerDrag,      // それ以外(枠外・選択なし): 最前面層掴み (選択は保持)
}

public static class MovePress
{
    /// <param name="isMoveTool">currentTool == Move.</param>
    /// <param name="isHandTool">currentTool == Hand.</param>
    /// <param name="hasSelection">doc.Selection != null.</param>
    /// <param name="insideSelection">hasSelection && doc.InsideSelection(point).</param>
    /// <param name="cutOrCopyKeys">Ctrl/Meta/Alt のいずれか押下.</param>
    /// <param name="duplicateKey">Alt 押下 (cutOrCopyKeys に含まれる).</param>
    public static MovePressKind Classify(bool isMoveTool, bool isHandTool,
        bool hasSelection, bool insideSelection, bool cutOrCopyKeys, bool duplicateKey)
    {
        if (isHandTool) return MovePressKind.Pan;
        if (!isMoveTool) return MovePressKind.None;
        if (hasSelection && insideSelection)
            return duplicateKey ? MovePressKind.PixelDuplicate
                : cutOrCopyKeys ? MovePressKind.PixelMove
                : MovePressKind.FrameDrag;
        return MovePressKind.LayerDrag;
    }
}
