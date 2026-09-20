# TOOLS_PARITY — Mac版 Document/*.swift 41件 vs Windows移植版（t_569f69c1最終版）

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
| SelectionClipboard.swift（選択コピー/結合コピー） | Copy選択切抜き＋原位置Paste（単層・マスク抜き）＋Copy Merged結合コピー（可視合成全体をPNGでClipboardへ・Ctrl+Shift+C） | △（結合貼付なし） |
| MagicWand.swift | SelectionTools.WandMask（tolerance・contiguous・sampleAllLayers、WandPixels.c相当flood fill C#化）＋GapCloseOps.WandMaskGapClosed（gap-closing 0-8px・既定2・Krita隙間閉鎖相当）＋Gap滑子 | △（sample size固定・詳細アウトラインなし） |
| FloatingSelection.swift（フローティング選択） | FloatingSelection（切出し移動・複写・Enter確定・Esc取消・矢印nudge） | △（変形ハンドルなし・移動のみ） |
| MaskTracing.swift | — | × |
| ShapeTool.swift | ShapeOps（矩形/角丸/楕円・Shift正方形・Alt中心・新規層・U工具・種循環ボタン・矩形プレビュー）＋ShapeInfo様式保持 | ○（拡大時のベクタ再描画なし・ラスタ層化・角丸半径は作成時固定） |

## 変形系
| Mac | Windows | 状態 |
|---|---|---|
| LayerTransform.swift（回転/反転/サンプリング） | Layer.ScaleX/ScaleY＋Rotation/FlipH/FlipV（非破壊・Draw時適用・Undo可）＋数値直接入力（X/Y/W/H/°・比率固定・矢印nudge）＋Sampling選択（Nearest/Smooth/High） | ○（複数層グループ・スナップなし） |
| LayerFlip.swift | Flip H/Vボタン＋Flip Canvas H/V（全層・選択範囲ミラー・Undo可）＋canvas回転view（View°滑子・表示のみ画素不変・GIMP作画検証相当・P0） | ○ |
| Distort.swift（歪み・パース） | DistortWarp（角掴み歪み・Shift軸固定・確定/取消・CPU逆写像warp・Flip考慮）＋T工具 | ○（live画素previewは枠表示のみ・マスク連動なし） |
| CanvasSize.swift | CanvasSizeDraft（単位/相対/lock）＋CanvasSizeOptions（アンカー/拡張色・底層追加）＋ダイアログ | ○ |
| Crop.swift | CropGeometry/CropDrag（対称切抜・確定/取消・Space移動・比率）＋C工具 | ○（edge snapなし） |
| ImageSizeSheet.swift（画像寸法） | ImageSizeダイアログ（lock・resample・Sampling・解像度のみ変更は受理） | ○ |

