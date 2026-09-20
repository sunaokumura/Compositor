# 導入検証証跡 (クリーン環境・2026-09-20)

## 検証内容
配布zipからの導入→起動→基本表示を確認 (タスク t_76fd1b20 受入: インストーラーから導入成功証跡)。

## 成果物
- zip: `dist/Compositor-Windows-v1.0.0-windows-win-x64.zip` (41MB・gitignore対象、リポジトリには含めない)
- SHA256: `4ad7c10a70ceb77164397c66fff76c2b2e9bb0ae177a554a884173b839215bda`
- 同梱: `Compositor.exe` + 依存DLL一式 + `LICENSE.txt` + `README.md`
- 作成手順: `tools/Make-Release.ps1` (Windows側スクリプト。WSL側では同等手順をpython zipfileで実行して本zipを作成)

## クリーン導入手順 (実測)
1. zipを `C:\Temp\` に配置→ `%TEMP%\CompositorCleanTest` (既存なし・クリーン) に展開→ OK。
2. 展開物: `Compositor.exe` (151,552 bytes)、`LICENSE.txt` あり、`README.md` あり。
3. `Compositor.exe` を起動 (pid 7940)。
4. プロセス生存: YES。`MainWindowTitle = "Compositor for Windows"`。WorkingSet 約159.6MB。
5. 目視 (`docs/screenshots/05-clean-install.png`): タイトル・ツールバー (Undo/Redo/Copy/Paste/Move/Hand/ズーム/Subject Removal未対応)・レイヤーパネル・透明キャンバス(市松)を確認。
6. 終了: プロセスを正常停止。

## 備考 (正直記録)
- 起動直後のためレイヤーリストは空 (正常。`New`/`Import`で追加する)。
- スクリーンショット中央の `MouseMux Feedback` ダイアログは検証環境の他ソフトのポップアップで本アプリと無関係。背後のCompositor画面で判定した。
- クリーン先は `%TEMP%` 配下 (レジストリ・管理者権限を使わないxcopy配布のため)。
- ビルド警告: `OpenFileDialog`/`SaveFileDialog`旧形式警告3件 (既存・動作に影響なし。StorageProvider移行は機能タスク側の残課題)。

## 未実施項目 (理由付き)
- コード署名: 有効な証明書なしのため未実施。自己署名は配布署名として不適のため見送り。
- MSIXパッケージ化: 署名が前提のため `packaging/Package.appxmanifest` 草案まで。
- アプリ内 About でのLICENSE表示: 未実装 (機能タスク側の残課題として申告)。

## P0検証追記 (t_7d185144・2026-09-21・WSL headless)
- dotnet build Release: 0 errors (CS0618旧ダイアログ警告のみ・既存＋新規ASE対話分。Apps反映は利用者許可なく禁止のため未実施)。
- dotnet test Release: 全合格 (P0WorldBestTests 21件含む。内 headless E2E 1件: PNG書出→ImageImport.DecodeFile→Curves破壊→Hue調整層→gap wand選択→Solo→JPEG Export/CopyMerged PNGの復号確認)。
- XAML: Avalonia compile通過 (Release build時)＋x:Name計99件 (既存87件不変＋新規12件)・操作子名は追加のみで既存不変。
- 実機GUI目視 (Import→編集→Exportの手操作) はWSL headless制約のため未実施。後続実機工程で `Curves.../Levels.../Hue.../Filter...` 対話・Solo・View°・ASE/Harmony/Profile・`/`検索の目視確認を引き継ぐ。
