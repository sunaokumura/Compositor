# TOOLS_PARITY — Mac版 Document/*.swift 41件 vs Windows移植版（t_f04eebff時点）

○＝移植済み ／ △＝部分的 ／ ×＝未移植（UIに「未対応」バッジ＋無効化＋ツールチップで明示）

## ブラシ系
| Mac | Windows | 状態 |
|---|---|---|
| BrushStroke.swift（ブラシ/消しゴム/スポット修復） | PaintEngine.PaintBrushStroke（柔らか円ダブ・硬さ・間隔・不透明度・消しゴムClear）＋Brush/Eraserツール・HealTools.SpotHeal（3モード近似） | △（GPUタイル/曲線補間なし・ヒーリングは内容認識近似） |
| EditorSession+Brush.swift | MainWindow brush stroke handling（stroke単位Undo） | △ |
| CloneStamp.swift | CloneTools（Alt-click採取点・aligned・sampleAllLayers・stroke単位Undo・採取点×印＋硬さ表示）＋Cloneツール(S) | ○（回転層の採取写像は対象外） |
| SmudgeLiquify.scss → SmudgeLiquify.swift | SmudgeStroke（Smudge/Blur/Liquify＋strength・硬さ・間隔）＋Smudgeツール(R・モード切替） | ○（マスク上操作は対象外） |
| BlurTool.swift（ぼかしツール） | Layer.Blur（非破壊ガウスぼかしスライダー）＋Smudge/Blurモードのブラシベース部分ぼかし | ○ |

## 選択系
| Mac | Windows | 状態 |
|---|---|---|
| Selection.swift（パス選択・選択範囲クリップ） | Document.Selection＋SelKind/Polygon/Mask（矩形/楕円/投繩/多角＋破線・rubber-band・ブラシ制限） | △（アンチエイリアス・パス演算なし） |
| SelectionEdits.swift | Marquee作成（Shift正方形・Alt中心・加減算）・枠移動・Ctrl+D解除・Delete範囲消去 | △（Expand/Contractなし） |
| SelectionClipboard.swift（選択コピー/結合コピー） | Copy選択切抜き＋原位置Paste（単層・マスク抜き） | △（結合コピーなし） |
| MagicWand.swift | SelectionTools.WandMask（tolerance・contiguous・sampleAllLayers、WandPixels.c相当flood fill C#化） | △（sample size固定・詳細アウトラインなし） |
| FloatingSelection.swift（フローティング選択） | FloatingSelection（切出し移動・複写・Enter確定・Esc取消・矢印nudge） | △（変形ハンドルなし・移動のみ） |
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
| ColorPalette.swift | BrushColorBox（6色プリセット＋スポイト採取Picked枠）・Eyedropperツール(I・All Layers切替・Alt-click採取） | △（パレット編集・マスク塗り分けなし） |
| ProjectWorkspace.swift / EditorSession+Projects.swift / EditorSession.swift | ドキュメントタブ（複数Doc切替・タブ毎Undo） | △（プロジェクト保存なし） |

## 集計
- ○ 8件： LayerFlip / PixelInvert / LayerAppearance / DocumentHistory / CloneStamp / SmudgeLiquify / BlurTool（ブラシぼかし追加） / （部分含め実用中核）
- △ 16件： ブラシ（硬さ・間隔・不透明度・修復近似まで対応・GPUなし）・選択（矩形/楕円/投繩/多角/Wand/画素移動）・変形・調整・タブ等が実用サブセットで動作
- × 17件： すべてUIに「未対応」バッジ・無効化・ツールチップで明示（ContentFill/SubjectRemoval/Distort/Curves/Levels/GradientMap等。CloneStamp/Heal/Smudge/Lasso/MagicWandは対応済みのためバッジ解除）