## 調整系
| Mac | Windows | 状態 |
|---|---|---|
| ImageAdjustments.swift（露出/カラー調整基盤） | Document.BuildAdjustmentFilter（Brightness/Contrast/Saturation/Invert結合行列・非破壊）＋Exposure/GradientMap/Grainエンジン（Adjustments.cs・画素値試験済み）＋P0対話UI（Curves.../Levels.../Hue.../Filter...・破壊/調整層選択・`/`検索絞込） | △（露出単独・GradientMap単独のシートなし） |
| HueSaturation.swift | HueOps全域エンジン（Master＋6色域・Hue/Sat/Light・band weight/center/include/exclude・eyedropper相当SampledHue・colorize・破壊/調整層適用）＋Saturationスライダー（UI既存）＋Hue...対話（P0・破壊/調整層選択） | △（旧未対応バッジは意匠回帰のため残置・機能はHue...に移行） |
| Levels.swift / LevelsAutomatic.swift | LevelsOpsエンジン（LevelRange正規化・RGB合成順・LUT補間・alpha非premult処理・histogram・DisplayScale・Auto Contrast/Color/Neutral・black/gray/white sampling）＋破壊/調整層適用（8試験）＋Levels...対話（P0・Black/Gamma/White・破壊/調整層選択） | △（旧未対応バッジは意匠回帰のため残置・機能はLevels...に移行） |
| Curves.swift | CurvesSettingsエンジン（Hermite形状保持補間・isValid・per-channel→master順LUT）＋破壊/調整層適用（4試験）＋Curves...対話（P0・中点3点カーブ・ch切替・破壊/調整層選択） | △（旧未対応バッジは意匠回帰のため残置・機能はCurves...に移行） |
| Gradient.swift（GradientMap調整） | GradientMapエンジン（2126/7152/722輝度・反転・破壊/調整層適用・3試験） | △（GradientMapシートUIなし・ボタンは将来のダイアログ用に未対応表示を維持） |
| Gradient.swift（Gradient工具） | GradientOps（引張描画・端点再ドラッグ調整・Shift45°・Enter確定/Esc取消・Linear/Radial・FG→BG/透過・覆面への灰色描画・G工具＋線プレビュー） | ○ |
| PixelAdjust.swift | Brightness/Contrastスライダー | △ |
| PixelInvert.swift | Invertフラグ（非破壊・UI）＋ApplyInvert（破壊・API） | ○ |
| AdjustmentEditing.swift / LayerAdjustment.swift（調整レイヤー） | Layer.IsAdjustmentLayer＋LayerAdjustment（Hsv/Levels/Curves/Exposure/GradientMap/Grain・下層合成へ非破壊適用・Undo対応・5試験）。層単位Bright/Contrast等は互換維持だが新規調整では非推奨（Adjustments.cs移行注記） | △（編集セッションbegin/finish・シート連動なし） |

## フィルタ系
| Mac | Windows | 状態 |
|---|---|---|
| Filters.swift（ぼかし/ノイズ/レンズ等） | Blur（ガウス・非破壊）に加えエンジン移植：Grain（ドキュメント空間seed固定・3試験相当）・Noise（Uniform/Gaussian・Mono・seed固定・2試験）・Lens補正（k=distortion/100*0.35・bilinear・1試験）・Gaussian/Motion Blur層縁外広がり（margin=radius*3+2 / distance/2+2・位置補正・trim・3試験）＋Filter...対話（P0・Grain/Noise/Lens/Gauss/Motion・破壊適用） | △（Motionは方向性マルチタップ近似と文書化） |
| ContentFill.swift | ContentFillOps（選択内を周囲平均で境界内側へ反復補填・決定性・失敗時noSource相当・拡張貼付ExpandPaste・Fボタン） | △（Mac content_fill Cカーネル非搭載・パッチ探索＋膜充填なし・小穴用近似と文書化） |
| GuidedMatte.swift（マット精緻化） | — | ×（SubjectRemoval関連のため対象外） |
| SubjectRemoval.swift（Vision被写体除去） | — | ×（Subject Removal (未対応)バッジ・Vision相当なし） |

