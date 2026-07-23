$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceDir = Join-Path $projectRoot "bin\Release"
$targetRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetDir = Join-Path $targetRoot "TunnelCableTrayPlacementAddin"
$addinTarget = Join-Path $targetRoot "TunnelCableTrayPlacementAddin.addin"
$loaderDll = Join-Path $targetDir "TunnelCableTrayPlacementAddin.dll"

if (-not (Test-Path $sourceDir)) {
    throw "Release build folder not found: $sourceDir"
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir "TunnelCableTrayPlacementAddin.dll") -Destination $targetDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir "TunnelCableTrayPlacementEngine.dll") -Destination $targetDir -Force

$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Tunnel Cable Tray Placement</Name>
    <Assembly>$loaderDll</Assembly>
    <AddInId>8FE24A11-B44E-4A42-A6EE-74B18A11E50D</AddInId>
    <FullClassName>TunnelCableTrayPlacementAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Tunnel centerline based cable tray placement</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $addinTarget -Value $addinXml -Encoding UTF8

Write-Host "Installed TunnelCableTrayPlacementAddin to:"
Write-Host $targetDir
Write-Host "Manifest:"
Write-Host $addinTarget
