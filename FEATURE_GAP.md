# FEATURE_GAP — Mac版 vs Windows移植版（t_78505b8d時点に更新）

Mac版 Document/*.swift（41ファイル）に対する移植状態。○＝移植済み ／ △＝部分的 ／ ×＝未移植。
詳細は TOOLS_PARITY.md（41件対応表）・SHORTCUTS.md（ショートカット一覧）を参照。

## 移植済み（○）
- レイヤー追加/削除/複製/並べ替え/表示切替/Lock/リネーム/不透明度/スケール/位置（非破壊）— DocumentModel.cs＋レイヤーパネル仕上げ
- Merge Down（下へ結合）— 下レイヤーサイズにクリップされる旨を文書化
- 合成（bottom-up, ブレンド16種）・回転/反転（非破壊・Draw時適用）・非均一拡縮（ScaleX/ScaleY）
- 変形数値直接入力（X/Y/W/H/角度・比率固定・矢印nudge）・Sampling選択（Nearest/Smooth/High）
- Distort自由変形（角掴み・Shift軸固定・確定/取消・CPU逆写像warp）・Flip Canvas（水平/垂直・全層＋選択ミラー）
- Canvas Size（単位/相対/lock・アンカー・拡張色）・Image Size（lock・resample・Sampling）・Crop（対称・確定/取消・Space移動・比率・C工具/T工具）
- 調整（Brightness/Contrast/Saturation/Invert/Blur・非破壊・Undo可）・Invert破壊API（ApplyInvert）
- ブラシ/消しゴム（stroke単位Undo・COW・硬さ・間隔・不透明度キー）・CloneStamp（Alt-click採取・aligned・All Layers・stroke Undo）・Spot修復近似（3モード）・Smudge/Blur/Liquify（strength）・スポイト（Picked枠・All Layers切替）・ブラシ環表示（径・硬さ内環・Clone採取点×印）
- 矩形/楕円Marquee選択（Shift正方形・Alt中心・破線表示・ブラシ制限・Ctrl+D解除）
- Lasso自由投繩/多角投繩（rubber-band・Enter/ダブルクリック確定・Esc取消・加減算）
- MagicWand（tolerance・contiguous・sampleAllLayers・flood fill C#化）
- 選択内画素移動（枠移動・Ctrl切出移動・Alt複写・Enter確定・Esc取消・矢印nudge）・Delete範囲消去・Copy選択切抜き＋原位置Paste
- Import（PNG/JPG/BMP）/ Export PNG（File.WriteAllBytes）
- Undo/Redo（スナップショット式・上限100・ドラッグはPressed時push・スライダーは gesture単位・選択は履歴対象外）
- Copy/Paste（アプリ内PNG経由・Avalonia Clipboard）
- Move/Handツール・Zoom・キャンバスドラッグ移動（HitTest＋SafeSelect＋再入ガード）
- Photoshop流ショートカット（B/E/M/V/H/[/]/Ctrl+Z/Y/C/V/N/O/E/D/Del/矢印nudge）
- ドキュメントタブ（複数Doc・タブ毎Undoスタック）
- 直接ピクセル描画（WriteableBitmap＋SKSurface・PNG往復なし）・チェッカーボード背景

## 部分的（△）
- ブレンドUI: 16種選択可。Mac版のフォルダ/クリッピング連動なし
- 選択: 矩形/楕円Marquee・自由/多角Lasso・MagicWand・画素移動まで対応。結合コピー・Expand/Contract・パス演算・アンチエイリアスなし
- 変形: 移動・拡縮（均一スライダー＋非均一数値）・回転・反転・歪み・数値指定・Sampling・Flip Canvas・Canvas/Image Size・Cropまで対応。複数層グループ・スナップなし
- Distort: 角掴み・Shift軸固定・確定/取消・Flip考慮warpまで対応。live画素previewは枠表示のみ・マスク連動なし
- 調整: Bright/Contrast/Saturation/Blur/Invertのみ。Curves/Levels/GradientMapなし（未対応バッジあり）
- ブラシ: 柔らか円ダブ・硬さ・間隔・不透明度（1-0キー）・6色＋Picked枠まで対応。GPUタイル・曲線補間・Shift-クリック直線なし
- Clone: Alt-click採取・aligned・All Layers・stroke Undoまで対応。回転層の採取写像・採取プレビュー画像なし
- Heal: 内容認識近似（環平均＋縁フェザー・Create Textureは粒状付加・Proximityは狭環）。完全再現（パッチ探索＋膜充填）は重量級のため対象外と文書化
- Smudge/Blur/Liquify: strength・硬さ・間隔まで対応。マスク上操作なし
- タブ: 切替・新規のみ。プロジェクト保存なし

## 未移植（×・UIで「未対応」明示済み：無効ボタン＋ツールチップ）
- ContentFill / SubjectRemoval（Vision）
- Curves / Levels / GradientMap
- レイヤーマスク・フォルダ/グループ結合・Shape・プロジェクト保存/シート

## 未移植（×・実用レベル後の次フェーズ・すべてUIで「未対応」明示済み）
- 詳細は TOOLS_PARITY.md の×14件を参照（ContentFill/SubjectRemoval/Curves/Levels/GradientMap 等はいずれも無効ボタン＋ツールチップ）。
- レイヤーマスク・フォルダ/グループ・Shape・プロジェクト保存・シート・数値Exact入力（小数2桁表示のみ整数丸め）・結合コピーは次フェーズ。
- Merge Downは実装済みだが下レイヤー範囲外はクリップされる（v1制限）。

## 実用レベル6項目の自己評価（t_78505b8d更新）
1. 機能網羅: TOOLS_PARITY.mdで41件を○12/△15/×14に整理。×は全て未対応バッジ明示
2. 安定性: クラッシュ修正済み（t_3b5fced0）・単体77件合格（変形系新規35件含む）・1000回ストレス合格（既存）。100時間相当の長時間実機は安定化カードの範囲
3. 性能: 4K合成94ms（既存値・本カードで回帰なし）。起動3秒は要実機計測（安定化カード）
4. UX: Photoshop準拠ショートカット（SHORTCUTS.md）・タブ/レイヤーパネル仕上げ（複製/Merge/Lock/リネーム）・Undo/Redo往復テスト合格
5. 配布品質: zip配布可・README/LICENSE/GAP/PARITY/SHORTCUTS整備。MSIX/署名/チュートリアル/スクリーンショットは次フェーズ
6. ライセンス: 上流MIT継承明記・命名区別あり
