# WORLD_BEST_PAINT — 世界最良規範の調査書（Compositor.Windows 向け）

- 調査日: 2026-09-21 / 工程 t_3071e4f7
- 目的: 地球最高の描画製品を目指し、世界の絵描き program の優れた点を集めて本計画向け最良規範書を作る。
- 作業場所: `Compositor.Windows/docs/WORLD_BEST_PAINT.md`（本書）。符号変更なし（調査のみ）。
- 方法論: 推測で書かない。各主張に典拠 URL を付す。確度は [確]（一次資料で確認）/ [準]（二次資料・複数一致）/ [伝]（社区評価・要一次確認）で明示する。未確認は「未確認」と書く。

## 0. Mac 版 Compositor の思想と本書の評価軸

本計画の思想（課題文より）: 合成中心・非破壊・Photoshop 流近道。

- 合成中心: 複数層の bottom-up 合成とブレンドを中核に据える。[確] 現行 Windows 版も DocumentModel＋16種ブレンドで継承（TOOLS_PARITY.md 参照）。
- 非破壊: 画素を直接壊さず、調整層・覆面・変形パラメータで後から変えられる。[確] 現行は調整層方式（Layer.IsAdjustmentLayer＋LayerAdjustment）・覆面・非破壊移動を実装済み。
- Photoshop 流近道: B/E/M/V/H・Ctrl+Z/Y/C/V・Ctrl+Shift+C 等を SHORTCUTS.md で Photoshop 準拠に割当て。[確]

評価軸（課題指定の6軸＋導入の4点）:

- 6軸: 筆・選択・変形・調整・層・色。
- 4点: 導入・近道・頭欄（ツールバー/ヘッダ）・状態欄（ステータスバー）の工夫。初心者と熟練の両立で評価する。

---

## 1. 各製品の秀でた点（推測で書かず典拠を示す）

### 1.1 Adobe Photoshop（デスクトップ版）

一次資料は Adobe Helpx（helpx.adobe.com）。

- 筆: Mixer Brush（混色筆）で「濡れ絵具の混ざり」を再現する。[確] https://helpx.adobe.com/photoshop/using/tool-techniques/mixer-brush-tool.html 。Brush Settings パネルで先端・散布・テクスチャ・なめらかさ（stroke smoothing）を粒度制御する。[確] https://helpx.adobe.com/photoshop/using/nondestructive-editing.html （Painting tools / Brush presets 節）。
- 選択: Object Selection / Quick Selection / Magic Wand で対象を選び、右クリック→ Content-Aware Fill で周囲画素から補填する。[確] https://helpx.adobe.com/my_en/photoshop/using/content-aware-fill.html 。Remove tool（2024.3.1〜）はハロー低減を謳うが、本書では Helpx の Content-Aware Fill のみを確とする（第三者ベンチ数値は採用しない）。
- 変形: Smart Object 化で拡縮・回転・ワープを非破壊化する。[確] https://helpx.adobe.com/photoshop/using/nondestructive-editing.html 。
- 調整: 調整層（Levels/Curves/Hue-Saturation/Black&White/Brightness-Contrast 等）は画素を変えず「下の全層に適用・後から再編集可」。[確] https://helpx.adobe.com/photoshop/desktop/create-manage-layers/color-adjustment-fill-layers/adjustment-and-fill-layers-overview.html 。Adjustment Brush（2024〜）は「塗る→調整層＋層覆面を自動生成」の一段階化。[確] https://helpx.adobe.com/photoshop/using/tool-techniques/adjustment-brush-tool.html 。
- 層: Smart Filters（Smart Object 上のフィルタ）は後から係数を変えられる。[確] https://helpx.adobe.com/photoshop/using/nondestructive-editing.html 。別層 retouch（Clone/Healing を Sample All Layers で別層に記録）が非破壊作法として文書化されている。[確] 同上。
- 色: Fill 層（単色/グラデ/パターン）は下層に影響しない合成専用層。[確] 調整層概要ページ同上。
- 相性評価: 思想と完全一致（合成中心・非破壊・近道の源流）。調整層＋覆面自動生成・Smart Object・別層 retouch は本計画の規範核に据えるべき。[準→確：一次資料に基づく判断]

### 1.2 Krita（5.3系・KDE／Krita Foundation）

