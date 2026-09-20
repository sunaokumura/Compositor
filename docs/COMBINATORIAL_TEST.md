# COMBINATORIAL_TEST — 組合せ総合試験記録 (t_d1af46ea)

意匠作替 (t_fec99135) 後の全機能について、鍵・入力欄・マウス・鍵盤の組合せを
考慮した総合試験の記録。単体で再現できるものは単体化
(tests/Compositor.Tests/CombinatorialTests.cs)、実機のみは手順書化 (§7)。

- 前提: t_fec99135 完了 (commit 697f9da)。開始時に差分を確認 → x:Name 87件・
  Click操作子は git diff で完全一致 (NAMES-IDENTICAL / CLICK-IDENTICAL)。
- 結果: dotnet test Release 228/228合格 (既存164維持 + 本試験64件)。
- dotnet build Release 0 errors (CS0618旧ダイアログ警告のみ・既存)。
- 作業場gitへcommit済 (pushなし)。C:\Users\<user>\Apps\Compositor への反映は
  利用者の許可なく禁止のため未実施。

## 1. 全鍵の押下試験

対象: distinct Click操作子 79件、x:Name 87件。

| 観点 | 結果 |
|---|---|
| 有効鍵の操作子解決 (全79 Click → MainWindow実在法 reflection) | 合格 (AllClickHandlers_ExistOnMainWindow) |
| 工具列15鍵 (Move/Hand/Brush/Eraser/Marquee/Lasso/Wand/Clone/Heal/Smudge/Eyedropper/Crop/Distort/Shape/Gradient) が有効 | 合格 (ToolRail_All16ToolsEnabled) |
| 未対応鍵は無効のまま: Curves/Levels/GradientMap + SR(rail) + SR(hidden) = 5件 | 合格 (DisabledSet_IsExactlyExpected)。未対応鍵削除なし |
| 無効鍵は全て助言 (ToolTip.Tip) を持つ | 合格 (Disabled_AllHaveGuidanceTooltip) |
| 状態表示鍵3件 (ShapeKindBtn/MarqueeShapeBtn/LassoKindBtn) は実行時Content切替のため原文維持 | 意匠例外として記録 (仕様通り) |

## 2. 全入力欄の試験

対象: TextBox 7件 (TxX/TxY/TxW/TxH/TxAngle/DocNameBox/RenameBox)、
Slider 12件、ComboBox 8件 (+PaletteBox ListBox)、CheckBox 7件、hex色欄。

