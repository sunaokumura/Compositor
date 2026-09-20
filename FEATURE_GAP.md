# FEATURE_GAP — Mac版 vs Windows移植版（t_e5d70ed9時点に更新）

Mac版 Document/*.swift（41ファイル）に対する移植状態。○＝移植済み ／ △＝部分的 ／ ×＝未移植。
詳細は TOOLS_PARITY.md（41件対応表）・SHORTCUTS.md（ショートカット一覧）を参照。

## 移植済み（○）
- レイヤー追加/削除/複製/並べ替え/表示切替/Lock/リネーム/不透明度/スケール/位置（非破壊）— DocumentModel.cs＋レイヤーパネル仕上げ
- Merge Down（下へ結合）— 下レイヤーサイズにクリップされる旨を文書化
- 合成（bottom-up, ブレンド16種）・回転/反転（非破壊・Draw時適用）
- 調整（Brightness/Contrast/Saturation/Invert/Blur・非破壊・Undo可）・Invert破壊API（ApplyInvert）
- ブラシ/消しゴム（stroke単位Undo・COW）・矩形Marquee選択（破線表示・ブラシ制限・Ctrl+D解除）
- Import（PNG/JPG/BMP）/ Export PNG（File.WriteAllBytes）
- Undo/Redo（スナップショット式・上限100・ドラッグはPressed時push・スライダーは gesture単位・選択は履歴対象外）
- Copy/Paste（アプリ内PNG経由・Avalonia Clipboard）
- Move/Handツール・Zoom・キャンバスドラッグ移動（HitTest＋SafeSelect＋再入ガード）
- Photoshop流ショートカット（B/E/M/V/H/[/]/Ctrl+Z/Y/C/V/N/O/E/D/Del/矢印nudge）
- ドキュメントタブ（複数Doc・タブ毎Undoスタック）
- 直接ピクセル描画（WriteableBitmap＋SKSurface・PNG往復なし）・チェッカーボード背景

## 部分的（△）
- ブレンドUI: 16種選択可。Mac版のフォルダ/クリッピング連動なし
- 選択: 矩形Marqueeのみ。Lasso/MagicWand/パス選択なし（未対応バッジあり）
- 変形: 移動・スケール（スライダー）・回転・反転のみ。歪み・スナップ・数値指定なし（Distort未対応バッジあり）
- 調整: Bright/Contrast/Saturation/Blur/Invertのみ。Curves/Levels/GradientMapなし（未対応バッジあり）
- ブラシ: 丸ダブ・6色・サイズのみ。硬さ・間隔・ヒーリングなし
- タブ: 切替・新規のみ。プロジェクト保存なし

## 未移植（×・UIで「未対応」明示済み：無効ボタン＋ツールチップ）
- Lasso / MagicWand / CloneStamp / Heal / Smudge / ContentFill / SubjectRemoval（Vision）
- Distort / Curves / Levels / GradientMap
- レイヤーマスク・フォルダ/グループ結合・キャンバスサイズ変更・Crop・Shape・プロジェクト保存/シート
- Merge Downは実装済みだが下レイヤー範囲外はクリップされる（v1制限）

## 未移植（×・実用レベル後の次フェーズ・すべてUIで「未対応」明示済み）
- 詳細は TOOLS_PARITY.md の×21件を参照（Lasso/MagicWand/CloneStamp/Heal/Smudge/ContentFill/SubjectRemoval/Distort/Curves/Levels/GradientMap 等はいずれも無効ボタン＋ツールチップ）。
- レイヤーマスク・フォルダ/グループ・キャンバスサイズ変更・Crop・Shape・プロジェクト保存・シート・ブラシカーソル・数値Exact入力・Flip Canvasは次フェーズ。

## 実用レベル6項目の自己評価（t_e5d70ed9更新）
1. 機能網羅: TOOLS_PARITY.mdで41件を○5/△15/×21に整理。×は全て未対応バッジ明示
2. 安定性: クラッシュ修正済み（t_3b5fced0）・単体18件合格（新規8件含む）・1000回ストレス合格（既存）。100時間相当の長時間実機は安定化カードの範囲
3. 性能: 4K合成94ms（既存値・本カードで回帰なし）。起動3秒は要実機計測（安定化カード）
4. UX: Photoshop準拠ショートカット（SHORTCUTS.md）・タブ/レイヤーパネル仕上げ（複製/Merge/Lock/リネーム）・Undo/Redo往復テスト合格
5. 配布品質: zip配布可・README/LICENSE/GAP/PARITY/SHORTCUTS整備。MSIX/署名/チュートリアル/スクリーンショットは次フェーズ
6. ライセンス: 上流MIT継承明記・命名区別あり