- 筆: 9種の独立エンジン（Pixel / Color Smudge / Particle / Sketch / Shape / Hairy / Spray / Curve / Dyna 等）。一つの汎用筆ではなく用途別物理を使い分ける。[確] https://krita.org/en/krita-brush-engines/ ではなく本調査で取得した一次相当 https://krita.io/features.html （Brush Engine & Tool Inspection 節）。三種の stabilizer＋Dynamic Brush（drag/mass の重み付け）で震えを抑える。[準] https://cloudorian.net/krita-open-source-painting-software （Krita 5.3.2.1 時点の解説。公式機能頁と一致）。
- 選択・塗り: 5.3 で gap-closing fill（隙間閉鎖塗り）が改良され、線画の微小な隙で溢れない。[準] https://quietcanvas.art/art-software/krita （5.3 release 解説）。
- 変形・描画補助: Wrap-Around Mode（Wキー）で tiling を live 表示し、継ぎ目なし texture を直接描ける。[確] https://krita.io/features.html および https://fileeagle.com/software/359/Krita 。Drawing assistants（透視・楕円・spline・平行・魚眼）で消失点に吸着。[確] https://krita.io/features.html 。
- 調整・層: filter を直接層・filter mask・filter layer として適用し、on-canvas preview する。[確] https://krita.io/features.html ＋ https://fileeagle.com/software/359/Krita 。levels/brightness-contrast/HSV 等を adjustment として含む。[準] 同上。
- 色: RGB/CMYK/Lab/XYZ/grayscale、8/16bit＋16/32bit float（HDR）、ICC 対応。Advanced Color Selector＋Gamut Mask（和声関係に制限し配色を保つ）。[確] https://krita.io/features.html 。
- その他秀点: Pop-up Palette（右クリックで色＋筆を canvas から離れず切替）、frame-by-frame animation（onion skin＋timeline＋WebM 書出）、Python API（PyKrita）、 comic panel template・halftone。[確] https://krita.io/features.html 。
- 相性評価: 非破壊（filter mask/layer）・合成中心と相性良。筆エンジン分化・Pop-up Palette・Wrap-Around・助手透視は本計画の将来核。[準]

### 1.3 CLIP STUDIO PAINT（CELSYS・Ver.5.0 時点）

- 筆・線: ベクター層が開始点・終点・曲率を記録し、後から筆先・太さ・ハンドル形状を変えられる。[確] https://help.clip-studio.com/en-us/manual_en/180_layers/Vector_layers.htm 。Correct line 系（制御点移動/線幅調整/単純化/結線/描き直し）で描画後も線質を直す。[確] 同上＋ https://help.clip-studio.com/en-us/manual_en/500_menu/500_menu_layer_new_vector_edit_senshusei.htm （Correct line Tool 説明）。補正系（Stabilization／速度で調整／後補正／Bezier 化／テーパ／ベクター磁石）が筆触を粒度制御する。[確] http://www.clip-studio.com/site/gd_en/csp/toolguide/csp_toolguide/100_reference/Correction.htm 。
- 変形・作画支援: mesh 変形・puppet warp・複数層同時 Tonal Correction（Ver.5 改善）。[確] https://www.clip-studio.net/en/functions 。Smart Shape（手描き→線・曲線・形に整形）、3D（手・頭・人体・小物・UV 描画・複数配置一括移動）。[確] 同上。
- 層・色: 色 profile 設定＋ preview を palette に反映（環境差防止）。[確] 同上。Ver.5 で水彩筆の高速化・高安定でも速描・高品質混色の自然化。[確] 同上。
- 導入・復旧: 未保存で閉じても再起動で canvas 復元、空層・隠層一括削除、素材履歴。[確] 同上。
- 相性評価: ベクター線の「描いた後の可変性」は非破壊思想と直結し最優先の規範。補正粒度・透視定規・3D 素材は将来層。Photoshop 流近道とは競合しない（線画特化で補完）。[準]

### 1.4 Procreate（Savage Interactive・iPadOS）

- 筆: Valkyrie（64bit・Metal 基盤）上に数百の手作り筆、Brush Studio 100超設定、grain 二重筆（dual-brush）、Photoshop .abr 互換。[確] https://apps.apple.com/in/app/procreate/id425073498 （Highlights/Brushes 節）。
- 描画補助: QuickShape（描いて保持→直線・弧・楕円・三角・四角に snap。第二指で正形、保持 drag で拡縮回転、15°刻み磁石回転、Edit Shape の node 編集）。[確] https://help.procreate.com/procreate/handbook/guides/quickshape 。StreamLine＋安定化、Drawing Assist（透視・等角・2D・対称ガイド）。[確] App Store 頁同上。
- 層: Layer Mask＋Clipping Mask（非破壊）、Group、複数層同時移動・変形、25超ブレンド。[確] App Store 頁同上。Eyedropper の active-layer 限定採取、複数層一括 merge/duplicate/share。[準] App Store 更新履歴節。
- 色: ColorDrop/SwatchDrop、Disc/Classic/Harmony/Value/Palette の5色盤、Color Dynamics を任意筆に割当。[確] App Store 頁同上。
- 仕上・記録: Gaussian/Motion/Perspective Blur、Glitch/Chromatic/Bloom/Noise/Halftone、Color Balance/Curves/HSB/Gradient Map を筆で塗れる調整、Warp/Symmetry/Liquify、Time-lapse 自動記録（4K 書出）。[確] App Store 頁同上。
- 近道・頭欄: Gesture Controls（touch＋Pencil の割当・保持遅延・衝突警告）、hover 時の pinch（筆径）・slide（不透明度）、Brush Cursor。[確] https://help.procreate.com/procreate/handbook/interface-gestures/gestures 。
- 相性評価: 非破壊覆面・調整・ gesture 近道・QuickShape・Time-lapse は規範に直結。Valkyrie 級 GPU 最適化は Avalonia＋SkiaSharp 上では将来性能目標として参照。[準]

