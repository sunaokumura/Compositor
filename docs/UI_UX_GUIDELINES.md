# Compositor.Windows 右層板 UI/UX 指針書

- 対象: `src/Compositor.Avalonia/MainWindow.axaml` 右列 (`Grid.Column="2"`, L290〜L744)
- 主 grid: `Grid ColumnDefinitions="56,*,296"` (L221)。右列幅 296。
- 根元: `StackPanel Spacing="6" Margin="8,6,8,10"` (L292)。右列の全行はこの1本の縦積み。
- 有効幅: 296 − 左罫 1 − 左右余白 8×2 = **279 (呼称 280)**。本書で「280幅」と呼ぶ値はこの有効幅である。
- 色題材 (L8〜L11, L12〜L88 の Styles): 背景 `#1E1E1E` / 板 `#252526` / 頭欄 `#2D2D30` / 区切線 `#3E3E42` / 本文 `#E8E8E8` / 副文 `#9D9D9D` / 補助 `#6E6E6E` / 節標題 `#CCCCCC` / 選択 `#094771`+左線 `#007ACC` / 無効 Opacity 0.45。
- 本書は現行 axaml の実存のみを根拠にする。推測で書かない。`x:Name` と操作子名 (Click/ValueChanged/SelectionChanged ハンドラ名) は本指針の対象外であり、一切変更しない (調査のみ工程のため)。

調査日: 2026-09-21 / 調査者工程 t_3ddf5a70 / 子工程 t_9b8a94d8 への申送り用。

## 0. 実測の要約 (がたつきの原因の目録)

| # | 現象 (実存) | 箇所 (行) | 原因 |
|---|---|---|---|
| G1 | 標題幅が不統一。滑子の左端が揃わない | L424〜L449, L504〜L511, L556〜L600, L628〜L632, L671〜L685, L718〜L721 | `Rename/Blend/Opacity/Scale %/Rotate/Sample/Size/Hard/Space/Opac` の見出し `TextBlock` に `Width` 指定なし。`Adjust` 群 (`Bright/Contrast/Satur./Blur/W.Tol`, L582〜L600, L719) のみ `Width="52"` 固定。行ごとに自動幅が変わるため滑子・数値欄の x 座標が揃わない |
| G2 | 滑子幅が2種混在 | L443, L448 `Width="132"` ×2 (Opacity/Scale) 対 L453〜L720 `Width="112"` ×10 (Rotate/Bright/Contrast/Satur/Blur/BrushSize/Hardness/Spacing/BrushOpacity/WandTolerance) | 統一値の決定がないまま追加された。132 幅の2行だけ右へ 20 突き出す |
| G3 | 選択欄・数値欄幅が5種混在 | `ComboBox`: 100 (BrushColorBox L631), 110 (CropRatio/CloneSample/SmudgeMode/SelMode), 120 (BlendBox L439), 130 (SamplingBox L506, RenameBox相当), 140 (HealModeBox L699)。`TextBox`: 130 (RenameBox L429) 対 56 (TxX/TxY/TxW/TxH/TxAngle L485〜L495) | 一覧表なしで足された。`Brush Size` 行 (L628〜L632) は `Size` 見出し(自動幅)+112+100=`約249` で収まるが、`SmudgeMode(110)+HealMode(140)` 行 (L693〜L704) は `110+6+140=256` で有効幅 279 に残り 23 しかなく、窓縮小時はみ出す |
| G4 | Transform 数値入力行が有効幅を超過 | L483〜L492 `WrapPanel` 内: `X(14)+56+Y(14)+56+W(16)+56+H(14)+56` + `Spacing` 既定。合計 300 超 (後述試算 316) | `StackPanel Orientation="Horizontal"` ではなく `WrapPanel` で逃がしているため、幅不足時に X/Y/W/H が不規則に折り返す。これが「がたつき」の最大要因 |
| G5 | `WrapPanel` 13 箇所が折返しの危険源 | L299, L346, L397, L456, L483, L512, L527, L541, L566, L601, L709, L722, L726 | 層釦群・覆面群・変形釦群・画布釦群・切抜釦群・色釦群・選択釦群すべて `WrapPanel`。窓幅 296 固定時は収まるが、DPI 拡大・文字拡大・和文訳の長い釦 (`Canvas Size/Image Size/Crop確定/Distort確定/GradientMap (未対応)` 等) で折返し位置がずれ、行高がばらつく |
| G6 | 群間余白が二重定義 | 根元 `Spacing="6"` (L292) + 節標題 `Style TextBlock.section Margin="2,8,2,0"` (L72〜L77) | 節の上は 6+8=14、下は 6。群内は 6。意図せずそうなっているだけで、文書化されていない。現状値はそのまま本指針の群間 14 / 群内 6 の根拠にする |
| G7 | 子間 `Spacing` が 5 と 6 の2種混在 | 右列内 `Spacing="5"` ×43 (chip 内の画像+短文) 対 `Spacing="6"` ×25 (行の子間)。`Spacing="4"` は頭欄のみ | chip 内部 5 と行子間 6 の使い分けが文書化されていない。現状の使い分けをそのまま規格化する (4章) |
| G8 | 節標題 11 件は統一済み (良い点) | L345 Mask, L396 Group / Clip, L423 Shape, L450 Transform, L482 Transform 数値入力, L526 Canvas, L555 Crop, L580 Adjust, L627 Brush, L633 Color, L708 Selection | `Classes="section"` (SemiBold 12 `#CCCCCC`) で統一。維持する |
| G9 | 補足文 2 件は補助色で統一済み (良い点) | L740〜L741 `Foreground="#6E6E6E" FontSize="11" TextWrapping="Wrap"` の操作子覚書 2 行 | 維持する。3章に規格化 |
| G10 | 釦内は画像+短文で統一済み (良い点) | chip 内 `StackPanel Orientation="Horizontal" Spacing="5"` + `PathIcon 12×12` + `TextBlock` (L300〜L738 の各 chip) | 維持する。画像単独釦 (`PaletteAdd/Delete` L663〜L668 の `PathIcon` のみ) は例外として残す |

