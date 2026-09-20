# Third-Party Notices — Compositor for Windows

本ファイルは Compositor for Windows（移植版）の配布物に含まれる第三者蔵書の許諾表示である。
上流 Mac 版 Compositor（MIT License, Copyright (c) 2026 Wonder Assembly LLC）の LICENSE 継承に加え、
本移植版が依存する NuGet 蔵書の表示を補う。配布 zip には本ファイルを `THIRD-PARTY-NOTICES.md` として同梱する。

生成日: 2026-09-21
対象版: v1.1.0-windows
洗い出し方法: `src/**/ *.csproj` の PackageReference 全件 + `obj/project.assets.json` の推移的依存の突合。
許諾名は各 `.nupkg` の nuspec（`~/.nuget/packages` 実測）で確認。Web の入手先は NuGet Gallery。

## 0. 要約（漏れなし報告）

- 直接参照（Runtime・配布物に同梱）: Avalonia 11.2.1 / Avalonia.Desktop 11.2.1 /
  Avalonia.Themes.Fluent 11.2.1 / SkiaSharp 2.88.9。Avalonia.Diagnostics 11.2.1 は Debug 専用（Release 配布物に含めない）。
- 推移的依存（Release 配布物に同梱）: HarfBuzzSharp 7.3.0.2 / SkiaSharp.NativeAssets.Win32 2.88.9 /
  HarfBuzzSharp.NativeAssets.Win32 7.3.0.2 / MicroCom.Runtime 0.11.0 / Avalonia.Skia・Win32・Native 等 11.2.1 系 /
  ANGLE（Avalonia.Angle.Windows.Natives 2.1.22045.20230930）/ System.IO.Pipelines 8.0.0 / Tmds.DBus.Protocol 0.20.0（Linux のみ実使用）。
- 試験のみ（配布物に含めない）: Microsoft.NET.Test.Sdk 17.9.0 / xunit 2.7.0 /
  xunit.runner.visualstudio 2.5.7 / Microsoft.TestPlatform.* 17.9.0 / Microsoft.CodeCoverage 17.9.0 /
  Newtonsoft.Json 13.0.1（TestSdk 経由）/ xunit.* 2.7.0 系。
-  task 指示にあった CommunityToolkit.Mvvm は未使用: `src`・`tests` の grep および両 `project.assets.json`
  に存在せず、PackageReference なし。よって本書の対象外とする（将来導入時は本書へ追記すること）。
- .NET 8 ランタイム（自己完結同梱分）は MIT（https://github.com/dotnet/runtime）。

## 1. 配布物に含まれる蔵書（Runtime）

| 蔵書 | 版 | 許諾 | 入手先 |
|---|---|---|---|
| Avalonia | 11.2.1 | MIT | https://www.nuget.org/packages/Avalonia/11.2.1 |
| Avalonia.Desktop | 11.2.1 | MIT | https://www.nuget.org/packages/Avalonia.Desktop/11.2.1 |
| Avalonia.Themes.Fluent | 11.2.1 | MIT | https://www.nuget.org/packages/Avalonia.Themes.Fluent/11.2.1 |
| Avalonia.Skia / Avalonia.Win32 / Avalonia.Native / Avalonia.FreeDesktop / Avalonia.X11 / Avalonia.Remote.Protocol（推移的・同版） | 11.2.1 | MIT | https://www.nuget.org/packages/Avalonia/11.2.1 |
| SkiaSharp | 2.88.9 | MIT | https://www.nuget.org/packages/SkiaSharp/2.88.9 |
| SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT（同梱 native Skia は BSD-3、https://skia.org） | https://www.nuget.org/packages/SkiaSharp.NativeAssets.Win32/2.88.9 |
| HarfBuzzSharp（SkiaSharp/Avalonia 経由・推移的） | 7.3.0.2 | MIT（binding）。同梱 native HarfBuzz は Old MIT（https://harfbuzz.github.io） | https://www.nuget.org/packages/HarfBuzzSharp/7.3.0.2 |
| HarfBuzzSharp.NativeAssets.Win32 | 7.3.0.2 | MIT（binding）+ native Old MIT | https://www.nuget.org/packages/HarfBuzzSharp.NativeAssets.Win32/7.3.0.2 |
| MicroCom.Runtime（Avalonia 経由・推移的） | 0.11.0 | MIT | https://www.nuget.org/packages/MicroCom.Runtime/0.11.0 |
| Avalonia.Angle.Windows.Natives（推移的・ANGLE DLL） | 2.1.22045.20230930 | BSD-3-Clause（ANGLE Project Authors） | https://www.nuget.org/packages/Avalonia.Angle.Windows.Natives/2.1.22045.20230930 |
| System.IO.Pipelines（推移的） | 8.0.0 | MIT | https://www.nuget.org/packages/System.IO.Pipelines/8.0.0 |
| Tmds.DBus.Protocol（推移的・Linux 実使用のみ） | 0.20.0 | MIT | https://www.nuget.org/packages/Tmds.DBus.Protocol/0.20.0 |
| .NET 8 Runtime（自己完結同梱分） | 8.x | MIT | https://github.com/dotnet/runtime |