### 1.5 GIMP（2.10→3.0/3.2・GNOME）

- 非破壊基盤: 2.10 で全 tile 管理を GEGL 化し project 毎に acyclic graph を構築（後の非破壊化の前提）。[確] https://docs.gimp.org/2.10/en/gimp-introduction-history-2-10.html ＋ https://www.gimp.org/release-notes/gimp-2-10.html 。色工具を GEGL filter 化。[確] 同上。3.0 で非破壊 filter（既定で層に残留・再編集・on/off・選択削除・XCF 保存再読込）。[確] https://www.gimp.org/release-notes/gimp-3-0.html 。3.2 で非破壊層（Rasterize 前は破壊編集不可・Revert 可）。[確] https://www.gimp.org/release-notes/gimp-3-2.html 。
- 筆: MyPaint Brush 導入（GIMP-Painter 由来）、Smudge の No erase＋Flow 混色、Hardness/Force 明示、Brush lock to view（zoom・回転に筆を固定するか選択）、Symmetry Painting（mirror/mandala/tiling）。[確] 2.10 release notes 同上。canvas 回転・反転（作画検証用）。[確] 同上。
- 層・色: 高 bit Depth・多 thread・OpenCL 版 GEGL op、split preview（before/after 比較・入替）。[確] 2.10 notes 同上。
- 相性評価: GEGL graph＝非破壊の実装規範そのもの。本計画の調整層・filter 層化・履歴保持の設計参照に最適。MyPaint 互換・対称描画・canvas 回転は将来採用候補。[準]

### 1.6 PaintTool SAI（SYSTEMAX・v1/v2）

- 公式確点: 高品質軽量・digitizer 完全対応・anti-aliased 描画・16bit ARGB・単純強力 UI・MMX・異常終了保護。[確] http://systemax.jp/en/sai （Product Overview）。stroke stabilizer S-1〜S-7 の段位設定がある（FAQ 言及）。[確] https://www.systemax.jp/en/sai/faq_spec.html 。
- 社区で一致する秀点（要一次確認＝[伝]）: 水彩・乳化混色の自然さ、軽快動作、perspective ruler（消失点 drag・軸吸着）が v2 で使える。典拠: https://painttoolsai.readthedocs.io/en/latest/features/perspective-rulers-guide.html （社区 docs・[準]）、 https://camcatbooks.com/paint-tool-sai-2-the-underrated-digital-art-powerhouse-redefining-creative-workflows （[伝]）、Reddit の混色評価（[伝] https://www.reddit.com/r/PaintToolSAI/comments/16l4rkm/am_i_the_only_one_who_finds_sai_better_than_csp/ ）。
- 相性評価: 軽量・安定・stabilizer・混色感は規範に合う。ただし一次資料が薄いため本書では [伝] 扱いに留め、採用表では「検証後採用」とする。

### 1.7 Affinity Photo 2（Serif）

- 非破壊: Live filter layer（blur/sharpen/distort/lighting 等を層として reorder・mask・再調整可）、調整層同等、history を文書に保存し再開後も遡れる。[確] https://affinity.help/photo/en-US.lproj/pages/Introduction/keyFeatures.html および https://affinity.serif.com/en-us/photo/full-feature-list 。Live Perspective／Live Mesh Warp の非破壊 warp。[確] 同 full-feature-list。
- 人物・現像: Persona 分割（Photo／Develop／Liquify／Tone Mapping／Export）が job 毎に toolset を切替。[準] 二次解説と公式 features の personas 記述の一致。Develop は RAW を層 stack に渡し破壊しない。Frequency Separation 一発 retouch、focus/exposure/median stacking、panorama（seam 編集可）、HDR merge＋tone mapping、32bit float 全通。[確] App Store 記述 https://apps.apple.com/us/app/id1616822987 および公式 features 同上。
- 選択・色: Selection Brush＋Refine（髪束級）、spare channel の保存・層化編集、RGB/CMYK/Lab＋soft proof、PSD 入出力。[確] 同上。
- 相性評価: Live 層・Persona・history 保存・spare channel は合成中心＋非破壊の上位規範。Export Persona（slice・複 format）は書出 UI の規範。[準]