行高の実存: `Button.chip Padding="7,4" CornerRadius="5" FontSize="12"` (L54〜L61)、`Button.bar Padding="7,4"` (L35〜L42)、`Button.rail 40×40` (L13〜L23, 工具列のみで右列には使わない)、`ListBoxItem Padding="6,3" FontSize="12"` (L81〜L84)、`LayerList Height="220"` (L298)、`PaletteBox Width="170" Height="64"` (L661)。右列の行高は内容物に委ねられ、固定値は `LayerList 220` と `PaletteBox 64` のみ。

## 1. 整列の基本

原則 1-1 (左端揃え): 右列の全行の左端は根元 StackPanel の左余白 8 の直下に揃える。各行の先頭要素に個別 `Margin` を付けない。現状、節標題 Style が `Margin="2,8,2,0"` (L76) で左に 2 の追加ずれを持つ。これは節標題のみの例外として残し、それ以外の行には左 Margin を付けない。

原則 1-2 (標題幅の統一): 見出し `TextBlock` 列の幅を **56** に固定する。現状 `Adjust` 群の 52 (L582〜L600) が最も近い実績であり、英字 6 字 (`Contrast`, `Bright` 等) が `FontSize 12` で収まる最小幅である。和文・長い英字 (`Opacity`, `Scale %`, `Rotate`, `Sample`, `Size`, `Hard`, `Space`, `Opac`, `Rename`, `Blend`, `Ratio`) を同列に置くため 52→56 に切り上げる。`X/Y/W/H/°` の極小見出し (L484〜L494 の `Width 14/16`) は別枠 (数値入力行専用) とし、この統一の対象外とする。詳細は 4章と付録C。

原則 1-3 (操作子幅の統一): 滑子・数値欄・選択欄の幅は 4章の統一値に従う。同じ種類は同じ幅にする。現状の 132/112 混在 (G2)、100/110/120/130/140 混在 (G3) をなくすことが本指針の核心である。

原則 1-4 (行高の統一): 行高は固定値で詰めない。内容物の自然高に任せ、行間 (`Spacing`) で揃える。固定するのは `LayerList Height="220"` と `PaletteBox Height="64"` の2件のみ (現状維持)。釦の高さは `Padding 7,4` に委ね、個別 `Height` を付けない (現状、右列 chip に `Height` 指定なし。工具列の `Height="36"` L241〜L267 は右列の対象外)。

