$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceEngineDll = Join-Path $projectRoot "bin\Release\IfcReviewEngine.dll"
$targetFolder = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\IfcReviewAddin"
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$defaultEngineDll = Join-Path $targetFolder "IfcReviewEngine.dll"
$targetEngineDll = Join-Path $targetFolder "IfcReviewEngine_$stamp.dll"

if (-not (Test-Path -LiteralPath $sourceEngineDll)) {
    throw "Build the engine first. Missing: $sourceEngineDll"
}

New-Item -ItemType Directory -Path $targetFolder -Force | Out-Null
Copy-Item -LiteralPath $sourceEngineDll -Destination $targetEngineDll -Force
try {
    Copy-Item -LiteralPath $sourceEngineDll -Destination $defaultEngineDll -Force
    Write-Host "Updated default engine DLL: $defaultEngineDll"
}
catch {
    Write-Host "Default engine DLL is locked; versioned engine will be used by the updated loader."
}
Write-Host "Installed versioned engine DLL: $targetEngineDll"
Get-ChildItem -LiteralPath $targetFolder -Filter "IfcReviewEngine*.dll" |
    Sort-Object Name |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
Write-Host "Close and reopen the IFC Review window. Revit restart is not required after the loader update is active."