### 1.8 Procreate Dreams（iPadOS アニメ）

- Timeline 三 mode: Perform（gesture を keyframe として記録・motion filtering 平滑化）、Keyframe（ easing 付 key 打ち）、Flipbook（frame-by-frame 自含 container）。[確] https://help.procreate.com/dreams/handbook/interface-and-gestures/timeline および https://help.procreate.com/dreams 。
- Flipbook: 複数 track・frame 保持・blend/mask/opacity の非破壊 track option、Add frame／Fill duration／Frame duration（twos/fours 対応）、Multi-select（囲み選択・移動・Flip frames 反転 loop）。[確] https://help.procreate.com/dreams/handbook/draw-and-paint/flipbook 。
- Onion skins: 前後 1-8枚・色・不透明度を指定。[確] https://help.procreate.com/articles/dvpjed-onion-skins 。
- 相性評価: 本計画は静止画が核のため全面採用しない。ただし Time-lapse・onion skin 式 preview・Perform 式 gesture 記録の考え方は将来動画層の規範。[準]

### 1.9 ibisPaint（ibis・5億 DL 級スマホ筆頭）

- 規模・筆: 数万〜10万超 brush（dip／felt／digital／air／fan／flat／pencil／oil／charcoal／crayon／stamp）、120fps 平滑（OpenGL）、brush 毎 stabilizer・fade 個別化（新機能頁）。[確] https://apps.apple.com/at/app/ibis-paint-x/id450722833 および https://play.google.com/store/apps/details?hl=en_IN&id=jp.ne.ibis.ibispaintx.app 、 https://ibispaint.com/newFeature.jsp 。
- 層・漫画: 無制限層・opacity／加算減算乗算・clipping、複写・反転・回転・移動・zoom、縦横 Stroke 複 text、46 screentone・86 filter・27 blend・素材3万・font3千。[確] 同 App Store／Play 頁。
- 定規・AI:  radial・symmetry ruler、参照窓、vector tool（premium）、AI Disturbance（生成 AI fine-tune 妨害 noise・強度 slider）、AI Background Removal。[確] https://ibispaint.com/newFeature.jsp および https://ibispaint.com/lecture/index.jsp?lang=en&no=192 。
- 導入・SNS: 描画過程 video 記録・SNS で他者の過程 video から学ぶ、YouTube tutorial 群。[確] App Store 頁 Concept/Features 節。
- 相性評価: 初心者導入（過程 video・SNS 学習・定規・Smart Shape 相当）と素材・filter 物量が規範。AI Disturbance・Background Removal は将来層（ ethics・model 由来に注意）。[準]

---

## 2. 初心者と熟練の両立策（導入・近道・頭欄・状態欄の工夫）

### 2.1 導入（onboarding・5分で一枚）

- 規範A「開いて5分で一枚」: New Canvas→Import→drag 移動→Export の最短路を頭欄に固定し、TUTORIAL 相当を初回 overlay で3手に圧縮する。根拠: 現行 README の導入路（New Canvas→Import→drag→Save/Export）が既に最短路。[確] 現行 README.md。ibisPaint の「難しいと思わせない単純入口＋無制限拡張」[確] と ClipStudio の Simple Mode・復元起動[確] を規範化。
- 規範B「過程で学ぶ」: Procreate Time-lapse[確]・ibisPaint 過程 video＋SNS[確]・Krita の workspace 保存[確] を束ね、「記録→見返す→共有」の導入 loop を標準にする。現行は記録なし→将来。
- 規範C「失敗を壊さない」: Affinity の history 保存[確]・GIMP 3 の NDE filter[確]・Photoshop の調整層[確] を「Undo 100 を超える安全網」として導入直後に教える。現行 Undo 上限100・調整層あり。[確] FEATURE_GAP.md。

### 2.2 近道（shortcut・gesture）

- 規範D「Photoshop 流を崩さない」: B/E/S/J/R/I/M/L/W/C/T/U/G/V/H・Ctrl+Z/Y/C/V・Ctrl+Shift+C・X/D・Del・矢印 nudge は現行 SHORTCUTS.md を凍結し増やすのみ。[確] 現行 SHORTCUTS.md。
- 規範E「保持・第二指・hover を足す」: Procreate の Draw&Hold→QuickShape・第二指正形・15°磁石[確]、hover pinch（径）・slide（濃度）[確]、Krita の W（wrap）・右 click Pop-up[確] を、既存近道と衝突しない予備層として追加する。衝突時は Procreate 式警告（黄色衝突表示）[確] を出す。
- 規範F「速度・圧・傾き dynamics」: ClipStudio の Velocity dynamics[確]・SAI 系 stabilizer 段位[確] を、筆設定の既定 preset として配る（初心者は既定、熟練は editor で割る＝Krita Brush Editor 式[確]）。