悪い例 (現状の Opacity 行 L441〜L445。見出し幅なし・滑子 132):

```xml
<StackPanel Orientation="Horizontal" Spacing="6">
  <TextBlock Text="Opacity" VerticalAlignment="Center" Classes="dim"/>
  <Slider x:Name="OpacitySlider" Minimum="0" Maximum="100" Width="132" ValueChanged="OnOpacityChanged"/>
  <TextBlock x:Name="OpacityLabel" VerticalAlignment="Center" Text="100%" Foreground="#E8E8E8"/>
</StackPanel>
```

良い例 (指針準拠。見出し 56・滑子 112。`x:Name` とハンドラ名は変えない):

```xml
<StackPanel Orientation="Horizontal" Spacing="6">
  <TextBlock Text="Opacity" VerticalAlignment="Center" Width="56" Classes="dim"/>
  <Slider x:Name="OpacitySlider" Minimum="0" Maximum="100" Width="112" ValueChanged="OnOpacityChanged"/>
  <TextBlock x:Name="OpacityLabel" VerticalAlignment="Center" Text="100%" Foreground="#E8E8E8"/>
</StackPanel>
```

## 2. 余白の基本 (8点基準)

原則 2-1 (8点基準): 余白は 8 の倍数を基準にする。実存する値は `Margin 8,6,8,10` (L292)、節上 8 (L76)、`ListBoxItem Padding 6,3`、`chip Padding 7,4` である。新規に 3・5・7 等の端数を持ち込まない。ただし現状の 6 (根元 Spacing)・5 (chip 内 Spacing)・7,4 (Padding) は実績として凍結し、規格値として再定義する (下表)。

原則 2-2 (群間と群内の別): 群間 (節と節の間) は **14** (= 根元 Spacing 6 + 節標題上余白 8)。群内 (節の中の行間) は **6** (= 根元 Spacing)。区切りに `Separator` や罫線を足さない。節標題 (`Classes="section"`) が群の先頭であることを示す唯一の合図である。現状 G6 の二重定義をそのまま規格化する。計算式を変えないこと (`Spacing` も `Margin 2,8,2,0` も変えない)。

原則 2-3 (区切の用法): 右列内に `Separator` を持ち込まない (現状右列に `Separator` なし。頭欄・工具頭欄の `Separator` は対象外)。群の区切りは節標題+群間 14 のみで行う。罫線 (`Border`) は右列外周 (`BorderThickness="1,0,0,0"` L290) のみ。行間に罫線を足さない。

原則 2-4 (外余白): 根元 `Margin="8,6,8,10"` を変えない。上 6・左右 8・下 10 が右列の外余白である。下 10 は末尾の補足文 (L740〜L741) と ScrollViewer 末端の当たりを柔らかくする実績値として残す。

| 用途 | 値 | 根拠 (実存) |
|---|---|---|
| 外余白 | 上6・左右8・下10 | L292 根元 Margin |
| 群間 (節上) | 14 (=6+8) | L292 Spacing 6 + L76 節上 8 |
| 群内行間 | 6 | L292 根元 Spacing |
| 行内子間 | 6 | 右列 `Spacing="6"` ×25 の行子間 |
| 釦内子間 (画像+短文) | 5 | chip 内 `Spacing="5"` ×43 |
| 節標題余白 | 2,8,2,0 | L76 Style |

## 3. 視覚階層の基本 (節標題・本文・補足の寸法と色の使い分け)

原則 3-1 (3層のみ): 右列の文字は3層のみ。層を増やさない。

| 層 | 用途 | 寸法 | 色 | 実存 |
|---|---|---|---|---|
| 節標題 | Mask / Group / Clip / Shape / Transform / Transform 数値入力 / Canvas / Crop / Adjust / Brush / Color / Selection の11件 | SemiBold 12 | `#CCCCCC` | L72〜L77 Style `TextBlock.section`, L345〜L708 の11件 |
| 本文 | 釦文・値表示 (`100%`, `0°`, `100` 等)・見出し (`Rename/Blend/Opacity/...`) | 既定 (継承 12) | 釦文は継承 `#E8E8E8`、見出しは `Classes="dim"` `#9D9D9D`、値表示は `#E8E8E8` 明示 | L78〜L80 `TextBlock.dim`、L444 `OpacityLabel Foreground="#E8E8E8"` 等 |
| 補足 | 末尾の操作子覚書2行のみ | 11 | `#6E6E6E` + `TextWrapping="Wrap"` | L740〜L741 |

