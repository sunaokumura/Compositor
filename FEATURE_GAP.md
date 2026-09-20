# FEATURE_GAP — Mac版 vs Windows移植版（2026-09-20時点）

Mac版 Document/*.swift（約41ファイル）に対する移植状態。○＝移植済み ／ △＝部分的 ／ ×＝未移植。

## 移植済み（○）
- レイヤー追加/削除/並べ替え/表示切替/不透明度/スケール/位置（非破壊）— DocumentModel.cs
- 合成（bottom-up, ブレンド16種 SrcOver/Multiply/Screen/Overlay/Darken/Lighten/ColorDodge/ColorBurn/HardLight/SoftLight/Difference/Exclusion/Hue/Saturation/Color/Luminosity）
- Import（PNG/JPG/BMP）/ Export PNG（File.WriteAllBytes）
- Undo/Redo（スナップショット式・上限100・ドラッグはPressed時push）
- Copy/Paste（アプリ内PNG経由・Avalonia Clipboard）
- Move/Handツール・Zoom・キャンバスドラッグ移動（HitTest＋SafeSelect＋再入ガード）
- Photoshop流ショートカット（V/H/Ctrl+Z/Y/C/V/N/O/E/Del）
- 直接ピクセル描画（WriteableBitmap＋SKSurface・PNG往復なし）・チェッカーボード背景

## 部分的（△）
- ブレンドUI: 16種選択可。Mac版のフォルダ/クリッピング連動なし
- 選択: HitTest（矩形）のみ。Marquee/Lasso/MagicWandなし
- 変形: 移動・スケール（スライダー）のみ。回転・フリップ・歪み・スナップなし

## 未移植（×・実用レベル後の次フェーズ）
- ブラシ/消しゴム/クローンスタンプ/スマッジ・ブラシカーソル
- 調整レイヤー（Hue/Saturation, Levels, Curves, Exposure, Gradient Map, Grain）
- フィルタ（Blur等）・ContentFill・Heal・Lens
- レイヤーマスク（paint/fill/invert/blur/feather, link/unlink）・フォルダマスク・クリッピングマスク
- レイヤーグループ/フォルダ・Merge Down/Merge Layers/Merge Group・複製/リネーム/ドラッグ入替
- キャンバスサイズ変更・Crop・Exact値入力・矢印キー微調整・Flip Canvas
- フローティング選択・Shape・プロジェクト保存/タブ・シート
- SubjectRemoval → UIで「未対応」と明示済み（MainWindow.axaml の無効ボタン＋ツールチップ）

## 実用レベル6項目の自己評価
1. 機能網羅: MVP＋α（上記○）まで。×はUI未露出または「未対応」明示が残課題の一部あり
2. 安定性: クラッシュ修正済み（t_3b5fced0）・単体10件合格・1000回ストレス合格。100時間相当の長時間実機は未実施
3. 性能: 4K合成94ms・起動は未計測（3秒以内は要実機計測）
4. UX: ショートカット・Undo/Redo完全動作（ドラッグ修正含む）。タブ・レイヤーパネル仕上げは簡易版
5. 配布品質: zip配布可・README/LICENSE/GAP整備。MSIX/署名/チュートリアル/スクリーンショットは未整備
6. ライセンス: 上流MIT継承明記・命名区別あり