### 2.3 頭欄（header・toolbar）の工夫

- 規範G「最短路の固定＋文脈追従」: Photoshop の Contextual Task Bar（Adjustment Brush 時に調整・筆・精緻化をその場表示）[確] を頭欄に移植する。現行は上＝Undo/Redo・Copy/Paste・zoom・保存開封・書出の固定。[確] README.md。将来は工具毎に頭欄が切替わる（指針書 UI_UX_GUIDELINES の工具頭欄製品化の延長）。
- 規範H「Persona 式切替」: Affinity の Persona（現像・液状化・書出で toolset 切替）[確] を「描く／整える／出す」の3頭欄 preset として弱移植する。現行単一頭欄→将来。
- 規範I「題＋助言の整列」: 現行指針の工具頭欄（題印画像＋題＋区切＋助言）[確] UI_UX_GUIDELINES.md を維持し、ClipStudio の pop-up 関連 palette[確] を右層板に寄せる。

### 2.4 状態欄（status bar）の工夫

- 規範J「zoom・寸法・色の三区画＋助言省略」: 現行の状態欄製品化（zoom・寸法・色に画像＋11px 副文・助言省略）[確] を維持。将来は Affinity 式 slice 情報・Krita 式 profile/bit depth 表示・GIMP 式 split preview 切替を同欄に足す。
- 規範K「非破壊の可視化」: GIMP 3 の Fx 一覧[確]・Photoshop の調整層積層[確] を状態欄から一跳びで見せる（「何が載っているか」を常時1行で）。現行は層行の [F]/[M]/[C]/[S] 標識。[確] TOOLS_PARITY.md。

---

## 3. 本計画への適用表（採用・不採用・将来、理由付き）

凡例: ◎＝直ちに採用（現行延長で実装可）／○＝採用（工程化）／△＝将来（次期）／×＝不採用。理由は思想適合＋工数＋典拠で付す。

### 3.1 筆

| 他製品の秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: stroke smoothing・先端定義・Mixer 混色 | ○ | smoothing は SAI stabilizer と束ねて既定 preset 化。Mixer は Smudge 拡張の延長で工程化。典拠 Helpx [確] |
| Krita: 9 engine 分化・stabilizer 3種・Dynamic・Pop-up Palette | △（Pop-up のみ○） | engine 分化は Skia CPU 上で重い。Pop-up（canvas 駐留切替）は工数小で効果大のため先行。典拠 krita.io [確] |
| ClipStudio: ベクター線＋後補正・Correct line | ○ | 非破壊思想の核。Shape ラスタ化の上位として工程化。典拠 manual [確] |
| Procreate: dual-brush・100超設定・.abr 互換 | △ | .abr 読込は parser 工数大。dual の考えのみ Smudge に反映。典拠 App Store [確] |
| GIMP: MyPaint 互換・対称描画 | △ | 互換層が別 project。対称は Wrap と束ね将来。典拠 2.10 notes [確] |
| SAI: 軽快・stabilizer 段位・混色感 | ○（検証後） | 軽さは既存強みと合致。混色感は [伝] のため試作評価後に確定 |
| Affinity: 高度筆ライブラリ | △ | 写真 retouch 寄り。描画核が固まってから |
| ibisPaint: 10万筆・120fps・筆毎 fade | △ | 物量は資産依存。fade 個別化の考えのみ採用検討。典拠 store [確] |

### 3.2 選択

| 秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: Object Selection→Content-Aware Fill・Remove | ○（Fill 系） | 現行 ContentFill は周囲平均近似（小穴用）[確]。Fill 作業域・sampling 半径・色適応の UI を Photoshop 式に寄せる。生成系は× |
| Krita: gap-closing fill | ◎ | 線画導入の痛みを直接取る。現行 Wand/Marquee の延長で小工数。典拠 quietcanvas [準] |
| ClipStudio: 投繩→ベクター結線 | △ | ベクター化後に束ねる |
| Procreate: 選択 brush＋Refine | ○ | 髪束級 refine は Mask 羽化の延長。Eyedropper active-layer 限定も同時採用（典拠 App Store 更新節 [準]） |
| GIMP: spare channel 化・Quick mask | ○ | Affinity spare channel と同型。選択→channel 保存は小工数で非破壊に効く。典拠 features [確] |
| Affinity: ML Object/Subject・edge 検出 | ×（現行）→△ | model・苦荷重。古典 refine のみ先行。典拠 full-feature-list [確] |
| ibisPaint: 囲繞 Fill・囲繞 Eraser | ○ | 小穴 Fill の上位。現行近似の置換候補。典拠 premium 節 [確] |

### 3.3 変形

