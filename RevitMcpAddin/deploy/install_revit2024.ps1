$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceDll = Join-Path $projectRoot "bin\Release\RevitMcpAddin.dll"
$addinSource = Join-Path $projectRoot "RevitMcpAddin.addin"
$targetRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024"
$targetDir = Join-Path $targetRoot "RevitMcpAddin"

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Build output not found. Run deploy\build_revit2024.ps1 first."
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination (Join-Path $targetDir "RevitMcpAddin.dll") -Force
Copy-Item -LiteralPath $addinSource -Destination (Join-Path $targetRoot "RevitMcpAddin.addin") -Force

Write-Host "Installed: $targetDir"
Write-Host "Manifest: $(Join-Path $targetRoot 'RevitMcpAddin.addin')"
