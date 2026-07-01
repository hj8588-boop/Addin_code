$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$sourceLoaderDll = Join-Path $projectRoot "bin\$configuration\SharedParameterValuesExportAddin.dll"
$sourceEngineDll = Join-Path $projectRoot "bin\$configuration\SharedParameterValuesExportEngine.dll"

if (-not (Test-Path -LiteralPath $sourceLoaderDll)) {
    throw "Build the project first. Missing: $sourceLoaderDll"
}

if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Build the engine project first. Missing: $sourceEngineDll"
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetFolder = Join-Path $addinRoot "SharedParameterValuesExportAddin"
$targetAddin = Join-Path $addinRoot "SharedParameterValuesExportAddin.addin"
$targetLoaderDll = Join-Path $targetFolder "SharedParameterValuesExportAddin.dll"
$targetEngineDll = Join-Path $targetFolder "SharedParameterValuesExportEngine.dll"

New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
Copy-Item -LiteralPath $sourceLoaderDll -Destination $targetLoaderDll -Force
Copy-Item -LiteralPath $sourceEngineDll -Destination $targetEngineDll -Force

$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Shared Parameter Values Export</Name>
    <Assembly>$targetLoaderDll</Assembly>
    <AddInId>74304F8D-48F5-4F92-9E2F-2C1B79C7A501</AddInId>
    <FullClassName>SharedParameterValuesExportAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Shared parameter value export tools</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $targetAddin -Value $manifest -Encoding UTF8

Write-Host "Installed SharedParameterValuesExportAddin for Revit 2024."
Write-Host "Restart Revit once after installing the loader."
Write-Host "After that, use deploy\hot_swap_engine_revit2024.ps1 for engine/UI changes."
