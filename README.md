# Compositor for Windows（移植版）

Mac版 Compositor（Wonder Assembly LLC・MIT）のWindows移植版。
C# / .NET 8 LTS + Avalonia UI 11 + SkiaSharp。Document層は非破壊レイヤー構成。

![起動画面](docs/screenshots/01-launch.png)

## 要件
- OS: Windows 10 1809以降 / Windows 11 (x64)。
- ランタイム不要 (.NET 8自己完結・`Compositor.exe` 単体起動)。

## 導入
1. 配布zip (`Compositor-Windows-<version>-win-x64.zip`) を任意フォルダに展開。
2. `Compositor.exe` を起動 (インストール作業・管理者権限不要)。
3. 同梱の `LICENSE.txt` / `README.md` / `THIRD-PARTY-NOTICES.md` でライセンス・使い方を確認。
4. 既定の導入先: `C:\Users\<user>\Apps\Compositor.Windows\` (MVP配置 `...\Apps\Compositor\` とは別フォルダで共存可)。

## 使い方 (概要)
- 上=ツールバー (Undo/Redo・Copy/Paste・ズーム・保存開封・書出し)、左=工具レール、右=層・調整・ブラシパネル、中央=キャンバス。
- `New Canvas`（寸法・背景）→`Import` (PNG/JPG/BMP/WebP) でレイヤー追加→ドラッグで非破壊移動→`Save` (.comp)・`Export PNG`/`Export JPEG`（画質・背景・マット）で書出し。
- 前景/背景色はFG/BGボタン→Color Picker（H/S/B・RGB・hex）。X=入替・D=初期化。`Ctrl+Shift+C`=結合コピー。
- `.comp` は `manifest.json`＋`images/` のパッケージフォルダ（v1-8読込/v9書出・現行版 v1.1.0-windows）。HEIC/TIFFは復号器なしのため「未対応形式」と明示（PNG/JPEGへ変換）。
- 詳しくは [5分チュートリアル](docs/TUTORIAL.md)。操作画面は [スクリーンショット](docs/screenshots/README.md)。
- ショートカット: V=Move H=Hand Ctrl+Z/Y=Undo/Redo Ctrl+C/V=Copy/Paste Ctrl+Shift+C=結合コピー Ctrl+S=保存 Ctrl+N/O/E Del X/D=色入替/初期化。

## 上流ライセンス継承
- 上流: Mac版 Compositor（MIT License, Copyright (c) 2026 Wonder Assembly LLC）
- 本リポジトリの LICENSE は上流MITをそのまま継承 (改変なし)。
- 配布zipには `LICENSE.txt` として同梱。アプリ内 About での表示は未実装 (残課題)。
- 第三者蔵書の許諾表示は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照（配布zipにも `THIRD-PARTY-NOTICES.md` として同梱）。
- 命名・構成はMac版と区別するため `Compositor for Windows` / `Compositor.Windows` とする (詳細は [命名・構成](docs/NAMING.md))。

## 未対応の明示
- `Subject Removal (未対応)` ボタンは無効化＋ツールチップで明示（Mac版の機能。Windows版では未対応）。
- その他の未移植機能の一覧は FEATURE_GAP.md を参照。

## 開発者向け
- ビルド・テスト・配布zip作成・署名の扱いなど開発用の手順は [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) を参照。

