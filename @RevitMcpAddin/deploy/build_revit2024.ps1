$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outputDir = Join-Path $projectRoot "bin\Release"
$outputDll = Join-Path $outputDir "RevitMcpAddin.dll"
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
    /reference:"$wpfDir\PresentationCore.dll" `
    /reference:"$wpfDir\WindowsBase.dll" `
    "$projectRoot\src\App.cs" `
    "$projectRoot\src\McpStatusCommand.cs" `
    "$projectRoot\src\RevitMcpListener.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Revit MCP add-in build failed with exit code $LASTEXITCODE."
}

Write-Host "Built: $outputDll"
