$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$msbuild = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

& $msbuild (Join-Path $projectRoot "TunnelLightingPlacementEngine.csproj") /p:Configuration=Release
& $msbuild (Join-Path $projectRoot "TunnelLightingPlacementAddin.csproj") /p:Configuration=Release
