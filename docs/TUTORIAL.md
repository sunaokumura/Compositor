# チュートリアル (5分) — Compositor for Windows

起動→画像取込→レイヤー操作→書出しの最小経路。対応画面は `docs/screenshots/` を参照。

## 0. 起動
- 配布zipを展開し `Compositor.exe` を起動 (インストール不要・自己完結)。
- 初期画面: 上=ツールバー、左=レイヤーパネル、右=キャンバス (→ `01-launch.png`)。

## 1. 新規キャンバス
- ツールバー `New Canvas` を押す。幅・高さ（1-30000px）・背景（透明/白/黒）を指定して `Create canvas`。
- 白/黒を選ぶと背景レイヤーが1枚入る。透明は空の文書（`Import` で画像を追加）。
- 文書表題はツールバーの `DocNameBox`＋`Rename Doc` で変更。未保存があるとタブ名に `•` が付く。

## 2. 画像を読み込む (Import)
- `Import` → ファイルダイアログで PNG/JPG/BMP/WebP を選択 (→ `02-import.png`)。HEIC/TIFFは「未対応形式」と表示される（PNG/JPEGへ変換）。
- 読込画像は新規レイヤーとして追加される。100メガピクセル予算超過は取込時に警告。

## 3. 2枚を重ねる・動かす
- もう1枚 Import すると2レイヤー表示になる (→ `03-two-layers.png`)。
- ツール `Move` (Vキー) を選び、キャンバス上でドラッグで移動 (非破壊・元画像は保持)。
- `Hand` (Hキー) は表示位置のパン。`+`/`-`でズーム。
- 左パネルでレイヤー選択→ `Opacity` スライダーで不透明度、`Blend` で合成方式 (16種)、`Up`/`Down` で重なり順を変更。

## 4. 便利操作
- `Ctrl+Z` / `Ctrl+Y`: Undo/Redo (ドラッグ移動も取消可)。
- `Ctrl+C` / `Ctrl+V`: 選択レイヤーの Copy/Paste (アプリ内PNG経由)。`Ctrl+Shift+C`: 結合コピー（可視合成全体）。
- `Ctrl+S` 保存（.comp）/ `Ctrl+N` 新規タブ / `Ctrl+O` 開く(Import) / `Ctrl+E` 書出し / `Ctrl+D` 選択解除 / `Del` 削除。
- 前景/背景色: FG/BGボタン→Color Picker（H/S/B・RGB・hex）。`X`=入替・`D`=初期化（黒/白）。覆面編集中は白/黒選択になる。
- `Subject Removal (未対応)` はMac版の機能でWindows版では無効表示 (ツールチップ参照)。

## 5. 書出し (Export PNG / JPEG)
- `Export PNG` → 保存ダイアログで保存先を指定→ `.png` で保存。
- エクスポートは表示合成と同一ピクセル (E2E S4で完全一致を確認)。
- `Export JPEG` → 画質スライダ（encoded previewとバイト数で確認）・透明部の背景（白/黒/灰）→ `Export…` → 保存ダイアログで `.jpg` 保存。

## 6. 計画書の保存と開封 (.comp)
- `Save`（初回は保存先を聞かれる）/ `Save As`（別名）。`.comp` は `manifest.json`＋`images/` のパッケージフォルダ。
- `Open .comp` → フォルダダイアログで `.comp` を選択。破損時は現在の文書を置き換えない。
- 層・フォルダ・覆面・切抜・調整層・形状・解像度・作用層が往復する。Undo履歴と選択範囲は保存されない（Mac同様セッションのみ）。

## 困ったとき
- 真っ白の市松模様=透明キャンバス (正常)。
- レイヤーが空表示のまま→ `New`/`Import` で追加し直す。
- 未移植機能の一覧は `FEATURE_GAP.md` を参照。
