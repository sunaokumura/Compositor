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

## P1検証追記 (t_a81fcba0・2026-09-21・WSL headless)
- dotnet build Release: 0 errors (CS0618旧ダイアログ警告のみ・既存。Apps反映は利用者許可なく禁止のため未実施)。
- dotnet test Release: 307/307合格 (P1NonDestructiveTests 20件含む。内 headless E2E 1件: PNG書出→ImageImport.DecodeFile→AdjBrush塗り→Live層→QuickShape判定→Vector層化→channel/QuickMask→Timelapse→PNG/JPEG復号→.comp v8保存開封の往復確認)。
- XAML: Avalonia compile通過 (Release build時)＋x:Name計114件 (既存99件不変＋新規15件: P1ButtonsPanel・AdjBrush/LiveFilterAdd/LiveRetune/LiveUp/LiveDown/QuickShape/Vector/CorrectLine/ChannelSave/ChannelLoad/Timelapse各Btn・QuickMaskCheck・PersonaBox・SimpleCheck)・操作子名は追加のみで既存不変。
- .comp v8: 新規保存はversion 8 (Live filter 4種・ベクター線・spare channel)。v1-7読込維持 (Validateは1-8受理)。
- 実機GUI目視 (Import→P1編集→Exportの手操作) はWSL headless制約のため未実施。後続実機工程で AdjBrush塗り・Live層再調整/並替・QuickShape/Vector描画・Correct line・Channel・QuickMask・Time-lapse・Persona/Simple の目視確認を引き継ぐ。

## P2検証追記 (t_ceec521c・2026-09-21・WSL headless)
- dotnet build Release: 0 errors (CS0618旧ダイアログ警告のみ・既存＋新規P2対話分。Apps反映は利用者許可なく禁止のため未実施)。
- dotnet test Release: 346/346合格 (P2ExpressionTests 39件含む。内 headless E2E 1件: PNG書出→ImageImport.DecodeFile→Mixer塗り→対称複製→Mesh変形→Slice切出→macro再生→blend range・ICC・HDR設定→動画frame→PNG/JPEG復号→.comp v9保存開封の往復確認)。
- XAML: Avalonia compile通過 (Release build時)＋x:Name計130件 (既存114件不変＋新規16件: P2ButtonsPanel・Mixer/Symmetry/Wrap/Perspective/Mesh/History/Macro/Workspace/Hdr/Slice/Compound/BlendRange/Video/Proxy3D/AiAssist各Btn)・操作子名は追加のみで既存不変。
- .comp v9: 新規保存はversion 9 (history・slice・macro・blendRange・icc・hdrEv)。v1-8読込維持 (Validateは1-9受理・新fieldはv9要求)。
- 実機GUI目視 (Import→P2編集→Exportの手操作) はWSL headless制約のため未実施。後続実機工程で Mixer・対称・Wrap preview・透視吸着・Mesh/Puppet・History/Macro・Workspace・HDR/ICC/Gamut・Slice/Compound/Blend範囲・動画層・3D・AI補助の目視確認を引き継ぐ。
