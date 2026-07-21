$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $root "build.ps1"
$sourceDll = Join-Path $root "output\RevitSheetBatchEditor.Engine.dll"

Write-Host "[1/2] Building Release files..."
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
if ($LASTEXITCODE -ne 0) { throw "Build failed. Hot reload was cancelled." }

if (-not (Test-Path -LiteralPath $sourceDll)) {
    throw "Engine DLL was not found: $sourceDll"
}

Write-Host "[2/2] Verifying development Engine DLL..."
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceDll).Hash
Write-Host "Engine SHA256: $hash"
Write-Host "Hot reload build completed successfully. No administrator permission was required."
Write-Host "Close the add-in window and click the Revit ribbon button again."