原則 3-2 (見出しは dim): 行の見出し (`Rename/Blend/Opacity/Scale %/Rotate/Sample/Ratio/Bright/Contrast/Satur./Blur/Size/Hard/Space/Opac/W.Tol`) はすべて `Classes="dim"` (`#9D9D9D`) にする。現状、G1 の13行のうち `Rename/Blend/Opacity/Scale %/Rotate/Sample/Ratio/Size` (L428〜L449, L504〜L510, L556〜L564, L628〜L631) は dim 付き、`Hard/Space/Opac` (L672〜L685) は dim 付き、`X/Y/W/H/°` (L484〜L494) は dim なし (極小見出しの例外)。値表示 (`OpacityLabel 100%` 等) は `#E8E8E8` のままにし、見出しと値を色で分ける。色の使い分けを逆転させない (見出しを白くしない)。

原則 3-3 (節標題の文言): 節標題は英字単語のみ (`Mask`, `Canvas`, `Crop`, `Adjust`, `Brush`, `Color`, `Selection` 等)。`Transform 数値入力` (L482) のみ和文混じりの例外として残す。節標題に操作子 (釦・滑子) を入れない。

原則 3-4 (補足の用法): 補足色 `#6E6E6E` 11pt は末尾2行 (L740〜L741) の専用であり、行中の注意書きに流用しない。行中の助言は `ToolTip.Tip` で行う (現状各 chip/Slider/ComboBox に `ToolTip.Tip` あり。例 L506 SamplingBox、L688 CloneSampleBox)。画面上に常時出す説明文を増やさない。

原則 3-5 (画像の用法): 釦内画像は `PathIcon 12×12` (chip 内実績。L302〜L738)、節頭画像は `PathIcon 14` (`Layers` 行 L294 のみ)。色は継承 (指定なし) とし、個別 `Foreground` を付けない (`Layers` 行の `#9D9D9D` L294 は例外として残す)。無効釦は `Opacity 0.45` (L69〜L71 Style) で示し、文言に「(未対応)」を添える実績 (`Curves (未対応)` L611 等) を維持する。

## 4. 操作子规格 (280幅に収まる数値)

有効幅 279 (呼称 280) に収まることを第一条件にする。試算式は `見出し幅 + Spacing 6 + 操作子幅 (+ Spacing 6 + 値/第2操作子幅)` とし、合計が 279 以下であること。

### 4.1 統一値 (指針値)

| 種類 | 指針値 | 現状からの変更 | 根拠 |
|---|---|---|---|
| 見出し列 (行見出し) | **56** | `Bright/Contrast/Satur./Blur/W.Tol` の 52→56。`Rename/Blend/Opacity/Scale %/Rotate/Sample/Ratio/Size/Hard/Space/Opac` に 56 を新設 | G1 の解消。52 は英6字の最小実績。56 に切り上げて和文・長英字を吸収 |
| 極小見出し (X/Y/W/H/°) | 14 (X/Y/H/°)、16 (W) | 変更なし | L484〜L494 の実績を凍結 |
| 滑子 | **112** | `Opacity/Scale` の 132→112。ほか10件は 112 維持 | G2 の解消。多数派 112 に寄せる |
| 数値欄 (Tx系) | 56 | 変更なし | L485〜L495 実績 (TxX/TxY/TxW/TxH/TxAngle) |
| 一覧欄 (RenameBox) | 130 | 変更なし | L429 実績 |
| 選択欄 S (標準) | **110** | `BlendBox` 120→110、`BrushColorBox` 100→110 | G3 の解消。110 は CropRatio/CloneSample/SmudgeMode/SelMode の多数派実績 |
| 選択欄 L (広) | **130** | `BlendBox` の移行先は S(110)。`SamplingBox` 130 維持。`HealModeBox` 140→130 | 140 は唯一の突出値 (L699)。130 に寄せる |
| 一覧 (LayerList) | Height 220 | 変更なし | L298 実績 |
| 調色板 (PaletteBox) | Width 170 × Height 64 | 変更なし | L661 実績 |
| 行内子間 | 6 | 変更なし | 2章表 |
| 釦内子間 | 5 | 変更なし | 2章表 |

