$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msbuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$engineProject = Join-Path $projectRoot "SharedParameterValuesExportEngine.csproj"
$sourceEngine = Join-Path $projectRoot "bin\Release\SharedParameterValuesExportEngine.dll"
$targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\SharedParameterValuesExportAddin"
$targetEngine = Join-Path $targetDir "SharedParameterValuesExportEngine.dll"

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild not found: $msbuild"
}

& $msbuild $engineProject /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) {
    throw "Engine build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $sourceEngine)) {
    throw "Engine DLL not found after build: $sourceEngine"
}

if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

Copy-Item -LiteralPath $sourceEngine -Destination $targetEngine -Force

Write-Host "Hot-swapped SharedParameterValuesExportEngine.dll"
Write-Host "From: $sourceEngine"
Write-Host "To:   $targetEngine"
Write-Host "Run the Revit command again to load the updated engine."