Avalonia.Diagnostics 11.2.1（MIT）は `Condition="'$(Configuration)'=='Debug'"` のため Release 配布物に含めない。

## 2. 試験のみ（配布物に含めない）

| 蔵書 | 版 | 許諾 | 入手先 |
|---|---|---|---|
| Microsoft.NET.Test.Sdk | 17.9.0 | MIT（同梱 LICENSE_MIT.txt） | https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.9.0 |
| Microsoft.TestPlatform.ObjectModel / Microsoft.TestPlatform.TestHost | 17.9.0 | MIT | https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.9.0 |
| Microsoft.CodeCoverage | 17.9.0 | MIT | https://www.nuget.org/packages/Microsoft.CodeCoverage/17.9.0 |
| xunit | 2.7.0 | Apache-2.0 | https://www.nuget.org/packages/xunit/2.7.0 |
| xunit.runner.visualstudio | 2.5.7 | Apache-2.0 | https://www.nuget.org/packages/xunit.runner.visualstudio/2.5.7 |
| xunit.assert / xunit.core / xunit.extensibility.*（推移的） | 2.7.0 | Apache-2.0 | https://www.nuget.org/packages/xunit/2.7.0 |
| xunit.abstractions（推移的） | 2.0.3 | Apache-2.0（license.txt 参照） | https://www.nuget.org/packages/xunit.abstractions/2.0.3 |
| Newtonsoft.Json（TestSdk 経由・推移的） | 13.0.1 | MIT | https://www.nuget.org/packages/Newtonsoft.Json/13.0.1 |

## 3. 許諾文

### MIT（Avalonia / SkiaSharp / HarfBuzzSharp binding / MicroCom / .NET / TestSdk 系 / Newtonsoft.Json / Tmds.DBus / System.IO.Pipelines）

各蔵書の著作権者は NuGet の蔵書頁または同梱の LICENSE/License.txt を参照のこと。
本文は MIT 標準文（https://licenses.nuget.org/MIT）：

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache-2.0（xunit 系）

全文は https://www.apache.org/licenses/LICENSE-2.0 および
https://github.com/xunit/xunit/blob/main/LICENSE を参照。
xunit 2.7.0 の NuGet 表示: 「It is licensed under Apache 2 (an OSI approved license)」
（https://www.nuget.org/packages/xunit/2.7.0）。

### BSD-3-Clause（ANGLE・Avalonia.Angle.Windows.Natives 同梱分）

同梱の LICENSE より（Copyright 2018 The ANGLE Project Authors）：

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following
    disclaimer in the documentation and/or other materials provided
    with the distribution.

    Neither the name of TransGaming Inc., Google Inc., 3DLabs Inc.
    Ltd., nor the names of their contributors may be used to endorse
    or promote products derived from this software without specific
    prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
```

### Native Skia / HarfBuzz（参考）

- Skia（SkiaSharp.NativeAssets.Win32 同梱）: BSD-3（https://skia.org）。
- HarfBuzz（HarfBuzzSharp.NativeAssets.Win32 同梱）: Old MIT（https://harfbuzz.github.io）。
  binding 自体は MIT。詳細は各 native の配布元を参照。

## 4. 同梱と参照

- 配布 zip（`tools/Make-Release.ps1`）は `LICENSE.txt` / `README.md` に加え本ファイル
  （`THIRD-PARTY-NOTICES.md`）を同梱する。
- 本 repo の `LICENSE` は上流 MIT（Wonder Assembly LLC 2026）を継承・改変なし。
  上流帰属の詳細は README「上流ライセンス継承」節を参照。