### 4.2 収まり試算 (有効幅 279 に対する検算。`+6` は Spacing)

| 行 | 現状の式 | 合計 | 指針値の式 | 合計 | 判定 |
|---|---|---|---|---|---|
| Opacity (L441〜L445) | 見出し自動(~45)+6+132+6+値(~30) | 約219 収まるが左端不揃い | 56+6+112+6+30 | 210 | 収まる・揃う |
| Scale % (L446〜L449) | 同上 | 約219 | 56+6+112+6+値なし | 174 | 収まる・揃う |
| Rotate (L451〜L455) | 見出し自動(~40)+6+112+6+値(~20) | 約184 | 56+6+112+6+20 | 200 | 収まる・揃う |
| Bright/Contrast/Satur./Blur (L581〜L600) | 52+6+112+6+値(~24) | 200 | 56+6+112+6+24 | 204 | 収まる・揃う |
| Brush Size (L628〜L632) | 見出し自動(~25)+6+112+6+100 | 約249 | 56+6+112+6+100 | 280→279 超過1 | **注意** (下記N1) |
| Rename (L427〜L436) | 見出し自動(~45)+6+130+6+Apply釦(~70) | 約257 | 56+6+130+6+70 | 268 | 収まる |
| Blend (L437〜L440) | 見出し自動(~35)+6+120 | 約161 | 56+6+110 | 172 | 収まる |
| Sample (L504〜L511) | 見出し自動(~40)+6+130 | 約176 | 56+6+130 | 192 | 収まる |
| Smudge+Heal (L693〜L704) | 110+6+140 | 256 | 110+6+130 | 246 | 収まる (140→130 で余裕+10) |
| Transform 数値 X/Y/W/H (L483〜L492) | 14+6+56+6+14+6+56+6+16+6+56+6+14+6+56 | **316** | 同左 (Grid化で固定。下記N2) | 316→配置換え | **超過** (WrapPanel 折返しの根源) |
| °+Lock+Apply (L493〜L503) | 14+6+56+6+Lock(~60)+6+Apply(~70) | 約218 | 変更なし (収まる行) | 約218 | 収まる |

N1 (`Brush Size` 行): 指針値を素直に足すと 280 となり有効幅 279 を 1 超える。`Size` 見出しは短いため実幅は 279 に収まるが、余裕がない。子工程では `BrushColorBox` を次行へ分離するか、`SamplingBox` 行と同様の2段化を許す。滑子 112 と選択欄 110/130 の値は変えないこと。

N2 (`Transform 数値` 行): 1行に4組 (X/Y/W/H) を並べる現状配置は 280 幅に物理的に収まらない。子工程では 2組×2行 (`X/Y` 行と `W/H` 行) に分けるか、`Grid` 列定義で固定する (5章)。`TextBox Width 56` と極小見出し 14/16 の値は変えないこと。`WrapPanel` のまま詰めないこと。

### 4.3 釦の寸法

- chip 釦 (`Button.chip`): `Padding 7,4 / CornerRadius 5 / FontSize 12` (L54〜L61)。個別 `Width/Height` を付けない (現状右列 chip に寸法指定なし)。釦内は `StackPanel Orientation="Horizontal" Spacing="5" VerticalAlignment="Center"` + `PathIcon 12×12` + `TextBlock` (G10 実績)。
- 例外: `ShapeKindBtn` (L425 `Content="Rectangle"`)、`MarqueeShapeBtn Content="Rect"` (L710)、`LassoKindBtn Content="Freehand"` (L711) の3件は文字のみ釦。画像化しない (試験互換のため文言を変えない)。
- 例外: `PaletteAdd/Delete` (L663〜L668) の画像のみ釦 (`PathIcon` のみ) は残す。新規に画像のみ釦を増やさない (助言 `ToolTip.Tip` 必須)。

## 5. Avalonia制約 (WrapPanel折返しの危険・Grid列定義による固定の用法)

