$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msbuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$engineProject = Join-Path $projectRoot "TroughAutoPlacementEngine.csproj"
$sourceEngine = Join-Path $projectRoot "bin\Release\TroughAutoPlacementEngine.dll"
$targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\TroughAutoPlacementAddin"
$targetEngine = Join-Path $targetDir "TroughAutoPlacementEngine.dll"

& $msbuild $engineProject /p:Configuration=Release

if (-not (Test-Path $sourceEngine)) {
    throw "Engine DLL not found after build: $sourceEngine"
}

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

Copy-Item -LiteralPath $sourceEngine -Destination $targetEngine -Force

Write-Host "Hot-swapped TroughAutoPlacementEngine.dll"
Write-Host "From: $sourceEngine"
Write-Host "To:   $targetEngine"