## レイヤー系
| Mac | Windows | 状態 |
|---|---|---|
| LayerGroups.swift（グループ/フォルダ） | LayerHierarchy（作成・選択群化・解除・階層描画・実効可視性・検証・Up/Down移動・中身ごと削除・[F]表示・Merge Group） | △（折畳みUI・ドラッグ並替なし） |
| LayerMerge.swift | MergeOps（Merge Down拡張：フォルダ内・クリップ束込・覆面焼込・trim・Undo可・Duplicate覆面複写対応）＋Merge Group＋複数選択プラン（エンジン）＋複数選択UI（P0・LayerList複数選択＋MergeSel＋Group複数対応） | ○（ドラッグ並替なし） |
| LayerMask.swift / LiveLayerMask.swift | MaskOps（白/黒追加・選択から作成・描画/充填/反転/ぼかし羽化・有効切替・覆面選択描画・連結/解除＋矢印単独移動・結合焼込）＋ClipOps（切抜覆面：設定/解除・切替・adopt/detach・DstIn描画・[M][M*][C]表示） | △（配置変形は層内offset近似・歪み焼込・Option-drag複写・サムネイルなし・bakeダイアログなし） |
| LayerAppearance.swift（不透明度/表示） | Opacity/Visible/Blend16種＋Solo表示（P0・ibisPaint Solo相当・IsEffectivelyVisible連動・[Solo]標識・Undo対象外） | ○ |
| DocumentHistory.swift | UndoStack（上限100・ドラッグ/ストローク/スライダー単位・COW対応） | ○（選択は履歴対象外＝Photoshop同様） |
| ColorPalette.swift | PaletteState（FG/BG・12色+追加/削除・X入替・D初期化・HSBピッカー・RGB/hex・Quantized・覆面塗り分け白/黒）＋Eyedropperツール(I・All Layers切替・Alt-click採取・FG連動）＋FG/BGボタン・PaletteBox＋ASE読込（RGB/CMYK/Gray・P0）・Harmony盤5種（P0）・profile preview表示（P0・sRGB/P3模擬/Gray・画素不変） | △（パレット永続化・GradientMap端色連動なし） |
| ProjectWorkspace.swift / EditorSession+Projects.swift / EditorSession.swift | ドキュメントタブ（複数Doc切替・タブ毎Undo・•未保存ドット・文書表題改名DocNameBox）＋ProjectFormat（.comp保存開封 v1-7読込/v7書出・層/覆面v4/群v2/切抜v5/フォルダ覆面v6/調整層v7往復・検証・原子置換・7試験）＋NewCanvas（寸法1-30000検証・背景 透明/白/黒） | △（折畳みUI・ドラッグ並替・タブ間層複写なし） |

## 入出力・文書UI系（Mac IO/*.swift＋UI Sheet subset・42件外の別枠整理）

Macの文書・入出力・色UIに対するWindows対応。判定はDocument 42件の集計には含めない。

| Mac | Windows | 状態 |
|---|---|---|
| ProjectStore.swift（.comp保存開封・検証） | ProjectFormat（manifest v1-7読込/v7書出・層/覆面v4/群v2/切抜v5/フォルダ覆面v6/調整層v7・検証・原子置換・PNG資産・7試験） | △（Mac調整値の完全互換なし・調整はWindows設定の往復・CIImage由来画素の厳密一致なし） |
| ProjectController.swift（保存/開封/書出フロー） | Save/SaveAs/Open（ConfirmWindowで保存確認・開封前検証・破損時は現文書維持） | △（タブ毎quit順確認・最近使った項目・セキュリティスコープなし） |
| ImageExporter.swift（PNG/JPEG） | PNG書出（既存）＋JpegExport（画質0-1・マット白/黒/灰・<=1000px encoded preview・進捗表示・解像度メタ未書込＝画素のみ） | △（解像度DPIメタ・プログレス取消の厳密性なし） |
| ImageImporter.swift（JPEG/PNG/HEIC/TIFF） | ImageImport（PNG/JPG/BMP/WebP復号・HEIC/HEIF/TIFFは拡張子/署名で探知して「未対応形式」と明示・100MP/30k予算・2試験） | △（HEIC/TIFF復号器なし・文書化のみ） |
| NewCanvasSheet.swift | NewCanvasWindow（寸法1-30000検証・背景 透明/白/黒・作成） | △（クリップボード寸法提案・Open/Import導線なし） |
| JPEGExportSheet.swift | JpegExportWindow（画質スライダ・live encoded preview相当・バイト数表示・進捗バー・画質記憶は次回起動に残らない） | △（画質のUserDefaults永続化なし） |
| ColorPickerSheet.swift / ColorPaletteControls.swift | ColorPickerWindow（H/S/B・RGB・hex・preview）＋FG/BG・X入替・D初期化・覆面時は白/黒選択 | △（フローティングパネル・キャンバスクリック採取・GradientMap端色連動なし） |
| ProjectTabs.swift | DocTabs（切替・•未保存ドット・DocNameBox改名） | △（ドラッグ受付・閉じる×・折畳みなし） |

## 集計（最終版・t_569f69c1で実数確認）
- ○ 15件： LayerFlip / PixelInvert / LayerAppearance / DocumentHistory / CloneStamp / SmudgeLiquify / BlurTool（ブラシぼかし追加） / LayerTransform（数値入力・Sampling含む） / Distort / CanvasSize / Crop / ImageSize（＋部分含め実用中核）＋Shape / Gradient工具 / LayerMerge（Down拡張＋Group結合）
- △ 20件： ブラシ（硬さ・間隔・不透明度・修復近似まで対応・GPUなし）・選択（矩形/楕円/投繩/多角/Wand/画素移動）・調整エンジン（Curves/Levels/GradientMap/Exposure/Hue全域/Grain/Noise/Lens/Blur外広がり＋調整層）・層覆面/フォルダ/切抜覆面/ContentFill近似・タブ等が実用サブセットで動作
- × 3件（表内）： MaskTracing / GuidedMatte（SubjectRemoval関連のため対象外） / SubjectRemoval（Vision相当なし）。いずれもUIに「未対応」バッジ・無効化・ツールチップで明示
- ×相当バッジ計7： 表内×3に加え、調整・フィルタ系シートUI未対応ボタン4（Curves/Levels/Hue/Filterのダイアログ。エンジンは△欄に移植済みのため表判定は△、ボタンは将来のシートUI用に未対応表示を残置）。P0(t_7d185144)で機能対話（Curves.../Levels.../Hue.../Filter...）を追加し、旧バッジ4は意匠回帰試験の互換のため残置（機能の入口は新ボタン）。Shape/Gradient/ContentFill/Mask/Group/Clipは対応済みのためバッジ解除・Fボタン有効化
- 入出力・文書UI系（別枠・42件外）： 8行すべて△（.comp保存開封v1-7読込/v7書出・Save/SaveAs/Open・PNG/JPEG出力・Import・NewCanvas・JPEG/Colorピッカー・タブ）。HEIC/TIFFは復号器なしのため探知＋「未対応形式」明示
- 検証証跡（t_569f69c1）： dotnet test Release 158/158合格（内Perf 2件含む）。Perf回帰＝4K三層合成84ms（基準94ms台を下回る）・1000回合成ストレス 949ms・メモリ+7KB（漏洩目安50MB未満を大幅クリア）。win-x64自己完結発行（0 errors・CS0618ダイアログ警告のみ）＋Make-Release.ps1で配布zip更新（216 entries・SHA256はdist内.sha256参照）。C:\Users\sunao\Apps\Compositor への上書き反映は利用者許可待ちのため未実施（既配置版は21:42版のまま）。追補： 実機煙試験で起動直後のSyncPalette ERROR（Index範囲外）を検出→原因工程t_e1a8b05eのDocumentsUI.SyncPalettePanel再入ガード欠落と特定し修正（updatingPaletteガード＋SafeSelect化＋例外全文ログ化）。修正後は全158試験再合格・修正版で実機再起動し無エラー起動を確認（Compositor for Windows・Responding=True・compositor.logにERRORなし）。修正版でpublish-winと配布zipを再生成（SHA256はdist内.sha256参照）

## 意匠作替（t_fec99135・2026-09-20・機能 parity 不変）
- 目的： 素人感の脱却。画像中心の専門製品意匠へ作替え（Photoshop超えの操作感が目標）。機能追加・削除なし。
- 文字鍵の画像化： 工具列18鍵を Avalonia PathIcon の自作画像へ作替（Move/Hand/Brush/Eraser/Marquee/Lasso/Wand/Clone/Heal/Smudge/Eyedropper/Crop/Distort/Shape/Gradient/ContentFill/SR禁止印）。絵文字の使用なし。道具欄・層板の操作子は画像＋短文の hybrid（意味保持のため短文を残置）。状態表示鍵3件（ShapeKindBtn・MarqueeShapeBtn・LassoKindBtn）は実行時に Content 文字列を切替える仕様のため原文維持（意匠例外として記録）。
- 暗色題材の統一仕様： 背景 #1E1E1E / 板 #252526 / 頭欄 #2D2D30 / 区切線 #3E3E42 / 本文 #E8E8E8 / 副文 #9D9D9D / 補助 #6E6E6E / 選択 #094771＋#007ACC / 無効 Opacity 0.45。Fluent Dark 基調。Foreground="Gray" の未統一残存なし。Window.Styles に rail/bar/chip/section/dim/ListBoxItem/Slider を集約。選択強調は HighlightRail の DimGray から #094771 に変更（.cs 2行のみ・操作子名不変）。
- 層板の製品化： 見出し＋画像化（Layers・目玉相当 Visible・錠相当 Lock は助言に明示）、層列は暗地＋枠＋角丸＋行余白の統一意匠、行内容（[F]/[M]/[M*]/[C]/[S]/不透明度/座標）は data のため原文維持。Up/Down・Duplicate・Merge・Mask・Group/Clip・Canvas・Crop確定/取消・Deselect/Delete Sel を画像＋短文へ。
- 工具頭欄の製品化： 先頭に題印画像＋題（動的）＋区切線＋助言（動的）の整列。滑子幅 112 統一・数値欄は本文色 #E8E8E8、見出しは section 様式に統一。
- 画布域の製品化： 外余白12＋#252526板＋#3E3E42枠＋角丸6で額縁化。碁盤目・選択破線・環表示の描画は変更なし（寸法保持）。
- 状態欄の製品化： zoom・寸法・色表示の各区画に画像＋11px副文の統一書体、助言は省略表示対応。
- 未対応鍵の維持： Curves/Levels/GradientMap/SR/SR予備は画像＋IsEnabled=False＋助言を維持（削除なし）。
- 不変の検証： x:Name 87件・Click 等操作子は git 差分で完全一致を確認（NAMES-IDENTICAL / CLICK-IDENTICAL）。
- 検証証跡（t_fec99135）： dotnet build Release 0 errors（CS0618旧ダイアログ警告のみ・既存）。dotnet test Release 164/164合格（既存158維持＋意匠回帰 ThemeRegressionTests 6件）。起動画面の検証記録： WSL headless のため実窓表示は不可。代替として Release ビルド時の Avalonia XAML compile 通過（画像 data・様式の構文検証）＋164試験＋名称/操作子差分一致をもって起動画面構成の検証記録とする。実機の目視・操作検証は後続の組合せ総合試験（t_d1af46ea）に引継ぎ。

## P0実装証跡（t_7d185144・2026-09-21・WORLD_BEST_PAINT §4 P0）
- 追加engine: src/Compositor.Avalonia/P0WorldBest.cs（GapCloseOps・CanvasViewOps・SoloOps・AseReader・HarmonyOps・ProfilePreviewOps・FilterCatalog）。UI配線: P0WorldBestUI.cs（新規partial）＋MainWindow.axaml（新規x:Name 12件・既存87件と操作子名は不変・計99件）＋Document.SoloLayerId/CanvasAngle＋IsEffectivelyVisible Solo連動。
- 旧未対応バッジ4（Curves/Levels/GradientMap無効ボタン）は意匠回帰試験互換のため残置。機能入口は新ボタン Curves.../Levels.../Hue.../Filter...（破壊/調整層選択・`/`検索絞込）。GradientMap単独・露出単独シートは残件。
- 検証証跡: dotnet build Release 0 errors（CS0618旧ダイアログ警告のみ・既存＋新規ASE対話分）。dotnet test Release 全合格（P0WorldBestTests 21件含む・内 headless E2E 1件: Import→Curves破壊→Hue調整層→gap wand→Solo→JPEG/PNG書出の復号確認）。実機GUI目視はWSL headless制約のため後続実機工程に引継ぎ。
- Apps反映: 利用者許可なく禁止のため未実施。
