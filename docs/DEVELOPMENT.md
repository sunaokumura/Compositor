# 開発者向け手順 (DEVELOPMENT)

一般利用者向けの概要・使い方は [README.md](../README.md) を参照。この文書は開発用の手順（ビルド・テスト・発行・CI・内部証跡）を集約したもの。

## 開発要件

- .NET 8 SDK (WSL可、`EnableWindowsTargeting` でwin-x64向けビルド)。

## ビルド（WSLからWindows向け）

```
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test tests/Compositor.Tests/Compositor.Tests.csproj
dotnet publish src/Compositor.Avalonia -c Release -r win-x64 --self-contained true -o src/publish-win
```

配置: `src/publish-win/` をWindows側の導入先へ配置（上書き前に `taskkill /IM Compositor.exe /F`）。
既定の導入先: `C:\Users\<user>\Apps\Compositor.Windows\`。

## テスト

- 単体346件合格（`dotnet test -c Release`・2026-09-21実測：調整32・組合せ39・文書8・JPEG/調色板11・層35・押下10・P0 21・P1 20・P2 39・描画15・性能2・計画書7・選択9・安定4・意匠6・parity 8・変形41。文書系：保存往復・検証・JPEG・調色板・HEIC/TIFF探知を含む）。
- 性能実測（WSL, 2026-09-20）: 4K 3レイヤー合成 94ms／1000回合成ストレス 965ms・メモリ +373KB（リークなし目安50MB未満）。
- ビルド: `dotnet build -c Release -r win-x64` 0 errors（CS0618旧ダイアログ警告のみ・既存）。
- 発行: win-x64自己完結（214 entries）＋単独実行体（約90MB・単一exe）を再現確認。配布zipは `tools/Make-Release.ps1 -Version v1.1.0-windows` 手順で `dist/` に作成（現行版 v1.1.0-windows・.comp v9書出/v1-8読込）。
- ショートカット: V=Move H=Hand Ctrl+Z/Y=Undo/Redo Ctrl+C/V=Copy/Paste Ctrl+Shift+C=結合コピー Ctrl+S=保存 Ctrl+N/O/E Ctrl+D Del X/D Arrows=nudge。

## 動作確認済み（実機E2E）

- 起動・レイヤー追加/削除/並べ替え・非破壊移動（ドラッグ）・合成表示・PNG書出し
- E2E S1/S2/S4合格・S3合成表示合格（t_48a8bb54）。クラッシュ2件は t_3b5fced0 で修正済み。
- クリップボード Copy/Paste（アプリ内PNG経由）・Undo/Redo・ブレンド16種・Photoshop流ショートカットは本ブラッシュアップで実装。

## 配布

- `tools/Make-Release.ps1` で `src/publish-win` 自己完結出力を LICENSE.txt/README.md/THIRD-PARTY-NOTICES.md 同梱でzip化＋SHA256発行 (`dist/`)。
- リリースzip作成 (Windows側):

```powershell
powershell -ExecutionPolicy Bypass -File tools\Make-Release.ps1 -Version v1.1.0-windows
```

- MSIXは `packaging/Package.appxmanifest` 草案まで (コード署名は有効な証明書が必要なため未実施・保留)。
- クリーン導入→起動→基本操作の証跡は [INSTALL_VERIFY.md](INSTALL_VERIFY.md)。
