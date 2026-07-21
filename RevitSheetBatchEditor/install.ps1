param([string]$Source = (Join-Path $PSScriptRoot "output"))

$ErrorActionPreference = "Stop"
$addinRoot = Join-Path $env:ProgramData "Autodesk\Revit\Addins\2024"
$dllFolder = Join-Path $addinRoot "RevitSheetBatchEditor"

if (-not (Test-Path (Join-Path $Source "RevitSheetBatchEditor.Loader.dll"))) {
    throw "먼저 build.ps1을 실행해 주세요. 빌드 결과를 찾을 수 없습니다: $Source"
}

New-Item -ItemType Directory -Force -Path $dllFolder | Out-Null
Copy-Item (Join-Path $Source "RevitSheetBatchEditor.Loader.dll") $dllFolder -Force
Copy-Item (Join-Path $Source "RevitSheetBatchEditor.Engine.dll") $dllFolder -Force
Copy-Item (Join-Path $Source "RevitSheetBatchEditor.addin") $addinRoot -Force
Write-Host "설치 완료. Revit을 다시 시작하세요."
