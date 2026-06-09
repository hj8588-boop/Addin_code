$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceDir = Join-Path $projectRoot "bin\Release"
$targetRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetDir = Join-Path $targetRoot "TunnelLightingPlacementAddin"
$addinTarget = Join-Path $targetRoot "TunnelLightingPlacementAddin.addin"
$loaderDll = Join-Path $targetDir "TunnelLightingPlacementAddin.dll"

if (-not (Test-Path $sourceDir)) {
    throw "Release build folder not found: $sourceDir"
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir "TunnelLightingPlacementAddin.dll") -Destination $targetDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir "TunnelLightingPlacementEngine.dll") -Destination $targetDir -Force

$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Tunnel Lighting Placement</Name>
    <Assembly>$loaderDll</Assembly>
    <AddInId>2D33F327-A0D3-4977-A412-649A81F87B21</AddInId>
    <FullClassName>TunnelLightingPlacementAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Tunnel centerline based lighting fixture placement</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $addinTarget -Value $addinXml -Encoding UTF8

Write-Host "Installed TunnelLightingPlacementAddin to:"
Write-Host $targetDir
Write-Host "Manifest:"
Write-Host $addinTarget
