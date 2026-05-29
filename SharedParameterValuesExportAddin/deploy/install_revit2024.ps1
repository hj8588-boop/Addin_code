$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$sourceDll = Join-Path $projectRoot "bin\$configuration\SharedParameterValuesExportAddin.dll"

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Build the project first. Missing: $sourceDll"
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetFolder = Join-Path $addinRoot "SharedParameterValuesExportAddin"
$targetAddin = Join-Path $addinRoot "SharedParameterValuesExportAddin.addin"
$targetDll = Join-Path $targetFolder "SharedParameterValuesExportAddin.dll"

New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
try {
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
}
catch {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $targetDll = Join-Path $targetFolder "SharedParameterValuesExportAddin_$stamp.dll"
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    Write-Host "Existing DLL is locked; installed versioned DLL instead: $targetDll"
}

$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Shared Parameter Values Export</Name>
    <Assembly>$targetDll</Assembly>
    <AddInId>74304F8D-48F5-4F92-9E2F-2C1B79C7A501</AddInId>
    <FullClassName>SharedParameterValuesExportAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Shared parameter value export tools</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $targetAddin -Value $manifest -Encoding UTF8

Write-Host "Installed SharedParameterValuesExportAddin for Revit 2024."
Write-Host "Restart Revit, then open Codex Tools > Parameters > Shared Parameter Export."
