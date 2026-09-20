#Requires -Version 5.1
<#
.SYNOPSIS
  Compositor for Windows — リリースzip作成スクリプト。
.DESCRIPTION
  src/publish-win (dotnet publish済み自己完結出力) を
  LICENSE / README.md と同梱して dist/ に zip 化し、SHA256を出力する。
  MSIX/コード署名は有効な証明書がないため対象外 (packaging/Package.appxmanifest は草案)。
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\Make-Release.ps1
  powershell -ExecutionPolicy Bypass -File tools\Make-Release.ps1 -Version v1.0.0-windows
#>
param(
  [string]$Version = "v1.1.0-windows",
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "src\publish-win"
$DistDir = Join-Path $RepoRoot "dist"

if (-not (Test-Path $PublishDir)) {
  Write-Host "[Make-Release] publish-win がありません。先に次を実行してください:"
  Write-Host "  dotnet publish src/Compositor.Avalonia -c $Configuration -r $Runtime --self-contained true -o src/publish-win"
  exit 1
}

$Exe = Join-Path $PublishDir "Compositor.exe"
if (-not (Test-Path $Exe)) { Write-Host "[Make-Release] Compositor.exe が publish-win にありません。"; exit 1 }

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$Stage = Join-Path $DistDir "stage_Compositor-Windows_$Version"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Copy-Item "$PublishDir\*" -Destination $Stage -Recurse -Force
Copy-Item (Join-Path $RepoRoot "LICENSE") -Destination (Join-Path $Stage "LICENSE.txt") -Force
Copy-Item (Join-Path $RepoRoot "README.md") -Destination (Join-Path $Stage "README.md") -Force

$Zip = Join-Path $DistDir "Compositor-Windows-$Version-win-x64.zip"
if (Test-Path $Zip) { Remove-Item -Force $Zip }
Compress-Archive -Path "$Stage\*" -DestinationPath $Zip -CompressionLevel Optimal

$Hash = (Get-FileHash $Zip -Algorithm SHA256).Hash
$Size = (Get-Item $Zip).Length
"$Hash  $((Get-Item $Zip).Name)" | Out-File -FilePath "$Zip.sha256" -Encoding ascii

Write-Host "[Make-Release] OK"
Write-Host "  zip : $Zip"
Write-Host "  size: $Size bytes"
Write-Host "  sha256: $Hash"
Write-Host "  注意: 本zipは未署名 (コード署名は有効な証明書が必要なため未実施)。"