制約 5-1 (WrapPanel は折返し釦群専用): `WrapPanel Orientation="Horizontal"` (右列13箇所) は、釦群 (層・覆面・群・変形・画布・切抜・調整・選択) の折返しを許すための専用である。1対1対応の行 (見出し+滑子+値、見出し+選択欄、見出し+数値欄) に `WrapPanel` を使わない。現状、Transform 数値行 (L483) と `°+Lock+Apply` 行直後の `Sample` 行は `StackPanel`/`WrapPanel` が混在している。子工程では対応行をすべて `StackPanel Orientation="Horizontal" Spacing="6"` に寄せる。`WrapPanel` の既定 `ItemSpacing` がない (間隔0) ため、釦群の行内では釦同士が密着する。釦群の `WrapPanel` に変える必要はない (現状のまま。間隔は chip の `Padding 7,4` と文字幅に委ねる)。

制約 5-2 (Grid列定義による固定の用法): 見出し列+操作子列を厳密に揃える必要がある行 (4.2 の N2 `Transform 数値` 行) は `Grid ColumnDefinitions="14,56,6,14,56"` 等の列定義で固定する。列幅の合計が 279 を超えないこと。`Grid` を使うのは N2 の2行のみとし、ほかの行には持ち込まない (過剰な固定は和文・訳文の伸びを殺す)。

```xml
<!-- N2 の良い例 (X/Y 行。W/H 行も同様。x:Name とハンドラは変えない) -->
<Grid ColumnDefinitions="14,56,6,14,56">
  <TextBlock Grid.Column="0" Text="X" VerticalAlignment="Center"/>
  <TextBox Grid.Column="1" x:Name="TxX" Width="56"/>
  <TextBlock Grid.Column="3" Text="Y" VerticalAlignment="Center"/>
  <TextBox Grid.Column="4" x:Name="TxY" Width="56"/>
</Grid>
```

制約 5-3 (`ScrollViewer` の用法): 右列外側は `ScrollViewer VerticalScrollBarVisibility="Auto"` (L291) のままにする。`HorizontalScrollBarVisibility` を `Auto` にしない (既定 Disabled のまま)。横スクロールで逃がすと 280 幅の規格が崩れる。縦スクロールで逃がす。

制約 5-4 (`TextWrapping="Wrap"` の用法): 長文は末尾補足2行 (L740〜L741) のみ `TextWrapping="Wrap"` を許す。行中の見出し・釦文に `Wrap` を付けない。釦文が長い場合 (`GradientMap (未対応)` L623 等) は文言を変えず (試験互換)、`WrapPanel` の折返しに委ねる。

制約 5-5 (DPI・文字拡大への備え): 主 grid の右列幅 296 と `LayerList 220` / `PaletteBox 170×64` は固定値のままにする (変えない)。拡大部分は縦スクロールで吸収する。右列幅を `Auto` や `*` に変えないこと。

## 付録A. 実測目録 (右列の全行。L290〜L744)

凡例: S=StackPanel(H,6)=対応行 / W=WrapPanel=折返し釦群 / sec=節標題 / note=補足。

