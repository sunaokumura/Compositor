# SHORTCUTS — Photoshop準拠ショートカット一覧（Windows移植版）

動作確認： dotnet build 0エラー・単体42件合格後のコードレビュー＋ログ出力（OnKeyDown→compositor.log）で経路確認。
実機キー操作のE2E再検証は安定化フェーズで実施する（本表の割当は実装済み）。

## ツール
| キー | 動作 | 備考 |
|---|---|---|
| B | Brush | レイヤー直接描画・stroke単位Undo・硬さ/間隔/不透明度対応 |
| E | Eraser | 透明消去 |
| S | Clone Stamp | Alt-clickで採取点設定・Aligned/All Layers・stroke単位Undo |
| J | Spot Healing | 採取不要の簡易修復（内容認識近似3モード） |
| R | Smudge (Smear) | モード切替でSmudge/Blur/Liquify・Strength対応 |
| I | Eyedropper | Clickで色採取（All Layers切替・Picked枠へ） |
| M | Marquee（矩形/楕円切替） | Shift正方形・Alt中心・小クリックは解除扱い |
| L | Lasso（自由/多角切替） | Enter/ダブルクリック確定・Esc取消 |
| W | Magic Wand | Tolerance・Contiguous・All Layers設定 |
| V | Move | 選択内Drag=枠移動・Ctrl+Drag=画素移動・Alt+Drag=複写 |
| H | Hand | （スクロール任せ・Move同等） |
| [ / ] | ブラシサイズ -4 / +4 | 工具頭欄にØ表示 |
| Shift+[ / Shift+] | 硬さ -25% / +25% | 工具頭欄にH表示・内破線環で可視化 |
| 1 … 9 / 0 | 不透明度 10% … 90% / 100% | 描画系工具のみ（Smudge系はStrength） |
| Alt+Click | Clone=採取点設定・他描画系=スポイト採取 | MarqueeのAlt=中心とは排他（工具別） |

## 編集
| キー | 動作 |
|---|---|
| Ctrl+Z | Undo |
| Ctrl+Y / Ctrl+Shift+Z | Redo |
| Ctrl+C / Ctrl+V | Copy / Paste（選択あり=範囲切抜き＋原位置貼付・なし=レイヤー単位） |
| Ctrl+N | 新ドキュメントタブ |
| Ctrl+O | Import |
| Ctrl+E | Export PNG |
| Ctrl+D | 選択解除 |
| Enter | 投繩確定・画素移動確定 |
| Esc | 投繩取消・画素移動取消（切出しをUndo復元）・選択解除 |
| Del | 選択あり=範囲画素消去・選択なし=レイヤー削除 |
| ←→↑↓（Shiftで10px） | 選択レイヤー微調整（nudge・Undo可） |

## 未割当（Mac/Photoshopにあるが本版では対象外）
- Ctrl+T（自由変形）→ Rotate/Flipボタンで代替、Distortは未対応バッジ
- Shift+Ctrl+C（結合コピー）→ 未対応（レイヤー単位コピーのみ）
