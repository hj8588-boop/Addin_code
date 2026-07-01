$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msbuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
$loaderProject = Join-Path $projectRoot "SharedParameterValuesExportAddin.csproj"
$engineProject = Join-Path $projectRoot "SharedParameterValuesExportEngine.csproj"

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild not found: $msbuild"
}

& $msbuild $engineProject /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) {
    throw "Engine build failed with exit code $LASTEXITCODE."
}

& $msbuild $loaderProject /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) {
    throw "Loader build failed with exit code $LASTEXITCODE."
}

Write-Host "Built loader and engine:"
Write-Host (Join-Path $projectRoot "bin\Release\SharedParameterValuesExportAddin.dll")
Write-Host (Join-Path $projectRoot "bin\Release\SharedParameterValuesExportEngine.dll")