| 秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: Smart Object 非破壊変形 | ◎ | 現行は非破壊拡縮回転反転＋数値入力済み[確]。概念を「変形は常に非破壊」に格上げするのみ。典拠 Helpx [確] |
| Krita: Wrap-Around・助手透視 | △ | texture 需要が立ってから。W が既存近道と衝突するため割当設計要。典拠 krita.io [確] |
| ClipStudio: mesh・puppet・複数層同時変形 | △ | puppet は mesh 上位。mesh 先行。典拠 functions [確] |
| Procreate: QuickShape・move/scale 保持編集 | ○ | QuickShape は Shape 工具の上位操作として工程化（保持・第二指・15°）。典拠 handbook [確] |
| GIMP: Unified/Handle 変形・canvas 回転 | ○（回転のみ◎） | canvas 回転反転は作画検証の基本で小工数。Unified は Distort 延長で将来。典拠 2.10 [確] |
| Affinity: Live Perspective/Mesh Warp・Persona Liquify | △ | Live 系は filter 層化後に束ねる。典拠 features [確] |
| Dreams: Perform 記録・Warp grid | ×（静止画）→△動画層 | 静止画核を崩さない。Time-lapse のみ先行 |

### 3.4 調整・filter

| 秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: 調整層＋Fill 層・Smart Filter・Adjustment Brush | ◎ | 現行調整層方式あり[確]。Adjustment Brush 式「塗って調整層＋覆面生成」を次工程の核に。典拠 Helpx [確] |
| Krita: filter mask/layer・on-canvas preview | ○ | 現行調整層と同型。mask 運用を Krita 式に寄せる。典拠 features [確] |
| ClipStudio: 複数層同時 Tonal・profile preview | ○ | 複数選択 UI（現行 engine のみ[確]）の完成後に束ねる。典拠 functions [確] |
| Procreate: 筆塗り調整・Curves/HSB/Gradient Map | ◎（UI） | engine は移植済み（Curves/Levels/GradientMap/Exposure/Hue/Grain/Noise/Lens[確]）。残は sheet UI のみ→最優先工程。典拠 TOOLS_PARITY＋App Store [確] |
| GIMP 3: NDE filter（Fx 一覧・XCF 保持） | ○ | 「filter を層に残す」概念を .comp v8 以降に。典拠 3.0 notes [確] |
| Affinity: Live filter・history 保存・32bit | ○（history のみ△） | Live は NDE と同束。history 保存は .comp 肥大のため慎重。典拠 features [確] |
| ibisPaint: 86 filter・Tone Curve/Gradation/Levels（premium）・AI 系 | △ | 古典 3種は Procreate UI 完成後に横展開。AI Background Removal・Disturbance は ethics・model 由来で将来。典拠 store/newFeature [確] |

### 3.5 層・合成

| 秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: blend・clip・group・マスク運用 | ◎ | 現行は blend16・マスク・clip・group・Merge 拡張済み[確]。運用 UI（bake 対話・複数選択 UI）のみ残件 |
| Krita: filter 層・animation 層 | △ | animation は動画層。filter 層は NDE と同束 |
| ClipStudio: ベクター・3D・素材の一括移動 | △ | 3D は scope 外。ベクター完了後に素材束ね |
| Procreate: mask/clip・group・複数同時・25 blend | ◎ | 現行と同型。複数同時操作の UI を完成させる。典拠 App Store [確] |
| GIMP: GEGL graph・非破壊層 | ○ | 設計参照。実装は現行 DocumentModel 延長。典拠 3.2 notes [確] |
| Affinity: 無制限層・blend range・compound mask・layer state | ○ | blend range・compound・state は group 完成後に。典拠 features [確] |
| ibisPaint: 無制限層・clip・Solo Mode | ○（Solo のみ◎） | Solo（対象層のみ表示）は検図に直効。小工数で先行。典拠 newFeature [確] |

### 3.6 色

| 秀点 | 判定 | 理由 |
|---|---|---|
| Photoshop: Swatch/ASE・Fill 層 | ○ | ASE 読込は小工数。既存 12色＋追加削除[確] の延長 |
| Krita: ICC・HDR・Gamut Mask・Advanced Selector | △ | ICC/HDR は pipeline 改造が大。Gamut の考えのみ palette 拡張で先行。典拠 krita.io [確] |
| ClipStudio: profile preview palette 反映 | ○ | 環境差事故防止。小工数。典拠 functions [確] |
| Procreate: 5色盤・Dynamics・SwatchDrop | ○ | Harmony 式盤を ColorPicker 拡張として工程化。典拠 App Store [確] |
| GIMP: LCH blend・高 bit | △ | blend 拡張時に束ねる。典拠 2.10 [確] |
| Affinity: PANTONE/spot・soft proof | △ | 印刷需要が立ってから。典拠 features [確] |
| SAI: 16bit ARGB 高精度合成 | ○ | 現行 Skia 8bit の上位目標として記録。典拠 systemax [確] |

