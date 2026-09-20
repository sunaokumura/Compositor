# チュートリアル (5分) — Compositor for Windows

起動→画像取込→レイヤー操作→書出しの最小経路。対応画面は `docs/screenshots/` を参照。

## 0. 起動
- 配布zipを展開し `Compositor.exe` を起動 (インストール不要・自己完結)。
- 初期画面: 上=ツールバー、左=レイヤーパネル、右=キャンバス (→ `01-launch.png`)。

## 1. 新規キャンバス
- 左パネル `New` を押す。`Layer 1` が `Layers (top first)` に追加される。

## 2. 画像を読み込む (Import)
- `Import` → ファイルダイアログで PNG/JPG/BMP を選択 (→ `02-import.png`)。
- 読込画像は新規レイヤーとして追加される。

## 3. 2枚を重ねる・動かす
- もう1枚 Import すると2レイヤー表示になる (→ `03-two-layers.png`)。
- ツール `Move` (Vキー) を選び、キャンバス上でドラッグで移動 (非破壊・元画像は保持)。
- `Hand` (Hキー) は表示位置のパン。`+`/`-`でズーム。
- 左パネルでレイヤー選択→ `Opacity` スライダーで不透明度、`Blend` で合成方式 (16種)、`Up`/`Down` で重なり順を変更。

## 4. 便利操作
- `Ctrl+Z` / `Ctrl+Y`: Undo/Redo (ドラッグ移動も取消可)。
- `Ctrl+C` / `Ctrl+V`: 選択レイヤーの Copy/Paste (アプリ内PNG経由)。
- `Ctrl+N` 新規 / `Ctrl+O` 開く(Import) / `Ctrl+E` 書出し / `Del` 削除。
- `Subject Removal (未対応)` はMac版の機能でWindows版では無効表示 (ツールチップ参照)。

## 5. 書出し (Export PNG)
- `Export PNG` → 保存ダイアログで保存先を指定→ `.png` で保存。
- エクスポートは表示合成と同一ピクセル (E2E S4で完全一致を確認)。

## 困ったとき
- 真っ白の市松模様=透明キャンバス (正常)。
- レイヤーが空表示のまま→ `New`/`Import` で追加し直す。
- 未移植機能の一覧は `FEATURE_GAP.md` を参照。
