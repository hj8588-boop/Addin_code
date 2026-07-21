param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RevitInstallDir = "C:\Program Files\Autodesk\Revit 2024"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "RevitSheetBatchEditor.sln"
$msbuild = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if (-not (Test-Path (Join-Path $RevitInstallDir "RevitAPI.dll"))) {
    throw "RevitAPI.dll을 찾을 수 없습니다: $RevitInstallDir"
}

& $msbuild $solution /t:Rebuild /p:Configuration=$Configuration /p:RevitInstallDir="$RevitInstallDir" /m
if ($LASTEXITCODE -ne 0) { throw "빌드에 실패했습니다." }

$output = Join-Path $root "output"
New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item (Join-Path $root "src\RevitSheetBatchEditor.Loader\bin\$Configuration\RevitSheetBatchEditor.Loader.dll") $output -Force
Copy-Item (Join-Path $root "src\RevitSheetBatchEditor.Engine\bin\$Configuration\RevitSheetBatchEditor.Engine.dll") $output -Force
Copy-Item (Join-Path $root "deploy\RevitSheetBatchEditor.addin") $output -Force
Write-Host "빌드 완료: $output"
