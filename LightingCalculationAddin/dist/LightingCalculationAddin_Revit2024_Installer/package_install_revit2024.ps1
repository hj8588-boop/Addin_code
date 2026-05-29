$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDll = Join-Path $packageRoot "LightingCalculationAddin.dll"
$sourceEngineDll = Join-Path $packageRoot "LightingCalculationEngine.dll"

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Missing installer file: $sourceDll"
}

if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Missing installer file: $sourceEngineDll"
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetFolder = Join-Path $addinRoot "LightingCalculationAddin"
$targetAddin = Join-Path $addinRoot "LightingCalculationAddin.addin"
$targetDll = Join-Path $targetFolder "LightingCalculationAddin.dll"
$targetEngineDll = Join-Path $targetFolder "LightingCalculationEngine.dll"

New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
Copy-Item -LiteralPath $sourceEngineDll -Destination $targetEngineDll -Force

try {
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
}
catch {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $targetDll = Join-Path $targetFolder "LightingCalculationAddin_$stamp.dll"
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    Write-Host "Existing loader DLL is locked. Installed versioned DLL instead:"
    Write-Host $targetDll
}

$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Lighting Calculation</Name>
    <Assembly>$targetDll</Assembly>
    <AddInId>8F2601A6-11F7-44CE-A41E-2DD486C61B51</AddInId>
    <FullClassName>LightingCalculationAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Space lighting fixture count calculator</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $targetAddin -Value $manifest -Encoding UTF8

Write-Host ""
Write-Host "Installed LightingCalculationAddin for Revit 2024."
Write-Host "Target folder:"
Write-Host $targetFolder
Write-Host ""
Write-Host "Restart Revit, then run the Lighting Calculation command."