| 行 (目安) | 内容 | 形式 | 実寸 |
|---|---|---|---|
| L293〜L297 | Layers 頭 (画像14+節標題+数 0) | S | 画像14+標題 |
| L298 | LayerList | 単独 | Height 220 |
| L299〜L344 | 層釦7 (Layer/Delete/Up/Down/Duplicate/Merge/Group) + Visible/Lock 照合欄 | W | chip×7+CheckBox×2 |
| L345 | Mask | sec | section |
| L346〜L395 | 覆面釦8 (White/Black/Delete/On-Off/Target/Invert/Feather/Link) | W | chip×8 |
| L396 | Group / Clip | sec | section |
| L397〜L422 | 群釦4 (Folder/Group/Ungroup/Clip) | W | chip×4 |
| L423 | Shape | sec | section |
| L424〜L426 | ShapeKindBtn (Rectangle) | S | 文字のみ釦 |
| L427〜L436 | Rename + RenameBox 130 + Apply | S | 見出し自動+130+釦 |
| L437〜L440 | Blend + BlendBox 120 | S | 見出し自動+120 |
| L441〜L445 | Opacity + 滑子132 + 値100% | S | 見出し自動+132 |
| L446〜L449 | Scale % + 滑子132 | S | 見出し自動+132 |
| L450 | Transform | sec | section |
| L451〜L455 | Rotate + 滑子112 + 値0° | S | 見出し自動+112 |
| L456〜L481 | 変形釦4 (+90°/Flip H/Flip V/Distort) | W | chip×4 |
| L482 | Transform 数値入力 | sec | section |
| L483〜L492 | X/Y/W/H 数値4組 (56×4) | W | 316 超過 (G4) |
| L493〜L503 | °(14)+TxAngle 56+Lock+Apply | S | 約218 |
| L504〜L511 | Sample + SamplingBox 130 | S | 見出し自動+130 |
| L512〜L525 | Distort確定/取消 | W | chip×2 |
| L526 | Canvas | sec | section |
| L527〜L540 | Canvas Size / Image Size | W | chip×2 |
| L541〜L554 | Flip H / Flip V | W | chip×2 |
| L555 | Crop | sec | section |
| L556〜L565 | Ratio + CropRatioBox 110 | S | 見出し自動+110 |
| L566〜L579 | Crop確定/取消 | W | chip×2 |
| L580 | Adjust | sec | section |
| L581〜L585 | Bright + 滑子112 + 値0 | S | 52+112 |
| L586〜L590 | Contrast + 滑子112 + 値0 | S | 52+112 |
| L591〜L595 | Satur. + 滑子112 + 値100 | S | 52+112 |
| L596〜L600 | Blur + 滑子112 + 値0 | S | 52+112 |
| L601〜L626 | Invert/Curves(未対応)/Levels(未対応)/GradientMap(未対応) | W | chip×4 |
| L627 | Brush | sec | section |
| L628〜L632 | Size + 滑子112 + BrushColorBox 100 | S | 見出し自動+112+100 |
| L633 | Color | sec | section |
| L634〜L659 | FG/BG/X/D | S | chip×4 (1行) |
| L660〜L670 | PaletteBox 170×64 + 追加/削除 | S | 170+釦2 |
| L671〜L675 | Hard + 滑子112 + 値100% | S | 見出し自動+112 |
| L676〜L680 | Space + 滑子112 + 値15% | S | 見出し自動+112 |
| L681〜L685 | Opac + 滑子112 + 値100% | S | 見出し自動+112 |
| L687〜L692 | Aligned + CloneSampleBox 110 | S | 照合欄+110 |
| L693〜L704 | SmudgeModeBox 110 + HealModeBox 140 | S | 110+140=256 |
| L705〜L707 | Eyedrop All Layers | S | 照合欄1 |
| L708 | Selection | sec | section |
| L709〜L717 | Rect/Freehand/SelMode 110 | W | 文字釦2+110 |
| L718〜L721 | W.Tol + 滑子112 | S | 52+112 |
| L722〜L725 | Contiguous + All Layers | W | 照合欄2 |
| L726〜L739 | Deselect / Delete Sel | W | chip×2 |
| L740〜L741 | 操作子覚書2行 | note | 11pt `#6E6E6E` Wrap |

## 付録B. 悪い例と良い例 (抜粋。`x:Name`・ハンドラ名は両例とも不変)

B1. 見出し幅なし (悪) → 56 固定 (良)。対象: Rename/Blend/Opacity/Scale %/Rotate/Sample/Ratio/Size/Hard/Space/Opac の全行。例は1章の Opacity 行を参照。

B2. 滑子 132 (悪) → 112 (良)。対象: OpacitySlider (L443)、ScaleSlider (L448)。

```xml
<!-- 悪 (現状 L443) -->
<Slider x:Name="OpacitySlider" Minimum="0" Maximum="100" Width="132" ValueChanged="OnOpacityChanged"/>
<!-- 良 -->
<Slider x:Name="OpacitySlider" Minimum="0" Maximum="100" Width="112" ValueChanged="OnOpacityChanged"/>
```

B3. 選択欄 120/100/140 (悪) → 110/130 (良)。対象: BlendBox 120→110、BrushColorBox 100→110、HealModeBox 140→130。

