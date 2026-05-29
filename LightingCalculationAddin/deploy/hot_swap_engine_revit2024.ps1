$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outputDir = Join-Path $projectRoot "bin\Release"
$engineDll = Join-Path $outputDir "LightingCalculationEngine.dll"
$targetEngineDll = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\LightingCalculationAddin\LightingCalculationEngine.dll"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$revitDir = "C:\Program Files\Autodesk\Revit 2024"

if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found: $csc"
}

if (-not (Test-Path -LiteralPath (Join-Path $revitDir "RevitAPI.dll"))) {
    throw "Revit 2024 API not found: $revitDir"
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $csc `
    /target:library `
    /langversion:5 `
    /define:REVIT2024_OR_LESS `
    /out:$engineDll `
    /reference:"$revitDir\RevitAPI.dll" `
    /reference:"$revitDir\RevitAPIUI.dll" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Xml.dll `
    "$projectRoot\src\EngineEntry.cs" `
    "$projectRoot\src\LightingCalculationForm.cs" `
    "$projectRoot\src\LightingCalculationService.cs" `
    "$projectRoot\src\LightingFixtureType.cs" `
    "$projectRoot\src\LightingSpaceRow.cs" `
    "$projectRoot\src\SimpleXlsxWriter.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Engine build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath (Split-Path -Parent $targetEngineDll))) {
    throw "Revit add-in folder not found. Install the add-in first: $(Split-Path -Parent $targetEngineDll)"
}

Copy-Item -LiteralPath $engineDll -Destination $targetEngineDll -Force

Write-Host "Built engine DLL: $engineDll"
Write-Host "Updated Revit engine DLL: $targetEngineDll"
Write-Host "In Revit, close the lighting calculation window if it is open, then run the command again."