### 3.7 初心者・熟練・運用（採用抜粋）

| 秀点 | 判定 | 理由 |
|---|---|---|
| ibisPaint 過程 video・SNS・tutorial 群 | ○ | 導入 loop の核。Time-lapse と一体工程。典拠 store [確] |
| Procreate Time-lapse 4K・Gesture 割当・hover | ○ | 記録は小工数（連番 PNG→動画化）。Gesture は近道 E 層。典拠 handbook/store [確] |
| ClipStudio Simple Mode・復元・素材履歴 | ○ | Simple  preset は Persona 弱移植と同束。復元は .comp 自動保存で小工数。典拠 functions [確] |
| Affinity Persona・Export slice・macro | △ | Export slice は書出 UI 完成後に。macro は記録完成後に |
| Krita workspace・Python API | △ | workspace preset は小工数で先行可。API は安定後に |
| GIMP `/` filter 検索・split preview | ○ | filter 増殖後の必須 UI。検索は小工数。典拠 3.0 notes [確] |

不採用（×・当面）：生成 AI Fill／ML Subject・Background Removal の自動切抜の無批判導入（model・license・再現性の未整理のため。手動 refine の下位として将来再評価）、Vision 系 SubjectRemoval・GuidedMatte（現行対象外維持［確] FEATURE_GAP）、MSIX 署名・store 配布（証明書要［確] README）。

---

## 4. 行程案（phase・符号変更は本書工程では行わない）

- P0（直ちに・現行延長）: 調整 sheet UI（Curves/Levels/Hue/Filter の対話。engine 済み）／gap-closing fill／canvas 回転／Solo 表示／複数選択 UI 完成／ASE 読込・Harmony 盤・profile preview／`/` filter 検索。
- P1（次期・非破壊深化）: Adjustment Brush 式塗り調整／filter mask・Live 層（.comp v8 検討）／QuickShape／ベクター線＋Correct line／ベクター磁石／spare channel・Quick mask／Time-lapse 記録／Simple  preset＋Persona 弱移植＋Contextual 頭欄。
- P2（将来・表現拡張）: 9 engine 分化の入口（Smudge/Mixer 先行）／Wrap-Around・対称・透視助手／mesh→puppet／History 保存・macro／workspace preset・Python API 下地／HDR・ICC・Gamut／Export slice・compound mask・blend range／3D 素材・Dreams 式動画層・AI 系（手動下位・opt-in）。
- 各 phase の受入は「build 0 errors・dotnet test 全合格・実機 E2E（Import→編集→Export）・INSTALL_VERIFY 更新」を踏襲する。[確] 現行 README/INSTALL_VERIFY 運用。

---

## 5. 現行実装との差分（最終節・2026-09-21 時点）

### 5.1 現行が既に持つもの（本書 P0 の土台）

- 描画核: ブラシ/消しゴム（硬さ・間隔・不透明度キー・stroke Undo・COW）・Clone（Alt 採取・aligned・All Layers）・Spot 修復近似 3 mode・Smudge/Blur/Liquify（strength）・スポイト（All Layers 切替）・環表示。[確] FEATURE_GAP.md（t_e1a8b05e 更新）。
- 選択変形: 矩形楕円 Marquee・自由多角 Lasso・Wand（tolerance/contiguous/all-layers）・画素移動（切出・複写・nudge）・Copy/Paste・結合コピー・Distort・Crop・Canvas/Image Size・Flip・数値 X/Y/W/H/°・Sampling。[確] 同上＋TOOLS_PARITY.md。
- 調整 engine: Curves/Levels/GradientMap/Exposure/Hue 全域/Grain/Noise/Lens/Blur 外広がり（画素値試験 32 件相当）＋調整層方式。[確] 同上。
- 層合成: 16 blend・覆面（描画充填反転羽化・連結矢印移動・焼込）・clip・group・Merge Down 拡張・Merge Group・Undo100・タブ（•点・改名）・.comp v1-7 読込/v7 書出・PNG/JPEG（画質マット preview 進捗）・FG/BG＋HSB/RGB/hex・Photoshop 流近道。[確] 同上＋SHORTCUTS.md。
- 意匠: 暗色題材・chip 画像＋短文・頭欄題助言・額縁 canvas・状態欄三区画・未対応 badge 維持・x:Name/操作子不変。[確] TOOLS_PARITY 意匠節（t_fec99135）＋docs/UI_UX_GUIDELINES.md（t_3ddf5a70・274行級）。
- 右層板指針: 見出し56・滑子112・選択 S110/L130・Transform 数値 Grid 化・Brush 色欄次行分離（t_9b8a94d8 適用済み）。[確] git log 51ce16d。
- 検証: 直近 242 試験合格（t_2e336e81）→本書時点も回帰なしを前提とせず P0 開始前に再走する。配布 zip・SHA・INSTALL_VERIFY あり。[確] git log・README・dist/。