```xml
<!-- 悪 (現状 L439/L631/L699) -->
<ComboBox x:Name="BlendBox" Width="120" SelectionChanged="OnBlendChanged"/>
<ComboBox x:Name="BrushColorBox" Width="100" ToolTip.Tip="ブラシ色 (旧6色・FGと連動)"/>
<ComboBox x:Name="HealModeBox" Width="140" SelectedIndex="0" SelectionChanged="OnHealModeChanged" ...>
<!-- 良 -->
<ComboBox x:Name="BlendBox" Width="110" SelectionChanged="OnBlendChanged"/>
<ComboBox x:Name="BrushColorBox" Width="110" ToolTip.Tip="ブラシ色 (旧6色・FGと連動)"/>
<ComboBox x:Name="HealModeBox" Width="130" SelectedIndex="0" SelectionChanged="OnHealModeChanged" ...>
```

B4. Transform 数値の WrapPanel 1行4組 (悪) → Grid 2組×2行 (良)。5章制約 5-2 の例を参照。

## 付録C. 右列への適用表 (子工程 t_9b8a94d8 用。見た目のみの変更に限定)

| 対象行 | 現状 | 指針値 | 子工程の作業 |
|---|---|---|---|
| Rename/Blend/Opacity/Scale %/Rotate/Sample/Ratio/Size/Hard/Space/Opac の見出し | Width なし | Width 56 + `Classes="dim"` 維持 | `Width="56"` を付ける。文言・`x:Name` 不変 |
| Opacity/Scale 滑子 | 132 | 112 | `Width="112"` に寄せる |
| BlendBox | 120 | 110 (S) | `Width="110"` に寄せる |
| BrushColorBox | 100 | 110 (S) | `Width="110"` に寄せる。`Size` 行の収まりは N1 に従い次行分離を許す |
| HealModeBox | 140 | 130 (L) | `Width="130"` に寄せる |
| Transform 数値 X/Y/W/H | WrapPanel 1行 (316 超過) | Grid 2組×2行 (X/Y・W/H) | 5-2 の Grid 化。`TextBox 56`・極小見出し 14/16 不変。折返し解消 |
| 対応行の `WrapPanel` | Transform 数値行のみ | `StackPanel Orientation="Horizontal" Spacing="6"` | 数値行の `WrapPanel` をやめる。釦群の `WrapPanel` は残す |
| 節標題・群間・補足・色・釦内 | 現状維持 | 変更なし | 触らない (2章・3章の凍結値) |
| `x:Name`・Click/ValueChanged/SelectionChanged 名・文言・ToolTip | 現状維持 | 変更なし | 触らない (試験互換。本書の適用は見た目のみ) |

## 変更禁止事項

- `x:Name` (87件の実績。子工程の前提) と操作子名 (Click/ValueChanged/SelectionChanged) の変更を禁止する。本指針の適用は `Width`・`StackPanel/Grid`・`Spacing/Margin` の見た目のみに限定する。
- 機能変更の禁止。`IsEnabled="False"` の未対応釦 (L274 被写体除去、L608 Curves、L614 Levels、L620 GradientMap、L742 SubjectRemovalBtn) の有効化・削除を行わない。
- 本指針と現状が矛盾する場合は本指針を優先し、差分を子工程の検証報告に記録する (子工程 t_9b8a94d8 の前提)。

## 検証報告 (本工程 t_3ddf5a70)

- 実存確認: `MainWindow.axaml` 全765行を通読。右列 L290〜L744 の全行を付録Aに目録化。`Slider Width` 12件 (132×2・112×10)、`ComboBox Width` 8件 (100/110/120/130/140)、`TextBox Width` 6件 (130×1・56×5)、`WrapPanel` 13件、`Spacing 5×43/6×25`、`section` 11件を数え上げで確認 (数え上げは python 正規表現による機械集計)。
- 有効幅の検算: 296−1−16=279 (呼称280)。4.2 の各行を `見出し+6+操作子(+6+値)` で検算し、Transform 数値行のみ超過 (316) であることを確認。
- 成果物: `docs/UI_UX_GUIDELINES.md` (本書) を作業場 `Compositor.Windows/docs/` に保存。符号変更なし (MainWindow.axaml 無改変)。
- 受入条件: 指針書の保存・作業場 git へ commit (push なし)・検証報告 (本節)・符号変更の禁止 (遵守)。
