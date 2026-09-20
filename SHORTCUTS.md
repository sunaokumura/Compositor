# SHORTCUTS — Photoshop準拠ショートカット一覧（Windows移植版）

動作確認： dotnet build 0エラー・単体18件合格後のコードレビュー＋ログ出力（OnKeyDown→compositor.log）で経路確認。
実機キー操作のE2E再検証は安定化フェーズで実施する（本表の割当は実装済み）。

## ツール
| キー | 動作 | 備考 |
|---|---|---|
| B | Brush | レイヤー直接描画・stroke単位Undo |
| E | Eraser | 透明消去 |
| M | Marquee | 矩形選択（小クリックは解除扱い） |
| V | Move | レイヤードラッグ移動 |
| H | Hand | （スクロール任せ・Move同等） |
| [ / ] | ブラシサイズ -4 / +4 | |

## 編集
| キー | 動作 |
|---|---|
| Ctrl+Z | Undo |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+C / Ctrl+V | Copy / Paste（レイヤー単位） |
| Ctrl+N | 新ドキュメントタブ |
| Ctrl+O | Import |
| Ctrl+E | Export PNG |
| Ctrl+D | 選択解除 |
| Del | レイヤー削除 |
| ←→↑↓（Shiftで10px） | 選択レイヤー微調整（nudge・Undo可） |

## 未割当（Mac/Photoshopにあるが本版では対象外）
- Ctrl+T（自由変形）→ Rotate/Flipボタンで代替、Distortは未対応バッジ
- Shift+Ctrl+C（結合コピー）→ 未対応（レイヤー単位コピーのみ）
