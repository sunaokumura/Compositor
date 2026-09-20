# FEATURE_GAP — Mac版 vs Windows移植版（t_e1a8b05e時点に作成・P0/P1/P2追記あり・現行 v1.1.0-windows/.comp v9は t_6b69f8bb で確認）

Mac版 Document/*.swift（41ファイル）に対する移植状態。○＝移植済み ／ △＝部分的 ／ ×＝未移植。
詳細は TOOLS_PARITY.md（42件対応表：Gradient工具行を分割）・SHORTCUTS.md（ショートカット一覧）を参照。

## 移植済み（○）
- レイヤー追加/削除/複製/並べ替え/表示切替/Lock/リネーム/不透明度/スケール/位置（非破壊）— DocumentModel.cs＋レイヤーパネル仕上げ
- Merge Down拡張（フォルダ内結合・クリップ束込・覆面焼込・trim・Undo可）・Merge Group（フォルダ中身結合）・Duplicate（覆面複写・クリップ非継承）
- Shape工具（矩形/角丸/楕円・Shift正方形・Alt中心・新規層作成・U工具・プレビュー・様式保持）
- Gradient工具（引張描画・端点再調整・Shift45°・Enter確定/Esc取消・Linear/Radial・FG→BG/透過・覆面描画・G工具＋線プレビュー）
- 合成（bottom-up, ブレンド16種）・回転/反転（非破壊・Draw時適用）・非均一拡縮（ScaleX/ScaleY）
- 変形数値直接入力（X/Y/W/H/角度・比率固定・矢印nudge）・Sampling選択（Nearest/Smooth/High）
- Distort自由変形（角掴み・Shift軸固定・確定/取消・CPU逆写像warp）・Flip Canvas（水平/垂直・全層＋選択ミラー）
- Canvas Size（単位/相対/lock・アンカー・拡張色）・Image Size（lock・resample・Sampling）・Crop（対称・確定/取消・Space移動・比率・C工具/T工具）
- 調整（Brightness/Contrast/Saturation/Invert/Blur・非破壊・Undo可）・Invert破壊API（ApplyInvert）
- 調整エンジン（Adjustments.cs・画素値試験32件）：Curves（Hermite・per-channel→master順）・Levels（正規化・RGB合成順・histogram・Auto Contrast/Color/Neutral・black/gray/white sampling）・GradientMap（輝度重み・反転）・Exposure（sRGBテーブル）・Hue全域（Master＋6色域・Hue/Sat/Light・band操作・eyedropper相当SampledHue・colorize）・Grain（ドキュメント空間seed固定・決定性）・Noise（Uniform/Gaussian・Mono・seed固定）・Lens補正・Gaussian/Motion Blur層縁外広がり（margin＋位置補正＋trim）
- 調整層方式（Layer.IsAdjustmentLayer＋LayerAdjustment・下層合成へ非破壊適用・Undo対応・Compose/Draw連動）。層単位Bright/Contrast/Saturation/Blur/Invertは互換維持だが新規調整では非推奨
- ブラシ/消しゴム（stroke単位Undo・COW・硬さ・間隔・不透明度キー）・CloneStamp（Alt-click採取・aligned・All Layers・stroke Undo）・Spot修復近似（3モード）・Smudge/Blur/Liquify（strength）・スポイト（Picked枠・All Layers切替）・ブラシ環表示（径・硬さ内環・Clone採取点×印）
- 矩形/楕円Marquee選択（Shift正方形・Alt中心・破線表示・ブラシ制限・Ctrl+D解除）
- Lasso自由投繩/多角投繩（rubber-band・Enter/ダブルクリック確定・Esc取消・加減算）
- MagicWand（tolerance・contiguous・sampleAllLayers・flood fill C#化）
- 選択内画素移動（枠移動・Ctrl切出移動・Alt複写・Enter確定・Esc取消・矢印nudge）・Delete範囲消去・Copy選択切抜き＋原位置Paste
- Import（PNG/JPG/BMP/WebP・HEIC/TIFFは探知して未対応明示）/ Export PNG（File.WriteAllBytes）/ Export JPEG（画質・マット・encoded preview・進捗）/ Copy Merged結合コピー（Ctrl+Shift+C）
- 計画書保存開封（.compパッケージ・v1-7読込/v7書出・層/覆面/群/切抜/フォルダ覆面/調整層/形状/解像度/作用層往復・検証・原子置換・7試験）・NewCanvas（寸法検証・背景 透明/白/黒）・文書表題改名・タブ未保存ドット
- 色選択全般（FG/BG・12色+追加/削除・X入替・D初期化・HSBピッカー・RGB/hex・覆面塗り分け・11試験）
- Undo/Redo（スナップショット式・上限100・ドラッグはPressed時push・スライダーは gesture単位・選択は履歴対象外）
- Copy/Paste（アプリ内PNG経由・Avalonia Clipboard）
- Move/Handツール・Zoom・キャンバスドラッグ移動（HitTest＋SafeSelect＋再入ガード）
- Photoshop流ショートカット（B/E/M/V/H/[/]/Ctrl+Z/Y/C/V/N/O/E/D/Del/矢印nudge）
- ドキュメントタブ（複数Doc・タブ毎Undoスタック）
- 直接ピクセル描画（WriteableBitmap＋SKSurface・PNG往復なし）・チェッカーボード背景

## 部分的（△）
- ブレンドUI: 16種選択可。フォルダ内・切抜覆面の描画連動あり（実効可視性・DstIn適用）
- 層覆面: 白/黒追加・選択から作成・描画/充填/反転/羽化・有効切替・覆面選択描画・連結/解除＋矢印単独移動・結合焼込まで対応。配置変形は層内offset近似・歪み焼込・Option-drag複写・サムネイルなし
- 切抜覆面（ライブマスク）: 設定/解除・切替・adopt/detach・DstIn描画まで対応。bakeダイアログなし（削除時はリンク解除）
- フォルダ/グループ: 作成・群化・解除・階層描画・実効可視性・Up/Down移動・中身ごと削除・Merge Groupまで対応。折畳みUI・ドラッグ並替なし
- ContentFill: 周囲平均の反復補填（決定性・小穴用）＋拡張貼付（層拡大）まで対応。Mac content_fillカーネル非搭載のため近似と文書化
- 選択: 矩形/楕円Marquee・自由/多角Lasso・MagicWand・画素移動・結合コピーまで対応。Expand/Contract・パス演算・アンチエイリアスなし
- 変形: 移動・拡縮（均一スライダー＋非均一数値）・回転・反転・歪み・数値指定・Sampling・Flip Canvas・Canvas/Image Size・Cropまで対応。複数層グループ・スナップなし。P0追加: canvas回転view（View°・表示のみ画素不変）・Solo表示（対象層のみ・IsEffectivelyVisible連動）・複数選択UI（LayerList複数選択＋MergeSel＋Group複数対応）
- Distort: 角掴み・Shift軸固定・確定/取消・Flip考慮warpまで対応。live画素previewは枠表示のみ・マスク連動なし
- 調整: Bright/Contrast/Saturation/Blur/Invertに加えCurves/Levels/GradientMap/Exposure/Hue全域/Grain/Noise/Lens/Blur外広がりのエンジン＋調整層方式まで対応（画素値試験32件）。P0追加: Curves.../Levels.../Hue.../Filter...対話（破壊/調整層選択）・`/`filter検索・Wand gap-closing（Gap滑子0-8・既定2）・ASE読込・Harmony盤5種・profile preview表示。残件: GradientMap単独・露出単独シート
- ブラシ: 柔らか円ダブ・硬さ・間隔・不透明度（1-0キー）・6色＋Picked枠まで対応。GPUタイル・曲線補間・Shift-クリック直線なし
- Clone: Alt-click採取・aligned・All Layers・stroke Undoまで対応。回転層の採取写像・採取プレビュー画像なし
- Heal: 内容認識近似（環平均＋縁フェザー・Create Textureは粒状付加・Proximityは狭環）。完全再現（パッチ探索＋膜充填）は重量級のため対象外と文書化
- Smudge/Blur/Liquify: strength・硬さ・間隔まで対応。マスク上操作なし
- タブ: 切替・新規・•未保存ドット・文書表題改名・計画書保存開封まで対応。折畳み・ドラッグ並替・タブ間層複写なし
- 取込: PNG/JPG/BMP/WebP復号まで対応。HEIC/TIFFは復号器なしのため探知＋「未対応形式」明示（要PNG/JPEG変換）
- JPEG出力: 画質・マット・encoded preview・進捗まで対応。解像度DPIメタ未書込・画質の次回起動への引継ぎなし

## 未移植（×・UIで「未対応」明示済み：無効ボタン＋ツールチップ）
- SubjectRemoval（Vision）/ GuidedMatte（マット精緻化・対象外として維持）
- 調整シートUI残件（GradientMap単独・露出単独の破壊ダイアログ。P1のLive層対話（追加/再調整・10種対応）で非破壊路は代替可。旧無効ボタンは意匠回帰互換のため残置）
- MaskTracing

## P1非破壊深化（t_a81fcba0・2026-09-21・WORLD_BEST_PAINT §4 P1）
- Adjustment Brush式塗り調整（黒覆面つき調整層＋Brush白塗り・覆面変調合成で塗所のみ効く）
- filter mask・Live層（Noise/Lens/GaussBlur/MotionBlurの非破壊層化・覆面つき・並替/再調整/表示切替・.comp v8）
- QuickShape（線/矩形/楕円/三角snap・15°磁石・離すと層化）・ベクター線＋Correct line（制御点移動/線幅/単純化/結線・再焼成）・ベクター磁石（端点吸着・既定ON）
- spare channel（保存/選択へ/削除・v8往復）・Quick mask（Brush白黒描画・赤overlay・選択へ確定）
- Time-lapse記録（操作名＋時刻・Start/Stop/Export/Clear・session-only・連番画像はP2）
- Simple preset（Brush既定・白黒・描く）・Persona弱移植（描く/整える/出す）・Contextual頭欄（新工具2種の題＋助言追加）
- 不採用維持：生成AI Fill・ML自動切抜・Vision系・MSIX署名

## P2表現拡張（t_ceec521c・2026-09-21・WORLD_BEST_PAINT §4 P2）
- Mixer筆（Wet・Load・Mix三係数・対角stroke破壊適用・Undo可・Smudgeとは別物。他8 engine分化は対象外）
- 対称描画（鏡像複製層・MirrorH/V/HV・Kaleido4）・Wrap-Around（2x2 tiling preview書出・画素不変・view状態）・透視助手（消失点設定＋選択ベクター線の15°放射線吸着）
- mesh変形（粗格子ワープ・格子点数・膨らみ・選択層へ破壊適用）・puppet（ハンドル1点のガウス減衰移動・mesh上位の入口）
- History保存（操作名＋時刻＋層数・上限200・画素なし・.comp v9往復）・macro（Brightness/Contrast/Invert・上限64・選択層へ再生・v9往復）
- workspace preset（Persona・Simpleの保存・復元）・Python API下地（文書要約JSON＋操作stub生成・interpreterは将来）
- HDR（露出EV・Reinhard ToneMap・選択層へ破壊適用・HdrEvはv9保存）・ICC profile近似3種（文書保存・v9）・Gamut窓3種（session-only）
- Export slice（現選択の登録・PNG書出・上限64・v9往復）・compound mask（現選択とspare channel先頭の集合演算・選択へ）・blend range（下地輝度変調・Compose/DrawのCPU合成路で実効・層行[BR]・v9往復）
- 3D素材の置き場（手続きprimitive・Box/Sphere/Cylinder・新規層へ）・動画層の下地（frame列・上限256・session-only・onion preview・連番manifest・timeline本格は対象外）
- AI補助（手動下位・opt-inのみ：羽化refine・背景hint提案・保護noise・Undoで可逆。自動切抜・生成Fill・Vision系は対象外維持）
- 計画書保存開封は .comp v9（新規保存v9・v1-8読込維持・history/slice/macro/blendRange/icc/hdrEv往復・検証・原子置換）
- 不採用維持：生成AI Fill・ML自動切抜・Vision系・MSIX署名・store配布・HEIC/TIFF復号器

## 未移植（×・実用レベル後の次フェーズ・すべてUIで「未対応」明示済み）
- 詳細は TOOLS_PARITY.md の×7件を参照（SubjectRemoval/GuidedMatte/調整シートUI残件はいずれも無効ボタン＋ツールチップ。P0でCurves.../Levels.../Hue.../Filter...対話を追加済み）。
- Merge Downはフォルダ内・クリップ込・覆面焼込・trim対応へ拡張済み（旧v1制限のクリップ注記は解消）。複数選択UIはP0で完成（LayerList複数選択＋MergeSel＋Group複数対応・ドラッグ並替は残件）。
- 覆面の歪み焼込・Option-drag複写・サムネイル、フォルダ折畳みUI・ドラッグ並替は次フェーズ。

## 実用レベル6項目の自己評価（t_e1a8b05e更新）
1. 機能網羅: TOOLS_PARITY.mdで42件を○15/△20/×7に整理（集計不変・行内容を更新）。×は全て未対応バッジ明示（SubjectRemovalは対象外維持）。文書・入出力・色UIは別枠8行で△整理（計画書保存開封・JPEG・結合コピー・FG/BG調色板・タブ標識が本カードの追加分）
2. 安定性: クラッシュ修正済み（t_3b5fced0）・単体158件合格（文書系新規18件含む）・1000回ストレス合格（既存）。100時間相当の長時間実機は安定化カードの範囲
3. 性能: 4K合成94ms（既存値・本カードで回帰なし）。起動3秒は要実機計測（安定化カード）
4. UX: Photoshop準拠ショートカット（SHORTCUTS.md）・タブ/レイヤーパネル仕上げ（複製/Merge/Lock/リネーム）・Undo/Redo往復テスト合格
5. 配布品質: zip配布可・README/LICENSE/GAP/PARITY/SHORTCUTS整備。MSIX/署名/チュートリアル/スクリーンショットは次フェーズ
6. ライセンス: 上流MIT継承明記・命名区別あり

## 現行版注記 (t_6b69f8bb・2026-09-21・v1.1.0-windows)
- 単体346件合格 (内訳は README「テスト」欄参照)・.comp v9書出/v1-8読込。P0/P1/P2節の内容は本文に追記済み。
- 上記「実用レベル6項目の自己評価」は t_e1a8b05e 時点の記録として保持 (数値は当時のもの)。
