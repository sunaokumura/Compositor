# Compositor for Windows（移植版）

Mac版 Compositor（Wonder Assembly LLC・MIT）のWindows移植版。
C# / .NET 8 LTS + Avalonia UI 11 + SkiaSharp。Document層は非破壊レイヤー構成。

![起動画面](docs/screenshots/01-launch.png)

## 要件
- OS: Windows 10 1809以降 / Windows 11 (x64)。
- ランタイム不要 (.NET 8自己完結・`Compositor.exe` 単体起動)。
- 開発: .NET 8 SDK (WSL可、`EnableWindowsTargeting` でwin-x64向けビルド)。

## 導入
1. 配布zip (`Compositor-Windows-<version>-win-x64.zip`) を任意フォルダに展開。
2. `Compositor.exe` を起動 (インストール作業・管理者権限不要)。
3. 同梱の `LICENSE.txt` / `README.md` / `THIRD-PARTY-NOTICES.md` でライセンス・使い方を確認。
4. 既定の導入先: `C:\Users\<user>\Apps\Compositor.Windows\` (MVP配置 `...\Apps\Compositor\` とは別フォルダで共存可)。

```powershell
# リリースzip作成 (Windows側)
powershell -ExecutionPolicy Bypass -File tools\Make-Release.ps1 -Version v1.1.0-windows
```

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

## 動作確認済み（実機E2E）
- 起動・レイヤー追加/削除/並べ替え・非破壊移動（ドラッグ）・合成表示・PNG書出し
- E2E S1/S2/S4合格・S3合成表示合格（t_48a8bb54）。クラッシュ2件は t_3b5fced0 で修正済み。
- クリップボード Copy/Paste（アプリ内PNG経由）・Undo/Redo・ブレンド16種・Photoshop流ショートカットは本ブラッシュアップで実装。

## 未対応の明示
- `Subject Removal (未対応)` ボタンは無効化＋ツールチップで明示（Mac版の機能。Windows版では未対応）。
- その他の未移植機能の一覧は FEATURE_GAP.md を参照。

## ビルド（WSLからWindows向け）
```
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/Compositor.Tests/Compositor.Tests.csproj
dotnet publish src/Compositor.Avalonia -c Release -r win-x64 --self-contained true -o src/publish-win
```
配置: `src/publish-win/` → `C:\Users\<user>\Apps\Compositor\`（上書き前に taskkill /IM Compositor.exe /F）

## テスト
- 単体346件合格（`dotnet test -c Release`・2026-09-21実測：調整32・組合せ39・文書8・JPEG/調色板11・層35・押下10・P0 21・P1 20・P2 39・描画15・性能2・計画書7・選択9・安定4・意匠6・parity 8・変形41。文書系：保存往復・検証・JPEG・調色板・HEIC/TIFF探知を含む）。
- 性能実測（WSL, 2026-09-20）: 4K 3レイヤー合成 94ms／1000回合成ストレス 965ms・メモリ +373KB（リークなし目安50MB未満）。
- ビルド: `dotnet build -c Release -r win-x64` 0 errors（CS0618旧ダイアログ警告のみ・既存）。
- 発行: win-x64自己完結（214 entries）＋単独実行体（約90MB・単一exe）を再現確認。配布zipは `tools/Make-Release.ps1 -Version v1.1.0-windows` 手順で `dist/` に作成（現行版 v1.1.0-windows・.comp v9書出/v1-8読込）。
- ショートカット: V=Move H=Hand Ctrl+Z/Y=Undo/Redo Ctrl+C/V=Copy/Paste Ctrl+Shift+C=結合コピー Ctrl+S=保存 Ctrl+N/O/E Ctrl+D Del X/D Arrows=nudge。

## 配布
- `tools/Make-Release.ps1` で `src/publish-win` 自己完結出力を LICENSE.txt/README.md/THIRD-PARTY-NOTICES.md 同梱でzip化＋SHA256発行 (`dist/`)。
- MSIXは `packaging/Package.appxmanifest` 草案まで (コード署名は有効な証明書が必要なため未実施・保留)。
- クリーン導入→起動→基本操作の証跡は [docs/INSTALL_VERIFY.md](docs/INSTALL_VERIFY.md)。
- 公開リリース手順・証跡はタスクカード t_a33fadbd のコメント参照。
