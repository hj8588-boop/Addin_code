$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msbuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$engineProject = Join-Path $projectRoot "TunnelLightingPlacementEngine.csproj"
$sourceEngine = Join-Path $projectRoot "bin\Release\TunnelLightingPlacementEngine.dll"
$targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\TunnelLightingPlacementAddin"
$targetEngine = Join-Path $targetDir "TunnelLightingPlacementEngine.dll"

& $msbuild $engineProject /p:Configuration=Release

if (-not (Test-Path $sourceEngine)) {
    throw "Engine DLL not found after build: $sourceEngine"
}

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

Copy-Item -LiteralPath $sourceEngine -Destination $targetEngine -Force

Write-Host "Hot-swapped TunnelLightingPlacementEngine.dll"
Write-Host "From: $sourceEngine"
Write-Host "To:   $targetEngine"
Write-Host "Run the Tunnel Lights command again in Revit. Revit restart is not required for Engine changes."
