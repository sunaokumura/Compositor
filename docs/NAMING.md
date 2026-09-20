# 命名・リポジトリ構成 (Mac版との区別)

## 命名 (重複なし・2026-09-20確認)
| 対象 | 本移植版 | Mac版 (上流) | 衝突判定 |
|---|---|---|---|
| 表示名 (ウィンドウタイトル) | `Compositor for Windows` | `Compositor` | 異なる・OK |
| リポジトリ/プロジェクト名 | `Compositor.Windows` (`src/Compositor.Avalonia`, 名前空間 `Compositor`) | Mac版リポジトリ `Compositor` | 異なる・OK |
| 実行ファイル | `Compositor.exe` (win-x64自己完結) | `Compositor.app` (macOSバンドル) | 拡張子・配置先が異なり衝突なし・OK |
| インストール先 (Windows) | `C:\Users\<user>\Apps\Compositor.Windows\` (新規・移植版専用) | — (macOS専用) | OK。MVP配置 `...\Apps\Compositor\` とは別フォルダで共存可 |
| MSIX Identity草案 | `WonderAssembly.CompositorForWindows` | — | OK (packaging/Package.appxmanifest) |

- exe名 `Compositor.exe` はMVP配置との互換維持のため変更しない。区別は表示名・フォルダ・MSIX Identityで行う。
- Mac版バンドルIDとの衝突なし (プラットフォームが異なる)。

## リポジトリ構成
```
Compositor.Windows/
  README.md / LICENSE (上流MIT継承) / FEATURE_GAP.md
  docs/
    TUTORIAL.md          5分チュートリアル
    NAMING.md            本ファイル (命名・構成)
    INSTALL_VERIFY.md    クリーン導入→起動→基本操作の証跡
    screenshots/         起動・操作スクリーンショット (由来付き)
  tools/
    Make-Release.ps1     リリースzip作成 (publish-win + LICENSE/README同梱 + SHA256)
  packaging/
    Package.appxmanifest MSIXマニフェスト草案 (未署名・証明書待ち)
  src/Compositor.Avalonia/  アプリ本体 (.NET8 + Avalonia 11 + SkiaSharp)
  tests/Compositor.Tests/   単体8件 + 性能2件
  src/publish-win/          ビルド出力 (gitignore・配布物ではない)
  dist/                     リリースzip出力先 (gitignore)
```

## 上流ライセンス継承
- 上流: Mac版 Compositor (MIT License, Copyright (c) 2026 Wonder Assembly LLC)。
- 本リポジトリの `LICENSE` は上流MIT文面をそのまま継承。改変なし。
- 配布zipには `LICENSE.txt` として同梱する (tools/Make-Release.ps1)。
- アプリ内 About ダイアログでのライセンス表示は未実装 (残課題・機能タスク側で対応予定)。