集計（TOOLS_PARITY 最終版）: ○15／△20／×3（表内）＋ sheet 未対応 badge 4＝×相当 7。入出力文書 UI 別枠 8 行すべて△。[確] TOOLS_PARITY.md。

### 5.2 本書が新たに求める差分（P0 優先順）

1. 調整 sheet UI 4種（Curves/Levels/Hue/Filter 対話）— engine△→○化。最大差分。
2. gap-closing fill・囲繞 Fill/Eraser — ContentFill 近似の置換。
3. canvas 回転反転・Solo・複数選択 UI — 操作完結。
4. 色・書出： ASE・Harmony・profile preview・Export slice 下地・`/` 検索。
5. 記録・導入： Time-lapse・Simple preset・復元・Contextual 頭欄。
6. 非破壊深化： Adjustment Brush・filter mask・Live 層（.comp v8）・QuickShape・ベクター・spare channel。

### 5.3 非目標（差分としない）

- Vision/生成系の自動化・3D・動画 timeline 本格・store 署名配布・HEIC/TIFF 復号器（探知明示を維持［確]）。符号・x:Name・操作子名の変更は本書工程では行わない（受入条件）。

---

## 典拠一覧（一次優先・取得 2026-09-21）

- Adobe: Adjustment Brush https://helpx.adobe.com/photoshop/using/tool-techniques/adjustment-brush-tool.html ／ Adjustment layers overview https://helpx.adobe.com/photoshop/desktop/create-manage-layers/color-adjustment-fill-layers/adjustment-and-fill-layers-overview.html ／ Nondestructive https://helpx.adobe.com/photoshop/using/nondestructive-editing.html ／ Mixer https://helpx.adobe.com/photoshop/using/tool-techniques/mixer-brush-tool.html ／ Content-Aware Fill https://helpx.adobe.com/my_en/photoshop/using/content-aware-fill.html
- Krita: https://krita.io/features.html ／ https://cloudorian.net/krita-open-source-painting-software ／ https://quietcanvas.art/art-software/krita ／ https://fileeagle.com/software/359/Krita
- ClipStudio: https://www.clipstudio.net/en/functions ／ Vector layers https://help.clip-studio.com/en-us/manual_en/180_layers/Vector_layers.htm ／ Correct line https://help.clip-studio.com/en-us/manual_en/500_menu/500_menu_layer_new_vector_edit_senshusei.htm ／ Correction https://help.clip-studio.com/site/gd_en/csp/toolguide/csp_toolguide/100_reference/Correction.htm
- Procreate: App https://apps.apple.com/in/app/procreate/id425073498 ／ QuickShape https://help.procreate.com/procreate/handbook/guides/quickshape ／ Gestures https://help.procreate.com/procreate/handbook/interface-gestures/gestures
- GIMP: 2.10 https://docs.gimp.org/2.10/en/gimp-introduction-history-2-10.html ・ https://www.gimp.org/release-notes/gimp-2-10.html ／ 3.0 https://www.gimp.org/release-notes/gimp-3-0.html ／ 3.2 https://www.gimp.org/release-notes/gimp-3-2.html
- SAI: http://systemax.jp/en/sai ／ FAQ https://www.systemax.jp/en/sai/faq_spec.html ／ perspective（社区） https://painttoolsai.readthedocs.io/en/latest/features/perspective-rulers-guide.html ／評価（伝） https://camcatbooks.com/paint-tool-sai-2-the-underrated-digital-art-powerhouse-redefining-creative-workflows
- Affinity: https://affinity.serif.com/en-us/photo/full-feature-list ／ https://affinity.help/photo/en-US.lproj/pages/Introduction/keyFeatures.html ／ App https://apps.apple.com/us/app/id1616822987
- Dreams: timeline https://help.procreate.com/dreams/handbook/interface-and-gestures/timeline ／ flipbook https://help.procreate.com/dreams/handbook/draw-and-paint/flipbook ／ onion https://help.procreate.com/articles/dvpjed-onion-skins
- ibisPaint: App https://apps.apple.com/at/app/ibis-paint-x/id450722833 ／ Play https://play.google.com/store/apps/details?hl=en_IN&id=jp.ne.ibis.ibispaintx.app ／ newFeature https://ibispaint.com/newFeature.jsp ／ AI Disturbance https://ibispaint.com/lecture/index.jsp?lang=en&no=192
- 現行: README.md・FEATURE_GAP.md・TOOLS_PARITY.md・SHORTCUTS.md・docs/UI_UX_GUIDELINES.md・docs/INSTALL_VERIFY.md・git log（58e8315〜）— いずれも Compositor.Windows 作業場内実存。
