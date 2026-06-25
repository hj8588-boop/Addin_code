$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceDir = Join-Path $projectRoot "bin\Release"
$targetRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetDir = Join-Path $targetRoot "TroughAutoPlacementAddin"
$addinTarget = Join-Path $targetRoot "TroughAutoPlacementAddin.addin"
$loaderDll = Join-Path $targetDir "TroughAutoPlacementAddin.dll"

if (-not (Test-Path $sourceDir)) {
    throw "Release build folder not found: $sourceDir"
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDir "TroughAutoPlacementAddin.dll") -Destination $targetDir -Force
Copy-Item -LiteralPath (Join-Path $sourceDir "TroughAutoPlacementEngine.dll") -Destination $targetDir -Force

$addinXml = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>&#xD2B8;&#xB85C;&#xD504; &#xC790;&#xB3D9; &#xBC30;&#xCE58;</Name>
    <Assembly>$loaderDll</Assembly>
    <AddInId>6A7155E1-C7BC-48CF-A125-BF14D45A4F4E</AddInId>
    <FullClassName>TroughAutoPlacementAddin.App</FullClassName>
    <VendorId>CODEX</VendorId>
    <VendorDescription>Dynamo-based trough auto placement</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $addinTarget -Value $addinXml -Encoding UTF8

Write-Host "Installed TroughAutoPlacementAddin to:"
Write-Host $targetDir
Write-Host "Manifest:"
Write-Host $addinTarget
