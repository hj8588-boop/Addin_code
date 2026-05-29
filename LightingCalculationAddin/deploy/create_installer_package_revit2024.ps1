$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$deployRoot = Join-Path $projectRoot "deploy"
$distRoot = Join-Path $projectRoot "dist"
$packageName = "LightingCalculationAddin_Revit2024_Installer"
$packageRoot = Join-Path $distRoot $packageName
$zipPath = Join-Path $distRoot ($packageName + ".zip")

& (Join-Path $deployRoot "build_revit2024.ps1")

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $projectRoot "bin\Release\LightingCalculationAddin.dll") -Destination (Join-Path $packageRoot "LightingCalculationAddin.dll") -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "bin\Release\LightingCalculationEngine.dll") -Destination (Join-Path $packageRoot "LightingCalculationEngine.dll") -Force
Copy-Item -LiteralPath (Join-Path $deployRoot "package_install_revit2024.ps1") -Destination (Join-Path $packageRoot "package_install_revit2024.ps1") -Force
Copy-Item -LiteralPath (Join-Path $deployRoot "package_install_revit2024.bat") -Destination (Join-Path $packageRoot "install_revit2024.bat") -Force
Copy-Item -LiteralPath (Join-Path $deployRoot "package_hot_swap_engine_revit2024.ps1") -Destination (Join-Path $packageRoot "package_hot_swap_engine_revit2024.ps1") -Force
Copy-Item -LiteralPath (Join-Path $deployRoot "package_hot_swap_engine_revit2024.bat") -Destination (Join-Path $packageRoot "hot_swap_engine_revit2024.bat") -Force

$readme = @"
LightingCalculationAddin Revit 2024 Installer

Install:
1. Close Revit.
2. Run install_revit2024.bat.
3. Restart Revit.

Hot-swap engine update:
1. Keep Revit open if needed, but close the lighting calculation window.
2. Run hot_swap_engine_revit2024.bat.
3. Run the Lighting Calculation command again in Revit.

Installed files:
- %APPDATA%\Autodesk\Revit\Addins\2024\LightingCalculationAddin.addin
- %APPDATA%\Autodesk\Revit\Addins\2024\LightingCalculationAddin\LightingCalculationAddin.dll
- %APPDATA%\Autodesk\Revit\Addins\2024\LightingCalculationAddin\LightingCalculationEngine.dll
"@

Set-Content -LiteralPath (Join-Path $packageRoot "README.txt") -Value $readme -Encoding UTF8

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force

Write-Host "Created installer folder:"
Write-Host $packageRoot
Write-Host "Created installer zip:"
Write-Host $zipPath
