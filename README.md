# Compositor for Windows（移植版）

Mac版 Compositor（Wonder Assembly LLC・MIT）のWindows移植版。
C# / .NET 8 LTS + Avalonia UI 11 + SkiaSharp。Document層は非破壊レイヤー構成。

## 上流ライセンス継承
- 上流: Mac版 Compositor（MIT License, Copyright (c) 2026 Wonder Assembly LLC）
- 本リポジトリの LICENSE は上流MITをそのまま継承。
- 命名・構成はMac版と区別するため `Compositor for Windows` / `Compositor.Windows` とする。

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
配置: `src/publish-win/` → `C:\Users\sunao\Apps\Compositor\`（上書き前に taskkill /IM Compositor.exe /F）

## テスト
- 単体8件＋性能2件＝10件合格（`dotnet test`）。
- 性能実測（WSL, 2026-09-20）: 4K 3レイヤー合成 94ms／1000回合成ストレス 965ms・メモリ +373KB（リークなし目安50MB未満）。
- ショートカット: V=Move H=Hand Ctrl+Z/Y=Undo/Redo Ctrl+C/V=Copy/Paste Ctrl+N/O/E Del。

## 配布
- 現状は `src/publish-win` 自己完結zip配布。MSIX・コード署名は未実施（証明書が必要なため保留→タスクのブロッカー参照）。
- 公開リリース手順・証跡はタスクカード t_a33fadbd のコメント参照。