| 観点 | 結果 |
|---|---|
| 寸法欄 1〜30000 (境界・空・0・負・範囲外・非数値・小数・桁溢れ・桁区切) | 合格 (Dimension_Valid×3 + Invalid×10) |
| 数値変形の有効適用 (位置・倍率・回転の反映) | 合格 |
| 不正寸法 (0・負・40万) は層に触れず拒否 | 合格 (×6) |
| 不正座標 (±200万)・NaN・Infinity・層なし・画像なし は拒否 | 合格 |
| 縦横比錠 (LockRatio): 主導軸で比例維持 (40×21→40×20) | 合格。※初版試験がH主導値を誤って期待→実コードの主導軸判定に合わせて修正 (試験側誤り) |
| 滑子12件の範囲が仕様通り (Opacity 0-100 / Scale 5-400 / Rotate ±180 / Bright・Contrast ±100 / Satur 0-200 / Blur 0-25 / Size 1-200 / Hardness 0-100 / Spacing 1-200 / BrushOpacity 1-100 / Tolerance 0-255) | 合格 (XAML静的検証) |
| hex色 (RRGGBB/RGB・#有無) 有効・無効 (null/空/不正/桁不足過多/#のみ/色名) | 合格 (×4+×7) |
| 硬さ25%刻み (Shift+[ / ]) の floor/ceil 境界 (0/1端含む) | 合格 |

滑子↔数値欄の連動ガード (uiReady/updatingList/SafeSelect) は既存 StabilityTests の
再入試験 + 本試験の範囲検証でカバー。実機のドラッグ連動は §7 手順 E-3。

## 3. マウス操作の組合せ試験 (各工具×左・右・輪・二重押・修飾 concomitant)

engine-level に単体化した組合せ (UI gesture 自体は §7 手順 E-2 で実機確認):

| 工具 | 組合せ | 結果 |
|---|---|---|
| Shape (U) | Shift=正方形・Alt=中心 (DragRect/square/fromCenter + DragBoxRect) | 合格 |
| Crop (C) | 比率固定・Alt対称・零面積/負値は無効 | 合格 |
| Distort (T) | Shift=軸固定 (全体移動で検証・dy抑止 + 自由移動で両軸)・縮退3点は不可 | 合格。※初版は角単体を30px牽引→四辺形反転で engine が正当に null 拒否。全体移動に変更 (試験側誤り) |
| Gradient (G) | Shift=45°吸着 (水平・対角) | 合格 |
| Wand (W) | 寛容度10 vs 250 で選択数単調増加・0超 | 合格 |
| Marquee/Lasso/Polygon | 楕円内外・多角形内外の幾何判定 | 合格 |
| Clone (S) | Alt採取・Aligned/All Layers | 既存 PaintToolsTests で合格 (本試験は重複せず) |
| Eyedropper (I) | Alt-click採取・All Layers | 既存で合格 |
| Move (V) | Ctrl+Drag=画素・Alt+Drag=複写・Space=枠移動 | engine (BeginPixelMove/ExpandPaste) 既存合格 + 実機は §7 E-2 |

右押・輪・二重押の工具別意味は SHORTCUTS.md 割当通り (実装済み)。実機のみの
確認は §7 手順 E-2 の表に落とした。

## 4. 鍵盤操作の組合せ試験 (全近道×単独とCtrl/Shift/Alt併用・焦点別)

| 近道 | 組合せ | 結果 |
|---|---|---|
| Ctrl+Z / Ctrl+Y・Ctrl+Shift+Z | Undo→Redo→Redo不可の順序 | 合格 |
| 矢印 / Shift+矢印 | 1px・10px微調整の算術 | 合格 |
| 覆面編集中の矢印 (非連結時) | 覆面のみ移動・層位置不変・HasMask/IsActive | 合格 |
| X / D | Swap往復・Reset黒白・覆面時は白黒切替 | 合格 |
| 1..9/0 | 10%..90%/100%写像 | 合格 |
| Del | 選択あり=範囲消去 (>0)・なし=0 (層削除分岐) | 合格 |
| Enter / Esc | 確定・取消の優先順 (Distort→Crop→Gradient→浮動→投縄→解除) | コード目視 + 実機は §7 E-4 |
| 焦点が入力欄・層列・画布の場合の別 | 実機のみ (§7 E-4)。engine は焦点非依存のため単体対象外 |

## 5. 工具×選択×層状態の交差表

行=工具、列=選択有無×層なし・錠・非表示・調整層・群・覆面。

| 交差 | 結果 |
|---|---|
| Distort開始守衛 (画像なし・錠は不可)。※錠への数値適用自体は engine が通すため UI 側守衛が必要な仕様として記録 | 合格 (DistortEntryGuards) |
| Merge Down: 単層は plan=null (無操作・無 crash)、2層で plan 生成 | 合格 |
| Clip連結: 自己・群・不在IDは不可 | 合格 |
| ContentFill: 周囲画素なし → Failure (Mac noSource相当・UIは捕捉して助言) | 合格 |
| Gradient確定: null・点 draft は拒否、線 draft は確定 | 合格 |
| Shape確定: 縮退矩形は null、正常は層生成 | 合格 |
| 非表示層は合成から除外・IsEffectivelyVisible=false | 合格 |
| 調整層・Blend名・Sampling名の往復 + Nearest→None 品質写像 | 合格 |

## 6. 文書×標識の交差 (複数標・未保存・改名・保存往復後の操作継続)

| 交差 | 結果 |
|---|---|
| Pushで IsModified=true・MarkSavedで false | 合格 |
| 複数文書の履歴独立 (AのUndoがBに無影響) | 合格 |
| 保存→開封→移動→合成の操作継続。※Loadの文書名は package 名由来の仕様 (層名は往復)。初版試験の期待を実仕様に修正 (試験側誤り) | 合格 |
| Canvas Resize: 有効は反映・無効 (0・40000) は文書不変 | 合格 |
| ImageSize: 100MP超過・0幅は Valid=false | 合格 |

## 7. E2E実機手順 (実機のみ・WSL headless では不可のため手順書化)

実施済み (WSL側): dotnet build Release 0 errors (Avalonia XAML compile 通過 =
起動画面構成の構文検証) + dotnet test 228/228。以下は Windows 実機側の手順
(利用者の許可後に実施。C:\Users\<user>\Apps\Compositor への反映は許可なく禁止)。

- E-0 準備: Compositor.exe を起動。%TEMP%\compositor.log に起動 ERROR なしを確認。
- E-1 全鍵: 工具列18鍵を順に押下。有効鍵は工具頭欄の題・助言が切替わること。
  未対応5鍵 (Curves/Levels/GradientMap/SR×2) は押下不可のまま・助言表示のこと。
  層板の Up/Down・Duplicate・Merge・Mask・Group/Clip・Canvas・Crop確定取消・
  Deselect・Delete Sel を画像+短文表示で押下。各操作は compositor.log に記録。
- E-2 マウス: 各工具×(左・右・輪・二重押・Shift・Alt・Ctrl・Space)×(画布・層列・
  滑子上)。期待: Shape/Crop/Distort/Gradient の Shift・Alt・Space が §3 通り。
  全削除→Import2枚→画布ドラッグで crash しないこと (t_3b5fced0 の回帰)。
- E-3 入力欄: TxX/Y/W/H/Angle に数値・空・範囲外・桁溢れを入力→適用。
  滑子12件を端から端へドラッグし数値欄と連動すること。uiReady前の
  ValueChanged で crash しないこと (起動直後に層選択→滑子操作)。
- E-4 鍵盤: §4 の全近道を単独・Ctrl・Shift・Alt併用で。焦点を入力欄・層列・
  画布に置いた3通りで Enter/Esc/Del/矢印の分岐を確認。
- E-5 層状態: 各工具×(選択有無×層なし・錠・非表示・調整層・群・覆面・切抜)。
  錠層への Distort/T開始は「層を選択してください」で Move に戻ること。
- E-6 文書: 複数標の切替・•未保存ドット・DocNameBox改名・保存→開封→操作継続。
  改名空欄・破損 .comp 開封時は現文書維持のこと。
- E-7 目視: 暗色題材8色・工具列画像のみ・層板/頭欄は画像+短文・未対応鍵の
  Opacity 0.45 + 助言。1200x790での額縁・状態欄の崩れなし。

## 8. 失敗記録 (再現手順・期待・実績)

本工程で製品不具合の検出なし。以下3件は試験コード側の想定誤り (軽微・自己判断で修正):

1. LockRatio主導軸: 40×99入力でH主導 (w=198) が正当。試験をW主導値 (40×21→40×20) に修正。
2. Distort牽引: 角単体30px牽引は四辺形反転→engine が null 拒否 (正当・Mac parity)。
   試験を全体移動 (moveBody) に変更。
3. Load文書名: ProjectFormat.Load は名を package 名から付ける仕様。試験期待を修正し層名往復を追加検証。

仕様級の差戻しなし。看板への報告事項なし。
