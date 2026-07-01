$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$configuration = if ($env:CONFIGURATION) { $env:CONFIGURATION } else { "Release" }
$sourceDll = Join-Path $projectRoot "bin\$configuration\IfcReviewAddin.dll"
$sourceEngineDll = Join-Path $projectRoot "bin\$configuration\IfcReviewEngine.dll"

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Build the project first. Missing: $sourceDll"
}
if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Build the project first. Missing: $sourceEngineDll"
}

$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetFolder = Join-Path $addinRoot "IfcReviewAddin"
$targetAddin = Join-Path $addinRoot "IfcReviewAddin.addin"
$targetDll = Join-Path $targetFolder "IfcReviewAddin.dll"
$targetEngineDll = Join-Path $targetFolder "IfcReviewEngine.dll"

New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
Copy-Item -LiteralPath $sourceEngineDll -Destination $targetEngineDll -Force

try {
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
}
catch {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $targetDll = Join-Path $targetFolder "IfcReviewAddin_$stamp.dll"
    Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
    Write-Host "Existing DLL is locked; installed versioned DLL instead: $targetDll"
}

$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>IFC Review Addin</Name>
    <Assembly>$targetDll</Assembly>
    <AddInId>9B46D9E0-4694-4FA0-985C-3D6D85EF0A53</AddInId>
    <FullClassName>IfcReviewAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>IFC export target review and detailed parameter report tool</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $targetAddin -Value $manifest -Encoding UTF8

Write-Host "Installed IfcReviewAddin for Revit 2024."
Write-Host "Restart Revit, then open Codex Tools > IFC Review > IFC Review."
