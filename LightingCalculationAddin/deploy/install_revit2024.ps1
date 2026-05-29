$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$sourceDll = Join-Path $projectRoot "bin\$configuration\LightingCalculationAddin.dll"
$sourceEngineDll = Join-Path $projectRoot "bin\$configuration\LightingCalculationEngine.dll"

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Build the project first. Missing: $sourceDll"
}
if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Build the project first. Missing: $sourceEngineDll"
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
    Write-Host "Existing DLL is locked; installed versioned DLL instead: $targetDll"
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

Write-Host "Installed LightingCalculationAddin for Revit 2024."
Write-Host "Restart Revit, then open Codex Tools > Lighting > 조도 계산서."
