$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceEngineDll = Join-Path $packageRoot "LightingCalculationEngine.dll"
$targetFolder = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\LightingCalculationAddin"
$targetEngineDll = Join-Path $targetFolder "LightingCalculationEngine.dll"

if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Missing package engine DLL: $sourceEngineDll"
}

if (-not (Test-Path -LiteralPath $targetFolder)) {
    throw "Revit add-in folder not found. Install the add-in first: $targetFolder"
}

Copy-Item -LiteralPath $sourceEngineDll -Destination $targetEngineDll -Force

Write-Host ""
Write-Host "Hot-swapped LightingCalculationEngine.dll for Revit 2024."
Write-Host "Source:"
Write-Host $sourceEngineDll
Write-Host "Target:"
Write-Host $targetEngineDll
Write-Host ""
Write-Host "In Revit, close the lighting calculation window if it is open, then run the command again."
