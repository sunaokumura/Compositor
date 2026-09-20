# TOOLS_PARITY — Mac版 Document/*.swift 41件 vs Windows移植版（t_e5d70ed9時点）

○＝移植済み ／ △＝部分的 ／ ×＝未移植（UIに「未対応」バッジ＋無効化＋ツールチップで明示）

## ブラシ系
| Mac | Windows | 状態 |
|---|---|---|
| BrushStroke.swift（ブラシ/消しゴム/スポット修復） | Document.PaintStroke（丸ダブ・不透明度・消しゴムClear）＋Brush/Eraserツール | △（硬さ・間隔ダイナミクス・ヒーリングなし） |
| EditorSession+Brush.swift | MainWindow brush stroke handling（stroke単位Undo） | △ |
| CloneStamp.swift | — | ×（CloneStamp (未対応)バッジ） |
| SmudgeLiquify.scss → SmudgeLiquify.swift | — | ×（Smudge (未対応)バッジ） |
| BlurTool.swift（ぼかしツール） | Layer.Blur（非破壊ガウスぼかしスライダー） | △（ブラシベースの部分ぼかしはなし） |

## 選択系
| Mac | Windows | 状態 |
|---|---|---|
| Selection.swift（パス選択・選択範囲クリップ） | Document.Selection（矩形Marquee＋破線オーバーレイ＋ブラシ制限） | △（パスのみ矩形） |
| SelectionEdits.swift | Marquee作成・Ctrl+D解除 | △ |
| SelectionClipboard.swift（選択コピー/結合コピー） | Copy/Paste（レイヤー単位PNG経由） | △（選択範囲切り抜き・結合コピーなし） |
| MagicWand.swift | — | ×（MagicWand (未対応)バッジ） |
| FloatingSelection.swift（フローティング選択） | — | × |
| MaskTracing.swift | — | × |
| ShapeTool.swift | — | × |

## 変形系
| Mac | Windows | 状態 |
|---|---|---|
| LayerTransform.swift（回転/反転/サンプリング） | Layer.Rotation/FlipH/FlipV（非破壊・Draw時適用・Undo可） | △（自由拡縮の数値指定・サンプリング選択なし） |
| LayerFlip.swift | Flip H/Vボタン | ○ |
| Distort.swift（歪み・パース） | — | ×（Distort (未対応)バッジ） |
| CanvasSize.swift | — | × |
| Crop.swift | — | × |

## 調整系
| Mac | Windows | 状態 |
|---|---|---|
| ImageAdjustments.swift（露出/カラー調整基盤） | Document.BuildAdjustmentFilter（Brightness/Contrast/Saturation/Invert結合行列・非破壊） | △ |
| HueSaturation.swift | Saturationスライダー（0=グレー〜200） | △（色相指定なし） |
| Levels.swift / LevelsAutomatic.swift | — | ×（Levels (未対応)バッジ） |
| Curves.swift | — | ×（Curves (未対応)バッジ） |
| Gradient.swift（GradientMap含む） | — | ×（GradientMap (未対応)バッジ） |
| PixelAdjust.swift | Brightness/Contrastスライダー | △ |
| PixelInvert.swift | Invertフラグ（非破壊・UI）＋ApplyInvert（破壊・API） | ○ |
| AdjustmentEditing.swift / LayerAdjustment.swift（調整レイヤー） | レイヤー単位パラメータ（調整レイヤー方式ではない） | △ |

## フィルタ系
| Mac | Windows | 状態 |
|---|---|---|
| Filters.swift（ぼかし/ノイズ/レンズ等） | Blur（ガウス・非破壊）のみ | △ |
| ContentFill.swift | — | ×（ContentFill (未対応)バッジ） |
| GuidedMatte.swift（マット精緻化） | — | ×（SubjectRemoval関連のため対象外） |
| SubjectRemoval.swift（Vision被写体除去） | — | ×（Subject Removal (未対応)バッジ・Vision相当なし） |

## レイヤー系
| Mac | Windows | 状態 |
|---|---|---|
| LayerGroups.swift（グループ/フォルダ） | — | × |
| LayerMerge.swift | Merge Down（下へ結合・Undo可）・Duplicate | △（グループ結合なし） |
| LayerMask.swift / LiveLayerMask.swift | — | × |
| LayerAppearance.swift（不透明度/表示） | Opacity/Visible/Blend16種 | ○ |
| DocumentHistory.swift | UndoStack（上限100・ドラッグ/ストローク/スライダー単位・COW対応） | ○（選択は履歴対象外＝Photoshop同様） |
| ColorPalette.swift | BrushColorBox（6色プリセット） | △ |
| ProjectWorkspace.swift / EditorSession+Projects.swift / EditorSession.swift | ドキュメントタブ（複数Doc切替・タブ毎Undo） | △（プロジェクト保存なし） |

## 集計
- ○ 5件： LayerFlip / PixelInvert / LayerAppearance / DocumentHistory / （部分含め実用中核）
- △ 15件： ブラシ・矩形選択・変形・調整・ぼかし・タブ等が実用サブセットで動作
- × 21件： すべてUIに「未対応」バッジ・無効化・ツールチップで明示（Lasso/MagicWand/CloneStamp/Heal/Smudge/ContentFill/SubjectRemoval/Distort/Curves/Levels/GradientMap）
