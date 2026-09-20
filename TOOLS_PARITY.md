# TOOLS_PARITY — Mac版 Document/*.swift 41件 vs Windows移植版（t_47f638b1時点）

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
| LayerTransform.swift（回転/反転/サンプリング） | Layer.ScaleX/ScaleY＋Rotation/FlipH/FlipV（非破壊・Draw時適用・Undo可）＋数値直接入力（X/Y/W/H/°・比率固定・矢印nudge）＋Sampling選択（Nearest/Smooth/High） | ○（複数層グループ・スナップなし） |
| LayerFlip.swift | Flip H/Vボタン＋Flip Canvas H/V（全層・選択範囲ミラー・Undo可） | ○ |
| Distort.swift（歪み・パース） | DistortWarp（角掴み歪み・Shift軸固定・確定/取消・CPU逆写像warp・Flip考慮）＋T工具 | ○（live画素previewは枠表示のみ・マスク連動なし） |
| CanvasSize.swift | CanvasSizeDraft（単位/相対/lock）＋CanvasSizeOptions（アンカー/拡張色・底層追加）＋ダイアログ | ○ |
| Crop.swift | CropGeometry/CropDrag（対称切抜・確定/取消・Space移動・比率）＋C工具 | ○（edge snapなし） |
| ImageSizeSheet.swift（画像寸法） | ImageSizeダイアログ（lock・resample・Sampling・解像度のみ変更は受理） | ○ |

## 調整系
| Mac | Windows | 状態 |
|---|---|---|
| ImageAdjustments.swift（露出/カラー調整基盤） | Document.BuildAdjustmentFilter（Brightness/Contrast/Saturation/Invert結合行列・非破壊）＋Exposure/GradientMap/Grainエンジン（Adjustments.cs・画素値試験済み） | △（露出スライダー等のシートUIなし） |
| HueSaturation.swift | HueOps全域エンジン（Master＋6色域・Hue/Sat/Light・band weight/center/include/exclude・eyedropper相当SampledHue・colorize・破壊/調整層適用）＋Saturationスライダー（UI既存） | △（Hue/SaturationシートUIなし・ボタンは将来のダイアログ用に未対応表示を維持） |
| Levels.swift / LevelsAutomatic.swift | LevelsOpsエンジン（LevelRange正規化・RGB合成順・LUT補間・alpha非premult処理・histogram・DisplayScale・Auto Contrast/Color/Neutral・black/gray/white sampling）＋破壊/調整層適用（8試験） | △（LevelsシートUIなし・ボタンは将来のダイアログ用に未対応表示を維持） |
| Curves.swift | CurvesSettingsエンジン（Hermite形状保持補間・isValid・per-channel→master順LUT）＋破壊/調整層適用（4試験） | △（CurvesシートUIなし・ボタンは将来のダイアログ用に未対応表示を維持） |
| Gradient.swift（GradientMap含む） | GradientMapエンジン（2126/7152/722輝度・反転・破壊/調整層適用・3試験） | △（GradientMapシートUIなし・ボタンは将来のダイアログ用に未対応表示を維持） |
| PixelAdjust.swift | Brightness/Contrastスライダー | △ |
| PixelInvert.swift | Invertフラグ（非破壊・UI）＋ApplyInvert（破壊・API） | ○ |
| AdjustmentEditing.swift / LayerAdjustment.swift（調整レイヤー） | Layer.IsAdjustmentLayer＋LayerAdjustment（Hsv/Levels/Curves/Exposure/GradientMap/Grain・下層合成へ非破壊適用・Undo対応・5試験）。層単位Bright/Contrast等は互換維持だが新規調整では非推奨（Adjustments.cs移行注記） | △（編集セッションbegin/finish・シート連動なし） |

## フィルタ系
| Mac | Windows | 状態 |
|---|---|---|
| Filters.swift（ぼかし/ノイズ/レンズ等） | Blur（ガウス・非破壊）に加えエンジン移植：Grain（ドキュメント空間seed固定・3試験相当）・Noise（Uniform/Gaussian・Mono・seed固定・2試験）・Lens補正（k=distortion/100*0.35・bilinear・1試験）・Gaussian/Motion Blur層縁外広がり（margin=radius*3+2 / distance/2+2・位置補正・trim・3試験） | △（FilterシートUIなし・Motionは方向性マルチタップ近似と文書化） |
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
- ○ 12件： LayerFlip / PixelInvert / LayerAppearance / DocumentHistory / CloneStamp / SmudgeLiquify / BlurTool（ブラシぼかし追加） / LayerTransform（数値入力・Sampling含む） / Distort / CanvasSize / Crop / ImageSize（＋部分含め実用中核）
- △ 18件： ブラシ（硬さ・間隔・不透明度・修復近似まで対応・GPUなし）・選択（矩形/楕円/投繩/多角/Wand/画素移動）・調整・タブ等が実用サブセットで動作（＋本カードでCurves/Levels/GradientMap/Exposure/Grain/Noise/Lens/Blur外広がり/調整層のエンジンを追加・シートUIは次段階）
- × 11件： すべてUIに「未対応」バッジ・無効化・ツールチップで明示（ContentFill/SubjectRemoval/調整シートUI等。Curves/Levels/GradientMapの無効ボタンはシートUI用に残置・エンジンは△欄参照。CloneStamp/Heal/Smudge/Lasso/MagicWand/Distort/Crop/CanvasSizeは対応済みのためバッジ解除）
