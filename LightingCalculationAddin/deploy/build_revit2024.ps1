$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outputDir = Join-Path $projectRoot "bin\Release"
$outputDll = Join-Path $outputDir "LightingCalculationAddin.dll"
$engineDll = Join-Path $outputDir "LightingCalculationEngine.dll"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\WPF"
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
    /out:$outputDll `
    /reference:"$revitDir\RevitAPI.dll" `
    /reference:"$revitDir\RevitAPIUI.dll" `
    /reference:"$revitDir\Newtonsoft.Json.dll" `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:"$wpfDir\PresentationCore.dll" `
    /reference:"$wpfDir\WindowsBase.dll" `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Xml.dll `
    "$projectRoot\src\App.cs" `
    "$projectRoot\src\Command.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Loader build failed with exit code $LASTEXITCODE."
}

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

Write-Host "Built: $outputDll"
Write-Host "Built: $engineDll"
